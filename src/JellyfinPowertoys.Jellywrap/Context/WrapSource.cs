using System;
using System.Collections.Generic;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record WrapSource
{
    public required int Year { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }
    public required TimeZoneInfo TimeZone { get; init; }

    public required string ServerName { get; init; }
    public required IReadOnlyList<SourceUser> Users { get; init; }
    public required IReadOnlyList<PlaybackRow> Activity { get; init; }
    public required IReadOnlyList<MediaItem> Items { get; init; }

    public required Growth Growth { get; init; }
}
