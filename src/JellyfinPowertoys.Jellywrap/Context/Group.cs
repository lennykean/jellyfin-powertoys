using System.Collections.Generic;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record Group(
    int Count,
    IReadOnlyList<string> Names,
    double Hours,
    double HoursPerPerson,
    int Episodes,
    int Movies,
    TitleRef? Top,
    GenreShare? TopGenre);
