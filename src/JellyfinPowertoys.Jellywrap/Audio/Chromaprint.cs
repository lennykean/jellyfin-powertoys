using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace JellyfinPowertoys.Jellywrap.Audio;

internal static class Chromaprint
{
    private const int MaxBitErrors = 8;
    private const double MaxGap = 2.0;
    private const int Alignments = 10;
    private const int MaxRepeats = 20;
    public static List<(double Start, double End)> Runs(uint[] first, uint[] second, double step, double minSeconds)
    {
        var secondPositions = second.Select((value, position) => (value, position)).ToLookup(item => item.value, item => item.position);
        var shifts = first
            .SelectMany((value, position) => secondPositions[value] is var matches && matches.Count() <= MaxRepeats
                ? matches.Select(match => position - match)
                : [])
            .GroupBy(shift => shift)
            .Select(votes => (Shift: votes.Key, Votes: votes.Count()))
            .OrderByDescending(shift => shift.Votes)
            .Take(Alignments);
        var maxGapItems = (int)(MaxGap / step);
        var runs = new List<(double, double)>();
        foreach (var (shift, _) in shifts)
        {
            var firstStart = Math.Max(0, shift);
            var secondStart = firstStart - shift;
            var overlap = Math.Min(first.Length - firstStart, second.Length - secondStart);
            int? runStart = null;
            var lastMatch = 0;
            for (var i = 0; i < overlap; i++)
            {
                if (BitOperations.PopCount(first[firstStart + i] ^ second[secondStart + i]) > MaxBitErrors)
                {
                    continue;
                }

                if (runStart is not null && i - lastMatch > maxGapItems)
                {
                    if ((lastMatch - runStart.Value) * step >= minSeconds)
                    {
                        runs.Add(((firstStart + runStart.Value) * step, (firstStart + lastMatch) * step));
                    }

                    runStart = null;
                }

                runStart ??= i;
                lastMatch = i;
            }

            if (runStart is not null && (lastMatch - runStart.Value) * step >= minSeconds)
            {
                runs.Add(((firstStart + runStart.Value) * step, (firstStart + lastMatch) * step));
            }
        }

        return runs;
    }

    public static List<(double Start, double End)> Common(
        IReadOnlyList<List<(double Start, double End)>> runsPerOther,
        double minSeconds)
    {
        var common = runsPerOther[0];
        foreach (var runs in runsPerOther.Skip(1))
        {
            common = [.. common
                .SelectMany(stretch => runs.Select(run => (Start: Math.Max(stretch.Start, run.Start), End: Math.Min(stretch.End, run.End))))
                .Where(stretch => stretch.End - stretch.Start >= minSeconds)];
        }

        return [.. common.Distinct().OrderBy(stretch => stretch.Start - stretch.End)];
    }
}
