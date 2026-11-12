using System.Collections.Generic;

namespace JellyfinPowertoys.Jellywrap.Audio;

internal sealed record TimelineSlide(
    string Id,
    double Start,
    double Duration,
    IReadOnlyList<string> FeaturedItemIds);
