using System;
using System.Linq;

namespace JellyfinPowertoys.Jellywrap.Context;

internal static class PersonaScorer
{
    private const double MinMinutes = 300;

    private static readonly Rule _casual = new("casual-viewer", "The Casual Viewer", _ => 0, (_, _) => "You watch when it suits you.");
    private static readonly Rule _happy = new("happy-watcher", "The Happy Watcher", _ => 0, (_, _) => "So many fun shows!");

    private static readonly Rule[] _adult =
    [
        new("night-owl", "The Night Owl", m => m.NightShare / 0.35, (_, _) => "Your best viewing happens after dark."),
        new("early-bird", "The Early Bird", m => m.EarlyShare / 0.20, (_, _) => "You start the day with the screen on."),
        new("weekend-warrior", "The Weekend Warrior", m => m.WeekendShare / 0.55, (_, _) => "Saturdays and Sundays are prime time."),
        new("daily-ritualist", "The Daily Ritualist", m => m.Streak / 30.0, (_, _) => "Jellyfin is part of every day."),
        new("binger", "The Binger", m => m.BingeEpisodes / 5.0, (_, _) => "One more episode? Always.", s => s.BingeSeries),
        new("superfan", "The Superfan", m => m.TopTitleShare / 0.25, (_, _) => "One title owned your year.", s => s.TopTitle),
        new("completionist", "The Completionist", m => m.Started >= 20 ? m.CompletedShare / 0.90 : 0, (_, _) => "You finish what you start."),
        new("sampler", "The Sampler", m => m.Started >= 20 ? m.AbandonedShare / 0.40 : 0, (_, _) => "A little of everything, then on to the next."),
        new("rewatcher", "The Rewatcher", m => m.RepeatCompletions >= 3 ? m.RepeatShare / 0.15 : 0, (_, _) => "Old favorites never get old.", s => s.MostReplayed),
        new("time-traveler", "The Time Traveler", m => m.ClassicShare / 0.40, (_, _) => "You live in the classics."),
        new("trendsetter", "The Trendsetter", m => m.RecentShare / 0.50, (_, _) => "Fresh releases are your thing."),
        new("genre-loyalist", "The Genre Loyalist", m => m.TopGenreShare / 0.45, (_, _) => "You know exactly what you like."),
        new("explorer", "The Explorer", m => m.TopGenreShare < 0.25 ? m.GenresOverAnHour / 10.0 : 0, (_, _) => "No genre can hold you."),
        new("cinephile", "The Cinephile", m => m.MovieShare >= 0.60 ? m.MoviesCompleted / 40.0 : 0, (_, _) => "Movies are your medium."),
        new("audiophile", "The Audiophile", m => m.MusicMinutes >= 600 ? m.MusicShare / 0.50 : 0, (_, _) => "Your Jellyfin sounds as good as it looks."),
    ];

    private static readonly Rule[] _kid =
    [
        new(
            "super-fan",
            "The Super Fan",
            m => m.TopTitleShare / 0.30,
            (_, subject) => subject is { Completions: > 1 } && subject.Kind == ItemKind.Series.ToContextKind()
                ? $"You loved {subject.Title}! {subject.Completions} episodes!"
                : "You found a favorite and watched it lots!",
            s => s.TopTitle),
        new(
            "replay-champion",
            "The Replay Champion",
            m => m.RepeatCompletions >= 5 ? m.RepeatShare / 0.25 : 0,
            (_, subject) => subject is null
                ? "You watched your favorite again and again!"
                : $"You watched {(subject.Kind == ItemKind.Episode.ToContextKind() ? $"'{subject.Title}'" : subject.Title)} {subject.Completions} times!",
            s => s.MostReplayed),
        new("movie-night-star", "The Movie Night Star", m => m.MovieShare >= 0.50 ? m.MoviesCompleted / 10.0 : 0, (m, _) => $"{m.MoviesCompleted} movies!"),
        new("story-explorer", "The Story Explorer", m => m.TopTitleShare < 0.25 ? m.TitlesCompleted / 15.0 : 0, (m, _) => $"{m.TitlesCompleted} different stories!"),
        new("weekend-watcher", "The Weekend Watcher", m => m.WeekendShare / 0.55, (_, _) => "Weekends are for shows!"),
        new("breakfast-watcher", "The Breakfast Watcher", m => m.BreakfastShare / 0.25, (_, _) => "Shows with breakfast!"),
        new("music-star", "The Music Star", m => m.MusicMinutes >= 300 ? m.MusicShare / 0.40 : 0, (_, _) => "You love to sing and dance!"),
    ];

    public static Persona Pick(PersonaMetrics metrics, bool isKid, PersonaSubjects subjects)
    {
        var fallback = isKid ? _happy : _casual;
        if (metrics.TotalMinutes < MinMinutes)
        {
            return Make(fallback, 0, null, metrics, subjects);
        }

        var scores = (isKid ? _kid : _adult)
            .Select(rule => (Rule: rule, Score: rule.Score(metrics)))
            .OrderByDescending(s => s.Score)
            .ToList();
        var best = scores[0];
        return best.Score >= 1
            ? Make(best.Rule, best.Score, scores.Skip(1).FirstOrDefault(s => s.Score > 0).Rule?.Name, metrics, subjects)
            : Make(fallback, 0, scores.FirstOrDefault(s => s.Score > 0).Rule?.Name, metrics, subjects);
    }

    private static Persona Make(Rule rule, double score, string? runnerUp, PersonaMetrics metrics, PersonaSubjects subjects)
    {
        var subject = rule.Subject?.Invoke(subjects);
        return new Persona(rule.Id, rule.Name, rule.OneLiner(metrics, subject), score, runnerUp, subject);
    }

    private sealed record Rule(
        string Id,
        string Name,
        Func<PersonaMetrics, double> Score,
        Func<PersonaMetrics, TitleRef?, string> OneLiner,
        Func<PersonaSubjects, TitleRef?>? Subject = null);
}
