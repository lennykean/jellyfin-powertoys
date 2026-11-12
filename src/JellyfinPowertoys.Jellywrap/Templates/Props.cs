using System.Collections.Generic;
using System.Linq;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class Props
{
    private static readonly HashSet<string> _paintProperties = ["color", "fill", "stroke", "boxFill", "boxStroke", "borderColor", "lineColor", "edgeColor"];

    public Dictionary<string, double> Numbers { get; } = [];

    public Dictionary<string, string> Texts { get; } = [];

    public Dictionary<string, double[]> Lists { get; } = [];

    public Dictionary<string, Paint> Paints { get; } = [];

    public Dictionary<string, List<Track>> Tracks { get; } = [];
    public double SlideStart { get; set; }
    public static bool IsPaint(string property)
    {
        return _paintProperties.Contains(property);
    }

    public bool Has(string property)
    {
        return Numbers.ContainsKey(property) || Tracks.ContainsKey(property);
    }

    public double Number(string property, double slideTime, double fallback = 0)
    {
        var number = Numbers.TryGetValue(property, out var value) ? value : fallback;
        if (!Tracks.TryGetValue(property, out var tracks))
        {
            return number;
        }

        var multiply = property is "opacity" or "scale" or "scaleX" or "scaleY";
        foreach (var track in tracks.Where(track => !track.Additive && !track.IsColor))
        {
            number = multiply ? number * track.ValueAt(slideTime, SlideStart) : track.ValueAt(slideTime, SlideStart);
        }

        foreach (var track in tracks.Where(track => track.Additive))
        {
            number += track.ValueAt(slideTime, SlideStart);
        }

        return number;
    }

    public string? Text(string property)
    {
        return Texts.GetValueOrDefault(property);
    }

    public Paint? Paint(string property, double slideTime)
    {
        var paint = Paints.GetValueOrDefault(property);
        var track = Tracks.GetValueOrDefault(property)?.LastOrDefault(track => track.IsColor);
        return track is null ? paint : new Paint(track.ColorAt(slideTime, SlideStart));
    }
}
