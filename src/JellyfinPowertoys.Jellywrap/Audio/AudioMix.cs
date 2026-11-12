using System.Collections.Generic;

using static System.FormattableString;

namespace JellyfinPowertoys.Jellywrap.Audio;

internal static class AudioMix
{
    private const string Loudnorm = "aformat=channel_layouts=stereo,loudnorm=I=-16:TP=-1.5:LRA=11,aresample=48000";
    private const double FadeIn = 1.5;
    private const double FadeOut = ClipArranger.Crossfade;

    public static (IReadOnlyList<string> Inputs, IReadOnlyList<string> Filters) Build(
        IReadOnlyList<PlannedClip> clips,
        double duration)
    {
        var inputs = new List<string>();
        var filters = new List<string>();
        if (clips.Count == 0)
        {
            return (inputs, filters);
        }

        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            inputs.AddRange(["-ss", Invariant($"{clip.In}"), "-t", Invariant($"{clip.Duration}"), "-i", clip.Path]);
            var fadeIn = i == 0 ? Invariant($",afade=t=in:d={FadeIn}") : string.Empty;
            filters.Add($"[{i + 1}:a]{Loudnorm}{fadeIn}[a{i + 1}]");
        }

        var mixed = "a1";
        for (var i = 1; i < clips.Count; i++)
        {
            filters.Add(Invariant($"[{mixed}][a{i + 1}]acrossfade=d={ClipArranger.Crossfade}[x{i}]"));
            mixed = $"x{i}";
        }

        filters.Add(Invariant($"[{mixed}]afade=t=out:st={duration - FadeOut}:d={FadeOut}[aout]"));
        return (inputs, filters);
    }
}
