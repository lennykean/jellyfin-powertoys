using System;
using System.Collections.Generic;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record UserStats(
    Guid Id,
    string Name,
    string Possessive,
    ImageRef? Avatar,
    bool IsKid,
    string Group,
    Totals Totals,
    Rhythm Rhythm,
    Mix Mix,
    IReadOnlyList<GenreShare> TopGenres,
    GenreShare? StoryGenre,
    IReadOnlyList<TitleRef> TopSeries,
    IReadOnlyList<TitleRef> TopMovies,
    IReadOnlyList<PersonRef> TopActors,
    Binge? Binge,
    Clock Clock,
    Busiest Busiest,
    TitleRef? MostReplayed,
    NewVsClassic NewVsClassic,
    MusicStats? Music,
    Persona Persona,
    PersonaMetrics Metrics)
{
    internal UserRef ToRef()
    {
        return new UserRef(Id, Name, Avatar, IsKid, Persona, Totals.Hours);
    }
}
