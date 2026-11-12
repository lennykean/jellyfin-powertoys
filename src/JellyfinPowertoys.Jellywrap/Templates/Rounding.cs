using System;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class Rounding
{
    public static double HalfUp(double value)
    {
        return Math.Floor(value + 0.5);
    }
}
