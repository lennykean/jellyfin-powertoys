using System;
using System.Collections.Generic;
using System.Linq;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class PieNode : Node
{
    private const double TwelveOClock = -90;

    public IReadOnlyList<(double Value, Paint Fill)> Parts { get; init; } = [];

    protected override void DrawSelf(Painter painter, double slideTime, double x, double y)
    {
        var sum = Parts.Sum(part => part.Value);
        var radius = Props.Number("radius", slideTime);
        var startAngle = Props.Number("startAngle", slideTime, TwelveOClock);
        var revealed = Math.Clamp(Props.Number("sweep", slideTime, 1), 0, 1) * Degrees.FullTurn;
        var before = 0.0;
        foreach (var (value, fill) in Parts)
        {
            var wedgeStart = before / sum * Degrees.FullTurn;
            var wedgeEnd = Math.Min(revealed, (before + value) / sum * Degrees.FullTurn);
            before += value;
            if (wedgeEnd <= wedgeStart)
            {
                continue;
            }

            using var wedge = new SKPath();
            wedge.MoveTo((float)x, (float)y);
            wedge.Arc(x, y, radius, radius, startAngle + wedgeStart, wedgeEnd - wedgeStart);
            wedge.Close();
            painter.Canvas.DrawPath(wedge, painter.Fill(fill));
            if (Props.Paint("stroke", slideTime) is { } stroke)
            {
                painter.Canvas.DrawPath(wedge, painter.Stroke(stroke, Props.Number("strokeWidth", slideTime, 1), join: StrokeStyle.Join(Props.Text("lineJoin"))));
            }
        }
    }
}
