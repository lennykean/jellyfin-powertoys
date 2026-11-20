using System;
using System.Collections.Generic;
using System.Linq;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class TextLayout
{
    private const string Ellipsis = "…";
    private const double MinSizeRatio = 0.5;
    private const double ShrinkFactor = 0.95;

    private const double WrapMinSize = 24;
    private const double BaselineBelowMiddle = 0.35;

    public static LaidOutText Layout(string text, TextFit fit, Func<string, double, double> measure)
    {
        switch (fit.Mode)
        {
            case "line":
                {
                    var (line, size) = FitLine(text, fit.Size, fit.MaxWidth, fit.MinSize, measure);
                    return SingleLine(line, size, fit.Y);
                }

            case "step":
                {
                    var size = StepSize(text, fit, measure);
                    return SingleLine(Ellipsize(text, size, fit.MaxWidth, measure), size, fit.Y);
                }

            case "lines":
                {
                    var (lines, size) = FitLines(text, fit.Size, fit.MaxWidth, fit.Lines, measure, fit.Shrink, fit.MinSize);
                    return StackedLines(lines, size, fit);
                }

            case "wraplines":
                return StackedLines(WrapLines(text, fit.Size, fit.MaxWidth, fit.Lines, measure), fit.Size, fit);
            case "title":
                return Title(text, fit, measure);
            case "box":
                return Box(text, fit, measure);
            case "block":
                {
                    var (line, size) = FitLine(text, fit.Size, fit.MaxWidth, null, measure);
                    return line == text && size >= fit.WrappedSize ? SingleLine(text, size, fit.Y) : Box(text, fit, measure);
                }

            case "wrap":
                return Wrap(text, fit, measure);
            case "split":
                return Split(text, fit, measure);
            default:
                return SingleLine(text, fit.Size, fit.Y);
        }
    }

    private static LaidOutText SingleLine(string text, double size, double y)
    {
        return new LaidOutText(size, [new LaidOutLine(text, y)]);
    }

    private static LaidOutText StackedLines(IReadOnlyList<string> lines, double size, TextFit fit)
    {
        var pitch = LinePitch(size, fit);
        var firstY = fit.LastLineY is { } lastLineY ? lastLineY - ((lines.Count - 1) * pitch) : fit.Y;
        return new LaidOutText(size, [.. lines.Select((line, index) => new LaidOutLine(line, firstY + (index * pitch)))]);
    }

    private static double LinePitch(double size, TextFit fit)
    {
        return fit.LinePitch ?? (fit.RoundLineHeight ? (size * fit.LineHeight).RoundHalfUp() : size * fit.LineHeight);
    }

    private static (string Text, double Size) FitLine(
        string text,
        double size,
        double maxWidth,
        double? minSize,
        Func<string, double, double> measure)
    {
        var width = measure(text, size);
        if (width <= maxWidth)
        {
            return (text, size);
        }

        var fitted = Math.Floor(size * maxWidth / width);
        var smallest = minSize ?? Math.Ceiling(size * MinSizeRatio);
        return fitted >= smallest ? (text, fitted) : (Ellipsize(text, smallest, maxWidth, measure), smallest);
    }

    private static double StepSize(string text, TextFit fit, Func<string, double, double> measure)
    {
        var smallest = fit.MinSize ?? Math.Ceiling(fit.Size / 2);
        var size = fit.Size;
        while (size > smallest && measure(text, size) > fit.MaxWidth)
        {
            size -= fit.Step;
        }

        return Math.Max(size, smallest);
    }

    private static string Ellipsize(string text, double size, double maxWidth, Func<string, double, double> measure)
    {
        if (measure(text, size) <= maxWidth)
        {
            return text;
        }

        var kept = text.TextElementCount();
        while (kept > 1 && measure(text.TakeTextElements(kept).TrimEnd() + Ellipsis, size) > maxWidth)
        {
            kept--;
        }

        return text.TakeTextElements(kept).TrimEnd() + Ellipsis;
    }

    private static List<string> WrapWords(string text, double size, double maxWidth, Func<string, double, double> measure)
    {
        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split(' '))
        {
            var longer = line.Length > 0 ? line + " " + word : word;
            if (line.Length > 0 && measure(longer, size) > maxWidth)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = longer;
            }
        }

        if (line.Length > 0)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static List<string> WrapLines(string text, double size, double maxWidth, int maxLines, Func<string, double, double> measure)
    {
        var lines = WrapWords(text, size, maxWidth, measure);
        if (lines.Count > maxLines)
        {
            var overflow = string.Join(' ', lines.Skip(maxLines - 1));
            lines.RemoveRange(maxLines - 1, lines.Count - (maxLines - 1));
            lines.Add(overflow);
        }

        return [.. lines.Select(line => Ellipsize(line, size, maxWidth, measure))];
    }

    private static (List<string> Lines, double Size) FitLines(
        string text,
        double startSize,
        double maxWidth,
        int maxLines,
        Func<string, double, double> measure,
        double? shrink,
        double? minSize)
    {
        var smallest = minSize ?? Math.Ceiling(startSize * MinSizeRatio);
        for (var size = startSize; ; size = Math.Max(smallest, shrink is { } step ? size - step : Math.Floor(size * ShrinkFactor)))
        {
            var lines = WrapWords(text, size, maxWidth, measure);
            if (lines.Count <= maxLines && lines.All(line => measure(line, size) <= maxWidth))
            {
                return (lines, size);
            }

            if (size <= smallest)
            {
                return (WrapLines(text, smallest, maxWidth, maxLines, measure), smallest);
            }
        }
    }

    private static LaidOutText Title(string text, TextFit fit, Func<string, double, double> measure)
    {
        var singleSize = Math.Min(fit.Size, Math.Floor(fit.Size * fit.MaxWidth / measure(text, fit.Size)));
        if (singleSize >= fit.WrappedSize)
        {
            return fit.Single is { } single ? Layout(text, single, measure) : SingleLine(text, singleSize, fit.Y);
        }

        var (lines, size) = FitLines(text, fit.WrappedSize, fit.MaxWidth, Math.Max(2, fit.Lines), measure, fit.Shrink, fit.WrappedMinSize);
        if (fit.Balance && lines.Count == 2 && string.Join(' ', lines) == text)
        {
            lines = NarrowestSplit(text, size, lines, lines.Max(line => measure(line, size)), measure);
        }

        if (lines.Count == 1)
        {
            return SingleLine(lines[0], size, fit.Y);
        }

        var pitch = LinePitch(size, fit);
        var lastLineY = fit.LastLineY ?? fit.Y;
        return new LaidOutText(size, [.. lines.Select((line, index) => new LaidOutLine(line, lastLineY - ((lines.Count - 1 - index) * pitch)))]);
    }

    private static List<string> NarrowestSplit(string text, double size, List<string> lines, double width, Func<string, double, double> measure)
    {
        var words = text.Split(' ');
        for (var i = 1; i < words.Length; i++)
        {
            var split = new List<string> { string.Join(' ', words[..i]), string.Join(' ', words[i..]) };
            var splitWidth = split.Max(line => measure(line, size));
            if (splitWidth < width)
            {
                (lines, width) = (split, splitWidth);
            }
        }

        return lines;
    }

    private static LaidOutText Box(string text, TextFit fit, Func<string, double, double> measure)
    {
        var smallest = Math.Ceiling(fit.Size * fit.MinSizeRatio);
        var (lines, size) = (new List<string>(), smallest);
        var found = false;
        for (var candidate = fit.Size; candidate > smallest && !found; candidate--)
        {
            var wrapped = WrapLines(text, candidate, fit.MaxWidth, int.MaxValue, measure);
            if (wrapped.Count * candidate * fit.LineHeight <= fit.MaxHeight && string.Join(' ', wrapped) == text)
            {
                (lines, size, found) = (wrapped, candidate, true);
            }
        }

        if (!found)
        {
            lines = WrapLines(text, smallest, fit.MaxWidth, Math.Max(1, (int)Math.Floor(fit.MaxHeight / (smallest * fit.LineHeight))), measure);
        }

        var pitch = size * fit.LineHeight;
        var firstY = fit.Y - (fit.Shift * (fit.Size - size)) - (fit.VerticalAlign == "middle" ? (lines.Count - 1) * pitch / 2 : 0);
        return new LaidOutText(size, [.. lines.Select((line, index) => new LaidOutLine(line, firstY + (index * pitch)))]);
    }

    private static LaidOutText Wrap(string text, TextFit fit, Func<string, double, double> measure)
    {
        var smallest = Math.Max(fit.MinSize ?? WrapMinSize, fit.Size / 2);
        var size = fit.Size;
        var lines = WordLines(text, size, fit.MaxWidth, measure);
        bool Overflows(List<LaidOutLine> candidate)
        {
            return candidate.Count > fit.Lines || candidate.Any(line => line.Width > fit.MaxWidth);
        }

        while (Overflows(lines) && size - fit.Step >= smallest)
        {
            size -= fit.Step;
            lines = WordLines(text, size, fit.MaxWidth, measure);
        }

        if (Overflows(lines))
        {
            lines = [.. WrapLines(text, size, fit.MaxWidth, fit.Lines, measure).SelectMany(line => WordLines(line, size, double.PositiveInfinity, measure))];
        }

        var pitch = fit.LinePitch ?? (fit.LineHeight * size);
        var firstY = fit.VerticalAlign == "middle" ? fit.Y - ((lines.Count - 1) * pitch / 2) + (BaselineBelowMiddle * size) : fit.Y;
        return new LaidOutText(size, [.. lines.Select((line, index) => line with { Y = firstY + (index * pitch) })]);
    }

    private static List<LaidOutLine> WordLines(string text, double size, double maxWidth, Func<string, double, double> measure)
    {
        var spaceWidth = measure(" ", size);
        var lines = new List<LaidOutLine>();
        var words = new List<(string Word, double X)>();
        var lineWidth = 0.0;
        foreach (var word in text.Split(' '))
        {
            var wordWidth = measure(word, size);
            if (words.Count > 0 && lineWidth + spaceWidth + wordWidth > maxWidth)
            {
                lines.Add(new LaidOutLine(string.Join(' ', words.Select(placed => placed.Word)), 0, words, lineWidth));
                words = [];
                lineWidth = 0;
            }

            var x = words.Count == 0 ? 0 : lineWidth + spaceWidth;
            words.Add((word, x));
            lineWidth = x + wordWidth;
        }

        lines.Add(new LaidOutLine(string.Join(' ', words.Select(placed => placed.Word)), 0, words, lineWidth));
        return lines;
    }

    private static LaidOutText Split(string text, TextFit fit, Func<string, double, double> measure)
    {
        List<string> lines;
        var breakIndex = fit.BreakAt is null ? -1 : text.IndexOf(fit.BreakAt, StringComparison.Ordinal);
        if (breakIndex >= 0)
        {
            lines = [text[..breakIndex] + fit.BreakAt!.TrimEnd(), text[(breakIndex + fit.BreakAt!.Length)..]];
        }
        else
        {
            lines = NarrowestSplit(text, fit.Size, [text], double.PositiveInfinity, measure);
        }

        var size = lines.Min(line => StepSize(line, fit, measure));
        if (lines.Count == 1 && fit.Single is { } single)
        {
            return SingleLine(Ellipsize(lines[0], size, fit.MaxWidth, measure), size, single.Y);
        }

        var pitch = fit.LinePitch ?? (fit.LineHeight * size);
        return new LaidOutText(size, [.. lines.Select((line, index) => new LaidOutLine(Ellipsize(line, size, fit.MaxWidth, measure), fit.Y + (index * pitch)))]);
    }
}
