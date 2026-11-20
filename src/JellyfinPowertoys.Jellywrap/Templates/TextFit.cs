using System;
using System.Text.Json.Nodes;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed record TextFit
{
    private const double DefaultSize = 40;
    private const double DefaultLineHeight = 1.1;
    private const double BoxLineHeight = 1.15;
    private const double WrapLineHeight = 1.12;
    private const double DefaultStep = 2;
    private const double WrapStep = 4;
    private const double DefaultMinSizeRatio = 0.5;
    private const double BlockMinSizeRatio = 1 / 3.0;
    private const double MaxSize = 2000;
    private const double MinStep = 0.5;
    public required string Mode { get; init; }

    public double Size { get; init; }

    public double MaxWidth { get; init; }

    public double MaxHeight { get; init; }

    public double? MinSize { get; init; }
    public double WrappedSize { get; init; }

    public double? WrappedMinSize { get; init; }

    public int Lines { get; init; }
    public double LineHeight { get; init; }

    public double? LinePitch { get; init; }

    public bool RoundLineHeight { get; init; }
    public double Step { get; init; }
    public double? Shrink { get; init; }

    public bool Balance { get; init; }
    public double MinSizeRatio { get; init; }
    public double Shift { get; init; }

    public required string VerticalAlign { get; init; }
    public double Y { get; init; }
    public double? LastLineY { get; init; }
    public string? BreakAt { get; init; }
    public TextFit? Single { get; init; }

    public static TextFit FromProps(Props props, JsonObject? single, Scope scope, double originY)
    {
        double? Number(string property)
        {
            return props.Numbers.TryGetValue(property, out var number) ? number : null;
        }

        // The fit loops walk from a size down to a smallest size in steps, so these limits bound them.
        double? Limited(string property, double min, double max)
        {
            if (Number(property) is not { } number || (number >= min && number <= max))
            {
                return Number(property);
            }

            var range = double.IsPositiveInfinity(max) ? $"at least {min}" : $"from {min} to {max}";
            throw new InvalidOperationException($"A text's \"{property}\" must be {range}, not {number}.");
        }

        TextFit? singleFit = null;
        if (single is not null)
        {
            var singleProps = new Props();
            foreach (var (property, number) in props.Numbers)
            {
                singleProps.Numbers[property] = number;
            }

            foreach (var (property, text) in props.Texts)
            {
                singleProps.Texts[property] = text;
            }

            foreach (var (property, value) in single)
            {
                PropertyReader.SetProperty(singleProps, property, value, scope);
            }

            singleFit = FromProps(singleProps, null, scope, originY);
        }

        var mode = props.Text("fit");
        var defaultLineHeight = mode switch
        {
            "box" => BoxLineHeight,
            "wrap" => WrapLineHeight,
            _ => DefaultLineHeight,
        };
        return new TextFit
        {
            Mode = mode ?? "none",
            Size = Limited("size", 0, MaxSize) ?? DefaultSize,
            MaxWidth = Number("maxWidth") ?? double.PositiveInfinity,
            MaxHeight = Number("maxHeight") ?? double.PositiveInfinity,
            MinSize = Limited("minSize", 0, MaxSize),
            WrappedSize = Limited("size2", 0, MaxSize) ?? 0,
            WrappedMinSize = Limited("min2", 0, MaxSize),
            Lines = (int)(Limited("lines", 1, int.MaxValue) ?? 1),
            LineHeight = Number("lineHeight") ?? defaultLineHeight,
            LinePitch = Number("linePitch"),
            RoundLineHeight = (Number("roundLineHeight") ?? 1) != 0,
            Step = Limited("step", MinStep, double.PositiveInfinity) ?? (mode is "wrap" ? WrapStep : DefaultStep),
            Shrink = Limited("shrink", MinStep, double.PositiveInfinity),
            Balance = (Number("balance") ?? 1) != 0,
            MinSizeRatio = Limited("minRatio", 0, double.PositiveInfinity) ?? (mode is "block" ? BlockMinSizeRatio : DefaultMinSizeRatio),
            Shift = Number("shift") ?? 0,
            VerticalAlign = props.Text("valign") ?? (mode is "block" ? "middle" : "top"),
            Y = (Number("y") ?? originY) - originY,
            LastLineY = Number("y2") is { } lastLineY ? lastLineY - originY : null,
            BreakAt = props.Text("breakAt"),
            Single = singleFit,
        };
    }
}
