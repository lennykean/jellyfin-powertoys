using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using JellyfinPowertoys.Jellywrap.Context;

using Microsoft.Extensions.Logging;

namespace JellyfinPowertoys.Jellywrap.Audio;

internal sealed class AudioPlanner(WrapYear wrapYear, ClipFinder finder, ILogger logger)
{
    private const int Wanted = 9;

    private const int SearchedSeries = 15;
    private const int SearchedSeriesCredits = 10;
    private const int SearchedMovies = 5;
    private const int SearchBatch = 2;

    private static readonly SearchDepth _ownDepth = new SearchDepth(SearchedSeries, SearchedSeriesCredits, SearchedMovies);
    private static readonly SearchDepth _fillDepth = new SearchDepth(int.MaxValue, int.MaxValue, int.MaxValue);

    private readonly Dictionary<(Guid Id, ClipKind Kind), Task<Clip?>> _searches = [];

    private readonly Dictionary<Guid, Task<Clip?>> _themeSongs = [];

    public async Task<IReadOnlyList<PlannedClip>> PlanAsync(
        WrapContext context,
        IReadOnlyList<TimelineSlide> timeline,
        CancellationToken cancellationToken)
    {
        var name = context.Me?.Name ?? context.Server.Name;
        var videoLength = timeline[^1].Start + timeline[^1].Duration;
        var viewer = ViewerOf(context);
        var featuredTitles = timeline.Select(slide => FeaturedTitle(slide, viewer)).ToList();
        var (ranked, byFeaturedTitle) = await CandidatesAsync(
            viewer,
            [.. featuredTitles.OfType<Guid>().Distinct()],
            _ownDepth,
            [],
            videoLength,
            cancellationToken);
        var featured = featuredTitles.Select(title => title is { } id ? byFeaturedTitle.GetValueOrDefault(id, []) : []).ToList();
        var serverFill = new List<Clip>();
        var ownClips = ranked.Concat(featured.SelectMany(clips => clips)).ToList();
        if (context.Me is not null && ClipArranger.Coverable(videoLength, ownClips) < videoLength)
        {
            // The person's context lists the server's top titles under the person's showing rule.
            var serverTitles = viewer with { Series = context.Server.TopSeries, Movies = context.Server.TopMovies, Songs = [] };
            var (serverRanked, _) = await CandidatesAsync(serverTitles, [], _fillDepth, ownClips, videoLength, cancellationToken);
            var ownMusic = ownClips.Select(clip => (clip.TitleId, clip.Kind)).ToHashSet();
            serverFill = [.. serverRanked.Where(clip => !ownMusic.Contains((clip.TitleId, clip.Kind)))];
        }

        var spans = ClipArranger.Arrange(name, timeline, featured, ranked, serverFill);
        if (spans.Count == 0)
        {
            logger.LogWarning(
                "The Jellywrap video of {Name} has no sound: {Seconds:F1} s of usable music cannot cover the {Length:F1} s video",
                name,
                ClipArranger.Coverable(videoLength, ownClips.Concat(serverFill)),
                videoLength);
        }

        return [.. spans.Select(span => new PlannedClip(span.Clip.Path, span.Clip.In, span.Duration, span.Clip.Title, span.Clip.Kind))];
    }

    private static IReadOnlyList<MediaItem> SeasonEpisodes(IEnumerable<MediaItem> episodes, IEnumerable<Guid> played)
    {
        var seriesEpisodes = episodes.ToList();
        var byId = seriesEpisodes.ToDictionary(e => e.Id);
        var watched = played
            .Where(byId.ContainsKey)
            .CountBy(id => id)
            .Select(w => (Item: byId[w.Key], Plays: w.Value))
            .ToList();
        var season = watched
            .GroupBy(w => w.Item.Season, w => w.Plays)
            .OrderByDescending(g => g.Sum())
            .Select(g => g.Key)
            .FirstOrDefault();
        var mostPlayed = watched.OrderByDescending(w => w.Plays).Select(w => w.Item).Where(e => e.Season == season).ToList();
        var rest = seriesEpisodes.Where(e => e.Season == season && mostPlayed.All(m => m.Id != e.Id)).OrderBy(e => e.Episode ?? 0);
        // Copies of one episode would match each other on everything, not only on the theme.
        return [.. mostPlayed.Concat(rest).DistinctBy(e => e.Episode is { } number ? (object)number : e.Id).Take(ClipFinder.MatchedEpisodes)];
    }

