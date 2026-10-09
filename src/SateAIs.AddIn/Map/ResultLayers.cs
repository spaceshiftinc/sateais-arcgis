using System.Diagnostics;
using System.IO;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using SateAIs.Core;
using SateAIs.Core.Api;
using SateAIs.Core.Jobs;

namespace SateAIs.AddIn.Map;

/// <summary>
/// Turns a result into layers: the GeoJSON becomes a feature class in the project's default
/// geodatabase (so the attribute table works), styled by detection type, under one group.
/// </summary>
internal static class ResultLayers
{
    public const double ZoomPaddingRatio = 0.15;

    private const string DeviationField = "deviation";

    /// <summary>Widths in the QGIS plugin are millimetres; Pro symbols use points.</summary>
    private const double MmToPt = 72 / 25.4;

    private static readonly CIMColor White = CIMColor.CreateRGBColor(255, 255, 255);

    /// <summary>Border colour, fill alpha (0–255) and point size (mm) per type, as in the QGIS plugin.</summary>
    private static readonly IReadOnlyDictionary<string, (string Color, int FillAlpha, double PointSize)> StyleByType =
        new Dictionary<string, (string, int, double)>
        {
            ["ship"] = ("#00d4ff", 100, 4),
            ["oilslick"] = ("#a855f7", 100, 4),
            ["newbuilding"] = ("#10b981", 90, 4),
            ["disappearbuilding"] = ("#ef4444", 90, 4),
            ["timeseries"] = ("#f59e0b", 90, 4),
        };

    private static readonly (string Color, int FillAlpha, double PointSize) DefaultStyle = ("#00d4ff", 90, 4);

    /// <summary>
    /// The diverging ramp for signed change. Red is increase and blue decrease, as the server and
    /// the web viewer draw them. Unchanged cells are a hairline with no fill: on a real job almost
    /// every cell is unchanged, and filling them buries the findings.
    /// </summary>
    private static readonly (string Key, string Label, string Color, int FillAlpha, bool Marker)[] ChangeClasses =
    [
        ("inc_strong", $"Increase (strong) ≥ {ResultStats.StrongDeviation:0.##}", "#c2312f", 200, true),
        ("inc", $"Increase ≥ {ResultStats.SignificantDeviation:0.##}", "#f0a0a0", 170, true),
        ("none", "No significant change", "#898781", 0, false),
        ("dec", $"Decrease ≤ -{ResultStats.SignificantDeviation:0.##}", "#9ec5f4", 170, true),
        ("dec_strong", $"Decrease (strong) ≤ -{ResultStats.StrongDeviation:0.##}", "#2a78d6", 200, true),
    ];

    private static readonly string ChangeExpression =
        $"var d = $feature.{DeviationField};\n" +
        "if (IsEmpty(d)) { return ''; }\n" +
        $"if (d >= {ResultStats.StrongDeviation}) {{ return 'inc_strong'; }}\n" +
        $"if (d >= {ResultStats.SignificantDeviation}) {{ return 'inc'; }}\n" +
        $"if (d <= -{ResultStats.StrongDeviation}) {{ return 'dec_strong'; }}\n" +
        $"if (d <= -{ResultStats.SignificantDeviation}) {{ return 'dec'; }}\n" +
        "return 'none';";

    /// <summary>The layer already on the map for this job, if any.</summary>
    public static Task<FeatureLayer?> FindResultLayerAsync(string analysisType, string jobId) =>
        QueuedTask.Run(() => FindLayer(ResultNames.FeatureClassName(analysisType, jobId)));

    public static Task ZoomToAsync(FeatureLayer layer) => QueuedTask.Run(() =>
    {
        if (MapView.Active is { } view)
        {
            Zoom(view, layer);
        }
    });

    /// <summary>Add the result as a styled layer at the top of the results group and zoom to it.</summary>
    public static async Task<FeatureLayer> AddResultAsync(string geojson, TrackedJob job, int count)
    {
        var featureClass = await ConvertAsync(geojson, ".geojson", ResultNames.FeatureClassName(job.AnalysisType, job.JobId));
        return await QueuedTask.Run(() =>
        {
            var view = MapView.Active ?? throw new InvalidOperationException("no active map");
            var layer = CreateLayer(view.Map, featureClass,
                ResultNames.LayerName(job.AnalysisType, job.JobId, job.SubmittedAt, count), MapMemberPosition.AddToTop);
            ApplyStyle(layer, job.AnalysisType);
            Zoom(view, layer);
            return layer;
        });
    }

