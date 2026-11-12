using System;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record TitleRef(
    Guid Id,
    string Title,
    int? Year,
    string Kind,
    string? SeriesTitle,
    ImageRef? Poster,
    double Hours,
    int Plays,
    int Completions,
    int Viewers);
