using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using JellyfinPowertoys.Jellywrap.Audio;
using JellyfinPowertoys.Jellywrap.Templates;

using MediaBrowser.Controller.MediaEncoding;

using Microsoft.Extensions.Logging;

using static System.FormattableString;

namespace JellyfinPowertoys.Jellywrap;

public sealed class VideoEncoder(IMediaEncoder mediaEncoder, ILogger<VideoEncoder> logger)
{
    private const string VideoFilter = "[0:v]scale=out_color_matrix=bt709:out_range=tv,format=yuv420p,"
        + "setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709:range=tv[v]";
    private const double MaxMixShortfall = 0.5;
    private const double MaxEncodeSecondsPerVideoSecond = 30;

    private static readonly TimeSpan _mixTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _encodeStartTimeout = TimeSpan.FromMinutes(1);

    internal async Task<bool> EncodeAsync(
        Scene scene,
        IReadOnlyList<PlannedClip> clips,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var mixPath = Path.ChangeExtension(outputPath, ".pcm");
        try
        {
            var mixed = clips.Count > 0 && await MixAsync(clips, scene.Duration, mixPath, outputPath, cancellationToken);
            await EncodeAsync(scene, mixed ? mixPath : null, outputPath, cancellationToken);
            return mixed;
        }
        finally
        {
            File.Delete(mixPath);
        }
    }

    private async Task<bool> MixAsync(
        IReadOnlyList<PlannedClip> clips,
        double duration,
        string mixPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await mediaEncoder.RunFfmpegAsync(AudioMix.Arguments(clips, duration, mixPath), _mixTimeout, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "The Jellywrap video {Path} has no sound: ffmpeg cannot mix its music", outputPath);
            return false;
        }

        // A clip is cut on whole audio frames of its file, so a complete mix can end a little early.
        var mixSeconds = AudioMix.Seconds(new FileInfo(mixPath).Length);
        if (mixSeconds < duration - MaxMixShortfall)
        {
            logger.LogWarning(
                "The Jellywrap video {Path} has no sound: its music ends after {Seconds:F1} s of the {Length:F1} s video",
                outputPath,
                mixSeconds,
                duration);
            return false;
        }

        return true;
    }

    private async Task EncodeAsync(Scene scene, string? mixPath, string outputPath, CancellationToken cancellationToken)
    {
        var audioInput = mixPath is null ? Array.Empty<string>() : [.. AudioMix.Format, "-i", mixPath];
        var audioMap = mixPath is null ? Array.Empty<string>() : ["-map", "1:a"];
        var audioCodec = mixPath is null ? Array.Empty<string>() : ["-c:a", "aac", "-b:a", "192k", "-ar", "48000"];
        var arguments = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-nostats", "-y",
            "-f", "rawvideo", "-pix_fmt", "bgra", "-s", $"{Scene.Width}x{Scene.Height}", "-r", $"{SceneRenderer.FramesPerSecond}", "-i", "pipe:0",
        };
        arguments.AddRange(
        [
            .. audioInput,
            "-filter_complex", VideoFilter,
            "-map", "[v]", .. audioMap, "-map_chapters", "-1", "-map_metadata", "-1", "-t", Invariant($"{scene.Duration}"),
            "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-profile:v", "high", "-level", "4.0", "-g", "60",
            .. audioCodec,
            "-movflags", "+faststart", "-f", "mp4", outputPath,
        ]);
        using var ffmpeg = Process.Start(new ProcessStartInfo(mediaEncoder.EncoderPath, arguments)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var timeout = _encodeStartTimeout + TimeSpan.FromSeconds(scene.Duration * MaxEncodeSecondsPerVideoSecond);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        // A write to a stalled ffmpeg ends only when it is killed.
        using var kill = timeoutSource.Token.Register(() => ffmpeg.Kill(entireProcessTree: true));
        var errors = ffmpeg.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            var allFramesWritten = await SceneRenderer.WriteFramesAsync(scene, ffmpeg.StandardInput.BaseStream, timeoutSource.Token);
            if (allFramesWritten)
            {
                ffmpeg.StandardInput.Close();
            }

            await ffmpeg.WaitForExitAsync(timeoutSource.Token);
            if (!allFramesWritten || ffmpeg.ExitCode != 0)
            {
                var reason = allFramesWritten ? $"exited with {ffmpeg.ExitCode}" : "stopped reading frames";
                throw new VideoEncodingException($"ffmpeg {reason}: {(await errors).Trim()}");
            }
        }
        catch (Exception exception)
        {
            if (!ffmpeg.HasExited)
            {
                ffmpeg.Kill(entireProcessTree: true);
                await ffmpeg.WaitForExitAsync(CancellationToken.None);
            }

            File.Delete(outputPath);
            if (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new VideoEncodingException($"ffmpeg did not finish the video in {timeout.TotalMinutes:F0} min", exception);
            }

            throw;
        }
    }
}
