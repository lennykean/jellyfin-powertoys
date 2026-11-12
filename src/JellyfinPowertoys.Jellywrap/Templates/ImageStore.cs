using System;
using System.Collections.Generic;
using System.IO;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class ImageStore(Func<string, byte[]?> readPackageFile) : IDisposable
{
    private const int MaxSide = 2 * Scene.Height;

    private static readonly SKSamplingOptions _downscale = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly Dictionary<string, SKImage?> _images = [];

    private readonly Dictionary<string, SKPicture?> _art = [];
    public SKImage? Get(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (!_images.TryGetValue(key, out var image))
        {
            image = Decode(key);
            _images[key] = image;
        }

        return image;
    }

    public SKPicture? Svg(string key, Func<string, string> fill)
    {
        try
        {
            var bytes = Read(key);
            if (bytes is null)
            {
                return null;
            }

            var svg = fill(SvgArt.ReadText(bytes));
            if (!_art.TryGetValue(svg, out var picture))
            {
                picture = SvgArt.Load(svg);
                _art[svg] = picture;
            }

            return picture;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        foreach (var image in _images.Values)
        {
            image?.Dispose();
        }

        foreach (var picture in _art.Values)
        {
            picture?.Dispose();
        }
    }

    private SKImage? Decode(string key)
    {
        try
        {
            var bytes = Read(key);
            using var data = bytes is null ? null : SKData.CreateCopy(bytes);
            using var encoded = data is null ? null : SKImage.FromEncodedData(data);
            if (encoded is null || Math.Max(encoded.Width, encoded.Height) <= MaxSide)
            {
                return encoded?.ToRasterImage(ensurePixelData: true);
            }

            var scale = (double)MaxSide / Math.Max(encoded.Width, encoded.Height);
            var size = new SKImageInfo(
                Math.Max(1, (int)Math.Round(encoded.Width * scale)),
                Math.Max(1, (int)Math.Round(encoded.Height * scale)),
                SKColorType.Rgba8888,
                SKAlphaType.Premul);
            using var surface = SKSurface.Create(size);
            surface.Canvas.DrawImage(encoded, SKRect.Create(size.Width, size.Height), _downscale);
            return surface.Snapshot();
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private byte[]? Read(string key)
    {
        return key.StartsWith(Template.PackagePrefix, StringComparison.Ordinal) ? readPackageFile(key) : File.ReadAllBytes(key);
    }
}
