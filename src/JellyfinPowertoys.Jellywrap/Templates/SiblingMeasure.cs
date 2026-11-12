using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class SiblingMeasure(List<JsonObject> siblings, FontStore fonts, Func<JsonObject, Placement, Node?> buildElement)
{
    private readonly Dictionary<string, object?> _measured = [];
    private readonly HashSet<string> _measuring = [];

    public bool Has(string id)
    {
        return Find(siblings, id, null) is not null;
    }

    public object? Measure(string id, Placement placement)
    {
        if (_measured.TryGetValue(id, out var known) || !_measuring.Add(id))
        {
            return known;
        }

        var definition = Find(siblings, id, placement.Scope);
        var node = definition is null ? null : buildElement(definition, placement);
        var (width, size, lines) = node switch
        {
            TextNode text => (
                text.LaidOut!.Lines.Select(line => line.Width ?? text.Measure(fonts, line.Text, text.LaidOut.Size)).DefaultIfEmpty().Max(),
                text.LaidOut.Size,
                text.LaidOut.Lines.Count),
            OdometerNode odometer => (odometer.Width(fonts), odometer.Props.Number("size", 0), 1),
            null => (0.0, 0.0, 0),
            _ => (node.Props.Number("width", 0), 0.0, 1),
        };
        var result = node is null ? null : new JsonObject { ["width"] = width, ["size"] = size, ["lines"] = (double)lines };
        _measured[id] = result;
        _measuring.Remove(id);
        return result;
    }

    private static JsonObject? Find(IEnumerable<JsonObject> definitions, string id, Scope? scope)
    {
        foreach (var definition in definitions)
        {
            if (definition["id"]?.GetValue<string>() == id)
            {
                return definition;
            }

            if (definition["children"] is JsonArray children
                && (scope is null || PropertyReader.ReadCondition(definition["when"], scope))
                && Find(children.OfType<JsonObject>(), id, scope) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
