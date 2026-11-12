using System;
using System.Collections.Generic;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class CssFont(IReadOnlyList<FontFace> chain, float size) : IDisposable
{
    private readonly Dictionary<FontFace, SKFont> _skiaFonts = [];

    public IReadOnlyList<FontFace> Chain { get; } = chain;

    public float Size { get; } = size;

    public SKFontMetrics Metrics => SkiaFont(Chain[0]).Metrics;

    public SKFont SkiaFont(FontFace face)
    {
        if (!_skiaFonts.TryGetValue(face, out var font))
        {
            font = new SKFont(face.Typeface, Size)
            {
                Subpixel = true,
                Edging = SKFontEdging.Antialias,
                Hinting = SKFontHinting.Slight,
            };
            _skiaFonts[face] = font;
        }

        return font;
    }

    public void Dispose()
    {
        foreach (var font in _skiaFonts.Values)
        {
            font.Dispose();
        }
    }
}
