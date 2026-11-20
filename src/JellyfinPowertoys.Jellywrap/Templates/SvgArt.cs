using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class SvgArt
{
    private const int ViewBoxNumbers = 4;
    private const int MatrixNumbers = 6;
    private const int MaxDepth = 64;
    private const int MaxOpacityLayers = 8;

    private static readonly Regex _numberPattern = new Regex(@"-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

    private static readonly Regex _transformPattern = new Regex(@"(\w+)\s*\(([^)]*)\)", RegexOptions.Compiled);

    private static readonly Style _initialStyle = new Style(SKColors.Black, null, 1, 1, 1, SKStrokeCap.Butt, SKStrokeJoin.Miter, SKPathFillType.Winding, [], 0);
    public static string ReadText(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes));
        var svg = reader.ReadToEnd();
        TextTemplate.CheckLength(svg.Length);
        return svg;
    }

    public static SKPicture? Load(string svg)
    {
        var root = XDocument.Parse(svg).Root;
        if (root?.Name.LocalName != "svg")
        {
            return null;
        }

        var viewBox = ViewBox(root);
        if (viewBox.Width <= 0 || viewBox.Height <= 0)
        {
            return null;
        }

        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(SKRect.Create(viewBox.Width, viewBox.Height));
        canvas.Translate(-viewBox.Left, -viewBox.Top);
        DrawChildren(canvas, root, Inherit(root, _initialStyle), 1, 0);
        return recorder.EndRecording();
    }

    private static SKRect ViewBox(XElement root)
    {
        var numbers = ParseNumbers(Attribute(root, "viewBox"));
        return numbers.Length == ViewBoxNumbers
            ? SKRect.Create(numbers[0], numbers[1], numbers[2], numbers[3])
            : SKRect.Create(ParseNumber(Attribute(root, "width"), 0), ParseNumber(Attribute(root, "height"), 0));
    }

    private static void DrawChildren(SKCanvas canvas, XElement parent, Style inherited, int depth, int opacityLayers)
    {
        if (depth > MaxDepth)
        {
            throw new FormatException($"The SVG nests elements deeper than {MaxDepth} levels.");
        }

        foreach (var element in parent.Elements())
        {
            if (Attribute(element, "display") == "none")
            {
                continue;
            }

            var style = Inherit(element, inherited);
            var saved = canvas.Save();
            if (Attribute(element, "transform") is { } transform)
            {
                var matrix = ParseTransform(transform);
                canvas.Concat(in matrix);
            }

            var layers = opacityLayers;
            if (ParseNumber(Attribute(element, "opacity"), 1) is var opacity and < 1)
            {
                // Each nested layer is a full offscreen surface when the picture is drawn.
                if (++layers > MaxOpacityLayers)
                {
                    throw new FormatException($"The SVG nests more than {MaxOpacityLayers} elements with an opacity.");
                }

                using var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(opacity, 0, 1) * byte.MaxValue)) };
                canvas.SaveLayer(layer);
            }

            if (element.Name.LocalName is "g" or "svg")
            {
                DrawChildren(canvas, element, style, depth + 1, layers);
            }
            else if (Geometry(element) is { } path)
            {
                using (path)
                {
                    DrawPath(canvas, path, style);
                }
            }

            canvas.RestoreToCount(saved);
        }
    }

    private static SKPath? Geometry(XElement element)
    {
        float Length(string name)
        {
            return ParseNumber(Attribute(element, name), 0);
        }

        var path = new SKPath();
        switch (element.Name.LocalName)
        {
            case "path":
                path.Dispose();
                return Attribute(element, "d") is { } data ? SKPath.ParseSvgPathData(data) : null;
            case "rect":
                var radiusX = Attribute(element, "rx") is { } ownRadiusX ? ParseNumber(ownRadiusX, 0) : Length("ry");
                var radiusY = Attribute(element, "ry") is { } ownRadiusY ? ParseNumber(ownRadiusY, 0) : radiusX;
                path.AddRoundRect(SKRect.Create(Length("x"), Length("y"), Length("width"), Length("height")), radiusX, radiusY);
                break;
            case "circle":
                path.AddCircle(Length("cx"), Length("cy"), Length("r"));
                break;
            case "ellipse":
                path.AddOval(new SKRect(Length("cx") - Length("rx"), Length("cy") - Length("ry"), Length("cx") + Length("rx"), Length("cy") + Length("ry")));
                break;
            case "line":
                path.MoveTo(Length("x1"), Length("y1"));
                path.LineTo(Length("x2"), Length("y2"));
                break;
            case "polyline" or "polygon":
                var coordinates = ParseNumbers(Attribute(element, "points"));
                var points = Enumerable.Range(0, coordinates.Length / 2).Select(i => new SKPoint(coordinates[2 * i], coordinates[(2 * i) + 1])).ToArray();
                path.AddPoly(points, element.Name.LocalName == "polygon");
                break;
            default:
                path.Dispose();
                return null;
        }

        return path;
    }

    private static void DrawPath(SKCanvas canvas, SKPath path, Style style)
    {
        path.FillType = style.FillRule;
        if (style.Fill is { } fill)
        {
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = Fade(fill, style.FillOpacity) };
            canvas.DrawPath(path, paint);
        }

        if (style.Stroke is { } stroke && style.StrokeWidth > 0)
        {
            using var dash = StrokeStyle.Dash(style.Dashes, style.DashOffset);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                Color = Fade(stroke, style.StrokeOpacity),
                StrokeWidth = style.StrokeWidth,
                StrokeCap = style.Cap,
                StrokeJoin = style.Join,
                PathEffect = dash,
            };
            canvas.DrawPath(path, paint);
        }
    }

    private static Style Inherit(XElement element, Style inherited)
    {
        return new Style(
            Attribute(element, "fill") is { } fill ? ColorOrNone(fill) : inherited.Fill,
            Attribute(element, "stroke") is { } stroke ? ColorOrNone(stroke) : inherited.Stroke,
            ParseNumber(Attribute(element, "stroke-width"), inherited.StrokeWidth),
            ParseNumber(Attribute(element, "fill-opacity"), inherited.FillOpacity),
            ParseNumber(Attribute(element, "stroke-opacity"), inherited.StrokeOpacity),
            Attribute(element, "stroke-linecap") is { } cap ? StrokeStyle.Cap(cap) : inherited.Cap,
            Attribute(element, "stroke-linejoin") is { } join ? StrokeStyle.Join(join) : inherited.Join,
            Attribute(element, "fill-rule") is { } rule ? FillRule(rule) : inherited.FillRule,
            Attribute(element, "stroke-dasharray") is { } dashes ? ParseNumbers(dashes) : inherited.Dashes,
            ParseNumber(Attribute(element, "stroke-dashoffset"), inherited.DashOffset));
    }

    private static SKPathFillType FillRule(string rule)
    {
        return rule == "evenodd" ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
    }

    private static SKMatrix ParseTransform(string list)
    {
        var matrix = SKMatrix.Identity;
        foreach (Match function in _transformPattern.Matches(list))
        {
            var numbers = ParseNumbers(function.Groups[2].Value);
            float Number(int index, float fallback)
            {
                return index < numbers.Length ? numbers[index] : fallback;
            }

            var next = function.Groups[1].Value switch
            {
                "matrix" when numbers.Length == MatrixNumbers => new SKMatrix(numbers[0], numbers[2], numbers[4], numbers[1], numbers[3], numbers[5], 0, 0, 1),
                "translate" => SKMatrix.CreateTranslation(Number(0, 0), Number(1, 0)),
                "scale" => SKMatrix.CreateScale(Number(0, 1), Number(1, Number(0, 1))),
                "rotate" => SKMatrix.CreateRotationDegrees(Number(0, 0), Number(1, 0), Number(2, 0)),
                "skewX" => SKMatrix.CreateSkew(MathF.Tan(Number(0, 0) * MathF.PI / 180), 0),
                "skewY" => SKMatrix.CreateSkew(0, MathF.Tan(Number(0, 0) * MathF.PI / 180)),
                _ => throw new FormatException($"Unknown SVG transform \"{function.Value}\"."),
            };
            matrix = matrix.PreConcat(next);
        }

        return matrix;
    }

    private static SKColor? ColorOrNone(string css)
    {
        return css.Trim() == "none" ? null : CssColor.Parse(css);
    }

    private static SKColor Fade(SKColor color, float opacity)
    {
        return color.WithAlpha((byte)Math.Round(color.Alpha * Math.Clamp(opacity, 0, 1)));
    }

    private static string? Attribute(XElement element, string name)
    {
        var declared = element.Attribute("style")?.Value
            .Split(';')
            .Select(declaration => declaration.Split(':', 2))
            .FirstOrDefault(declaration => declaration.Length == 2 && declaration[0].Trim() == name)?[1]
            .Trim();
        return declared ?? element.Attribute(name)?.Value;
    }

    private static float ParseNumber(string? text, float fallback)
    {
        return text is null ? fallback : float.Parse(text.Trim().TrimEnd('x').TrimEnd('p'), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static float[] ParseNumbers(string? text)
    {
        return text is null
            ? []
            : [.. _numberPattern.Matches(text).Select(match => float.Parse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture))];
    }

    private sealed record Style(
        SKColor? Fill,
        SKColor? Stroke,
        float StrokeWidth,
        float FillOpacity,
        float StrokeOpacity,
        SKStrokeCap Cap,
        SKStrokeJoin Join,
        SKPathFillType FillRule,
        float[] Dashes,
        float DashOffset);
}
