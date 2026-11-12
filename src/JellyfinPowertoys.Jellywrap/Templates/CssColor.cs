using System;
using System.Globalization;
using System.Linq;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class CssColor
{
    private const int ShortHexLength = 3;
    private const int ShortHexWithAlphaLength = 4;
    private const int LongHexLength = 6;
    private const int LongHexWithAlphaLength = 8;
    private const int RgbNumbers = 3;
    public static SKColor Parse(string css)
    {
        var color = css.Trim();
        return color switch
        {
            "white" => SKColors.White,
            "black" => SKColors.Black,
            "transparent" => SKColors.Transparent,
            _ => Hex(color) ?? Rgb(color) ?? throw new FormatException($"Unknown color \"{css}\"."),
        };
    }

    private static SKColor? Hex(string color)
    {
        if (!color.StartsWith('#')
            || color.Length - 1 is not (ShortHexLength or ShortHexWithAlphaLength or LongHexLength or LongHexWithAlphaLength))
        {
            return null;
        }

        var hex = color.Length - 1 is ShortHexLength or ShortHexWithAlphaLength ? string.Concat(color[1..].Select(digit => $"{digit}{digit}")) : color[1..];
        if (!uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var bits))
        {
            return null;
        }

        return hex.Length == LongHexWithAlphaLength
            ? new SKColor((byte)(bits >> 24), (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits)
            : new SKColor((byte)(bits >> 16), (byte)(bits >> 8), (byte)bits);
    }

    private static SKColor? Rgb(string color)
    {
        var open = color.IndexOf('(', StringComparison.Ordinal);
        if (open < 0 || color[..open] is not ("rgb" or "rgba") || !color.EndsWith(')'))
        {
            return null;
        }

        var numbers = color[(open + 1)..^1]
            .Split(',')
            .Select(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : (double?)null)
            .ToArray();
        if (numbers.Length is not (RgbNumbers or RgbNumbers + 1) || numbers.Contains(null))
        {
            return null;
        }

        var alpha = numbers.Length > RgbNumbers ? Math.Clamp(numbers[RgbNumbers]!.Value, 0, 1) : 1;
        return new SKColor(Channel(numbers[0]!.Value), Channel(numbers[1]!.Value), Channel(numbers[2]!.Value), (byte)Math.Round(alpha * byte.MaxValue));
    }

    private static byte Channel(double value)
    {
        return (byte)Math.Round(Math.Clamp(value, 0, byte.MaxValue));
    }
}