    /// <summary>Add the job's area as an outline-only layer below the results, so it can be switched off.</summary>
    public static async Task AddAoiAsync(string wkt, TrackedJob job)
    {
        var json = await QueuedTask.Run(() =>
        {
            var geometry = MapGeometry.FromWkt(wkt).ToJson();
            return "{\"geometryType\":\"esriGeometryPolygon\",\"spatialReference\":{\"wkid\":4326},"
                + "\"fields\":[{\"name\":\"job_id\",\"type\":\"esriFieldTypeString\",\"alias\":\"job_id\",\"length\":64}],"
                + $"\"features\":[{{\"geometry\":{geometry},\"attributes\":{{\"job_id\":\"{job.JobId}\"}}}}]}}";
        });
        var featureClass = await ConvertAsync(json, ".json", ResultNames.AoiFeatureClassName(job.AnalysisType, job.JobId));
        await QueuedTask.Run(() =>
        {
            var view = MapView.Active ?? throw new InvalidOperationException("no active map");
            var layer = CreateLayer(view.Map, featureClass,
                ResultNames.AoiLayerName(job.AnalysisType, job.JobId), MapMemberPosition.AddToBottom);
            layer.SetRenderer(new CIMSimpleRenderer { Symbol = AoiSymbol().MakeSymbolReference() });
        });
    }

    // --- conversion -------------------------------------------------------------

