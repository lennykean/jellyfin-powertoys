using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Data.Queries;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;

using JellyfinPowertoys.Jellywrap.Configuration;
using JellyfinPowertoys.Jellywrap.Library;

using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.MediaInfo;

using AudioItem = MediaBrowser.Controller.Entities.Audio.Audio;

namespace JellyfinPowertoys.Jellywrap.Context;

public sealed class WrapSourceReader(
    ILibraryManager libraryManager,
    IUserManager userManager,
    IActivityManager activityManager,
    IServerApplicationHost host,
    ILocalizationManager localization)
{
    private const int PageSize = 1000;

    private static readonly BaseItemKind[] _libraryKinds = [BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.Audio, BaseItemKind.AudioBook, BaseItemKind.Video];
    internal async Task<IReadOnlyList<int>> ReadYearsWithPlaysAsync()
    {
        var years = new List<int>();
        for (var year = DateTime.Today.Year; ; year--)
        {
            var start = TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, 1, 1), TimeZoneInfo.Local);
            var end = TimeZoneInfo.ConvertTimeToUtc(new DateTime(year + 1, 1, 1), TimeZoneInfo.Local);
            if (await HasPlaysAsync(start, end))
            {
                years.Add(year);
            }

            if (!await HasPlaysAsync(null, start))
            {
                return years;
            }
        }
    }

    internal async Task<WrapSource> ReadAsync(
        int year,
        DateTimeOffset now,
        IReadOnlyList<UserSettings> userSettings,
        CancellationToken cancellationToken)
    {
        var entries = new List<ActivityLogEntry>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await activityManager
                .GetPagedResultAsync(new ActivityLogQuery { Type = "Playback", MaxDate = now.UtcDateTime, Skip = entries.Count, Limit = PageSize });
            entries.AddRange(page.Items);
            if (page.Items.Count == 0 || entries.Count >= page.TotalRecordCount)
            {
                break;
            }
        }

        var rows = entries
            .Where(e => e.Type.Contains("Playback", StringComparison.Ordinal))
            .Select(e => (Entry: e, ItemId: Guid.TryParse(e.ItemId, out var id) ? id : Guid.Empty))
            .Where(e => e.ItemId != Guid.Empty)
            .Select(e => new PlaybackRow(e.Entry.UserId, e.ItemId, !e.Entry.Type.EndsWith("Stopped", StringComparison.Ordinal), e.Entry.Date, e.Entry.Name))
            .ToList();

        var items = libraryManager
            .GetItemList(new InternalItemsQuery { IncludeItemTypes = _libraryKinds, Recursive = true })
            .ToDictionary(i => i.Id);
        foreach (var id in rows.Select(r => r.ItemId).Distinct().Where(id => !items.ContainsKey(id)).ToList())
        {
            if (libraryManager.GetItemById(id) is { } item)
            {
                items[id] = item;
            }
        }

        var ownItemIds = items.Values.Where(i => i.Tags.Any(JellywrapTags.IsJellywrapTag)).Select(i => i.Id).ToHashSet();
        var overrides = userSettings.ToDictionary(settings => settings.UserId, settings => settings.IsKid);
        var accounts = userManager.GetUsers().ToList();
        var basicItems = items.Values.Select(ToBasicItem).ToList();
        var users = accounts
            .Select(account => new SourceUser(
                account.Id,
                account.Username,
                account.MaxParentalRatingScore,
                overrides.GetValueOrDefault(account.Id),
                null,
                Avatar(account)))
            .ToList();
        rows = [.. ReplacedFileMatcher.Match(rows, basicItems, users).Where(r => !ownItemIds.Contains(r.ItemId))];

        var played = rows.Select(r => r.ItemId).Distinct().Where(items.ContainsKey).Select(id => items[id]).ToList();
        var playedWithSeries = played
            .Concat(played.OfType<Episode>().Select(e => e.Series).OfType<Series>())
            .DistinctBy(i => i.Id)
            .ToList();
        var fullItems = ToFullItems(playedWithSeries).ToDictionary(i => i.Id);
        var start = TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, 1, 1), TimeZoneInfo.Local);
        return new WrapSource
        {
            Year = year,
            GeneratedAt = now,
            TimeZone = TimeZoneInfo.Local,
            ServerName = host.FriendlyName,
            Users =
            [
                .. accounts.Zip(users, (account, user) => user with
                {
                    Visible = IsRestricted(account, user.IsKidOverride)
                        ? playedWithSeries.Where(i => i.IsVisibleStandalone(account)).Select(i => i.Id).ToHashSet()
                        : null,
                }),
            ],
            Activity = rows,
            Items = [.. basicItems.Select(i => fullItems.GetValueOrDefault(i.Id) ?? i).Concat(fullItems.Values.Where(i => !items.ContainsKey(i.Id)))],
            Growth = new Growth(Added(BaseItemKind.Movie, start), Added(BaseItemKind.Episode, start), Added(BaseItemKind.MusicAlbum, start)),
        };
    }

    private static bool IsRestricted(User user, bool? isKidOverride)
    {
        return isKidOverride == true
            || user.MaxParentalRatingScore is not null
            || user.GetPreferenceValues<string>(PreferenceKind.BlockedTags).Any(t => !JellywrapTags.IsJellywrapTag(t))
            || user.GetPreferenceValues<string>(PreferenceKind.AllowedTags).Length > 0
            || user.GetPreferenceValues<UnratedItem>(PreferenceKind.BlockUnratedItems).Length > 0;
    }

    private static ImageRef? Avatar(User user)
    {
        return user.ProfileImage is { } image ? new ImageRef(image.Path, 0, 0) : null;
    }

    private static ImageRef? Image(BaseItem? item, ImageType type)
    {
        return item?.GetImageInfo(type, 0) is { } image ? new ImageRef(image.Path, image.Width, image.Height) : null;
    }

    private static ItemKind KindOf(BaseItem item)
    {
        return item.GetBaseItemKind() switch
        {
            BaseItemKind.Movie => ItemKind.Movie,
            BaseItemKind.Series => ItemKind.Series,
            BaseItemKind.Episode => ItemKind.Episode,
            BaseItemKind.Audio => ItemKind.Song,
            BaseItemKind.AudioBook => ItemKind.AudioBook,
            _ => ItemKind.Video,
        };
    }

    private static MediaItem ToBasicItem(BaseItem item)
    {
        var series = (item as Episode)?.Series;
        return new MediaItem
        {
            Id = item.Id,
            Kind = KindOf(item),
            Name = item.Name ?? string.Empty,
            Year = item.ProductionYear is > 0 ? item.ProductionYear : item.PremiereDate?.Year,
            Runtime = item.RunTimeTicks is > 0 and var ticks ? TimeSpan.FromTicks(ticks).TotalSeconds : null,
            SeriesId = series?.Id,
            SeriesName = series?.Name,
            Season = item.ParentIndexNumber,
            Episode = item.IndexNumber,
            Artists = (item as AudioItem)?.Artists ?? [],
            Path = item.PathProtocol == MediaProtocol.File && !item.IsShortcut ? item.Path : null,
        };
    }

    private IEnumerable<MediaItem> ToFullItems(IReadOnlyList<BaseItem> items)
    {
        var people = libraryManager.GetPeopleByItems([.. items.Where(i => i is Video or Series).Select(i => i.Id)]);
        var images = new Dictionary<string, ImageRef?>();
        ImageRef? PersonImage(string name)
        {
            if (!images.TryGetValue(name, out var image))
            {
                image = images[name] = Image(libraryManager.GetPerson(name), ImageType.Primary);
            }

            return image;
        }

        Credit? ArtistOf(AudioItem song)
        {
            var name = song.AlbumArtists.FirstOrDefault(a => a.Length > 0) ?? song.Artists.FirstOrDefault();
            var artist = name is null ? null : libraryManager.GetArtist(name);
            return artist is null ? null : new Credit(artist.Id, artist.Name, Image(artist, ImageType.Primary));
        }

        foreach (var item in items)
        {
            var parents = item.GetParents().ToList();
            yield return ToBasicItem(item) with
            {
                ProviderKey = new[] { MetadataProvider.Tvdb, MetadataProvider.Tmdb, MetadataProvider.Imdb }
                    .Select(p => item.GetProviderId(p) is { Length: > 0 } value ? $"{p}={value}" : null)
                    .FirstOrDefault(k => k is not null),
                RatingScore = string.IsNullOrEmpty(item.OfficialRatingForComparison)
                    ? null
                    : localization.GetRatingScore(item.OfficialRatingForComparison)?.Score,
                Genres = item.Genres,
                Actors =
                [
                    .. people.GetValueOrDefault(item.Id, [])
                        .Where(p => p.Type == PersonKind.Actor)
                        .Select(p => new Credit(p.Id, p.Name, PersonImage(p.Name))),
                ],
                Artist = item is AudioItem song ? ArtistOf(song) : null,
                Primary = Image(item, ImageType.Primary),
                Thumb = Image(item, ImageType.Thumb),
                Backdrop = Image(item, ImageType.Backdrop),
                ParentImage = parents.Select(p => Image(p, ImageType.Primary)).FirstOrDefault(i => i is not null)
                    ?? parents.Select(p => Image(p, ImageType.Thumb)).FirstOrDefault(i => i is not null)
                    ?? parents.Select(p => Image(p, ImageType.Backdrop)).FirstOrDefault(i => i is not null),
                ThemeSongs = item is Series
                    ?
                    [
                        .. item.GetThemeSongs(null)
                            .Where(s => s.PathProtocol == MediaProtocol.File && s.RunTimeTicks > 0)
                            .Select(s => new ThemeSong(s.Path, TimeSpan.FromTicks(s.RunTimeTicks!.Value).TotalSeconds)),
                    ]
                    : [],
            };
        }
    }

    private async Task<bool> HasPlaysAsync(DateTime? fromUtc, DateTime beforeUtc)
    {
        var page = await activityManager.GetPagedResultAsync(new ActivityLogQuery { Type = "Playback", MinDate = fromUtc, MaxDate = beforeUtc, Limit = 1 });
        return page.Items.Count > 0;
    }

    private int Added(BaseItemKind kind, DateTime sinceUtc)
    {
        return libraryManager.GetCount(new InternalItemsQuery { IncludeItemTypes = [kind], MinDateCreated = sinceUtc, Recursive = true });
    }
}
