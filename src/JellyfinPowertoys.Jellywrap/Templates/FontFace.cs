using System;
using System.IO;

using HarfBuzzSharp;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class FontFace : IDisposable
{
    private readonly SKData _data;
    private readonly Blob _blob;
    private readonly Face _face;

    private FontFace(SKData data, SKTypeface typeface, int index)
    {
        _data = data;
        Typeface = typeface;

        _blob = new Blob(_data.Data, (int)_data.Size, MemoryMode.ReadOnly, null);
        _face = new Face(_blob, index);
        UnitsPerEm = _face.UnitsPerEm;
        Font = new Font(_face);
        Font.SetScale(UnitsPerEm, UnitsPerEm);
        Font.SetFunctionsOpenType();
    }

    public SKTypeface Typeface { get; }

    public Font Font { get; }

    public int UnitsPerEm { get; }
    public static FontFace FromFile(byte[] bytes)
    {
        return Load(SKData.CreateCopy(bytes), 0) ?? throw new InvalidDataException("A font file does not load.");
    }

    public static FontFace? FromSystem(SKTypeface typeface)
    {
        using var stream = typeface.OpenStream(out var index);
        return stream is null ? null : Load(SKData.Create(stream), index);
    }

    public void Dispose()
    {
        Font.Dispose();
        _face.Dispose();
        _blob.Dispose();
        Typeface.Dispose();
        _data.Dispose();
    }

    private static FontFace? Load(SKData data, int index)
    {
        var typeface = SKTypeface.FromData(data, index);
        if (typeface is null)
        {
            data.Dispose();
            return null;
        }

        return new FontFace(data, typeface, index);
    }
}
