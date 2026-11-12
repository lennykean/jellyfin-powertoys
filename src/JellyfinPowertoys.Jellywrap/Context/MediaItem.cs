using System;
using System.Collections.Generic;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record MediaItem
{
    public required Guid Id { get; init; }

    public required ItemKind Kind { get; init; }

    public required string Name { get; init; }
    public int? Year { get; init; }
    public double? Runtime { get; init; }

    public Guid? SeriesId { get; init; }

    public string? SeriesName { get; init; }

    public int? Season { get; init; }

    public int? Episode { get; init; }
    public string? ProviderKey { get; init; }
    public int? RatingScore { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    public IReadOnlyList<Credit> Actors { get; init; } = [];
    public IReadOnlyList<string> Artists { get; init; } = [];
    public Credit? Artist { get; init; }

    public ImageRef? Primary { get; init; }

    public ImageRef? Thumb { get; init; }

    public ImageRef? Backdrop { get; init; }
    public ImageRef? ParentImage { get; init; }
    public string? Path { get; init; }
    public IReadOnlyList<ThemeSong> ThemeSongs { get; init; } = [];
}
