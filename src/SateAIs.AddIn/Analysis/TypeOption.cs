namespace SateAIs.AddIn.Analysis;

/// <summary>One of the detection types in step ①.</summary>
public sealed class TypeOption(string id, string label, string color) : ObservableObject
{
    private bool _isChosen;

    public string Id { get; } = id;

    public string Label { get; } = label;

    /// <summary>The type's colour, the same in every SateAIs client.</summary>
    public string Color { get; } = color;

    public bool IsChosen
    {
        get => _isChosen;
        set => Set(ref _isChosen, value);
    }

    public static IReadOnlyList<TypeOption> All { get; } =
    [
        new("ship", "Ship detection", "#00d4ff"),
        new("oilslick", "Oil slick detection", "#a855f7"),
        new("newbuilding", "New building detection", "#10b981"),
        new("disappearbuilding", "Disappeared building detection", "#ef4444"),
        new("timeseries", "Time-series change", "#f59e0b"),
    ];
}
