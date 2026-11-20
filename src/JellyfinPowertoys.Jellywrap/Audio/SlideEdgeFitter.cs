using System;
using System.Collections.Generic;
using System.Linq;

namespace JellyfinPowertoys.Jellywrap.Audio;

internal sealed class SlideEdgeFitter
{
    private const double HalfCrossfade = ClipArranger.Crossfade / 2;

    private readonly IReadOnlyList<TimelineSlide> _timeline;
    private readonly IReadOnlyList<Clip> _ranked;
    private readonly double _maxSpan;
    private readonly int _slideCount;
    private readonly double _videoLength;
    private readonly List<Clip> _allClips;
    private readonly List<List<Clip>> _ownClips;
    private readonly HashSet<(string Path, double In)> _reservedMusic;
    private readonly HashSet<(string Path, double In)> _usedMusic = [];
    private readonly List<ClipSpan> _spans = [];
    private double _lastSpanStart;
    private int _lastSpanLastSlide;

    public SlideEdgeFitter(
        IReadOnlyList<TimelineSlide> timeline,
        IReadOnlyList<IReadOnlyList<Clip>> featured,
        IReadOnlyList<Clip> ranked,
        IReadOnlyList<Clip> serverFill,
        double maxSpan)
    {
        _timeline = timeline;
        _ranked = ranked;
        _maxSpan = maxSpan;
        _slideCount = timeline.Count;
        _videoLength = timeline[^1].Start + timeline[^1].Duration;
        _allClips = ranked.Concat(featured.SelectMany(clips => clips)).Concat(serverFill).DistinctBy(clip => clip.MusicId).ToList();
        _ownClips = featured.Select((clips, slide) => clips.Where(clip => CanStartAt(clip, slide)).ToList()).ToList();
        _reservedMusic = _ownClips.Where(clips => clips.Count > 0).Select(clips => clips[0].MusicId).ToHashSet();
    }

    public List<ClipSpan>? Fit()
    {
        var position = 0.0;
        var firstSlide = 0;
        while (firstSlide < _slideCount)
        {
            var earliestLastSlide = FirstLongEnough(position, firstSlide);
            var midSlideStop = MidSlideStop(position, firstSlide, earliestLastSlide);
            var chosen = Choose(position, firstSlide, earliestLastSlide, midSlideStop is not null);
            if (chosen is null)
            {
                if (_spans.Count > 0 && ExtendLastSpan() is { } extendedTo)
                {
                    position = SlideEnd(extendedTo);
                    firstSlide = extendedTo + 1;
                    continue;
                }

                return null;
            }

            if (midSlideStop is { } stop)
            {
                var lastSlide = Enumerable.Range(firstSlide, _slideCount - firstSlide).Last(slide => _timeline[slide].Start < stop);
                ReleaseReserved(firstSlide + 1, lastSlide - 1);
                Add(new ClipSpan(chosen, Clip.MinLength), position, lastSlide);
                position = stop;
                firstSlide = lastSlide;
            }
            else
            {
                var lastSlide = LastSlide(position, earliestLastSlide, chosen);
                ReleaseReserved(firstSlide + 1, lastSlide);
                Add(new ClipSpan(chosen, LengthTo(position, lastSlide)), position, lastSlide);
                position = SlideEnd(lastSlide);
                firstSlide = lastSlide + 1;
            }
        }

        return _spans;
    }

    private double SlideEnd(int slide)
    {
        return _timeline[slide].Start + _timeline[slide].Duration;
    }

    private double LengthWithCrossfades(double from, double to)
    {
        return to - from + (from == 0 ? 0 : HalfCrossfade) + (to == _videoLength ? 0 : HalfCrossfade);
    }

    private double LengthTo(double from, int slide)
    {
        return LengthWithCrossfades(from, SlideEnd(slide));
    }

    private int FirstLongEnough(double from, int fromSlide)
    {
        return Enumerable.Range(fromSlide, _slideCount - fromSlide).Where(slide => LengthTo(from, slide) >= Clip.MinLength).DefaultIfEmpty(_slideCount - 1).First();
    }

    private double? MinClipEnd(double from)
    {
        var end = from + Clip.MinLength - (from == 0 ? 0 : HalfCrossfade) - HalfCrossfade;
        return LengthWithCrossfades(end, _videoLength) >= Clip.MinLength ? end : null;
    }

    private bool CanStartAt(Clip clip, int slide)
    {
        var start = _timeline[slide].Start;
        return clip.Length >= LengthTo(start, FirstLongEnough(start, slide)) || (clip.Length >= Clip.MinLength && MinClipEnd(start) is not null);
    }

    private bool HasOwnClip(int slide)
    {
        return _ownClips[slide].Any(clip => !_usedMusic.Contains(clip.MusicId));
    }