    private static bool Enough(IEnumerable<Clip> found, double videoLength)
    {
        var clips = found.DistinctBy(clip => clip.MusicId).ToList();
        return clips.Count >= Wanted && ClipArranger.Coverable(videoLength, clips) >= videoLength;
    }

    private Viewer ViewerOf(WrapContext context)
    {
        var me = context.Me;
        var plays = me is null ? wrapYear.Plays : [.. wrapYear.Plays.Where(p => p.UserId == me.Id)];
        var visible = me is null ? null : wrapYear.VisibleByUser[me.Id];
        bool IsVisible(MediaItem item) => visible?.Contains(item.Id) ?? true;
        var seconds = plays.Where(p => p.Kind is not null).GroupBy(p => p.Kind!.Value, p => p.Seconds).ToDictionary(g => g.Key, g => g.Sum());
        var music = seconds.GetValueOrDefault(ItemKind.Song);
        var visiblePlays = plays.Where(p => p.Item is { } item && IsVisible(item)).ToList();
        var themeIds = visible is null
            ? (me?.TopSeries ?? []).Concat(context.Server.TopSeries).Select(title => title.Id)
            : plays.Concat(wrapYear.Plays)
                .Select(play => play.Item)
                .OfType<MediaItem>()
                .Where(item => item is { Kind: ItemKind.Episode, SeriesId: not null } && IsVisible(item))
                .Select(item => item.SeriesId!.Value);
        var themeSeries = themeIds
            .Distinct()
            .Select(wrapYear.ItemIndex.Get)
            .OfType<MediaItem>()
            .Where(series => series.ThemeSongs.Count > 0)
            .ToLookup(series => wrapYear.ItemIndex.CanonicalId(series.Id));
        return new Viewer(
            IsVisible,
            themeSeries,
            me?.TopSeries ?? context.Server.TopSeries,
            me?.TopMovies ?? context.Server.TopMovies,
            music > seconds.GetValueOrDefault(ItemKind.Episode) && music > seconds.GetValueOrDefault(ItemKind.Movie)
                ? [.. visiblePlays.Where(p => p.Kind == ItemKind.Song).GroupBy(p => p.Item!, p => p.Seconds).OrderByDescending(g => g.Sum()).Select(g => g.Key)]
                : []);
    }

    private Guid? FeaturedTitle(TimelineSlide slide, Viewer viewer)
    {
        if (slide.FeaturedItemIds.Count == 0 || !Guid.TryParse(slide.FeaturedItemIds[0], out var id))
        {
            return null;
        }

        var title = wrapYear.ItemIndex.Get(id) is { } item ? wrapYear.ItemIndex.TitleOf(item).Id : id;
        return viewer.Series.Concat(viewer.Movies).Any(t => t.Id == title) ? title : null;
    }

