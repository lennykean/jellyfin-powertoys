using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed class StatsCalculator(ItemIndex itemIndex, int year, Func<MediaItem, bool> shows)
{
    private const double SecondsPerMinute = 60;
    private const double SecondsPerHour = 3600;
    private const double SecondsPerDay = 86400;
    private const double SessionGap = 1800;

    private const double MinMusicSeconds = 1800;
    private const double MinStartedSeconds = 120;
    private const double AbandonedShare = 0.5;
    private const int RecentAge = 1;
    private const int ClassicAge = 25;

    private const int TopActorCount = 10;
    private const int MorningStart = 5;

    private const int AfternoonStart = 12;
    private const int EveningStart = 17;
    private const int NightStart = 22;

    private static readonly HashSet<string> _formatGenres = ["Animation", "Kids", "Family"];
    private static readonly string[] _dayParts = ["morning", "afternoon", "evening", "night"];
    private static readonly DayOfWeek[] _weekdays = [.. Enumerable.Range(1, 7).Select(d => (DayOfWeek)(d % 7))];

    public UserStats User(
        SourceUser user,
        bool isKid,
        IReadOnlyList<Play> plays,
        IReadOnlyList<Play> prior)
    {
        var shown = Shown(plays);
        var allShown = shown.Count == plays.Count(p => p.Item is not null);
        var allTitles = TitlesByHours(plays);
        var titles = allShown ? allTitles : TitlesByHours(shown);
        var allGenres = Genres(plays);
        var genres = allShown ? allGenres : Genres(shown);
        var allBinge = BestBinge(plays, _ => true);
        var binge = allShown ? allBinge : BestBinge(plays, shows);
        var allReplayed = MostReplayed(plays);
        var replayed = allShown ? allReplayed : MostReplayed(shown);
        var rhythm = Rhythm(plays);
        var metrics = Metrics(plays, prior, allTitles, allGenres, rhythm, allBinge?.Episodes ?? 0);
        var subjects = new PersonaSubjects(
            titles.FirstOrDefault(t => t.Id == allTitles.FirstOrDefault()?.Id),
            titles.FirstOrDefault(t => t.Id == allBinge?.Series),
            allReplayed is not null && shows(allReplayed) ? ReplayedTitle(allReplayed, shown) : null);

        return new UserStats(
            user.Id,
            user.Name,
            $"{user.Name}'s",
            user.Avatar,
            isKid,
            isKid ? "kid" : "grownup",
            Totals(plays),
            rhythm,
            Mix(plays),
            genres,
            genres.FirstOrDefault(g => !_formatGenres.Contains(g.Name)) ?? genres.FirstOrDefault(),
            [.. titles.Where(t => t.Kind == ItemKind.Series.ToContextKind())],
            [.. titles.Where(t => t.Kind == ItemKind.Movie.ToContextKind())],
            Actors(shown),
            binge is null
                ? null
                : new Binge(
                    DateOnly.FromDateTime(binge.Plays[0].Local),
                    binge.Episodes,
                    titles.First(t => t.Id == binge.Series),
                    binge.Plays.Sum(p => p.Seconds) / SecondsPerHour),
            Clock(plays),
            Busiest(plays),
            replayed is null ? null : ReplayedTitle(replayed, shown),
            NewVsClassic(plays, titles),
            Music(plays, shown),
            PersonaScorer.Pick(metrics, isKid, subjects),
            metrics);
    }

    public GroupStats Groups(IReadOnlyList<UserStats> users, IReadOnlyList<Play> plays)
    {
        Group GroupOf(bool isKid)
        {
            var members = users.Where(u => u.IsKid == isKid).ToList();
            var ids = members.Select(u => u.Id).ToHashSet();
            var shown = Shown(plays.Where(p => ids.Contains(p.UserId)));
            var hours = members.Sum(u => u.Totals.Hours);
            return new Group(
                members.Count,
                [.. members.Select(u => u.Name)],
                hours,
                members.Count > 0 ? hours / members.Count : 0,
                members.Sum(u => u.Totals.Episodes),
                members.Sum(u => u.Totals.Movies),
                TitlesByHours(shown).FirstOrDefault(),
                Genres(shown).FirstOrDefault());
        }

        var kids = GroupOf(isKid: true);
        var grownups = GroupOf(isKid: false);
        var hasSplit = kids.Count > 0 && grownups.Count > 0;
        return new GroupStats(
            kids,
            grownups,
            hasSplit,
            kids.Count == 1,
            hasSplit ? Leader(kids.HoursPerPerson, grownups.HoursPerPerson) : null,
            hasSplit ? Leader((double)kids.Episodes / kids.Count, (double)grownups.Episodes / grownups.Count) : null);
    }

    public ServerStats Server(
        string name,
        IReadOnlyList<UserStats> users,
        IReadOnlyList<UserStats> inServerOrder,
        IReadOnlyList<Play> plays,
        Growth growth)
    {
        var shown = Shown(plays);
        var titles = TitlesByHours(shown);
        var kids = users.Where(u => u.IsKid).Select(u => u.Id).ToHashSet();
        var commonGround = shown
            .Where(p => p.IsVideo)
            .GroupBy(p => itemIndex.TitleOf(p.Item!).Id)
            .Select(titlePlays => (
                Id: titlePlays.Key,
                Kids: titlePlays.Where(p => kids.Contains(p.UserId)).Sum(p => p.Seconds),
                Grownups: titlePlays.Where(p => !kids.Contains(p.UserId)).Sum(p => p.Seconds)))
            .OrderByDescending(t => Math.Min(t.Kids, t.Grownups))
            .FirstOrDefault(t => Math.Min(t.Kids, t.Grownups) > 0);

        Award? Most(Func<UserStats, double> value)
        {
            return inServerOrder.MaxBy(value) is { } winner && value(winner) > 0 ? new Award(winner.ToRef(), value(winner)) : null;
        }

        var isDefaultName = name == "Jellyfin";
        return new ServerStats(
            name,
            isDefaultName,
            isDefaultName ? "Our" : $"{name}'s",
            isDefaultName ? "our" : $"{name}'s",
            Totals(plays),
            [.. titles.Where(t => t.Kind == ItemKind.Series.ToContextKind())],
            [.. titles.Where(t => t.Kind == ItemKind.Movie.ToContextKind())],
            Genres(shown),
            Busiest(plays),
            growth,
            WhoOwnsWhen(plays, kids),
            commonGround.Id == Guid.Empty ? null : titles.First(t => t.Id == commonGround.Id),
            commonGround.Kids / SecondsPerHour,
            commonGround.Grownups / SecondsPerHour,
            titles.OrderByDescending(t => t.Viewers).FirstOrDefault(),
            [.. users.Select((user, index) => new LeaderRow(index + 1, user.ToRef(), user.Totals.Hours))],
            new Superlatives(
                Most(u => u.Totals.Movies),
                Most(u => u.Totals.Episodes),
                Most(u => u.Metrics.RepeatCompletions),
                Most(u => u.Totals.MusicHours)),
            [.. users.OrderBy(u => u.IsKid).Select(u => u.ToRef())],
            Music(plays, shown));
    }

    private List<Play> Shown(IEnumerable<Play> plays)
    {
        return [.. plays.Where(p => p.Item is { } item && shows(item))];
    }

    private static string Leader(double kids, double grownups)
    {
        return kids >= grownups ? "kids" : "grownups";
    }

    private static double Share(IReadOnlyList<Play> plays, Func<Play, bool> where)
    {
        var total = plays.Sum(p => p.Seconds);
        return total > 0 ? plays.Where(where).Sum(p => p.Seconds) / total : 0;
    }

    private Totals Totals(IReadOnlyList<Play> plays)
    {
        var seconds = plays.Sum(p => p.Seconds);
        return new Totals(
            seconds / SecondsPerHour,
            seconds / SecondsPerDay,
            plays.Count,
            plays.Count(p => p is { Kind: ItemKind.Episode, Completed: true }),
            plays.Count(p => p is { Kind: ItemKind.Movie, Completed: true }),
            plays.Where(p => p.Kind == ItemKind.Song).Sum(p => p.Seconds) / SecondsPerHour,
            plays.Where(p => p.IsVideo).Select(p => itemIndex.TitleOf(p.Item!).Id).Distinct().Count(),
            plays.Select(p => p.UserId).Distinct().Count());
    }

    private static Mix Mix(IReadOnlyList<Play> plays)
    {
        var tv = plays.Where(p => p.Kind == ItemKind.Episode).Sum(p => p.Seconds);
        var movies = plays.Where(p => p.Kind == ItemKind.Movie).Sum(p => p.Seconds);
        var music = plays.Where(p => p.Kind == ItemKind.Song).Sum(p => p.Seconds);
        var total = tv + movies + music;
        return total > 0 ? new Mix(tv / total, movies / total, music / total) : new Mix(0, 0, 0);
    }

    private static Rhythm Rhythm(IReadOnlyList<Play> plays)
    {
        var days = plays.Select(p => DateOnly.FromDateTime(p.Local)).Distinct().Order().ToList();
        var longest = days
            .Select((day, index) => (Day: day, Run: day.DayNumber - index))
            .GroupBy(d => d.Run, d => d.Day)
            .MaxBy(run => run.Count())
            ?.ToList();
        return new Rhythm(days.Count, longest?.Count ?? 0, longest?[0], longest?[^1]);
    }

    private static Clock Clock(IReadOnlyList<Play> plays)
    {
        var total = plays.Sum(p => p.Seconds);
        var byHour = plays.GroupBy(p => p.Local.Hour).ToDictionary(g => g.Key, g => g.Sum(p => p.Seconds));
        var hours = Enumerable.Range(0, 24).Select(hour => byHour.GetValueOrDefault(hour)).ToList();
        return new Clock(
            Enumerable.Range(0, 24).MaxBy(hour => hours[hour]),
            [.. hours.Select(seconds => seconds / total)],
            Share(plays, p => IsNight(p.Local.Hour)),
            Share(plays, p => p.Local.Hour is >= MorningStart and < AfternoonStart),
            plays
                .Where(p => DayPartOf(p.Local) != "night")
                .GroupBy(p => DayPartOf(p.Local), p => p.Seconds)
                .MaxBy(g => g.Sum())
                ?.Key);
    }

    private static Busiest Busiest(IReadOnlyList<Play> plays)
    {
        var month = plays.GroupBy(p => p.Local.Month, p => p.Seconds).MaxBy(g => g.Sum())!;
        var day = plays.GroupBy(p => DateOnly.FromDateTime(p.Local), p => p.Seconds).MaxBy(g => g.Sum())!;
        var starts = plays.CountBy(p => p.Local.Hour).ToDictionary();
        return new Busiest(
            CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month.Key),
            month.Sum() / SecondsPerHour,
            day.Key,
            day.Sum() / SecondsPerHour,
            Enumerable.Range(0, 24).MaxBy(hour => starts.GetValueOrDefault(hour)));
    }

    private static string DayPartOf(DateTime local)
    {
        return local.Hour switch
        {
            >= MorningStart and < AfternoonStart => _dayParts[0],
            >= AfternoonStart and < EveningStart => _dayParts[1],
            >= EveningStart and < NightStart => _dayParts[2],
            _ => _dayParts[3],
        };
    }

    private static bool IsNight(int hour)
    {
        return hour is >= NightStart or < MorningStart;
    }

    private static List<SlotShare> WhoOwnsWhen(IReadOnlyList<Play> plays, IReadOnlySet<Guid> kids)
    {
        var total = plays.Sum(p => p.Seconds);
        var slots = plays
            .GroupBy(p => (Day: (p.Local.Hour < MorningStart ? p.Local.AddDays(-1) : p.Local).DayOfWeek, Part: DayPartOf(p.Local)))
            .ToDictionary(g => g.Key, g => (Kids: g.Where(p => kids.Contains(p.UserId)).Sum(p => p.Seconds), All: g.Sum(p => p.Seconds)));
        return
        [
            .. _weekdays.SelectMany(day => _dayParts.Select(part =>
            {
                var slot = slots.GetValueOrDefault((day, part));
                return new SlotShare(day.ToString(), part, slot.All > 0 ? slot.Kids / slot.All : 0, total > 0 ? slot.All / total : 0);
            })),
        ];
    }

    private List<TitleRef> TitlesByHours(IReadOnlyList<Play> plays)
    {
        var titles = plays
            .Where(p => p.IsVideo)
            .GroupBy(p => itemIndex.TitleOf(p.Item!).Id)
            .Select(g => (Key: itemIndex.TitleOf(g.Last().Item!), Plays: g.ToList()))
            .ToList();
        var sharedNames = titles.CountBy(t => t.Key.Name).Where(n => n.Value > 1).Select(n => n.Key).ToHashSet();

        return
        [
            .. titles
                .Select(t => (Title: t, Year: itemIndex.YearOf(t.Key.Id)))
                .Select(dated => NewTitleRef(
                    dated.Title.Key.Id,
                    sharedNames.Contains(dated.Title.Key.Name) && dated.Year is { } titleYear ? $"{dated.Title.Key.Name} ({titleYear})" : dated.Title.Key.Name,
                    dated.Year,
                    dated.Title.Key.Kind,
                    null,
                    itemIndex.Image(dated.Title.Key.Id),
                    dated.Title.Plays))
                .OrderByDescending(t => t.Hours),
        ];
    }

    private TitleRef ReplayedTitle(MediaItem item, IReadOnlyList<Play> plays)
    {
        var title = itemIndex.CanonicalId(item.Id);
        return TitleRefOfItem(item, [.. plays.Where(p => itemIndex.CanonicalId(p.ItemId) == title)]);
    }

    private TitleRef TitleRefOfItem(MediaItem item, IReadOnlyList<Play> plays)
    {
        return NewTitleRef(
            item.Id,
            item.Name,
            item.Year,
            item.Kind.ToContextKind(),
            item.Kind == ItemKind.Song ? item.Artist?.Name : item.SeriesName,
            item.Kind == ItemKind.Song ? itemIndex.Image(item.Id) : itemIndex.PosterFor(item),
            plays);
    }

    private static TitleRef NewTitleRef(
        Guid id,
        string title,
        int? year,
        string kind,
        string? seriesTitle,
        ImageRef? poster,
        IReadOnlyList<Play> plays)
    {
        return new TitleRef(
            id,
            title,
            year,
            kind,
            seriesTitle,
            poster,
            plays.Sum(p => p.Seconds) / SecondsPerHour,
            plays.Count,
            plays.Count(p => p.Completed),
            plays.Select(p => p.UserId).Distinct().Count());
    }

    private List<GenreShare> Genres(IReadOnlyList<Play> plays)
    {
        var genres = plays
            .Where(p => p.IsVideo)
            .SelectMany(play =>
            {
                var names = itemIndex.GenresOf(play.Item!);
                return names.Select(name => (Name: name, Seconds: play.Seconds / names.Count));
            })
            .GroupBy(g => g.Name, g => g.Seconds)
            .Select(g => (Name: g.Key, Seconds: g.Sum()))
            .ToList();
        var total = genres.Sum(g => g.Seconds);
        return [.. genres.OrderByDescending(g => g.Seconds).Select(g => new GenreShare(g.Name, g.Seconds / total, g.Seconds / SecondsPerHour))];
    }

    private List<PersonRef> Actors(IReadOnlyList<Play> plays)
    {
        return
        [
            .. plays
                .Where(p => p.IsVideo)
                .SelectMany(p => itemIndex.ActorsOf(p.Item!).Select(a => (Actor: a, p.Seconds)))
                .GroupBy(a => a.Actor, a => a.Seconds)
                .Select(g => new PersonRef(g.Key.Id, g.Key.Name, g.Key.Image, g.Sum() / SecondsPerHour))
                .OrderByDescending(a => a.Hours)
                .Take(TopActorCount),
        ];
    }

    private MediaItem? MostReplayed(IReadOnlyList<Play> plays)
    {
        var most = plays
            .Where(p => p is { IsVideo: true, Completed: true })
            .GroupBy(p => itemIndex.CanonicalId(p.ItemId))
            .MaxBy(g => g.Count());
        return most?.Count() > 1 ? most.First().Item : null;
    }

    private static IEnumerable<List<Play>> Sessions(IReadOnlyList<Play> plays)
    {
        var session = new List<Play>();
        var end = DateTime.MinValue;
        foreach (var play in plays)
        {
            if (session.Count > 0 && (play.Start - end).TotalSeconds > SessionGap)
            {
                yield return session;
                session = [];
            }

            end = session.Count == 0 || play.End > end ? play.End : end;
            session.Add(play);
        }

        if (session.Count > 0)
        {
            yield return session;
        }
    }

    private SessionSeries? BestBinge(IReadOnlyList<Play> plays, Func<MediaItem, bool> counts)
    {
        return Sessions(plays)
            .SelectMany(session => session
                .Where(p => p.Item is { Kind: ItemKind.Episode, SeriesId: not null } item && counts(item))
                .GroupBy(p => itemIndex.CanonicalId(p.Item!.SeriesId!.Value)))
            .Select(g => new SessionSeries(g.Key, g.Select(p => p.ItemId).Distinct().Count(), [.. g]))
            .OrderByDescending(b => b.Episodes)
            .FirstOrDefault();
    }

    private NewVsClassic NewVsClassic(IReadOnlyList<Play> plays, IReadOnlyList<TitleRef> titles)
    {
        var dated = plays.Where(p => p.IsVideo && p.Item!.Year is not null).ToList();
        var oldest = titles
            .Where(t => t.Year is not null)
            .OrderBy(t => t.Year)
            .ThenBy(t => t.Title, StringComparer.Ordinal)
            .ThenBy(t => t.Id.ToString("N"), StringComparer.Ordinal)
            .FirstOrDefault();

        // The slide prints "{title} ({year})", and Jellyfin names a remade series "Name (Year)" to tell it apart.
        var suffix = $" ({oldest?.Year})";
        return new NewVsClassic(
            Share(dated, p => p.Item!.Year >= year - RecentAge),
            Share(dated, p => p.Item!.Year <= year - ClassicAge),
            oldest is not null && oldest.Title.EndsWith(suffix, StringComparison.Ordinal) ? oldest with { Title = oldest.Title[..^suffix.Length] } : oldest);
    }

    private MusicStats? Music(IReadOnlyList<Play> plays, IReadOnlyList<Play> shown)
    {
        var seconds = plays.Where(p => p.Kind == ItemKind.Song).Sum(p => p.Seconds);
        if (seconds < MinMusicSeconds)
        {
            return null;
        }

        var songs = shown.Where(p => p.Kind == ItemKind.Song).ToList();
        return new MusicStats(
            seconds / SecondsPerHour,
            songs
                .Where(p => p.Item!.Artist is not null)
                .GroupBy(p => p.Item!.Artist!, p => p.Seconds)
                .Select(g => new PersonRef(g.Key.Id, g.Key.Name, g.Key.Image, g.Sum() / SecondsPerHour))
                .MaxBy(a => a.Hours),
            [
                .. songs
                    .GroupBy(p => p.ItemId)
                    .Select(g => TitleRefOfItem(g.First().Item!, [.. g]))
                    .OrderByDescending(s => s.Plays)
                    .ThenByDescending(s => s.Hours),
            ]);
    }

    private PersonaMetrics Metrics(
        IReadOnlyList<Play> plays,
        IReadOnlyList<Play> prior,
        IReadOnlyList<TitleRef> titles,
        IReadOnlyList<GenreShare> genres,
        Rhythm rhythm,
        int bingeEpisodes)
    {
        var total = plays.Sum(p => p.Seconds);
        var video = plays.Where(p => p.IsVideo).ToList();
        var videoSeconds = video.Sum(p => p.Seconds);
        var music = plays.Where(p => p.Kind == ItemKind.Song).Sum(p => p.Seconds);
        var started = video.Where(p => p.Item!.Runtime > 0 && p.Seconds >= MinStartedSeconds).ToList();
        var completedBefore = prior.Where(p => p is { IsVideo: true, Completed: true }).Select(p => itemIndex.CanonicalId(p.ItemId)).ToHashSet();
        var completions = video.Where(p => p.Completed).CountBy(p => itemIndex.CanonicalId(p.ItemId)).ToList();
        var completionCount = completions.Sum(c => c.Value);
        var repeats = completions.Sum(c => completedBefore.Contains(c.Key) ? c.Value : c.Value - 1);
        var dated = video.Where(p => p.Item!.Year is not null).ToList();
        return new PersonaMetrics(
            total / SecondsPerMinute,
            Share(plays, p => IsNight(p.Local.Hour)),
            Share(plays, p => p.Local.Hour is >= 5 and < 9),
            Share(plays, p => p.Local.Hour is >= 6 and < 9),
            Share(plays, p => p.Local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday),
            rhythm.Streak,
            bingeEpisodes,
            titles.Count > 0 && videoSeconds > 0 ? titles[0].Hours * SecondsPerHour / videoSeconds : 0,
            started.Count,
            started.Count > 0 ? (double)started.Count(p => p.Completed) / started.Count : 0,
            started.Count > 0 ? (double)started.Count(p => p.Seconds < AbandonedShare * p.Item!.Runtime) / started.Count : 0,
            repeats,
            completionCount > 0 ? (double)repeats / completionCount : 0,
            Share(dated, p => p.Item!.Year <= year - ClassicAge),
            Share(dated, p => p.Item!.Year >= year - RecentAge),
            genres.FirstOrDefault()?.Share ?? 0,
            genres.Count(g => g.Hours >= 1),
            video.Count(p => p is { Kind: ItemKind.Movie, Completed: true }),
            videoSeconds > 0 ? video.Where(p => p.Kind == ItemKind.Movie).Sum(p => p.Seconds) / videoSeconds : 0,
            total > 0 ? music / total : 0,
            music / SecondsPerMinute,
            video.Where(p => p.Completed).Select(p => itemIndex.TitleOf(p.Item!).Id).Distinct().Count());
    }

    private sealed record SessionSeries(Guid Series, int Episodes, IReadOnlyList<Play> Plays);
}
