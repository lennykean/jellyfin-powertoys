using System;
using System.Collections.Generic;
using System.Linq;

namespace JellyfinPowertoys.Jellywrap.Audio;

internal static class ClipArranger
{
    public const double Crossfade = 3;

    private const double HalfCrossfade = Crossfade / 2;
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

        var spans = FitToSlideEdges(timeline, featured, ranked, [], MaxSpan)
            ?? FitToSlideEdges(timeline, featured, ranked, [], double.PositiveInfinity)
            ?? SpreadEvenly(videoLength, featured, ranked, [])
            ?? FitToSlideEdges(timeline, featured, ranked, serverFill, double.PositiveInfinity)
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

    private static List<ClipSpan>? FitToSlideEdges(
        IReadOnlyList<TimelineSlide> timeline,
        IReadOnlyList<IReadOnlyList<Clip>> featured,
        IReadOnlyList<Clip> ranked,
        IReadOnlyList<Clip> serverFill,
        double maxSpan)
    {
        var slideCount = timeline.Count;
        var videoLength = timeline[^1].Start + timeline[^1].Duration;
        var allClips = ranked.Concat(featured.SelectMany(clips => clips)).Concat(serverFill).DistinctBy(clip => clip.MusicId).ToList();
        var usedMusic = new HashSet<(string, double)>();
        var spans = new List<ClipSpan>();

        double SlideEnd(int slide)
        {
            return timeline[slide].Start + timeline[slide].Duration;
        }

        double LengthWithCrossfades(double from, double to)
        {
            return to - from + (from == 0 ? 0 : HalfCrossfade) + (to == videoLength ? 0 : HalfCrossfade);
        }

        Func<int, double> LengthsFrom(double from)
        {
            return slide => LengthWithCrossfades(from, SlideEnd(slide));
        }

        int FirstLongEnough(double from, int fromSlide)
        {
            var lengthTo = LengthsFrom(from);
            return Enumerable.Range(fromSlide, slideCount - fromSlide).Where(slide => lengthTo(slide) >= Clip.MinLength).DefaultIfEmpty(slideCount - 1).First();
        }

        double? MinClipEnd(double from)
        {
            var end = from + Clip.MinLength - (from == 0 ? 0 : HalfCrossfade) - HalfCrossfade;
            return LengthWithCrossfades(end, videoLength) >= Clip.MinLength ? end : null;
        }

        var ownClips = featured
            .Select((clips, slide) =>
            {
                var start = timeline[slide].Start;
                return clips
                    .Where(clip => clip.Length >= LengthsFrom(start)(FirstLongEnough(start, slide)) || (clip.Length >= Clip.MinLength && MinClipEnd(start) is not null))
                    .ToList();
            })
            .ToList();
        var reservedMusic = ownClips.Where(clips => clips.Count > 0).Select(clips => clips[0].MusicId).ToHashSet();

        bool HasOwnClip(int slide)
        {
            return ownClips[slide].Any(clip => !usedMusic.Contains(clip.MusicId));
        }

        bool RestTooShort(int slide)
        {
            return slide < slideCount - 1 && videoLength - timeline[slide + 1].Start + HalfCrossfade < Clip.MinLength;
        }

        int NextOwnSlide(int slide)
        {
            return Enumerable.Range(slide + 1, slideCount - slide - 1).Where(HasOwnClip).DefaultIfEmpty(slideCount).First();
        }

        bool GapTooShort(int slide)
        {
            return RestTooShort(slide) || (NextOwnSlide(slide) > slide + 1 && LengthsFrom(timeline[slide + 1].Start)(NextOwnSlide(slide) - 1) < Clip.MinLength);
        }

        int GapLastSlide(int slide)
        {
            return RestTooShort(slide) ? slideCount - 1 : NextOwnSlide(slide) - 1;
        }

        var position = 0.0;
        var lastSpanStart = 0.0;
        var lastSpanLastSlide = 0;
        var firstSlide = 0;
        while (firstSlide < slideCount)
        {
            var lengthTo = LengthsFrom(position);
            var earliestLastSlide = FirstLongEnough(position, firstSlide);
            var nextOwnSlide = NextOwnSlide(firstSlide);
            var ownClip = ownClips[firstSlide].FirstOrDefault(clip => !usedMusic.Contains(clip.MusicId));
            var candidates = ownClips[firstSlide].Where(clip => !usedMusic.Contains(clip.MusicId)).ToList();
            candidates.AddRange([.. ranked.Where(clip => !usedMusic.Contains(clip.MusicId) && !reservedMusic.Contains(clip.MusicId) && !candidates.Contains(clip))]);
            var unreservedCount = candidates.Count;
            candidates.AddRange([.. allClips.Where(clip => !usedMusic.Contains(clip.MusicId) && !candidates.Contains(clip))]);

            var midSlideStop = (earliestLastSlide >= nextOwnSlide && MinClipEnd(position) < SlideEnd(nextOwnSlide)) || (ownClip is not null && ownClip.Length < lengthTo(earliestLastSlide))
                ? MinClipEnd(position)
                : null;

            var chosen = midSlideStop is not null
                ? candidates.FirstOrDefault(clip => clip.Length >= Clip.MinLength)
                : candidates.Take(unreservedCount).FirstOrDefault(clip => clip.Length >= lengthTo(GapTooShort(earliestLastSlide) ? GapLastSlide(earliestLastSlide) : earliestLastSlide))
                    ?? candidates.FirstOrDefault(clip => clip.Length >= lengthTo(RestTooShort(earliestLastSlide) ? slideCount - 1 : earliestLastSlide));
            if (chosen is null)
            {
                if (spans.Count > 0 && ExtendLastSpan() is { } extendedTo)
                {
                    position = SlideEnd(extendedTo);
                    firstSlide = extendedTo + 1;
                    continue;
                }

                return null;
            }

            var lastSlide = earliestLastSlide;
            if (midSlideStop is { } stop)
            {
                lastSlide = Enumerable.Range(firstSlide, slideCount - firstSlide).Last(slide => timeline[slide].Start < stop);
            }
            else
            {
                while (lastSlide + 1 < slideCount && !HasOwnClip(lastSlide + 1) && lengthTo(lastSlide + 1) <= Math.Min(maxSpan, chosen.Length))
                {
                    lastSlide++;
                }

                if (GapTooShort(lastSlide) && lengthTo(GapLastSlide(lastSlide)) <= chosen.Length)
                {
                    lastSlide = GapLastSlide(lastSlide);
                }

                while (lastSlide > earliestLastSlide && GapTooShort(lastSlide))
                {
                    lastSlide--;
                }
            }

            foreach (var absorbedSlide in Enumerable.Range(firstSlide + 1, Math.Max(0, midSlideStop is null ? lastSlide - firstSlide : lastSlide - firstSlide - 1)))
            {
                foreach (var absorbedClip in ownClips[absorbedSlide])
                {
                    reservedMusic.Remove(absorbedClip.MusicId);
                }
            }

            spans.Add(new ClipSpan(chosen, midSlideStop is null ? lengthTo(lastSlide) : Clip.MinLength));
            usedMusic.Add(chosen.MusicId);
            lastSpanStart = position;
            lastSpanLastSlide = lastSlide;
            position = midSlideStop ?? SlideEnd(lastSlide);
            firstSlide = midSlideStop is null ? lastSlide + 1 : lastSlide;
        }

        return spans;

        int? ExtendLastSpan()
        {
            var lastSpan = spans[^1];
            var lengthTo = LengthsFrom(lastSpanStart);
            var lastSlide = lastSpanLastSlide;
            while (lastSlide + 1 < slideCount && lengthTo(lastSlide + 1) <= lastSpan.Clip.Length)
            {
                lastSlide++;
            }

            while (lastSlide > lastSpanLastSlide && RestTooShort(lastSlide))
            {
                lastSlide--;
            }

            if (lengthTo(lastSlide) <= lastSpan.Duration || lengthTo(lastSlide) > lastSpan.Clip.Length || RestTooShort(lastSlide))
            {
                return null;
            }

            spans[^1] = lastSpan with { Duration = lengthTo(lastSlide) };
            lastSpanLastSlide = lastSlide;
            return lastSlide;
        }
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
