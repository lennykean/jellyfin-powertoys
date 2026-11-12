using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class EffectParameters(JsonObject parameters, Palette palette)
{
    public JsonNode? this[string key] => parameters[key];
    public SKColor Color(string key, string fallback)
    {
        return palette.Color(parameters[key] is { } name ? Text(name, key) : fallback);
    }

    public IReadOnlyList<SKColor> Colors(string key, params string[] fallback)
    {
        var names = parameters[key] is JsonArray { Count: > 0 } array ? [.. array.Select(name => Text(name, key))] : fallback;
        return [.. names.Select(palette.Color)];
    }

    public static double Number(JsonNode? node, string name)
    {
        return node is JsonValue value && value.GetValueKind() == JsonValueKind.Number
            ? value.GetValue<double>()
            : throw new FormatException($"\"{name}\" must be a number.");
    }

    public static string Text(JsonNode? node, string name)
    {
        return node is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : throw new FormatException($"\"{name}\" must be a text.");
    }
}
