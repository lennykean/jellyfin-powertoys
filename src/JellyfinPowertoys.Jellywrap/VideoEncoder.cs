using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using JellyfinPowertoys.Jellywrap.Audio;
using JellyfinPowertoys.Jellywrap.Templates;

using MediaBrowser.Controller.MediaEncoding;

using static System.FormattableString;

namespace JellyfinPowertoys.Jellywrap;

public sealed class VideoEncoder(IMediaEncoder mediaEncoder)
{
    private const string VideoFilter = "[0:v]scale=out_color_matrix=bt709:out_range=tv,format=yuv420p,"
        + "setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709:range=tv[v]";
    internal async Task EncodeAsync(
        Scene scene,
        IReadOnlyList<PlannedClip> clips,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var audio = AudioMix.Build(clips, scene.Duration);
        string[] audioMap = clips.Count > 0 ? ["-map", "[aout]"] : [];
        string[] audioCodec = clips.Count > 0 ? ["-c:a", "aac", "-b:a", "192k", "-ar", "48000"] : [];
        var arguments = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-nostats", "-y",
            "-f", "rawvideo", "-pix_fmt", "bgra", "-s", $"{Scene.Width}x{Scene.Height}", "-r", $"{SceneRenderer.FramesPerSecond}", "-i", "pipe:0",
        };
        arguments.AddRange(audio.Inputs);
        arguments.AddRange(
        [
            "-filter_complex", string.Join(';', audio.Filters.Prepend(VideoFilter)),
            "-map", "[v]", .. audioMap, "-map_chapters", "-1", "-map_metadata", "-1", "-t", Invariant($"{scene.Duration}"),
            "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-profile:v", "high", "-level", "4.0", "-g", "60",
            .. audioCodec,
            "-movflags", "+faststart", "-xerror", "-f", "mp4", outputPath,
        ]);
        using var ffmpeg = Process.Start(new ProcessStartInfo(mediaEncoder.EncoderPath, arguments)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var errors = ffmpeg.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            var allFramesWritten = await SceneRenderer.WriteFramesAsync(scene, ffmpeg.StandardInput.BaseStream, cancellationToken);
            if (allFramesWritten)
            {
                ffmpeg.StandardInput.Close();
            }

            await ffmpeg.WaitForExitAsync(cancellationToken);
            if (!allFramesWritten || ffmpeg.ExitCode != 0)
            {
                var reason = allFramesWritten ? $"exited with {ffmpeg.ExitCode}" : "stopped reading frames";
                throw new InvalidOperationException($"ffmpeg {reason}: {(await errors).Trim()}");
            }
        }
        catch
        {
            if (!ffmpeg.HasExited)
            {
                ffmpeg.Kill(entireProcessTree: true);
                await ffmpeg.WaitForExitAsync(CancellationToken.None);
            }

            File.Delete(outputPath);
            throw;
        }
    }
}
