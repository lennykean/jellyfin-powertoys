using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class TemplateMerge
{
    public static JsonObject OntoBase(JsonObject baseTemplate, string baseId, IReadOnlyList<TemplatePackage> chain)
    {
        var merged = (JsonObject)baseTemplate.DeepClone();
        foreach (var package in chain.Reverse())
        {
            var layer = (JsonObject)package.Json!.DeepClone();
            var byBase = layer["byBase"]?[baseId]?.DeepClone() as JsonObject;
            layer.Remove("byBase");
            merged = (JsonObject)Merge(merged, layer)!;
            if (byBase is not null)
            {
                merged = (JsonObject)Merge(merged, byBase)!;
            }
        }

        var slides = merged["slides"] as JsonObject ?? [];
        foreach (var slide in slides.Select(slide => slide.Key).Where(slide => baseTemplate["slides"]?[slide] is null).ToList())
        {
            slides.Remove(slide);
        }

        merged.Remove("extends");
        return merged;
    }

    private static JsonNode? Merge(JsonNode? target, JsonNode? overlay)
    {
        if (target is JsonObject targetObject && overlay is JsonObject overlayObject)
        {
            var merged = (JsonObject)targetObject.DeepClone();

            foreach (var (key, value) in overlayObject)
            {
                var mergedValue = Merge(merged[key], value);
                if (mergedValue is null)
                {
                    merged.Remove(key);
                }
                else
                {
                    merged[key] = mergedValue;
                }
            }

            return merged;
        }

        if (target is JsonArray targetArray && overlay is JsonArray overlayArray && HasIds(targetArray) && HasIds(overlayArray))
        {
            return MergeById(targetArray, overlayArray);
        }

        return overlay?.DeepClone();
    }

    private static JsonArray MergeById(JsonArray target, JsonArray overlay)
    {
        var items = target.Select(item => (JsonNode?)item!.DeepClone()).ToList();
        int IndexOf(string id)
        {
            return items.FindIndex(item => item!["id"]?.GetValue<string>() == id);
        }

        foreach (var item in overlay.OfType<JsonObject>())
        {
            var index = IndexOf(item["id"]!.GetValue<string>());
            if (item["remove"]?.GetValue<bool>() == true)
            {
                if (index >= 0)
                {
                    items.RemoveAt(index);
                }

                continue;
            }

            if (index >= 0)
            {
                items[index] = Merge(items[index], item);
                continue;
            }

            var neighbor = item["before"]?.GetValue<string>() ?? item["after"]?.GetValue<string>();
            var neighborIndex = neighbor is null ? -1 : IndexOf(neighbor);
            var added = item.DeepClone();
            if (neighborIndex < 0)
            {
                items.Add(added);
            }
            else
            {
                items.Insert(item["before"] is not null ? neighborIndex : neighborIndex + 1, added);
            }
        }

        return new JsonArray([.. items]);
    }

    private static bool HasIds(JsonArray array)
    {
        return array.Count > 0 && array.All(item => item is JsonObject element && element["id"] is JsonValue);
    }
}
