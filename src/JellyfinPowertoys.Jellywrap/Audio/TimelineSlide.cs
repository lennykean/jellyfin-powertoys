using System.Collections.Generic;

namespace JellyfinPowertoys.Jellywrap.Audio;

internal sealed record TimelineSlide(
    double Start,
    double Duration,
    IReadOnlyList<string> FeaturedItemIds);
