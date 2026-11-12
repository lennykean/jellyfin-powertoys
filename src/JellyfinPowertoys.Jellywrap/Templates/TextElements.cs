using System.Globalization;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class TextElements
{
    public static int Count(string text)
    {
        return StringInfo.ParseCombiningCharacters(text).Length;
    }

    public static string Take(string text, int count)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        var kept = int.Clamp(count, 0, starts.Length);
        return kept == starts.Length ? text : text[..starts[kept]];
    }
}
