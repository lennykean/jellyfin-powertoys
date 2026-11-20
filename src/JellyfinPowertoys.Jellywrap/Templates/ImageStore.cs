using System;
using System.Collections.Generic;
using System.IO;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class ImageStore(Func<string, byte[]?> readPackageFile, IReadOnlySet<string> contextImagePaths) : IDisposable
{
    public const int MaxMegapixels = 36;

    private const int MaxSide = 2 * Scene.Height;
    private const int BytesPerPixel = 4;

    // Every decoded image is kept for the whole video.
    private const long MaxDecodedBytes = 512L * 1024 * 1024;

    private static readonly SKSamplingOptions _downscale = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly Dictionary<string, SKImage?> _images = [];

    private readonly Dictionary<string, SKPicture> _art = [];
    private long _decodedBytes;

    public static bool IsTooLarge(byte[] file)
    {
        using var data = SKData.CreateCopy(file);
        using var encoded = SKImage.FromEncodedData(data);
        return encoded is not null && IsTooLarge(encoded);
    }

    public SKImage? Get(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (!_images.TryGetValue(key, out var image))
        {
            image = Decode(key);
            var pixels = image is null ? 0 : (long)image.Width * image.Height * BytesPerPixel;
            if (!Reserve(key, pixels + ((long)key.Length * sizeof(char)), image))
            {
                return null;
            }

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
                if (picture is not null)
                {
                    var bytesUsed = ((long)svg.Length * sizeof(char)) + picture.ApproximateBytesUsed;
                    if (!Reserve(key, bytesUsed, picture))
                    {
                        return null;
                    }

                    _art[svg] = picture;
                }
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
            picture.Dispose();
        }
    }

    private bool Reserve(string key, long bytes, IDisposable? image)
    {
        if (bytes <= MaxDecodedBytes - _decodedBytes)
        {
            _decodedBytes += bytes;
            return true;
        }

        image?.Dispose();
        if (contextImagePaths.Contains(key))
        {
            return false;
        }

        throw new InvalidOperationException($"The images of the video take more than {MaxDecodedBytes / (1024 * 1024)} MB of memory.");
    }

    private SKImage? Decode(string key)
    {
        try
        {
            var bytes = Read(key);
            using var data = bytes is null ? null : SKData.CreateCopy(bytes);
            using var encoded = data is null ? null : SKImage.FromEncodedData(data);
            if (encoded is not null && IsTooLarge(encoded))
            {
                return null;
            }

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
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException || contextImagePaths.Contains(key))
        {
            return null;
        }
    }

    // The image is decoded whole before it is scaled down.
    private static bool IsTooLarge(SKImage encoded)
    {
        return (long)encoded.Width * encoded.Height > MaxMegapixels * 1_000_000L;
    }

    private byte[]? Read(string key)
    {
        if (key.StartsWith(Template.PackagePrefix, StringComparison.Ordinal))
        {
            return readPackageFile(key);
        }

        if (!contextImagePaths.Contains(key))
        {
            throw new InvalidOperationException($"The image \"{key}\" is not a file of the template's package or an image of the wrap.");
        }

        return key.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? null : File.ReadAllBytes(key);
    }
}
