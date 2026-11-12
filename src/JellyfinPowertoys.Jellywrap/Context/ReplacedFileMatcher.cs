using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace JellyfinPowertoys.Jellywrap.Context;

internal static class ReplacedFileMatcher
{
    private static readonly ItemKind[] _playableKinds = [ItemKind.Movie, ItemKind.Episode, ItemKind.Song, ItemKind.AudioBook, ItemKind.Video];

    private static readonly Regex _played = new(@"^(?:is playing|has finished playing) (.+) on .+$", RegexOptions.Compiled);

    private static readonly Regex _anyUserPlayed = new(@"^.+? (?:is playing|has finished playing) (.+) on .+$", RegexOptions.Compiled);

    private static readonly Regex _episodeNumber = new(@"[Ss](\d{1,2})[Ee](\d{1,3})", RegexOptions.Compiled);

    private static readonly Regex _trackNumber = new(@"^\d{1,3}[ .-]+", RegexOptions.Compiled);

    public static IReadOnlyList<PlaybackRow> Match(
        IReadOnlyList<PlaybackRow> rows,
        IReadOnlyList<MediaItem> items,
        IReadOnlyList<SourceUser> users)
    {
        var liveIds = items.Select(i => i.Id).ToHashSet();
        var playable = items.Where(i => _playableKinds.Contains(i.Kind)).ToList();
        var byDisplayName = playable.ToLookup(DisplayName, i => i.Id, StringComparer.OrdinalIgnoreCase);
        var byEpisode = playable
            .Where(i => i is { Kind: ItemKind.Episode, SeriesName: not null, Season: not null, Episode: not null })
            .ToLookup(i => (Series: i.SeriesName!.ToUpperInvariant(), Season: i.Season!.Value, Episode: i.Episode!.Value), i => i.Id);
        var seriesNames = playable
            .Where(i => i is { Kind: ItemKind.Episode, SeriesName: not null })
            .Select(i => i.SeriesName!)
            .Distinct()
            .OrderByDescending(n => n.Length)
            .ToList();
        var songsByName = playable
            .Where(i => i.Kind == ItemKind.Song)
            .ToLookup(i => i.Name, i => i.Id, StringComparer.OrdinalIgnoreCase);
        var userNames = users.ToDictionary(u => u.Id, u => u.Name);

        Guid? OnlyOne(IEnumerable<Guid> ids)
        {
            var distinct = ids.Distinct().Take(2).ToList();
            return distinct.Count == 1 ? distinct[0] : null;
        }

        Guid? Find(string name)
        {
            if (byDisplayName.Contains(name))
            {
                return OnlyOne(byDisplayName[name]);
            }

            var number = _episodeNumber.Match(name);
            var series = number.Success
                ? seriesNames.FirstOrDefault(s => name.StartsWith(s + " - ", StringComparison.OrdinalIgnoreCase))
                : null;
            if (series is not null)
            {
                var key = (series.ToUpperInvariant(), int.Parse(number.Groups[1].Value), int.Parse(number.Groups[2].Value));
                if (OnlyOne(byEpisode[key]) is { } episode)
                {
                    return episode;
                }
            }

            return name.Contains(" - ", StringComparison.Ordinal) ? null : OnlyOne(songsByName[_trackNumber.Replace(name, string.Empty)]);
        }

        return [.. rows
            .Select(row => liveIds.Contains(row.ItemId) || LoggedName(row, userNames.GetValueOrDefault(row.UserId)) is not { } name || Find(name) is not { } id
                ? row
                : row with { ItemId = id })];
    }

    private static string DisplayName(MediaItem item)
    {
        var name = item.SeriesName is { Length: > 0 } series ? $"{series} - {item.Name}" : item.Name;
        return item.Artists.Count > 0 ? $"{item.Artists[0]} - {name}" : name;
    }

    private static string? LoggedName(PlaybackRow row, string? userName)
    {
        if (userName is not null
            && row.Name.StartsWith(userName + " ", StringComparison.Ordinal)
            && _played.Match(row.Name[(userName.Length + 1)..]) is { Success: true } ownMatch)
        {
            return ownMatch.Groups[1].Value;
        }

        var anyUserMatch = _anyUserPlayed.Match(row.Name);
        return anyUserMatch.Success ? anyUserMatch.Groups[1].Value : null;
    }
}
