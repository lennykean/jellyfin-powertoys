namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class Anchors
{
    public static (double X, double Y) Of(string? anchor, (double X, double Y) fallback)
    {
        return anchor switch
        {
            "top-left" => (0, 0),
            "top" => (0.5, 0),
            "top-right" => (1, 0),
            "left" => (0, 0.5),
            "center" => (0.5, 0.5),
            "right" => (1, 0.5),
            "bottom-left" => (0, 1),
            "bottom" => (0.5, 1),
            "bottom-right" => (1, 1),
            _ => fallback,
        };
    }

    public static double Align(string? align)
    {
        return align switch
        {
            "center" => 0.5,
            "right" => 1,
            _ => 0,
        };
    }
}