    /// <summary>JSON text → feature class in the default geodatabase, through the JSON To Features tool. Returns the feature class path.</summary>
    private static async Task<string> ConvertAsync(string json, string extension, string featureClassName)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sateais_{Guid.NewGuid():N}{extension}");
        await File.WriteAllTextAsync(path, json);
        try
        {
            var output = Path.Combine(Project.Current.DefaultGeodatabasePath, featureClassName);
            var arguments = Geoprocessing.MakeValueArray(path, output);
            var environment = Geoprocessing.MakeEnvironmentArray(overwriteoutput: true);
            // Not added to the map here (it goes under our group) and kept out of the history pane.
            var result = await Geoprocessing.ExecuteToolAsync(
                "conversion.JSONToFeatures", arguments, environment, null, null,
                GPExecuteToolFlags.GPThread | GPExecuteToolFlags.RefreshProjectItems);
            if (result.IsFailed)
            {
                throw new InvalidOperationException(string.Join(" ", result.Messages.Select(m => m.Text)));
            }
            return output;
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // left in the temp folder
            }
        }
    }

    // --- layers (MCT) -------------------------------------------------------------

    private static FeatureLayer? FindLayer(string featureClassName)
    {
        var map = MapView.Active?.Map;
        if (map is null)
        {
            return null;
        }
        foreach (var layer in map.GetLayersAsFlattenedList().OfType<FeatureLayer>())
        {
            try
            {
                using var table = layer.GetTable();
                if (table is not null && string.Equals(table.GetName(), featureClassName, StringComparison.OrdinalIgnoreCase))
                {
                    return layer;
                }
            }
            catch (Exception)
            {
                // a layer whose source cannot be opened is not one of ours
            }
        }
        return null;
    }

    private static FeatureLayer CreateLayer(ArcGIS.Desktop.Mapping.Map map, string featureClass, string name, MapMemberPosition position)
    {
        var group = map.GetLayersAsFlattenedList().OfType<GroupLayer>().FirstOrDefault(g => g.Name == ResultNames.GroupName)
            ?? LayerFactory.Instance.CreateGroupLayer(map, 0, ResultNames.GroupName);
        var parameters = new FeatureLayerCreationParams(new Uri(featureClass))
        {
            Name = name,
            MapMemberPosition = position,
        };
        return LayerFactory.Instance.CreateLayer<FeatureLayer>(parameters, group);
    }

    private static void Zoom(MapView view, FeatureLayer layer)
    {
        var extent = layer.QueryExtent();
        if (extent is null || extent.IsEmpty)
        {
            return;
        }
        Geometry framed = extent;
        var mapReference = view.Map.SpatialReference;
        if (mapReference is not null && extent.SpatialReference is not null && extent.SpatialReference.Wkid != mapReference.Wkid)
        {
            try
            {
                framed = GeometryEngine.Instance.Project(extent, mapReference);
            }
            catch (Exception)
            {
                // zoom to the untransformed extent rather than not at all
            }
        }
        view.ZoomTo(MapGeometry.Padded(framed, ZoomPaddingRatio), TimeSpan.FromMilliseconds(300));
    }

    // --- symbology -------------------------------------------------------------

    /// <summary>The richest renderer the result supports; a renderer that cannot be built falls back to one colour.</summary>
    private static void ApplyStyle(FeatureLayer layer, string analysisType)
    {
        CIMRenderer? renderer = null;
        try
        {
            renderer = ChangeRenderer(layer, analysisType);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"SateAIs: change ramp unavailable for {analysisType}: {e.Message}");
        }
        renderer ??= new CIMSimpleRenderer { Symbol = FlatSymbol(layer.ShapeType, analysisType).MakeSymbolReference() };
        layer.SetRenderer(renderer);
    }

    private static CIMSymbol FlatSymbol(esriGeometryType shape, string analysisType)
    {
        var style = StyleByType.GetValueOrDefault(analysisType, DefaultStyle);
        var color = Rgb(style.Color);
        switch (shape)
        {
            case esriGeometryType.esriGeometryPoint:
            case esriGeometryType.esriGeometryMultipoint:
                return PointSymbol(color, style.PointSize * MmToPt, White, 0.4 * MmToPt);
            case esriGeometryType.esriGeometryPolyline:
                return SymbolFactory.Instance.ConstructLineSymbol(color, 0.6 * MmToPt, SimpleLineStyle.Solid);
            default:
                return SymbolFactory.Instance.ConstructPolygonSymbol(
                    Rgb(style.Color, style.FillAlpha), SimpleFillStyle.Solid,
                    SymbolFactory.Instance.ConstructStroke(color, 0.6 * MmToPt, SimpleLineStyle.Solid));
        }
    }

    private static CIMPointSymbol PointSymbol(CIMColor fill, double size, CIMColor outline, double outlineWidth)
    {
        var symbol = SymbolFactory.Instance.ConstructPointSymbol(fill, size, SimpleMarkerStyle.Circle);
        if (symbol.SymbolLayers.FirstOrDefault() is CIMVectorMarker marker)
        {
            Outline(marker, fill, outline, outlineWidth);
        }
        return symbol;
    }

    /// <summary>Give a circle marker a contrasting outline.</summary>
    private static void Outline(CIMVectorMarker marker, CIMColor fill, CIMColor outline, double width)
    {
        if (marker.MarkerGraphics.FirstOrDefault()?.Symbol is CIMPolygonSymbol shape)
        {
            shape.SymbolLayers =
            [
                SymbolFactory.Instance.ConstructStroke(outline, width, SimpleLineStyle.Solid),
                SymbolFactory.Instance.ConstructSolidFill(fill),
            ];
        }
    }

    private static CIMRenderer? ChangeRenderer(FeatureLayer layer, string analysisType)
    {
        if (analysisType != AnalysisTypes.TimeSeries || layer.ShapeType != esriGeometryType.esriGeometryPolygon)
        {
            return null;
        }
        using var table = layer.GetTable();
        using var definition = table.GetDefinition();
        if (!definition.GetFields().Any(f => string.Equals(f.Name, DeviationField, StringComparison.OrdinalIgnoreCase)))
        {
            return null; // an older result schema: nothing to classify on
        }
        var classes = ChangeClasses.Select(c => new CIMUniqueValueClass
        {
            Label = c.Label,
            Values = [new CIMUniqueValue { FieldValues = [c.Key] }],
            Symbol = ChangeSymbol(c.Color, c.FillAlpha, c.Marker).MakeSymbolReference(),
            Visible = true,
        }).ToArray();
        return new CIMUniqueValueRenderer
        {
            ValueExpressionInfo = new CIMExpressionInfo
            {
                Title = "Change",
                Expression = ChangeExpression,
                ReturnType = ExpressionReturnType.String,
            },
            Groups = [new CIMUniqueValueGroup { Heading = "Change", Classes = classes }],
            // A cell without a readable deviation is still drawn, in the flat colour.
            UseDefaultSymbol = true,
            DefaultLabel = "Unclassified",
            DefaultSymbol = FlatSymbol(layer.ShapeType, analysisType).MakeSymbolReference(),
        };
    }

    private static CIMPolygonSymbol ChangeSymbol(string hex, int fillAlpha, bool withMarker)
    {
        var baseColor = Rgb(hex);
        if (fillAlpha <= 0)
        {
            return SymbolFactory.Instance.ConstructPolygonSymbol(
                Rgb(hex, 0), SimpleFillStyle.Null,
                SymbolFactory.Instance.ConstructStroke(Rgb(hex, 70), 0.1 * MmToPt, SimpleLineStyle.Solid));
        }
        // A white hairline rather than a darker shade: the backdrop is arbitrary imagery.
        var symbol = SymbolFactory.Instance.ConstructPolygonSymbol(
            Rgb(hex, fillAlpha), SimpleFillStyle.Solid,
            SymbolFactory.Instance.ConstructStroke(White, 0.2 * MmToPt, SimpleLineStyle.Solid));
        if (withMarker && SymbolFactory.Instance.ConstructMarker(baseColor, 2.6 * MmToPt, SimpleMarkerStyle.Circle) is CIMVectorMarker marker)
        {
            // A constant-size dot per cell keeps a handful of changed cells findable when zoomed out.
            Outline(marker, baseColor, White, 0.3 * MmToPt);
            marker.MarkerPlacement = new CIMMarkerPlacementPolygonCenter { Method = PlacementPolygonCenterMethod.CenterOfMass };
            symbol.SymbolLayers = [marker, .. symbol.SymbolLayers];
        }
        return symbol;
    }

    /// <summary>Quiet slate dashes, duller than any detection colour.</summary>
    private static CIMPolygonSymbol AoiSymbol() => SymbolFactory.Instance.ConstructPolygonSymbol(
        CIMColor.CreateRGBColor(148, 163, 184, 0), SimpleFillStyle.Null,
        SymbolFactory.Instance.ConstructStroke(CIMColor.CreateRGBColor(148, 163, 184), 0.5 * MmToPt, SimpleLineStyle.Dash));

    /// <summary>"#rrggbb" plus an alpha on the 0–255 scale to a CIM colour (whose alpha is a percentage).</summary>
    private static CIMColor Rgb(string hex, int alpha = 255)
    {
        var value = Convert.ToInt32(hex.TrimStart('#'), 16);
        return CIMColor.CreateRGBColor((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF, alpha * 100.0 / 255);
    }
}
