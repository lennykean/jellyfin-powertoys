using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

using SkiaSharp;

using HarfBuzzBuffer = HarfBuzzSharp.Buffer;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class TextShaper : IDisposable
{
    private const int ZeroWidthNonJoiner = 0x200C;
    private const int ZeroWidthJoiner = 0x200D;
    private const int FirstVariationSelector = 0xFE00;
    private const int LastVariationSelector = 0xFE0F;

    // Every shaped text is kept for the whole video, and fitting one long text shapes it once per character.
    private const long MaxShapedCharacters = 10_000_000;

    private readonly Dictionary<string, FontFace> _faces = [];
    private readonly Dictionary<string, IReadOnlyList<FontFace>> _chains = [];
    private readonly Dictionary<string, FontFace?> _systemFaces = [];
    private readonly Dictionary<int, FontFace?> _systemFaceOfCharacter = [];
    private readonly Dictionary<(string Name, double Size), SizedFontChain> _fonts = [];
    private readonly Dictionary<(SizedFontChain Font, double Spacing, string Text), ShapedText> _shaped = [];
    private readonly HarfBuzzBuffer _buffer = new HarfBuzzBuffer();
    private long _shapedCharacters;

    public void LoadFonts(string name, IEnumerable<string> files, Template template, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var chain = new List<FontFace>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_faces.TryGetValue(file, out var face))
            {
                var bytes = template.ReadFile(file) ?? throw new InvalidOperationException($"The font file {file} is missing.");
                face = FontFace.FromFile(bytes);
                _faces[file] = face;
            }

            chain.Add(face);
        }

        _chains[name] = chain;
    }

    public bool Has(string name)
    {
        return _chains.ContainsKey(name);
    }

    public SizedFontChain Font(string name, double size)
    {
        if (!_fonts.TryGetValue((name, size), out var font))
        {
            font = new SizedFontChain(_chains[name], (float)size);
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
            _shapedCharacters += text.Length;
            if (_shapedCharacters > MaxShapedCharacters)
            {
                throw new InvalidOperationException($"The template shapes more than {MaxShapedCharacters} characters of text.");
            }

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

    private ShapedText ShapeWithHarfBuzz(SizedFontChain font, float spacing, string text)
    {
        if (text.Length == 0)
        {
            return new ShapedText(null, 0);
        }

        text = text.Normalize(NormalizationForm.FormC);
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

    // One face draws a whole grapheme cluster, so that marks, joined emoji and keycaps stay together.
    private FontFace FaceFor(SizedFontChain font, string text, int index, out int length)
    {
        length = StringInfo.GetNextTextElementLength(text.AsSpan(index));
        var characters = text.Substring(index, length).EnumerateRunes().Select(rune => rune.Value).Where(character => !JoinsOrSelects(character)).ToList();
        if (font.Chain.FirstOrDefault(face => characters.All(character => Has(face, character))) is { } whole)
        {
            return whole;
        }

        var missing = characters.Where(character => !font.Chain.Any(face => Has(face, character))).Take(1).ToList();
        return (missing.Count > 0 ? SystemFace(missing[0]) : null)
            ?? font.Chain.FirstOrDefault(face => Has(face, characters[0]))
            ?? font.Chain[0];
    }

    private static bool Has(FontFace face, int character)
    {
        return face.Typeface.GetGlyph(character) != 0;
    }

    private static bool JoinsOrSelects(int character)
    {
        return character is ZeroWidthNonJoiner or ZeroWidthJoiner or (>= FirstVariationSelector and <= LastVariationSelector);
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
