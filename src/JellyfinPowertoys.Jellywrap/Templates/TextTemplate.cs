using System;
using System.Collections.Generic;
using System.Text;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class TextTemplate
{
    private const string HoleOpen = "{{";
    private const string HoleClose = "}}";

    // A text that names itself twice doubles each time it is used.
    private const int MaxLength = 1_000_000;

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

    public static void CheckLength(long length)
    {
        if (length > MaxLength)
        {
            throw new InvalidOperationException($"A text of the template is longer than {MaxLength} characters.");
        }
    }

    public string Render(Scope scope)
    {
        var result = new StringBuilder();
        foreach (var part in _parts)
        {
            var text = part is Expression expression ? BindingValue.Print(expression.Evaluate(scope)) : (string)part;
            CheckLength((long)result.Length + text.Length);
            result.Append(text);
        }

        return result.ToString();
    }
}
