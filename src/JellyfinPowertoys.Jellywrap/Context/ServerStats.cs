using System.Collections.Generic;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record ServerStats(
    string Name,
    bool IsDefaultName,
    string Possessive,
    string PossessiveInline,
    Totals Totals,
    IReadOnlyList<TitleRef> TopSeries,
    IReadOnlyList<TitleRef> TopMovies,
    IReadOnlyList<GenreShare> TopGenres,
    Busiest Busiest,
    Growth Growth,
    IReadOnlyList<SlotShare> WhoOwnsWhen,
    TitleRef? CommonGround,
    double CommonGroundKidsHours,
    double CommonGroundGrownupsHours,
    TitleRef? MostShared,
    IReadOnlyList<LeaderRow> Leaderboard,
    Superlatives Superlatives,
    IReadOnlyList<UserRef> Lineup,
    MusicStats? Music);
