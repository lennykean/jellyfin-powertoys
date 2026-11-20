using System;
using System.Collections.Generic;
using System.Linq;

namespace JellyfinPowertoys.Jellywrap.Audio;

internal static class ClipArranger
{
    public const double Crossfade = 3;

    private const double MaxSpan = 30;
    private const double Tolerance = 1e-6;

    public static IReadOnlyList<ClipSpan> Arrange(
        string name,
        IReadOnlyList<TimelineSlide> timeline,
        IReadOnlyList<IReadOnlyList<Clip>> featured,
        IReadOnlyList<Clip> ranked,
        IReadOnlyList<Clip> serverFill)
    {
        var videoLength = timeline[^1].Start + timeline[^1].Duration;
        if (videoLength < Clip.MinLength)
        {
            return [];
        }

        var spans = new SlideEdgeFitter(timeline, featured, ranked, [], MaxSpan).Fit()
            ?? new SlideEdgeFitter(timeline, featured, ranked, [], double.PositiveInfinity).Fit()
            ?? SpreadEvenly(videoLength, featured, ranked, [])
            ?? FitToSlideEdgesWithFill(timeline, featured, ranked, serverFill)
            ?? SpreadEvenly(videoLength, featured, ranked, serverFill)
            ?? [];
        var covered = spans.Sum(s => s.Duration) - (Crossfade * (spans.Count - 1));
        if (spans.Count > 0 && (Math.Abs(covered - videoLength) > Tolerance || spans.Any(s => s.Duration < Clip.MinLength)))
        {
            throw new InvalidOperationException(
                $"The audio plan for {name} does not cover the {videoLength} s video exactly with clips of {Clip.MinLength} s or more: "
                + $"{string.Join(", ", spans.Select(s => s.Duration))}.");
        }

        return spans;
    }

    public static double Coverable(double videoLength, IEnumerable<Clip> clips)
    {
        var lengths = Usable(clips).Select(clip => clip.Length).OrderDescending().Take(MaxClips(videoLength)).ToList();
        return lengths.Count == 0 ? 0 : lengths.Sum() - (Crossfade * (lengths.Count - 1));
    }

    private static int MaxClips(double videoLength)
    {
        return Math.Max(1, (int)((videoLength - Crossfade) / (Clip.MinLength - Crossfade)));
    }

    private static List<Clip> Usable(IEnumerable<Clip> clips)
    {
        return [.. clips.DistinctBy(clip => clip.MusicId).Where(clip => clip.Length >= Clip.MinLength)];
    }

    private static List<ClipSpan>? FitToSlideEdgesWithFill(
        IReadOnlyList<TimelineSlide> timeline,
        IReadOnlyList<IReadOnlyList<Clip>> featured,
        IReadOnlyList<Clip> ranked,
        IReadOnlyList<Clip> serverFill)
    {
        var spans = new SlideEdgeFitter(timeline, featured, ranked, serverFill, double.PositiveInfinity).Fit();
        var own = Usable(ranked.Concat(featured.SelectMany(slideClips => slideClips)));
        return spans is not null && own.All(clip => spans.Any(span => span.Clip.MusicId == clip.MusicId)) ? spans : null;
    }

    private static List<ClipSpan>? SpreadEvenly(
        double videoLength,
        IReadOnlyList<IReadOnlyList<Clip>> featured,
        IReadOnlyList<Clip> ranked,
        IReadOnlyList<Clip> serverFill)
    {
        bool CanCover(IEnumerable<Clip> clips)
        {
            return Coverable(videoLength, clips) + Tolerance >= videoLength;
        }

        var own = Usable(ranked.Concat(featured.SelectMany(slideClips => slideClips)));
        var clips = Usable(own.Concat(serverFill));
        if (!CanCover(clips))
        {
            return null;
        }

        while (clips.Count > own.Count && CanCover(clips.Take(clips.Count - 1)))
        {
            clips.RemoveAt(clips.Count - 1);
        }

        while (clips.Count > MaxClips(videoLength))
        {
            clips.RemoveAt(Enumerable.Range(0, clips.Count).Last(spare => CanCover(clips.Where((_, index) => index != spare))));
        }

        var remaining = videoLength + (Crossfade * (clips.Count - 1));
        var clipsLeft = clips.Count;

        var sharedDuration = double.PositiveInfinity;
        foreach (var length in clips.Select(clip => clip.Length).Order())
        {
            if (length * clipsLeft >= remaining)
            {
                sharedDuration = remaining / clipsLeft;
                break;
            }

            remaining -= length;
            clipsLeft--;
        }

        return [.. clips.Select(clip => new ClipSpan(clip, Math.Min(clip.Length, sharedDuration)))];
    }
}
