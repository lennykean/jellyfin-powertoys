using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.MediaEncoding;

namespace JellyfinPowertoys.Jellywrap.Audio;


public sealed class AudioProbe(IMediaEncoder mediaEncoder)
{
    private static readonly Regex _loudnessFrame = new Regex(@"t:\s*([\d.]+)\s+TARGET:.*?M:\s*(-?[\d.]+)\s+S:\s*(-?[\d.]+)", RegexOptions.Compiled);
    private static readonly TimeSpan _timeout = TimeSpan.FromMinutes(2);

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

    private Task<(byte[] Output, string Log)> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        return mediaEncoder.RunFfmpegAsync(new[] { "-hide_banner", "-nostats", "-threads", "1" }.Concat(arguments), _timeout, cancellationToken);
    }
}
