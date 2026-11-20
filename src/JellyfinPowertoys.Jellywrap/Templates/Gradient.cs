using System;
using System.Linq;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed record Gradient(bool Radial, float[] From, float[] To, (float Offset, SKColor Color)[] Stops, int Repeat, SKColor? End)
{
    public const int MaxRepeat = 100;

    public SKShader Shader()
    {
        var bands = Math.Clamp(Repeat, 1, MaxRepeat);
        var stops = Enumerable.Range(0, bands)
            .SelectMany(band => Stops.Select(stop => (Offset: (band + stop.Offset) / bands, stop.Color)))
            .Concat(End is { } end ? [(1f, end)] : [])
            .ToArray();
        var colors = stops.Select(stop => stop.Color).ToArray();
        var offsets = stops.Select(stop => stop.Offset).ToArray();
        if (!Radial)
        {
            return SKShader.CreateLinearGradient(new SKPoint(From[0], From[1]), new SKPoint(To[0], To[1]), colors, offsets, SKShaderTileMode.Clamp);
        }

        var fromRadius = From.Length > 2 ? From[2] : 0;
        var toRadius = To.Length > 2 ? To[2] : 0;
        return SKShader.CreateTwoPointConicalGradient(
            new SKPoint(From[0], From[1]),
            fromRadius,
            new SKPoint(To[0], To[1]),
            toRadius,
            colors,
            offsets,
            SKShaderTileMode.Clamp);
    }
}
