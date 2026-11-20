using System;
using System.Collections.Generic;
using System.Linq;

namespace JellyfinPowertoys.Jellywrap.Audio;

internal static class LoudnessFrameExtensions
{
    private const double SilentLufs = -45;
    private const double MinSound = 1;
    private const double MaxSilence = 2;
    private const double MaxFrameStep = 0.15;
    private const double MaxCheck = 30;
    private const double MomentaryWindow = 0.4;
    private const double ShortTermWindow = 3;
    private const int MinMomentaryValues = 20;
    private const int ShortTermFrames = 30;
    private const int MinLevelChanges = 5;
    private const double MaxSilentShare = 0.10;
    private const double MinMedianLufs = -40;
    private const double MaxLevelChange = 3.0;

    public static (double Start, double Length) TrimSilence(this IReadOnlyList<LoudnessFrame> frames, double start, double length)
    {
        var soundStart = frames
            .Select(frame => frame.Time)
            .Where(time => time >= start && frames.Where(frame => frame.Time >= time && frame.Time <= time + MinSound).All(frame => frame.Momentary >= SilentLufs))
            .Cast<double?>()
            .FirstOrDefault() ?? start;
        length = Math.Min(length - (soundStart - start), frames.DecodedTo(soundStart) - soundStart);
        double? silentFrom = null;
        foreach (var frame in frames.Where(f => f.Time >= soundStart + MomentaryWindow))
        {
            if (frame.Momentary >= SilentLufs)
            {
                silentFrom = null;
            }
            else if (silentFrom is null)
            {
                silentFrom = frame.Time;
            }
            else if (frame.Time - silentFrom >= MaxSilence)
            {
                length = Math.Min(length, silentFrom.Value - soundStart);
                break;
            }
        }

        return (soundStart, length);
    }

    public static (bool IsMusic, string Measured) MusicLike(this IReadOnlyList<LoudnessFrame> frames, double start, double length)
    {
        var end = start + Math.Min(length, MaxCheck);
        var momentary = frames.Where(f => f.Time >= start + MomentaryWindow && f.Time <= end).Select(f => f.Momentary).ToList();
        var shortTerm = frames.Where(f => f.Time >= start + ShortTermWindow && f.Time <= end).Select(f => f.ShortTerm).ToList();
        if (momentary.Count < MinMomentaryValues)
        {
            return (false, "no audio");
        }

        var silentShare = (double)momentary.Count(m => m < SilentLufs) / momentary.Count;
        var median = momentary.Order().ElementAt(momentary.Count / 2);
        var levelChange = 0.0;
        var levelChanges = shortTerm.Skip(ShortTermFrames).Zip(shortTerm, (later, earlier) => Math.Abs(later - earlier)).Order().ToList();
        if (levelChanges.Count > MinLevelChanges)
        {
            levelChange = levelChanges[levelChanges.Count / 2];
        }

        return (
            silentShare <= MaxSilentShare && median >= MinMedianLufs && levelChange <= MaxLevelChange,
            $"silent {silentShare:P0}, median {median:F0} LUFS, level change {levelChange:F1} LU");
    }

    private static double DecodedTo(this IReadOnlyList<LoudnessFrame> frames, double from)
    {
        var decodedTo = from;
        foreach (var frame in frames.Where(f => f.Time > from))
        {
            if (frame.Time - decodedTo > MaxFrameStep)
            {
                break;
            }

            decodedTo = frame.Time;
        }

        return decodedTo;
    }
}