    private async Task<(List<Clip> Ranked, Dictionary<Guid, List<Clip>> ByFeaturedTitle)> CandidatesAsync(
        Viewer viewer,
        IReadOnlyList<Guid> featuredTitles,
        SearchDepth depth,
        IReadOnlyList<Clip> alreadyFound,
        double videoLength,
        CancellationToken cancellationToken)
    {
        var titles = viewer.Series.Concat(viewer.Movies).ToDictionary(t => t.Id);
        var series = viewer.Series.Take(depth.Series).Select(t => t.Id).ToList();
        var movies = viewer.Movies.Take(depth.Movies).Select(t => t.Id).ToList();
        var results = new Dictionary<(Guid Id, ClipKind Kind), Clip?>();

        bool Searchable((Guid Id, ClipKind Kind) key)
        {
            return key.Kind == ClipKind.Song
                || (titles.TryGetValue(key.Id, out var title)
                    && (title.Kind == ItemKind.Series.ToContextKind() || (title.Kind == ItemKind.Movie.ToContextKind() && key.Kind == ClipKind.Credits)));
        }

        async Task SearchAllAsync(IEnumerable<(Guid Id, ClipKind Kind)> keys)
        {
            foreach (var key in keys.Where(k => Searchable(k) && !results.ContainsKey(k)).ToList())
            {
                if (key.Kind == ClipKind.Theme && await ThemeSongAsync(viewer, titles[key.Id], cancellationToken) is { } song)
                {
                    results[key] = song;
                    continue;
                }

                if (!_searches.TryGetValue(key, out var search))
                {
                    search = _searches[key] = FindAsync(viewer, key.Id, key.Kind, titles, cancellationToken);
                }

                results[key] = await search is { } clip && wrapYear.ItemIndex.Get(clip.SourceId) is { } source && viewer.Shows(source) ? clip : null;
            }
        }

        await SearchAllAsync(featuredTitles.SelectMany(id => new[] { (id, ClipKind.Theme), (id, ClipKind.Credits) }));
        var seriesThemes = series.Select(id => (id, ClipKind.Theme)).ToList();
        var topSeriesCredits = series.Take(depth.SeriesCredits).Select(id => (id, ClipKind.Credits)).ToList();
        var movieCredits = movies.Select(id => (id, ClipKind.Credits)).ToList();
        var songs = viewer.Songs.Select(s => (s.Id, ClipKind.Song)).ToList();
        foreach (var tier in new[] { seriesThemes, topSeriesCredits, movieCredits, songs })
        {
            foreach (var batch in tier.Where(k => Searchable(k) && !results.ContainsKey(k)).ToList().Chunk(SearchBatch))
            {
                if (Enough(alreadyFound.Concat(results.Values.OfType<Clip>()), videoLength))
                {
                    break;
                }

                await SearchAllAsync(batch);
            }
        }

        var ranked = seriesThemes
            .Concat(series.Select(id => (id, ClipKind.Credits)))
            .Concat(movieCredits)
            .Concat(songs)
            .Select(k => results.GetValueOrDefault(k))
            .OfType<Clip>()
            .ToList();
        var byFeaturedTitle = featuredTitles.ToDictionary(
            id => id,
            id => new[] { results.GetValueOrDefault((id, ClipKind.Theme)), results.GetValueOrDefault((id, ClipKind.Credits)) }.OfType<Clip>().ToList());
        return (ranked, byFeaturedTitle);
    }

    private async Task<Clip?> ThemeSongAsync(Viewer viewer, TitleRef title, CancellationToken cancellationToken)
    {
        foreach (var copy in viewer.ThemeSeries[title.Id])
        {
            if (!_themeSongs.TryGetValue(copy.Id, out var search))
            {
                search = _themeSongs[copy.Id] = finder.ThemeSongAsync(title.Id, title.Title, copy, cancellationToken);
            }

            if (await search is { } song)
            {
                return song;
            }
        }

        return null;
    }

    private Task<Clip?> FindAsync(
        Viewer viewer,
        Guid id,
        ClipKind kind,
        IReadOnlyDictionary<Guid, TitleRef> titles,
        CancellationToken cancellationToken)
    {
        if (kind == ClipKind.Song)
        {
            return finder.SongAsync(viewer.Songs.First(s => s.Id == id), cancellationToken);
        }

        var title = titles[id];
        if (title.Kind == ItemKind.Movie.ToContextKind())
        {
            var movie = wrapYear.Plays.Select(p => p.Item).OfType<MediaItem>().FirstOrDefault(item => wrapYear.ItemIndex.CanonicalId(item.Id) == id)
                ?? wrapYear.ItemIndex.Get(id);
            return movie is null ? Task.FromResult<Clip?>(null) : finder.MovieCreditsAsync(id, title.Title, movie, cancellationToken);
        }

        var episodes = SeasonEpisodes(wrapYear.ItemIndex.Episodes(id), wrapYear.Plays.Select(p => p.ItemId));
        return finder.MatchedAsync(id, title.Title, kind, episodes, cancellationToken);
    }

    private sealed record Viewer(
        Func<MediaItem, bool> Shows,
        ILookup<Guid, MediaItem> ThemeSeries,
        IReadOnlyList<TitleRef> Series,
        IReadOnlyList<TitleRef> Movies,
        IReadOnlyList<MediaItem> Songs);

    private sealed record SearchDepth(int Series, int SeriesCredits, int Movies);
}
