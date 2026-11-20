namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class UnitMarks
{
    public const char Open = '';
    public const char Close = '';

    public static string Mark(string unit)
    {
        return $"{Open}{unit}{Close}";
    }
}
