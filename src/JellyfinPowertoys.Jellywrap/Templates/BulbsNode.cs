using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class BulbsNode(EffectParameters parameters) : Node
{
    public const int MaxCount = 2000;

    private const double Radius = 14;
    private const double DimAlpha = 0.3;
    private const double ChaseRate = 6;
    private const double ChasePeriod = 3;
    private const double Gap = 50;
    private const double Count = 2;
    private const double GlintOffset = 0.3;
    private const double GlintSize = 0.32;

    private readonly string _layout = parameters["layout"] is { } layout ? EffectParameters.Text(layout, "layout") : "line";
    private readonly IReadOnlyList<SKColor> _colors = parameters.Colors("colors", "#F5EBD7", "#E3B04B");
    private readonly SKColor _offColor = parameters.Color("offColor", "#E3B04B");
    private readonly SKColor _highlight = parameters.Color("highlight", "#FFFDF6");
    private readonly IReadOnlyList<(double From, double To)> _flashes = Flashes(parameters["flash"]);
    private readonly FlashRepeat? _flashEvery = parameters["flashEvery"] is JsonObject every ? FlashRepeat.Read(every) : null;

    protected override void DrawSelf(Painter painter, double slideTime, double x, double y)
    {
        var radius = Props.Number("radius", slideTime, Radius);
        var dim = Props.Number("dim", slideTime, DimAlpha);
        var chaseRate = Props.Number("chaseRate", slideTime, ChaseRate);
        var chasePeriod = (int)Props.Number("chasePeriod", slideTime, ChasePeriod);
        var chaseFrom = Props.Number("chaseFrom", slideTime, double.NegativeInfinity);
        var chaseInvert = Props.Number("chaseInvert", slideTime) != 0;
        var on = slideTime >= Props.Number("onAt", slideTime, double.NegativeInfinity);
        var flashing = _flashes.Any(flash => slideTime >= flash.From && slideTime < flash.To) || _flashEvery?.Holds(slideTime) == true;
        var points = Points(slideTime, x, y);
        var firstIndex = (int)Props.Number("index", slideTime);
        var canvas = painter.Canvas;
        var chaseStep = Math.Floor(slideTime * chaseRate);
        for (var i = 0; i < points.Count; i++)
        {
            var index = firstIndex + i;
            var (bulbX, bulbY) = points[i];
            var chased = ((((index - chaseStep) % chasePeriod) + chasePeriod) % chasePeriod) == 0;
            var lit = on && (flashing || slideTime <= chaseFrom || chased != chaseInvert);
            var color = on ? _colors[((index % _colors.Count) + _colors.Count) % _colors.Count] : _offColor;
            var alpha = lit ? 1 : dim;
            using var bulb = new SKPath();
            bulb.Circle(bulbX, bulbY, radius);
            canvas.DrawPath(bulb, painter.Fill(new Paint(color), alpha));
            using var glint = new SKPath();
            glint.Circle(bulbX - (radius * GlintOffset), bulbY - (radius * GlintOffset), radius * GlintSize);
            canvas.DrawPath(glint, painter.Fill(new Paint(_highlight), alpha));
        }
    }

    private static List<(double From, double To)> Flashes(JsonNode? flash)
    {
        return [.. (flash as JsonArray ?? [])
            .Select(window => window is JsonArray { Count: >= 2 } pair
                ? (EffectParameters.Number(pair[0], "flash"), EffectParameters.Number(pair[1], "flash"))
                : throw new FormatException("\"flash\" must be a list of [from, to]."))];
    }

    private List<(double X, double Y)> Points(double slideTime, double x, double y)
    {
        switch (_layout)
        {
            case "frame":
                {
                    var width = Props.Number("width", slideTime);
                    var height = Props.Number("height", slideTime);
                    var gap = Props.Number("gap", slideTime, Gap);
                    var across = gap > 0 ? (int)Math.Clamp((width / gap).RoundHalfUp(), 0, MaxCount) : 0;
                    var down = gap > 0 ? (int)Math.Clamp((height / gap).RoundHalfUp(), 0, MaxCount) : 0;
                    return
                    [
                        .. Enumerable.Range(0, across).Select(i => (x + (i * width / across), y)),
                        .. Enumerable.Range(0, down).Select(i => (x + width, y + (i * height / down))),
                        .. Enumerable.Range(0, across).Select(i => (x + width - (i * width / across), y + height)),
                        .. Enumerable.Range(0, down).Select(i => (x, y + height - (i * height / down))),
                    ];
                }

            case "single":
                return [(x, y)];
            default:
                {
                    var count = (int)Math.Clamp(Props.Number("count", slideTime, Count), 0, MaxCount);
                    var startX = Props.Number("x1", slideTime);
                    var endX = Props.Number("x2", slideTime);
                    return [.. Enumerable.Range(0, count).Select(i => (startX + ((endX - startX) * i / (count - 1)), y))];
                }
        }
    }

    private sealed record FlashRepeat(double Start, double Period, double Length, double Count)
    {
        public static FlashRepeat Read(JsonObject every)
        {
            return new FlashRepeat(
                EffectParameters.Number(every["start"], "flashEvery.start"),
                EffectParameters.Number(every["period"], "flashEvery.period"),
                EffectParameters.Number(every["length"], "flashEvery.length"),
                every["count"] is { } count ? EffectParameters.Number(count, "flashEvery.count") : double.PositiveInfinity);
        }

        public bool Holds(double slideTime)
        {
            var repeat = Math.Floor((slideTime - Start) / Period);
            return slideTime >= Start && repeat < Count && slideTime - Start - (repeat * Period) < Length;
        }
    }
}
