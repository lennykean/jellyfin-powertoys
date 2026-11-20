using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class Palette(JsonObject colors)
{
    private const char AlphaSeparator = '@';
    private const string NoPaint = "none";
    public const int PointNumbers = 2;
    public const int StopParts = 2;
    public SKColor Color(string name)
    {
        var alphas = new Stack<double>();
        var followed = new HashSet<string>();
        while (true)
        {
            var separator = name.LastIndexOf(AlphaSeparator);
            if (separator > 0)
            {
                alphas.Push(double.Parse(name[(separator + 1)..], CultureInfo.InvariantCulture));
                name = name[..separator];
            }
            else if (colors[name] is JsonValue aliased && aliased.GetValueKind() == JsonValueKind.String)
            {
                if (!followed.Add(name))
                {
                    throw new FormatException($"The palette color \"{name}\" names itself.");
                }

                name = aliased.GetValue<string>();
            }
            else
            {
                break;
            }
        }

        var color = CssColor.Parse(name);
        foreach (var alpha in alphas)
        {
            color = color.WithAlpha((byte)Math.Round(color.Alpha * alpha));
        }

        return color;
    }

    public Paint? Paint(JsonNode? node, Scope scope)
    {
        switch (node)
        {
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                {
                    var name = PropertyReader.ReadText(value, scope);
                    return string.IsNullOrEmpty(name) || name == NoPaint ? null : new Paint(Color(name));
                }

            case JsonObject gradient:
                {
                    var stops = (gradient["stops"] as JsonArray ?? [])
                        .OfType<JsonArray>()
                        .Select(stop => stop.Count >= StopParts
                            ? ((float)(PropertyReader.ReadNumber(stop[0], scope) ?? 0), Paint(stop[1], scope)?.Color ?? SKColors.Transparent)
                            : throw new FormatException("A gradient stop must be [offset, color]."))
                        .ToArray();
                    var repeat = PropertyReader.ReadNumber(gradient["repeat"], scope) ?? 1;
                    if (repeat > Gradient.MaxRepeat)
                    {
                        throw new FormatException($"A gradient's \"repeat\" must be at most {Gradient.MaxRepeat}, not {repeat}.");
                    }

                    float[] Point(string property)
                    {
                        var point = (gradient[property] as JsonArray ?? []).Select(coordinate => (float)(PropertyReader.ReadNumber(coordinate, scope) ?? 0)).ToArray();
                        return point.Length >= PointNumbers ? point : throw new FormatException($"A gradient needs \"{property}\": [x, y].");
                    }

                    return new Paint(
                        SKColors.Black,
                        new Gradient(
                            PropertyReader.IsString(gradient["type"], out var type) && type == "radial",
                            Point("from"),
                            Point("to"),
                            stops,
                            (int)repeat,
                            gradient["end"] is { } end ? Paint(end, scope)?.Color : null));
                }

            default:
                return null;
        }
    }
}
