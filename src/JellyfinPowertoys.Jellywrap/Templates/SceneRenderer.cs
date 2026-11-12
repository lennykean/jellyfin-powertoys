using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class SceneRenderer
{
    public const int FramesPerSecond = 30;

    private const int TitleCardJpegQuality = 90;

    private static readonly SKImageInfo _frameInfo = new(Scene.Width, Scene.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
    public static async Task<bool> WriteFramesAsync(Scene scene, Stream output, CancellationToken cancellationToken)
    {
        var frameCount = (int)Rounding.HalfUp(scene.Duration * FramesPerSecond);
        var pixels = GC.AllocateArray<byte>(_frameInfo.BytesSize, pinned: true);
        using var surface = SKSurface.Create(_frameInfo, Marshal.UnsafeAddrOfPinnedArrayElement(pixels, 0), _frameInfo.RowBytes);
        using var painter = new Painter(surface.Canvas, scene.Fonts, scene.Images);
        for (var frame = 0; frame < frameCount; frame++)
        {
            scene.Draw(painter, (double)frame / FramesPerSecond);
            surface.Flush();
            try
            {
                await output.WriteAsync(pixels, cancellationToken);
            }
            catch (IOException)
            {
                return false;
            }
        }

        return true;
    }

    public static void RenderTitleCard(Scene scene, string outputJpg)
    {
        using var surface = SKSurface.Create(_frameInfo);
        using var painter = new Painter(surface.Canvas, scene.Fonts, scene.Images);
        scene.Draw(painter, scene.CardAt);
        using var image = surface.Snapshot();
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, TitleCardJpegQuality);
        using var file = File.Create(outputJpg);
        jpeg.SaveTo(file);
    }
}
