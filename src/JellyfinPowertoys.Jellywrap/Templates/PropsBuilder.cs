using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class PropsBuilder(Palette palette, TrackBuilder tracks)
{
    private static readonly HashSet<string> _structureKeys =
    [
        "id", "type", "when", "in", "out", "idle", "animate", "children", "template", "items", "vars", "clip", "fallback", "text",
        "src", "title", "words", "lead", "single", "parts", "shape", "effect", "remove", "before", "after", "box", "border", "layout",
        "where", "value", "pivot", "d", "points", "dash", "layer", "params",
    ];

    private static readonly string[] _groupKeys = ["box", "border"];
    public Props Build(JsonObject definition, Placement placement)
    {
        var props = new Props();
        Fill(props, definition, placement);
        return props;
    }

    public void Fill(Props props, JsonObject definition, Placement placement)
    {
        var scope = placement.Scope;
        props.SlideStart = placement.Start;
        foreach (var (key, value) in definition)
        {
            if (_structureKeys.Contains(key) && key is not ("layout" or "value"))
            {
                continue;
            }

            if (Props.IsPaint(key))
            {
                SetPaint(props, key, value, scope);
                continue;
            }

            PropertyReader.SetProperty(props, key, value, scope);
        }

        foreach (var group in _groupKeys)
        {
            foreach (var (key, value) in definition[group] as JsonObject ?? [])
            {
                var property = group + char.ToUpperInvariant(key[0]) + key[1..];
                if (Props.IsPaint(property))
                {
                    SetPaint(props, property, value, scope);
                }
                else
                {
                    PropertyReader.SetProperty(props, property, value, scope);
                }
            }
        }

        if (definition["pivot"] is JsonArray pivot)
        {
            props.Numbers["pivotX"] = PropertyReader.ReadNumber(pivot[0], scope) ?? 0;
            props.Numbers["pivotY"] = PropertyReader.ReadNumber(pivot[1], scope) ?? 0;
        }

        if (definition["dash"] is JsonArray dash)
        {
            props.Lists["dash"] = PropertyReader.ReadNumbers(dash, scope);
        }

        var delay = props.Numbers.GetValueOrDefault("delay");
        tracks.AddTracks(props, definition["in"], placement, placement.Shift + delay, fromEnd: false);
        tracks.AddTracks(props, definition["out"], placement, 0, fromEnd: true);
        tracks.AddTracks(props, definition["idle"], placement, 0, fromEnd: false);
        tracks.AddTracks(props, definition["animate"], placement, placement.Shift + delay, fromEnd: false);
    }

    private void SetPaint(Props props, string property, JsonNode? node, Scope scope)
    {
        if (palette.Paint(node, scope) is { } paint)
        {
            props.Paints[property] = paint;
        }
    }
}
