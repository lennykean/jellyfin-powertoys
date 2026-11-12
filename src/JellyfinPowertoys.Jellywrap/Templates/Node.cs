using System;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal abstract class Node
{
    protected const double MinVisibleScale = 0.001;

    private const double MinVisibleBlur = 0.01;

    public string Id { get; init; } = string.Empty;

    public Props Props { get; init; } = new();
    public ShapeNode? Clip { get; set; }

    protected virtual bool Translates => false;

    public void Draw(Painter painter, double slideTime)
    {
        if (slideTime < Props.Number("start", slideTime, double.NegativeInfinity) || slideTime >= Props.Number("end", slideTime, double.PositiveInfinity))
        {
            return;
        }

        var opacity = Props.Number("opacity", slideTime, 1);
        var scale = Props.Number("scale", slideTime, 1);
        var scaleX = scale * Props.Number("scaleX", slideTime, 1);
        var scaleY = scale * Props.Number("scaleY", slideTime, 1);
        if (opacity <= 0 || Math.Abs(scaleX) <= MinVisibleScale || Math.Abs(scaleY) <= MinVisibleScale)
        {
            return;
        }

        var x = Props.Number("x", slideTime);
        var y = Props.Number("y", slideTime);
        var rotation = Props.Number("rotation", slideTime);
        var canvas = painter.Canvas;
        var outerAlpha = painter.Alpha;
        var saved = canvas.Save();
        painter.Alpha = outerAlpha * Math.Min(1, opacity);
        if (Translates)
        {
            canvas.Translate((float)x, (float)y);
        }

        if (rotation != 0 || scaleX != 1 || scaleY != 1)
        {
            var pivotX = Props.Number("pivotX", slideTime, Translates ? 0 : x);
            var pivotY = Props.Number("pivotY", slideTime, Translates ? 0 : y);
            canvas.Translate((float)pivotX, (float)pivotY);
            canvas.RotateDegrees((float)rotation);
            canvas.Scale((float)scaleX, (float)scaleY);
            canvas.Translate((float)-pivotX, (float)-pivotY);
        }

        if (Clip is not null)
        {
            using var clip = Clip.Geometry(slideTime);
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
        }

        var blur = Props.Number("blur", slideTime);
        if (blur > MinVisibleBlur)
        {
            using var filter = SKImageFilter.CreateBlur((float)blur / 2, (float)blur / 2);
            using var layer = new SKPaint { ImageFilter = filter };
            canvas.SaveLayer(layer);
        }

        DrawSelf(painter, slideTime, x, y);
        painter.Alpha = outerAlpha;
        canvas.RestoreToCount(saved);
    }

    protected abstract void DrawSelf(Painter painter, double slideTime, double x, double y);
}
