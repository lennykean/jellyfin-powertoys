using System.Collections.Generic;

using static System.FormattableString;

namespace JellyfinPowertoys.Jellywrap.Audio;

internal static class AudioMix
{
    private const string Loudnorm = "aformat=channel_layouts=stereo,loudnorm=I=-16:TP=-1.5:LRA=11,aresample=48000";
    private const double FadeIn = 1.5;
    private const double FadeOut = ClipArranger.Crossfade;
    private const int SampleRate = 48000;
    private const int Channels = 2;

    public static IReadOnlyList<string> Format { get; } = ["-f", "f32le", "-ar", Invariant($"{SampleRate}"), "-ac", Invariant($"{Channels}")];

    public static IReadOnlyList<string> Arguments(IReadOnlyList<PlannedClip> clips, double duration, string outputPath)
    {
        var inputs = new List<string>();
        var filters = new List<string>();
        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            inputs.AddRange(["-ss", Invariant($"{clip.In}"), "-t", Invariant($"{clip.Duration}"), "-i", clip.Path]);
            var fadeIn = i == 0 ? Invariant($",afade=t=in:d={FadeIn}") : string.Empty;
            filters.Add($"[{i}:a]{Loudnorm}{fadeIn}[a{i + 1}]");
        }

        var mixed = "a1";
        for (var i = 1; i < clips.Count; i++)
        {
            filters.Add(Invariant($"[{mixed}][a{i + 1}]acrossfade=d={ClipArranger.Crossfade}[x{i}]"));
            mixed = $"x{i}";
        }

        filters.Add(Invariant($"[{mixed}]afade=t=out:st={duration - FadeOut}:d={FadeOut}[aout]"));
        return
        [
            "-hide_banner", "-loglevel", "error", "-nostats", "-y",
            .. inputs,
            "-filter_complex", string.Join(';', filters),
            "-map", "[aout]", "-t", Invariant($"{duration}"),
            .. Format, outputPath,
        ];
    }

    public static double Seconds(long bytes)
    {
        return (double)bytes / (SampleRate * Channels * sizeof(float));
    }
}
