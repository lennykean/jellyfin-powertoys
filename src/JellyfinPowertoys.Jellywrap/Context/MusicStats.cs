using System.Collections.Generic;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record MusicStats(
    double Hours,
    PersonRef? TopArtist,
    IReadOnlyList<TitleRef> TopSongs);
