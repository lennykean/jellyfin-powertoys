using System;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class UnitMarks
{
    public const char Open = '';
    public const char Close = '';

    public static string Mark(string unit)
    {
        return $"{Open}{unit}{Close}";
    }

    public static string Strip(string text)
    {
        return text.Replace(Open.ToString(), string.Empty, StringComparison.Ordinal).Replace(Close.ToString(), string.Empty, StringComparison.Ordinal);
    }
}
