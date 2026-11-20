using System;
using System.Collections.Generic;
using System.Linq;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class TextNode : Node
{
    private const double WordPivotY = -0.32;

    public required string Font { get; init; }

    public required TextFit Fit { get; set; }

    public string Text { get; init; } = string.Empty;
    public TextTemplate? Template { get; init; }

    public Scope? Scope { get; init; }

    public double Value { get; init; }
    public bool FitOnFinal { get; init; }

    public string? Case { get; init; }
    public (string Text, Paint Paint)? Lead { get; init; }
    public IReadOnlyList<Props>? Words { get; set; }
    public LaidOutText? LaidOut { get; set; }
    public double Spacing(double size)
    {
        var tracking = Props.Number("tracking", 0);
        return tracking == 0 ? Props.Number("letterSpacing", 0) : Math.Sign(tracking) * Math.Floor(Math.Abs(size * tracking) + 0.5);
    }

    public double Measure(TextShaper shaper, string text, double size)
    {
        return shaper.Shape(Font, size, Spacing(size), text.StripUnitMarks()).Width;
    }

    public LaidOutText Layout(TextShaper shaper, string text)
    {
        return TextLayout.Layout(text, Fit, (measured, size) => Measure(shaper, measured, size));
    }

    public string Current(double slideTime)
    {
        if (Template is null || !Props.Tracks.ContainsKey("count"))
        {
            return Text;
        }

        var shown = Template.Render(Scope!.With("value", Value * Math.Clamp(Props.Number("count", slideTime, 1), 0, 1)));
        return shown.ApplyCase(Case ?? string.Empty);
    }

    protected override void DrawSelf(Painter painter, double slideTime, double x, double y)
    {
        var text = Current(slideTime);
        var laidOut = text == Text && LaidOut is not null ? LaidOut : Layout(painter.Shaper, text);
        if (FitOnFinal && LaidOut is not null && !ReferenceEquals(laidOut, LaidOut))
        {
            laidOut = new LaidOutText(LaidOut.Size, [new LaidOutLine(text, LaidOut.Lines[0].Y)]);
        }

        var isTyped = Props.Has("chars");
        var typed = isTyped
            ? (int)Math.Floor(Math.Clamp(Props.Number("chars", slideTime), 0, 1) * (laidOut.Lines.Sum(line => line.Text.TextElementCount() + 1) - 1))
            : 0;
        var color = Props.Paint("color", slideTime) ?? new Paint(SKColors.Black);
        var size = laidOut.Size;
        var hasBox = Props.Has("boxHeight");
        var boxShift = hasBox ? Props.Number("boxShift", slideTime) * size : 0;
        var wordIndex = 0;
        foreach (var line in laidOut.Lines)
        {
            var shown = line.Text;
            if (isTyped)
            {
                shown = line.Text.TakeTextElements(typed);
                typed -= line.Text.TextElementCount() + 1;
            }

            var width = line.Width ?? Measure(painter.Shaper, line.Text, size);
            var left = x - (Anchors.Align(Props.Text("align")) * width);
            var baseline = y + line.Y + boxShift;
            if (hasBox)
            {
                DrawBox(painter, slideTime, left, y + line.Y, width);
            }

            if (Words is not null)
            {
                foreach (var (word, wordX) in line.Words ?? WordsOf(painter.Shaper, line.Text, size))
                {
                    if (wordIndex < Words.Count)
                    {
                        DrawWord(painter, Words[wordIndex], slideTime, word, left + wordX, baseline, size, color);
                    }

                    wordIndex++;
                }

                continue;
            }

            if (line.Words is not null)
            {
                foreach (var (word, wordX) in line.Words)
                {
                    DrawRun(painter, word, left + wordX, baseline, size, color);
                }
            }
            else if (Lead is { } lead && shown.StartsWith(lead.Text, StringComparison.Ordinal))
            {
                DrawRun(painter, lead.Text, left, baseline, size, lead.Paint);
                DrawRun(painter, shown[lead.Text.Length..], left + Measure(painter.Shaper, lead.Text, size), baseline, size, color);
            }
            else
            {
                DrawRun(painter, shown, left, baseline, size, color);
            }
        }
    }

    private IEnumerable<(string Word, double X)> WordsOf(TextShaper shaper, string line, double size)
    {
        var words = line.Split(' ');
        return words.Select((word, index) => (word, index == 0 ? 0 : Measure(shaper, string.Join(' ', words[..index]) + " ", size)));
    }

    private void DrawWord(
        Painter painter,
        Props word,
        double slideTime,
        string text,
        double x,
        double baseline,
        double size,
        Paint color)
    {
        var opacity = word.Number("opacity", slideTime, 1);
        var scale = word.Number("scale", slideTime, 1);
        if (opacity <= 0 || scale <= MinVisibleScale)
        {
            return;
        }

        var width = Measure(painter.Shaper, text, size);
        var canvas = painter.Canvas;
        var outerAlpha = painter.Alpha;
        canvas.Save();
        painter.Alpha = outerAlpha * Math.Min(1, opacity);
        var pivotX = x + (width / 2);
        var pivotY = baseline + (word.Number("pivotY", slideTime, WordPivotY) * size);
        canvas.Translate((float)pivotX, (float)(pivotY + word.Number("y", slideTime)));
        canvas.Scale((float)scale);
        canvas.Translate((float)-pivotX, (float)-pivotY);
        DrawRun(painter, text, x, baseline, size, color);
        painter.Alpha = outerAlpha;
        canvas.Restore();
    }

    private void DrawBox(Painter painter, double slideTime, double left, double centerY, double width)
    {
        var paddingX = Props.Number("boxPadX", slideTime);
        var height = Props.Number("boxHeight", slideTime);
        using var box = new SKPath();
        box.RoundRect(left - paddingX, centerY - (height / 2), width + (2 * paddingX), height, Props.Number("boxRadius", slideTime, height / 2));
        if (Props.Paint("boxFill", slideTime) is { } fill)
        {
            painter.Canvas.DrawPath(box, painter.Fill(fill));
        }

        if (Props.Paint("boxStroke", slideTime) is { } stroke && Props.Number("boxStrokeWidth", slideTime) is var strokeWidth and > 0)
        {
            painter.Canvas.DrawPath(box, painter.Stroke(stroke, strokeWidth, join: SKStrokeJoin.Round));
        }
    }

    private void DrawRun(Painter painter, string text, double x, double baseline, double size, Paint color)
    {
        var shaped = painter.Shaper.Shape(Font, size, Spacing(size), text.StripUnitMarks());
        if (shaped.Blob is null)
        {
            return;
        }

        var baselineOffset = 0f;
        var baselineMode = Props.Text("baseline");
        if (baselineMode is "middle" or "top" or "bottom")
        {
            var font = painter.Shaper.Font(Font, size);
            var metrics = font.Metrics;
            var ascent = font.Size * -metrics.Ascent / (metrics.Descent - metrics.Ascent);
            var descent = font.Size - ascent;
            baselineOffset = baselineMode switch
            {
                "middle" => (ascent - descent) / 2,
                "top" => ascent,
                _ => -descent,
            };
        }

        painter.Canvas.DrawText(shaped.Blob, (float)x, (float)baseline + baselineOffset, painter.Fill(color));
    }
}
