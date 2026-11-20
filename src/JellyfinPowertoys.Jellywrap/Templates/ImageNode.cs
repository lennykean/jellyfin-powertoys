using System;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class ImageNode : Node
{
    public SKImage? Image { get; init; }
    public SKPicture? Art { get; init; }

    public ShapeNode? Shape { get; init; }

    public GroupNode? Fallback { get; init; }

    protected override void DrawSelf(Painter painter, double slideTime, double x, double y)
    {
        var width = Props.Number("width", slideTime);
        var height = Props.Number("height", slideTime);
        var anchor = Anchors.Of(Props.Text("anchor"), (0, 0));
        var left = x - (anchor.X * width);
        var top = y - (anchor.Y * height);
        var canvas = painter.Canvas;
        canvas.Save();
        if (Shape is null)
        {
            canvas.ClipRect(SKRect.Create((float)left, (float)top, (float)width, (float)height), SKClipOperation.Intersect, antialias: true);
        }
        else
        {
            using var clip = Shape.Geometry(slideTime);
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
        }

        var (sourceWidth, sourceHeight) = SourceSize();
        if (sourceWidth > 0 && sourceHeight > 0)
        {
            var full = SKRect.Create(sourceWidth, sourceHeight);
            var box = SKRect.Create((float)left, (float)top, (float)width, (float)height);
            var fit = Props.Text("fit");
            var scale = fit == "contain" ? Math.Min(width / sourceWidth, height / sourceHeight) : Math.Max(width / sourceWidth, height / sourceHeight);
            var (source, destination) = fit switch
            {
                "fill" => (full, box),
                "contain" => (full, SKRect.Create(
                    (float)(left + ((width - (sourceWidth * scale)) / 2)),
                    (float)(top + ((height - (sourceHeight * scale)) / 2)),
                    (float)(sourceWidth * scale),
                    (float)(sourceHeight * scale))),
                _ => (SKRect.Create(
                    (float)((sourceWidth - (width / scale)) / 2),
                    (float)((sourceHeight - (height / scale)) / 2),
                    (float)(width / scale),
                    (float)(height / scale)), box),
            };
            if (Image is not null)
            {
                painter.Image(Image, source, destination);
            }
            else
            {
                painter.Image(Art!, source, destination);
            }
        }
        else if (Fallback is not null)
        {
            canvas.Translate((float)left, (float)top);
            Fallback.Draw(painter, slideTime);
        }

        canvas.Restore();
        if (Props.Paint("borderColor", slideTime) is { } border && Props.Number("borderWidth", slideTime) is var borderWidth and > 0)
        {
            using var outline = Shape?.Geometry(slideTime) ?? Rect(left, top, width, height, Props.Number("borderOffset", slideTime));
            canvas.DrawPath(outline, painter.Stroke(border, borderWidth, join: StrokeStyle.Join(Props.Text("lineJoin"))));
        }
    }

    private (float Width, float Height) SourceSize()
    {
        if (Image is { } image)
        {
            return (image.Width, image.Height);
        }

        if (Art is { } art)
        {
            return (art.CullRect.Width, art.CullRect.Height);
        }

        return (0, 0);
    }

    private static SKPath Rect(double x, double y, double width, double height, double outset)
    {
        var path = new SKPath();
        path.AddRect(SKRect.Create((float)(x - outset), (float)(y - outset), (float)(width + (2 * outset)), (float)(height + (2 * outset))));
        return path;
    }
}
