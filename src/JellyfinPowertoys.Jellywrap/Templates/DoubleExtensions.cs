using System;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class DoubleExtensions
{
    public static double RoundHalfUp(this double value)
    {
        return Math.Floor(value + 0.5);
    }
}
