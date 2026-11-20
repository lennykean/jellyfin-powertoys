using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class StrokeStyle
{
    public static SKStrokeCap Cap(string? cap)
    {
        return cap switch
        {
            "round" => SKStrokeCap.Round,
            "square" => SKStrokeCap.Square,
            _ => SKStrokeCap.Butt,
        };
    }

    public static SKStrokeJoin Join(string? join)
    {
        return join switch
        {
            "round" => SKStrokeJoin.Round,
            "bevel" => SKStrokeJoin.Bevel,
            _ => SKStrokeJoin.Miter,
        };
    }

    public static SKPathEffect? Dash(float[] intervals, float offset)
    {
        if (intervals.Length == 0)
        {
            return null;
        }

        return SKPathEffect.CreateDash(intervals.Length % 2 == 0 ? intervals : [.. intervals, .. intervals], offset);
    }
}
