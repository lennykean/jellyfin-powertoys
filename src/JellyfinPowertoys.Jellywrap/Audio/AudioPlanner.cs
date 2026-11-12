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

    private readonly Dictionary<(Guid? Viewer, Guid Title, ClipKind Kind), Task<Clip?>> _searches = [];

    public async Task<IReadOnlyList<PlannedClip>> PlanAsync(
        WrapContext context,
        IReadOnlyList<TimelineSlide> timeline,
        CancellationToken cancellationToken)
    {
        var name = context.Me?.Name ?? context.Server.Name;
        var videoLength = timeline[^1].Start + timeline[^1].Duration;
        var viewer = ViewerOf(context);
        var featuredTitles = timeline.Select(slide => FeaturedTitle(slide, viewer)).ToList();
        var (ranked, byFeaturedTitle) = await CandidatesAsync(viewer, [.. featuredTitles.OfType<Guid>().Distinct()], videoLength, cancellationToken);
        var featured = featuredTitles.Select(title => title is { } id ? byFeaturedTitle.GetValueOrDefault(id, []) : []).ToList();
        var serverFill = new List<Clip>();
        if (context.Me is not null && wrapYear.Server is not null)
        {
            var ownClips = ranked.Concat(featured.SelectMany(clips => clips)).Select(clip => (clip.TitleId, clip.Kind)).ToHashSet();

            // The person's context lists the server's top titles under the person's showing rule.
            var visibleTitles = context.Server.TopSeries.Concat(context.Server.TopMovies).Select(title => title.Id).ToHashSet();
            var (serverRanked, _) = await CandidatesAsync(ViewerOf(wrapYear.Server), [], videoLength, cancellationToken);
            serverFill = [.. serverRanked
                .Where(clip => clip.Kind != ClipKind.Song
                    && !ownClips.Contains((clip.TitleId, clip.Kind))
                    && visibleTitles.Contains(clip.TitleId))];
        }

        var spans = ClipArranger.Arrange(name, timeline, featured, ranked, serverFill);
        if (spans.Count == 0)
        {
            logger.LogWarning(
                "The Jellywrap video of {Name} has no sound: {Seconds:F1} s of usable music cannot cover the {Length:F1} s video",
                name,
                ClipArranger.Coverable(videoLength, ranked.Concat(featured.SelectMany(clips => clips)).Concat(serverFill)),
                videoLength);
        }

        return [.. spans.Select(span => new PlannedClip(span.Clip.Path, span.Clip.In, span.Duration, span.Clip.Title, span.Clip.Kind.ToName()))];
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
        var mine = watched.OrderByDescending(w => w.Plays).Select(w => w.Item).Where(e => e.Season == season).ToList();
        var rest = seriesEpisodes.Where(e => e.Season == season && mine.All(m => m.Id != e.Id)).OrderBy(e => e.Episode ?? 0);
        return [.. mine.Concat(rest).Take(ClipFinder.MatchedEpisodes)];
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
        var shows = me is null ? _ => true : wrapYear.Rules[me.Id];
        var seconds = plays.Where(p => p.Kind is not null).GroupBy(p => p.Kind!.Value, p => p.Seconds).ToDictionary(g => g.Key, g => g.Sum());
        var music = seconds.GetValueOrDefault(ItemKind.Song);
        var shown = plays.Where(p => p.Item is { } item && shows(item)).ToList();
        return new Viewer(
            me?.Id,
            shown,
            me?.TopSeries ?? context.Server.TopSeries,
            me?.TopMovies ?? context.Server.TopMovies,
            music > seconds.GetValueOrDefault(ItemKind.Episode) && music > seconds.GetValueOrDefault(ItemKind.Movie)
                ? [.. shown.Where(p => p.Kind == ItemKind.Song).GroupBy(p => p.Item!, p => p.Seconds).OrderByDescending(g => g.Sum()).Select(g => g.Key)]
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
        double videoLength,
        CancellationToken cancellationToken)
    {
        var titles = viewer.Series.Concat(viewer.Movies).ToDictionary(t => t.Id);
        var series = viewer.Series.Take(SearchedSeries).Select(t => t.Id).ToList();
        var movies = viewer.Movies.Take(SearchedMovies).Select(t => t.Id).ToList();
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
                if (!_searches.TryGetValue((viewer.Id, key.Id, key.Kind), out var search))
                {
                    search = _searches[(viewer.Id, key.Id, key.Kind)] = FindAsync(viewer, key.Id, key.Kind, titles, cancellationToken);
                }

                results[key] = await search;
            }
        }

        await SearchAllAsync(featuredTitles.SelectMany(id => new[] { (id, ClipKind.Theme), (id, ClipKind.Credits) }));
        var seriesThemes = series.Select(id => (id, ClipKind.Theme)).ToList();
        var topSeriesCredits = series.Take(SearchedSeriesCredits).Select(id => (id, ClipKind.Credits)).ToList();
        var movieCredits = movies.Select(id => (id, ClipKind.Credits)).ToList();
        var songs = viewer.Songs.Select(s => (s.Id, ClipKind.Song)).ToList();
        foreach (var tier in new[] { seriesThemes, topSeriesCredits, movieCredits, songs })
        {
            foreach (var batch in tier.Where(k => Searchable(k) && !results.ContainsKey(k)).ToList().Chunk(SearchBatch))
            {
                if (Enough(results.Values.OfType<Clip>(), videoLength))
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
            var movie = viewer.Plays.Select(p => p.Item!).FirstOrDefault(item => wrapYear.ItemIndex.CanonicalId(item.Id) == id) ?? wrapYear.ItemIndex.Get(id);
            return movie is null ? Task.FromResult<Clip?>(null) : finder.MovieCreditsAsync(id, title.Title, movie, cancellationToken);
        }

        var episodes = SeasonEpisodes(wrapYear.ItemIndex.Episodes(id), viewer.Plays.Select(p => p.ItemId));
        return kind == ClipKind.Theme
            ? finder.ThemeAsync(id, title.Title, wrapYear.ItemIndex.Get(id)?.ThemeSongs ?? [], episodes, cancellationToken)
            : finder.SeriesCreditsAsync(id, title.Title, episodes, cancellationToken);
    }

    private sealed record Viewer(
        Guid? Id,
        IReadOnlyList<Play> Plays,
        IReadOnlyList<TitleRef> Series,
        IReadOnlyList<TitleRef> Movies,
        IReadOnlyList<MediaItem> Songs);
}
