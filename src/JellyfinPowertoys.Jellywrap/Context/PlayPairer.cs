using System;
using System.Collections.Generic;
using System.Linq;

namespace JellyfinPowertoys.Jellywrap.Context;

internal static class PlayPairer
{
    private const double CompletedShare = 0.9;
    private const double MinShare = 0.1;
    private const double MinSecondsWithoutRuntime = 30;

    public static IReadOnlyList<Play> Pair(
        IReadOnlyList<PlaybackRow> rows,
        ItemIndex itemIndex,
        TimeZoneInfo timeZone,
        DateTime generatedAt)
    {
        var sorted = rows.OrderBy(r => r.Time).ToList();
        var nextStart = sorted
            .Where(r => r.IsStart)
            .GroupBy(r => r.UserId, r => r.Time)
            .SelectMany(g => g.Distinct().Zip(g.Distinct().Skip(1), (time, next) => (Key: (g.Key, time), Next: next)))
            .ToDictionary(start => start.Key, start => start.Next);

        double SecondsOf(PlaybackRow start, DateTime? stop, double? runtime)
        {
            if (stop is { } stopTime)
            {
                var watched = (stopTime - start.Time).TotalSeconds;
                return runtime is { } limit ? Math.Min(watched, limit) : watched;
            }

            var open = (generatedAt - start.Time).TotalSeconds;
            if (nextStart.TryGetValue((start.UserId, start.Time), out var later))
            {
                open = Math.Min(open, (later - start.Time).TotalSeconds);
            }

            // A play without a stop of an item without a runtime has no other upper bound.
            return Math.Min(open, runtime ?? MinSecondsWithoutRuntime);
        }

        var paired = sorted
            .GroupBy(r => (r.UserId, r.ItemId))
            .SelectMany(itemRows =>
            {
                var group = itemRows.ToList();
                return group.Select((row, index) => (Start: row, Next: index + 1 < group.Count ? group[index + 1] : null));
            })
            .Where(pair => pair.Start.IsStart)
            .Select(pair =>
            {
                var item = itemIndex.Get(pair.Start.ItemId);
                var runtime = item?.Runtime is > 0 and var length ? length : (double?)null;
                var stop = pair.Next is { IsStart: false } next ? next.Time : (DateTime?)null;
                return (Row: pair.Start, Item: item, Runtime: runtime, Stop: stop, Seconds: SecondsOf(pair.Start, stop, runtime));
            })
            .ToList();

        var longest = paired.Where(p => p.Item?.Kind is ItemKind.Movie or ItemKind.Episode or ItemKind.Video && p.Runtime is not null)
            .Select(p => p.Runtime!.Value)
            .DefaultIfEmpty()
            .Max();

        return [.. paired
            .Where(pair => pair.Item is not null)
            .Select(pair =>
            {
                var seconds = pair.Runtime is null && longest > 0 ? Math.Min(pair.Seconds, longest) : pair.Seconds;
                return new Play(
                    pair.Row.UserId,
                    pair.Row.ItemId,
                    pair.Item,
                    pair.Row.Time,
                    TimeZoneInfo.ConvertTimeFromUtc(pair.Row.Time, timeZone),
                    seconds,
                    pair.Stop ?? pair.Row.Time.AddSeconds(seconds),
                    pair.Runtime is { } runtime && seconds >= CompletedShare * runtime);
            })
            .Where(IsLongEnough)
            .OrderBy(p => p.Start)];
    }

    private static bool IsLongEnough(Play play)
    {
        return play.Seconds >= (play.Item?.Runtime is > 0 and var runtime ? MinShare * runtime : MinSecondsWithoutRuntime);
    }
}
