using System.Collections.Generic;

namespace JellyfinPowertoys.Jellywrap.Audio;

internal sealed record ItemMarkers(IReadOnlyDictionary<string, (double Start, double End)> Segments, IReadOnlyList<Chapter> Chapters);
