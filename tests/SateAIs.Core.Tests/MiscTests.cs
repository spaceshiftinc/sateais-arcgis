using System.Text.RegularExpressions;
using System.Xml.Linq;
using SateAIs.Core.Credentials;
using SateAIs.Core.Geometry;
using Xunit;

namespace SateAIs.Core.Tests;

public class BoundingBoxTests
{
    [Fact]
    public void Wkt_is_a_closed_counter_clockwise_polygon()
    {
        var box = new BoundingBox(139.5, 35.2, 139.9, 35.6);
        Assert.Equal("POLYGON ((139.5 35.2, 139.9 35.2, 139.9 35.6, 139.5 35.6, 139.5 35.2))", box.ToWkt());
    }

    [Fact]
    public void Coordinates_are_rounded_and_trailing_zeros_dropped()
    {
        var wkt = new BoundingBox(139.123456789, 35.0, 140.0, 36.0).ToWkt();
        Assert.StartsWith("POLYGON ((139.123457 35, 140 35, 140 36, 139.123457 36", wkt);
        Assert.DoesNotContain("MULTI", wkt);
    }

    [Fact]
    public void Bounds_read_as_compass_points()
    {
        Assert.Equal("W 139.50000  S 35.20000  E 139.90000  N 35.60000  (WGS 84)",
            new BoundingBox(139.5, 35.2, 139.9, 35.6).DescribeBounds());
    }

    [Fact]
    public void Degenerate_and_out_of_world_boxes_are_detected()
    {
        Assert.True(new BoundingBox(1, 1, 1, 2).IsDegenerate);
        Assert.False(new BoundingBox(0, 0, 1, 1).IsDegenerate);
        Assert.False(new BoundingBox(-181, 0, 1, 1).IsWithinWorld);
        Assert.True(new BoundingBox(-180, -90, 180, 90).IsWithinWorld);
    }
}

public class ApiKeysTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sateais-tests", Guid.NewGuid().ToString("N"));

    public ApiKeysTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("""{"api_key": "sk_live_abc"}""", "sk_live_abc")]
    [InlineData("sk_live_bare\n", "sk_live_bare")]
    [InlineData("", null)]
    [InlineData("{not json", null)]
    [InlineData("""{"api_key": ""}""", null)]
    [InlineData("""{"other": "x"}""", null)]
    public void The_cli_credentials_file_is_read_in_both_forms(string content, string? expected)
    {
        var path = Path.Combine(_dir, "credentials");
        File.WriteAllText(path, content);
        Assert.Equal(expected, ApiKeys.ReadCredentialsFile(path));
    }

    [Fact]
    public void A_missing_file_is_no_key()
    {
        Assert.Null(ApiKeys.ReadCredentialsFile(Path.Combine(_dir, "nope")));
    }

    [Theory]
    [InlineData(null, "(not set)")]
    [InlineData("short", "***")]
    [InlineData("sk_live_abcdefghijkl1234", "sk_live_****1234")]
    public void Keys_are_masked_for_display(string? key, string expected)
    {
        Assert.Equal(expected, ApiKeys.Mask(key));
    }

    [Fact]
    public void The_credential_store_round_trips_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // the store only exists on Windows; the Windows CI job covers this
        }
        WindowsCredentials.Write("SateAIs.Test", "api_key", "sk_test_roundtrip");
        try
        {
            Assert.Equal("sk_test_roundtrip", WindowsCredentials.Read("SateAIs.Test", "api_key"));
        }
        finally
        {
            WindowsCredentials.Delete("SateAIs.Test", "api_key");
        }
        Assert.Null(WindowsCredentials.Read("SateAIs.Test", "api_key"));
    }
}

public class VersionTests
{
    [Fact]
    public void The_client_version_is_semver()
    {
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), ClientVersion.Current);
    }

    [Fact]
    public void The_client_version_is_the_one_in_the_build_props()
    {
        var props = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Directory.Build.props"));
        var declared = props.Descendants("Version").Single().Value;
        Assert.Equal(declared, ClientVersion.Current);
    }
}

public class ResultStatsTests
{
    private static string Collection(params string[] propertySets) =>
        """{"type":"FeatureCollection","features":[""" + string.Join(",", propertySets.Select(p => $$$"""{"type":"Feature","geometry":null,"properties":{{{{p}}}}}""")) + "]}";

    [Fact]
    public void Plain_detections_count_features()
    {
        Assert.Equal(3, ResultStats.CountDetections(Collection("", "", ""), "ship"));
    }

    [Fact]
    public void Timeseries_counts_significant_cells_only()
    {
        Assert.Equal(2, ResultStats.CountDetections(Collection("\"deviation\":0.1", "\"deviation\":0.0", "\"deviation\":-0.2"), "timeseries"));
        Assert.Equal(2, ResultStats.CountDetections(Collection("\"other\":1", "\"other\":2"), "timeseries"));
    }

    [Fact]
    public void Chart_series_are_stripped_from_timeseries_only()
    {
        var text = Collection("\"deviation\":0.1,\"upper_chart\":[1,2],\"lower_chart\":[3]", "\"deviation\":0.2");
        var (slim, removed) = ResultStats.StripHeavyProperties(text, "timeseries");
        Assert.Equal(2, removed);
        Assert.DoesNotContain("upper_chart", slim);
        Assert.Contains("deviation", slim);

        var (same, none) = ResultStats.StripHeavyProperties(text, "ship");
        Assert.Equal(0, none);
        Assert.Same(text, same);
    }
}
