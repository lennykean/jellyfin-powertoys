using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class BindingValue
{
    public static bool Truthy(object? value)
    {
        return value switch
        {
            null => false,
            bool flag => flag,
            double number => number != 0 && !double.IsNaN(number),
            string text => text.Length > 0,
            List<object?> list => list.Count > 0,
            _ => true,
        };
    }

    public static double? Number(object? value)
    {
        return value switch
        {
            double number => number,
            bool flag => flag ? 1 : 0,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    public static string Print(object? value)
    {
        return value switch
        {
            null => string.Empty,
            double number => number.ToString(CultureInfo.InvariantCulture),
            bool flag => flag ? "true" : "false",
            string text => text,
            List<object?> list => Join(list, ", ", ", "),
            JsonObject item => item.ToJsonString(),
            _ => value.ToString() ?? string.Empty,
        };
    }

    public static string Join(IReadOnlyList<object?> values, string separator, string lastSeparator)
    {
        var result = new StringBuilder();
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                var between = i == values.Count - 1 ? lastSeparator : separator;
                TextTemplate.CheckLength((long)result.Length + between.Length);
                result.Append(between);
            }

            var text = Print(values[i]);
            TextTemplate.CheckLength((long)result.Length + text.Length);
            result.Append(text);
        }

        return result.ToString();
    }

    public static object? FromNode(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject item:
                return item;
            case JsonArray array:
                return array.Select(FromNode).ToList();
            default:
                var value = node.AsValue();
                return value.GetValueKind() switch
                {
                    JsonValueKind.Number => value.TryGetValue<double>(out var number) ? number : double.Parse(value.ToJsonString(), CultureInfo.InvariantCulture),
                    JsonValueKind.String => value.GetValue<string>(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null,
                };
        }
    }

    public static JsonNode? ToNode(object? value)
    {
        return value switch
        {
            null => null,
            double number => number,
            bool flag => flag,
            string text => text,
            JsonObject item => item.DeepClone(),
            List<object?> list => new JsonArray([.. list.Select(ToNode)]),
            _ => Print(value),
        };
    }

    public static object? Get(object? item, string path)
    {
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            item = item is JsonObject parent ? FromNode(parent[part]) : null;
        }

        return item;
    }

    public static int Compare(object? left, object? right)
    {
        if (Number(left) is { } leftNumber && Number(right) is { } rightNumber && left is not string && right is not string)
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return string.CompareOrdinal(Print(left), Print(right));
    }

    public static bool Same(object? left, object? right)
    {
        return (left, right) switch
        {
            (null, null) => true,
            (null, _) or (_, null) => false,
            (double leftNumber, double rightNumber) => leftNumber == rightNumber,
            (bool leftFlag, bool rightFlag) => leftFlag == rightFlag,
            (string leftText, string rightText) => leftText == rightText,
            _ => ReferenceEquals(left, right),
        };
    }

    public static bool Matches(object? left, string comparison, object? right)
    {
        if (comparison is "==" or "!=")
        {
            return Same(left, right) == (comparison == "==");
        }

        if (left is null || right is null)
        {
            return false;
        }

        var order = Compare(left, right);
        return comparison switch
        {
            "<" => order < 0,
            "<=" => order <= 0,
            ">" => order > 0,
            ">=" => order >= 0,
            _ => false,
        };
    }
}