    private bool RestTooShort(int slide)
    {
        return slide < _slideCount - 1 && _videoLength - _timeline[slide + 1].Start + HalfCrossfade < Clip.MinLength;
    }

    private int NextOwnSlide(int slide)
    {
        return Enumerable.Range(slide + 1, _slideCount - slide - 1).Where(HasOwnClip).DefaultIfEmpty(_slideCount).First();
    }

    private bool GapTooShort(int slide)
    {
        return RestTooShort(slide) || (NextOwnSlide(slide) > slide + 1 && LengthTo(_timeline[slide + 1].Start, NextOwnSlide(slide) - 1) < Clip.MinLength);
    }

    private int GapLastSlide(int slide)
    {
        return RestTooShort(slide) ? _slideCount - 1 : NextOwnSlide(slide) - 1;
    }

    private double? MidSlideStop(double position, int firstSlide, int earliestLastSlide)
    {
        var nextOwnSlide = NextOwnSlide(firstSlide);
        var ownClip = _ownClips[firstSlide].FirstOrDefault(clip => !_usedMusic.Contains(clip.MusicId));
        return (earliestLastSlide >= nextOwnSlide && MinClipEnd(position) < SlideEnd(nextOwnSlide)) || (ownClip is not null && ownClip.Length < LengthTo(position, earliestLastSlide))
            ? MinClipEnd(position)
            : null;
    }

    private Clip? Choose(double position, int firstSlide, int earliestLastSlide, bool stopsMidSlide)
    {
        var candidates = _ownClips[firstSlide].Where(clip => !_usedMusic.Contains(clip.MusicId)).ToList();
        candidates.AddRange([.. _ranked.Where(clip => !_usedMusic.Contains(clip.MusicId) && !_reservedMusic.Contains(clip.MusicId) && !candidates.Contains(clip))]);
        var unreservedCount = candidates.Count;
        candidates.AddRange([.. _allClips.Where(clip => !_usedMusic.Contains(clip.MusicId) && !candidates.Contains(clip))]);
        if (stopsMidSlide)
        {
            return candidates.FirstOrDefault(clip => clip.Length >= Clip.MinLength);
        }

        var unreservedLastSlide = GapTooShort(earliestLastSlide) ? GapLastSlide(earliestLastSlide) : earliestLastSlide;
        var reservedLastSlide = RestTooShort(earliestLastSlide) ? _slideCount - 1 : earliestLastSlide;
        return candidates.Take(unreservedCount).FirstOrDefault(clip => clip.Length >= LengthTo(position, unreservedLastSlide))
            ?? candidates.FirstOrDefault(clip => clip.Length >= LengthTo(position, reservedLastSlide));
    }

    private int LastSlide(double position, int earliestLastSlide, Clip chosen)
    {
        var lastSlide = earliestLastSlide;
        while (lastSlide + 1 < _slideCount && !HasOwnClip(lastSlide + 1) && LengthTo(position, lastSlide + 1) <= Math.Min(_maxSpan, chosen.Length))
        {
            lastSlide++;
        }

        if (GapTooShort(lastSlide) && LengthTo(position, GapLastSlide(lastSlide)) <= chosen.Length)
        {
            lastSlide = GapLastSlide(lastSlide);
        }

        while (lastSlide > earliestLastSlide && GapTooShort(lastSlide))
        {
            lastSlide--;
        }

        return lastSlide;
    }

    private void ReleaseReserved(int firstSlide, int lastSlide)
    {
        for (var slide = firstSlide; slide <= lastSlide; slide++)
        {
            foreach (var clip in _ownClips[slide])
            {
                _reservedMusic.Remove(clip.MusicId);
            }
        }
    }

    private void Add(ClipSpan span, double start, int lastSlide)
    {
        _spans.Add(span);
        _usedMusic.Add(span.Clip.MusicId);
        _lastSpanStart = start;
        _lastSpanLastSlide = lastSlide;
    }

    private int? ExtendLastSpan()
    {
        var lastSpan = _spans[^1];
        var lastSlide = _lastSpanLastSlide;
        while (lastSlide + 1 < _slideCount && LengthTo(_lastSpanStart, lastSlide + 1) <= lastSpan.Clip.Length)
        {
            lastSlide++;
        }

        while (lastSlide > _lastSpanLastSlide && RestTooShort(lastSlide))
        {
            lastSlide--;
        }

        var length = LengthTo(_lastSpanStart, lastSlide);
        if (length <= lastSpan.Duration || length > lastSpan.Clip.Length || RestTooShort(lastSlide))
        {
            return null;
        }

        _spans[^1] = lastSpan with { Duration = length };
        _lastSpanLastSlide = lastSlide;
        return lastSlide;
    }
}
