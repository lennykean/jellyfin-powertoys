using System.Text;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class TextCase
{
    public static string Apply(string text, string mode)
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
            var cased = inUnit || character == UnitMarks.Close
                ? character
                : mode switch
                {
                    "upper" => char.ToUpperInvariant(character),
                    "lower" => char.ToLowerInvariant(character),
                    _ => wordStart ? char.ToUpperInvariant(character) : character,
                };
            changed.Append(cased);
            wordStart = char.IsWhiteSpace(character);
        }

        return changed.ToString();
    }
}
