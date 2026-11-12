using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using SkiaSharp;

using HarfBuzzBuffer = HarfBuzzSharp.Buffer;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class FontStore : IDisposable
{
    private readonly Dictionary<string, FontFace> _faces = [];
    private readonly Dictionary<string, IReadOnlyList<FontFace>> _chains = [];
    private readonly Dictionary<string, FontFace?> _systemFaces = [];
    private readonly Dictionary<int, FontFace?> _systemFaceOfCharacter = [];
    private readonly Dictionary<(string Name, double Size), CssFont> _fonts = [];
    private readonly Dictionary<(CssFont Font, double Spacing, string Text), ShapedText> _shaped = [];
    private readonly HarfBuzzBuffer _buffer = new();
    public FontStore(IReadOnlyDictionary<string, IReadOnlyList<string>> families, Func<string, byte[]> readFile)
    {
        foreach (var (name, files) in families)
        {
            _chains[name] = [.. files.Select(file => Face(file, readFile))];
        }
    }

    public bool Has(string name)
    {
        return _chains.ContainsKey(name);
    }

    public CssFont Font(string name, double size)
    {
        if (!_fonts.TryGetValue((name, size), out var font))
        {
            font = new CssFont(_chains[name], (float)size);
            _fonts[(name, size)] = font;
        }

        return font;
    }

    public ShapedText Shape(string name, double size, double spacing, string text)
    {
        var font = Font(name, size);
        var key = (font, spacing, text);
        if (!_shaped.TryGetValue(key, out var shaped))
        {
            shaped = ShapeWithHarfBuzz(font, (float)spacing, text);
            _shaped[key] = shaped;
        }

        return shaped;
    }

    public void Dispose()
    {
        foreach (var shaped in _shaped.Values)
        {
            shaped.Blob?.Dispose();
        }

        _buffer.Dispose();
        foreach (var font in _fonts.Values)
        {
            font.Dispose();
        }

        foreach (var face in _faces.Values.Concat(_systemFaces.Values))
        {
            face?.Dispose();
        }
    }

    private FontFace Face(string file, Func<string, byte[]> readFile)
    {
        if (!_faces.TryGetValue(file, out var face))
        {
            face = FontFace.FromFile(readFile(file));
            _faces[file] = face;
        }

        return face;
    }

    private ShapedText ShapeWithHarfBuzz(CssFont font, float spacing, string text)
    {
        if (text.Length == 0)
        {
            return new ShapedText(null, 0);
        }

        using var builder = new SKTextBlobBuilder();
        var x = 0f;
        var start = 0;
        while (start < text.Length)
        {
            var face = FaceFor(font, text, start, out var length);
            var end = start + length;
            while (end < text.Length && FaceFor(font, text, end, out length) == face)
            {
                end += length;
            }

            _buffer.ClearContents();
            _buffer.AddUtf16(text, start, end - start);
            _buffer.GuessSegmentProperties();
            face.Font.Shape(_buffer);
            var glyphInfos = _buffer.GlyphInfos;
            var glyphPositions = _buffer.GlyphPositions;
            var scale = font.Size / face.UnitsPerEm;
            var run = builder.AllocatePositionedRun(font.SkiaFont(face), glyphInfos.Length);
            var glyphs = run.Glyphs;
            var points = run.Positions;
            for (var i = 0; i < glyphInfos.Length; i++)
            {
                glyphs[i] = (ushort)glyphInfos[i].Codepoint;
                points[i] = new SKPoint(x + (glyphPositions[i].XOffset * scale), -glyphPositions[i].YOffset * scale);
                x += (glyphPositions[i].XAdvance * scale) + spacing;
            }

            start = end;
        }

        return new ShapedText(builder.Build(), x);
    }

    private FontFace FaceFor(CssFont font, string text, int index, out int length)
    {
        Rune.DecodeFromUtf16(text.AsSpan(index), out var character, out length);
        return font.Chain.FirstOrDefault(face => face.Typeface.GetGlyph(character.Value) != 0) ?? SystemFace(character.Value) ?? font.Chain[0];
    }

    private FontFace? SystemFace(int character)
    {
        if (!_systemFaceOfCharacter.TryGetValue(character, out var face))
        {
            using var typeface = SKFontManager.Default.MatchCharacter(character);
            if (typeface is not null && !_systemFaces.TryGetValue(typeface.FamilyName, out face))
            {
                face = FontFace.FromSystem(typeface);
                _systemFaces[typeface.FamilyName] = face;
            }

            _systemFaceOfCharacter[character] = face;
        }

        return face;
    }
}
