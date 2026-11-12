using System;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class Track
{
    public required string Property { get; init; }

    public Keyframe[] Keyframes { get; init; } = [];

    public bool Relative { get; init; }

    public bool IsColor { get; init; }

    public double LoopPeriod { get; init; }

    public double LoopCount { get; init; } = double.PositiveInfinity;

    public (double Amplitude, double AngularFrequency, double Phase)? Wave { get; init; }
    public double? Rate { get; init; }
    public bool VideoClock { get; init; }

    public double Start { get; init; } = double.NegativeInfinity;

    public double End { get; init; } = double.PositiveInfinity;
    public (double From, double To)? Grow { get; init; }

    public double Min { get; init; } = double.NegativeInfinity;

    public double Max { get; init; } = double.PositiveInfinity;

    public bool Additive => Relative || Wave is not null || Rate is not null;
    public double ValueAt(double slideTime, double slideStart)
    {
        return Math.Clamp(UnclampedValueAt(ClockTime(slideTime, slideStart)), Min, Max);
    }

    public SKColor ColorAt(double slideTime, double slideStart)
    {
        var (from, to, progress) = Segment(ClockTime(slideTime, slideStart));
        var eased = (float)to.Ease(progress);
        static byte Mix(byte start, byte end, float amount)
        {
            return (byte)Math.Clamp(Math.Round(start + ((end - start) * amount)), byte.MinValue, byte.MaxValue);
        }

        return new SKColor(
            Mix(from.Color.Red, to.Color.Red, eased),
            Mix(from.Color.Green, to.Color.Green, eased),
            Mix(from.Color.Blue, to.Color.Blue, eased),
            Mix(from.Color.Alpha, to.Color.Alpha, eased));
    }

    private double ClockTime(double slideTime, double slideStart)
    {
        return VideoClock ? slideTime + slideStart : slideTime;
    }

    private double UnclampedValueAt(double time)
    {
        if (Wave is { } wave)
        {
            var grown = Grow is { } grow ? Math.Clamp((time - grow.From) / (grow.To - grow.From), 0, 1) : 1;
            return time < Start || time >= End ? 0 : grown * wave.Amplitude * Math.Sin((wave.AngularFrequency * time) + wave.Phase);
        }

        if (Rate is { } rate)
        {
            return rate * (Math.Clamp(time, Start, End) - (double.IsNegativeInfinity(Start) ? 0 : Start));
        }

        var (from, to, progress) = Segment(time);
        return from.Value + ((to.Value - from.Value) * to.Ease(progress));
    }

    private (Keyframe From, Keyframe To, double Progress) Segment(double time)
    {
        var first = Keyframes[0];
        if (LoopPeriod > 0 && time > first.Time)
        {
            var laps = Math.Floor((time - first.Time) / LoopPeriod);
            time = laps >= LoopCount ? Keyframes[^1].Time : first.Time + ((time - first.Time) - (laps * LoopPeriod));
        }

        if (time <= first.Time || Keyframes.Length == 1)
        {
            return (first, first, 1);
        }

        for (var i = 1; i < Keyframes.Length; i++)
        {
            if (time < Keyframes[i].Time)
            {
                var from = Keyframes[i - 1];
                return (from, Keyframes[i], (time - from.Time) / (Keyframes[i].Time - from.Time));
            }
        }

        return (Keyframes[^1], Keyframes[^1], 1);
    }
}
