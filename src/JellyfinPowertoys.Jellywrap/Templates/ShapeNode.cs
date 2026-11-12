using System;
using System.Collections.Generic;
using System.Linq;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class ShapeNode : Node
{
    private const double StarPoints = 5;
    private const double StarInnerRatio = 0.48;

    public string Kind { get; init; } = "rect";
    public SKPath? SvgPath { get; init; }

    public SKPath Geometry(double slideTime)
    {
        var x = Props.Number("x", slideTime);
        var y = Props.Number("y", slideTime);
        var width = Props.Number("width", slideTime);
        var height = Props.Number("height", slideTime);
        var anchor = Anchors.Of(Props.Text("anchor"), Kind is "rect" or "roundRect" or "arch" ? (0, 0) : (0.5, 0.5));
        var left = x - (anchor.X * width);
        var top = y - (anchor.Y * height);
        var path = new SKPath();
        switch (Kind)
        {
            case "rect":
                path.AddRect(SKRect.Create((float)left, (float)top, (float)width, (float)height));
                break;
            case "roundRect":
                path.RoundRect(left, top, width, height, Props.Number("radius", slideTime));
                break;
            case "circle":
                path.Circle(x, y, Props.Number("radius", slideTime));
                break;
            case "ellipse":
                {
                    var startAngle = Props.Number("startAngle", slideTime);
                    path.Arc(
                        x,
                        y,
                        Props.Number("rx", slideTime),
                        Props.Number("ry", slideTime),
                        startAngle,
                        Props.Number("endAngle", slideTime, startAngle + Degrees.FullTurn) - startAngle,
                        Props.Number("angle", slideTime));
                    break;
                }

            case "arc":
                AddArc(path, slideTime, x, y);
                break;
            case "star":
                {
                    var points = (int)Props.Number("points", slideTime, StarPoints);
                    var outerRadius = Props.Number("radius", slideTime);
                    var innerRadius = outerRadius * Props.Number("inner", slideTime, StarInnerRatio);
                    var firstAngle = (Props.Number("angle", slideTime) * Math.PI / 180) - (Math.PI / 2);
                    for (var i = 0; i < points * 2; i++)
                    {
                        var radius = i % 2 == 1 ? innerRadius : outerRadius;
                        var angle = firstAngle + (i * Math.PI / points);
                        AddPoint(path, x + (radius * Math.Cos(angle)), y + (radius * Math.Sin(angle)));
                    }

                    path.Close();
                    break;
                }

            case "polygon" or "polyline" or "line":
                {
                    var isLine = Kind == "line";
                    var points = isLine
                        ? [Props.Number("x1", slideTime), Props.Number("y1", slideTime), Props.Number("x2", slideTime), Props.Number("y2", slideTime)]
                        : Props.Lists.GetValueOrDefault("points") ?? [];
                    var offsetX = isLine ? 0 : x;
                    var offsetY = isLine ? 0 : y;
                    for (var i = 0; i + 1 < points.Length; i += 2)
                    {
                        AddPoint(path, points[i] + offsetX, points[i + 1] + offsetY);
                    }

                    if (Kind == "polygon")
                    {
                        path.Close();
                    }

                    break;
                }

            case "arch":
                AddArch(path, left, top, width, height, Math.Min(Props.Number("radius", slideTime), width / 2));
                break;

            case "path" when SvgPath is not null:
                path.AddPath(SvgPath, (float)x, (float)y);
                break;
        }

        if (Props.Text("fillRule") == "evenodd")
        {
            path.FillType = SKPathFillType.EvenOdd;
        }

        var trim = Props.Number("trim", slideTime, 1);
        if (trim < 1)
        {
            var trimmed = Trimmed(path, trim);
            path.Dispose();
            return trimmed;
        }

        return path;
    }

    protected override void DrawSelf(Painter painter, double slideTime, double x, double y)
    {
        using var path = Geometry(slideTime);
        if (Props.Paint("fill", slideTime) is { } fill)
        {
            painter.Canvas.DrawPath(path, painter.Fill(fill));
        }

        if (Props.Paint("stroke", slideTime) is { } stroke && Props.Number("strokeWidth", slideTime, 1) is var strokeWidth and > 0)
        {
            var paint = painter.Stroke(stroke, strokeWidth, StrokeStyle.Cap(Props.Text("lineCap")), StrokeStyle.Join(Props.Text("lineJoin")));
            var intervals = Props.Lists.GetValueOrDefault("dash")?.Select(interval => (float)interval).ToArray() ?? [];
            using var dash = StrokeStyle.Dash(intervals, (float)Props.Number("dashOffset", slideTime));
            paint.PathEffect = dash;
            painter.Canvas.DrawPath(path, paint);
            paint.PathEffect = null;
        }
    }

    private void AddArc(SKPath path, double slideTime, double x, double y)
    {
        var radius = Props.Number("radius", slideTime);
        var innerRadius = Props.Number("innerRadius", slideTime);
        var startAngle = Props.Number("startAngle", slideTime);
        var sweep = Props.Number("endAngle", slideTime, startAngle + Degrees.FullTurn) - startAngle;
        var isWedge = Props.Number("wedge", slideTime) != 0;
        if (isWedge && innerRadius <= 0)
        {
            path.MoveTo((float)x, (float)y);
        }

        path.Arc(x, y, radius, radius, startAngle, sweep);
        if (innerRadius > 0)
        {
            path.Arc(x, y, innerRadius, innerRadius, startAngle + sweep, -sweep);
            path.Close();
        }
        else if (isWedge)
        {
            path.Close();
        }
    }

    private static void AddArch(SKPath path, double left, double top, double width, double height, double radius)
    {
        path.MoveTo((float)left, (float)(top + (width / 2)));
        path.Arc(left + (width / 2), top + (width / 2), width / 2, width / 2, Degrees.FullTurn / 2, Degrees.FullTurn / 2);
        AddPoint(path, left + width, top + height - radius);
        path.ArcTo((float)(left + width), (float)(top + height), (float)(left + width - radius), (float)(top + height), (float)radius);
        path.ArcTo((float)left, (float)(top + height), (float)left, (float)(top + height - radius), (float)radius);
        path.Close();
    }

    private static SKPath Trimmed(SKPath path, double share)
    {
        using var measure = new SKPathMeasure(path, false);
        var trimmed = new SKPath();
        var remaining = share * ContourLengths(path).Sum();
        do
        {
            var length = measure.Length;
            measure.GetSegment(0, (float)Math.Clamp(remaining, 0, length), trimmed, true);
            remaining -= length;
        }
        while (remaining > 0 && measure.NextContour());

        return trimmed;
    }

    private static void AddPoint(SKPath path, double x, double y)
    {
        if (path.IsEmpty)
        {
            path.MoveTo((float)x, (float)y);
        }
        else
        {
            path.LineTo((float)x, (float)y);
        }
    }

    private static IEnumerable<double> ContourLengths(SKPath path)
    {
        using var measure = new SKPathMeasure(path, false);
        do
        {
            yield return measure.Length;
        }
        while (measure.NextContour());
    }
}
