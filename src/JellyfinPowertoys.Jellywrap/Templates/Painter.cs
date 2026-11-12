using System;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class Painter(SKCanvas canvas, FontStore fonts, ImageStore images) : IDisposable
{
    private const float MiterLimit = 10;

    private static readonly SKSamplingOptions _smooth = new(SKCubicResampler.Mitchell);

    private readonly SKPaint _skiaPaint = new();

    public SKCanvas Canvas { get; } = canvas;

    public FontStore Fonts { get; } = fonts;

    public ImageStore Images { get; } = images;

    public double Alpha { get; set; } = 1;
    public SKPaint Fill(Paint paint, double alpha = 1)
    {
        Reset(paint, alpha);
        return _skiaPaint;
    }

    public SKPaint Stroke(
        Paint paint,
        double width,
        SKStrokeCap cap = SKStrokeCap.Butt,
        SKStrokeJoin join = SKStrokeJoin.Miter,
        double alpha = 1)
    {
        Reset(paint, alpha);
        _skiaPaint.Style = SKPaintStyle.Stroke;
        _skiaPaint.StrokeWidth = (float)width;
        _skiaPaint.StrokeCap = cap;
        _skiaPaint.StrokeJoin = join;
        _skiaPaint.StrokeMiter = MiterLimit;
        return _skiaPaint;
    }

    public void Rect(SKColor color, double x, double y, double width, double height)
    {
        Canvas.DrawRect(SKRect.Create((float)x, (float)y, (float)width, (float)height), Fill(new Paint(color)));
    }

    public void Image(SKImage image, SKRect source, SKRect destination)
    {
        Reset(new Paint(SKColors.Black), 1);
        Canvas.DrawImage(image, source, destination, _smooth, _skiaPaint);
    }

    public void Image(SKPicture art, SKRect source, SKRect destination)
    {
        Reset(new Paint(SKColors.Black), 1);
        var saved = Canvas.Save();
        Canvas.ClipRect(destination, SKClipOperation.Intersect, antialias: true);
        Canvas.Translate(destination.Left, destination.Top);
        Canvas.Scale(destination.Width / source.Width, destination.Height / source.Height);
        Canvas.Translate(-source.Left, -source.Top);
        Canvas.DrawPicture(art, _skiaPaint);
        Canvas.RestoreToCount(saved);
    }

    public void Dispose()
    {
        _skiaPaint.Shader?.Dispose();
        _skiaPaint.Dispose();
    }

    private void Reset(Paint paint, double alpha)
    {
        var shader = _skiaPaint.Shader;
        _skiaPaint.Reset();
        shader?.Dispose();
        _skiaPaint.IsAntialias = true;
        var color = paint.Gradient is null ? paint.Color : SKColors.Black;
        _skiaPaint.ColorF = new SKColorF(
            color.Red / 255f,
            color.Green / 255f,
            color.Blue / 255f,
            (float)(color.Alpha / 255.0 * Alpha * alpha));
        _skiaPaint.Shader = paint.Gradient?.Shader();
    }
}
