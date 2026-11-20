using System;
using System.Globalization;
using System.Text;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class StringExtensions
{
    public static int TextElementCount(this string text)
    {
        return StringInfo.ParseCombiningCharacters(text).Length;
    }

    public static string TakeTextElements(this string text, int count)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        var kept = int.Clamp(count, 0, starts.Length);
        return kept == starts.Length ? text : text[..starts[kept]];
    }

    public static string StripUnitMarks(this string text)
    {
        return text.Replace(UnitMarks.Open.ToString(), string.Empty, StringComparison.Ordinal).Replace(UnitMarks.Close.ToString(), string.Empty, StringComparison.Ordinal);
    }

    public static string ApplyCase(this string text, string mode)
    {
        if (mode is not ("upper" or "lower" or "title"))
        {
            return text;
        }

        var changed = new StringBuilder(text.Length);
        var inUnit = false;
        var wordStart = true;
        foreach (var character in text)
        {
            inUnit = character == UnitMarks.Open || (inUnit && character != UnitMarks.Close);
            var isUnit = inUnit || character == UnitMarks.Close;
            var cased = mode switch
            {
                _ when isUnit => character,
                "upper" => char.ToUpperInvariant(character),
                "lower" => char.ToLowerInvariant(character),
                _ when wordStart => char.ToUpperInvariant(character),
                _ => character,
            };
            changed.Append(cased);
            wordStart = char.IsWhiteSpace(character);
        }

        return changed.ToString();
    }
}
