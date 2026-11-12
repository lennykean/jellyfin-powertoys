using System;
using System.Collections.Generic;
using System.Linq;

namespace JellyfinPowertoys.Jellywrap.Context;

internal static class PlayPairer
{
    private const double CompletedShare = 0.9;
    private const double MinSeconds = 30;

    public static IReadOnlyList<Play> Pair(IReadOnlyList<PlaybackRow> rows, ItemIndex itemIndex, TimeZoneInfo timeZone)
    {
        var sorted = rows.OrderBy(r => r.Time).ToList();
        var nextStart = sorted
            .Where(r => r.IsStart)
            .GroupBy(r => r.UserId, r => r.Time)
            .SelectMany(g => g.Distinct().Zip(g.Distinct().Skip(1), (time, next) => (Key: (g.Key, time), Next: next)))
            .ToDictionary(start => start.Key, start => start.Next);
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
                var elapsed = stop is { } stopTime
                    ? (stopTime - pair.Start.Time).TotalSeconds
                    : nextStart.TryGetValue((pair.Start.UserId, pair.Start.Time), out var later) ? (later - pair.Start.Time).TotalSeconds : runtime ?? 0;
                return (Row: pair.Start, Item: item, Runtime: runtime, Stop: stop, Seconds: runtime is { } limit ? Math.Min(elapsed, limit) : elapsed);
            })
            .ToList();

        var longest = paired.Where(p => p.Item?.Kind is ItemKind.Movie or ItemKind.Episode or ItemKind.Video && p.Runtime is not null)
            .Select(p => p.Runtime!.Value)
            .DefaultIfEmpty()
            .Max();

        return [.. paired
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
            .Where(p => p.Seconds >= MinSeconds)
            .OrderBy(p => p.Start)];
    }
}
