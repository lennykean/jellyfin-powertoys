using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class PropertyReader
{
    public static object? ReadExpression(JsonNode? node, Scope scope)
    {
        if (!IsString(node, out var text))
        {
            return BindingValue.FromNode(node);
        }

        return TextTemplate.HasHoles(text) ? TextTemplate.Parse(text).Evaluate(scope) : Expression.Parse(text).Evaluate(scope);
    }

    public static bool ReadCondition(JsonNode? node, Scope scope)
    {
        return node is null || BindingValue.Truthy(ReadExpression(node, scope));
    }

    public static double? ReadNumber(JsonNode? node, Scope scope)
    {
        var number = node switch
        {
            JsonValue value when value.GetValueKind() == JsonValueKind.Number => value.GetValue<double>(),
            JsonValue value when value.GetValueKind() == JsonValueKind.String => BindingValue.Number(TextTemplate.Parse(value.GetValue<string>()).Evaluate(scope)),
            JsonValue value when value.GetValueKind() is JsonValueKind.True or JsonValueKind.False => value.GetValue<bool>() ? 1 : 0,
            _ => (double?)null,
        };
        return number is null || double.IsFinite(number.Value)
            ? number
            : throw new FormatException($"The number property {node!.ToJsonString()} is not finite.");
    }

    public static string ReadText(JsonNode node, Scope scope)
    {
        var text = node.GetValue<string>();
        return TextTemplate.HasHoles(text) ? TextTemplate.Parse(text).Render(scope) : text;
    }

    public static object? ReadValue(JsonNode? node, Scope scope)
    {
        if (!IsString(node, out var text))
        {
            return BindingValue.FromNode(node);
        }

        return TextTemplate.HasHoles(text) ? TextTemplate.Parse(text).Evaluate(scope) : text;
    }

    public static JsonObject ReadObject(JsonObject definition, Scope scope)
    {
        var item = new JsonObject();
        foreach (var (property, value) in definition)
        {
            item[property] = BindingValue.ToNode(ReadValue(value, scope));
        }

        return item;
    }

    public static JsonObject ResolveBindings(JsonObject definition, Scope scope)
    {
        var resolved = new JsonObject();
        foreach (var (property, value) in definition)
        {
            resolved[property] = IsString(value, out var text) && TextTemplate.HasHoles(text)
                ? BindingValue.ToNode(TextTemplate.Parse(text).Evaluate(scope))
                : value?.DeepClone();
        }

        return resolved;
    }

    public static void SetProperty(Props props, string property, JsonNode? node, Scope scope)
    {
        switch (node)
        {
            case JsonValue value when value.GetValueKind() == JsonValueKind.Number:
                props.Numbers[property] = value.GetValue<double>();
                break;
            case JsonValue value when value.GetValueKind() is JsonValueKind.True or JsonValueKind.False:
                props.Numbers[property] = value.GetValue<bool>() ? 1 : 0;
                break;
            case JsonArray array when array.All(item => item is JsonValue):
                props.Lists[property] = ReadNumbers(array, scope);
                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                var text = value.GetValue<string>();
                if (!TextTemplate.HasHoles(text))
                {
                    props.Texts[property] = text;
                    break;
                }

                switch (TextTemplate.Parse(text).Evaluate(scope))
                {
                    case double number when !double.IsFinite(number):
                        throw new FormatException($"The property {property} {node.ToJsonString()} is not finite.");
                    case double number:
                        props.Numbers[property] = number;
                        break;
                    case bool flag:
                        props.Numbers[property] = flag ? 1 : 0;
                        break;
                    case var other:
                        props.Texts[property] = BindingValue.Print(other);
                        break;
                }

                break;
        }
    }

    public static double[] ReadNumbers(JsonArray array, Scope scope)
    {
        return [.. array.Select(item => ReadNumber(item, scope) ?? 0)];
    }

    public static bool IsString(JsonNode? node, out string text)
    {
        var isString = node is JsonValue value && value.GetValueKind() == JsonValueKind.String;
        text = isString ? node!.GetValue<string>() : string.Empty;
        return isString;
    }
}
