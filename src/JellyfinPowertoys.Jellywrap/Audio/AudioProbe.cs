using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.MediaEncoding;

namespace JellyfinPowertoys.Jellywrap.Audio;


public sealed class AudioProbe(IMediaEncoder mediaEncoder)
{
    private static readonly Regex _loudnessFrame = new(@"t:\s*([\d.]+)\s+TARGET:.*?M:\s*(-?[\d.]+)\s+S:\s*(-?[\d.]+)", RegexOptions.Compiled);

    internal async Task<uint[]> FingerprintAsync(string path, double start, double length, CancellationToken cancellationToken)
    {
        var (output, _) = await RunAsync(
            ["-loglevel", "error", "-ss", Number(start), "-t", Number(length), "-i", path, "-map", "0:a:0", "-ac", "1", "-f", "chromaprint", "-fp_format", "raw", "-"],
            cancellationToken);
        return [.. Enumerable.Range(0, output.Length / sizeof(uint)).Select(i => BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(i * sizeof(uint))))];
    }

    internal async Task<IReadOnlyList<LoudnessFrame>> LoudnessAsync(
        string path,
        double start,
        double end,
        CancellationToken cancellationToken)
    {
        var (_, log) = await RunAsync(
            ["-ss", Number(start), "-t", Number(end - start), "-i", path, "-map", "0:a:0", "-af", "ebur128=framelog=info", "-f", "null", "-"],
            cancellationToken);
        return [.. _loudnessFrame.Matches(log)
            .Select(match => new LoudnessFrame(
                start + double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)))];
    }

    private static string Number(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private async Task<(byte[] Output, string Log)> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(mediaEncoder.EncoderPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "-hide_banner", "-nostats", "-threads", "1" }.Concat(arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        using var kill = cancellationToken.Register(() => process.Kill(entireProcessTree: true));
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
        var log = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(copy, log);
        await process.WaitForExitAsync(cancellationToken);

        // A killed ffmpeg exits with an error too; that is a cancellation, not a failure.
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg exited with {process.ExitCode}: {(await log).Trim()}");
        }

        return (output.ToArray(), await log);
    }
}
