using System;
using System.Collections.Generic;
using System.Linq;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class TextTemplate
{
    private const string HoleOpen = "{{";
    private const string HoleClose = "}}";

    private readonly IReadOnlyList<object> _parts;

    private TextTemplate(IReadOnlyList<object> parts)
    {
        _parts = parts;
    }

    public bool IsSingleHole => _parts is [Expression];

    public static bool HasHoles(string text)
    {
        return text.Contains(HoleOpen, StringComparison.Ordinal);
    }

    public static TextTemplate Parse(string text)
    {
        var parts = new List<object>();
        var position = 0;
        while (position < text.Length)
        {
            var open = text.IndexOf(HoleOpen, position, StringComparison.Ordinal);
            if (open < 0)
            {
                parts.Add(text[position..]);
                break;
            }

            if (open > position)
            {
                parts.Add(text[position..open]);
            }

            var close = text.IndexOf(HoleClose, open + HoleOpen.Length, StringComparison.Ordinal);
            if (close < 0)
            {
                throw new FormatException($"In \"{text}\": \"{HoleClose}\" is missing.");
            }

            parts.Add(Expression.Parse(text[(open + HoleOpen.Length)..close]));
            position = close + HoleClose.Length;
        }

        return new TextTemplate(parts);
    }

    public object? Evaluate(Scope scope)
    {
        return IsSingleHole ? ((Expression)_parts[0]).Evaluate(scope) : Render(scope);
    }

    public string Render(Scope scope)
    {
        return string.Concat(_parts.Select(part => part is Expression expression ? BindingValue.Print(expression.Evaluate(scope)) : (string)part));
    }
}
