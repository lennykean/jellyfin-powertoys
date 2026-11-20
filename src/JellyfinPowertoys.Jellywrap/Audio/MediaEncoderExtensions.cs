using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.MediaEncoding;

namespace JellyfinPowertoys.Jellywrap.Audio;

internal static class MediaEncoderExtensions
{
    public static async Task<(byte[] Output, string Log)> RunFfmpegAsync(
        this IMediaEncoder mediaEncoder,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(mediaEncoder.EncoderPath, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(startInfo)!;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        // Reads of a stalled ffmpeg's pipes end only when it is killed.
        using var kill = timeoutSource.Token.Register(() => process.Kill(entireProcessTree: true));
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output, CancellationToken.None);
        var log = process.StandardError.ReadToEndAsync(CancellationToken.None);
        await Task.WhenAll(copy, log);
        await process.WaitForExitAsync(CancellationToken.None);

        // A killed ffmpeg exits with an error too; that is a cancellation, not a failure.
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            var reason = timeoutSource.IsCancellationRequested ? $"did not finish in {timeout.TotalSeconds:F0} s" : $"exited with {process.ExitCode}";
            throw new InvalidOperationException($"ffmpeg {reason}: {(await log).Trim()}");
        }

        return (output.ToArray(), await log);
    }
}
