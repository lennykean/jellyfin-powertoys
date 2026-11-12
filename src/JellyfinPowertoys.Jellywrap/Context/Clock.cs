using System.Collections.Generic;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record Clock(
    int PeakHour,
    IReadOnlyList<double> HourShares,
    double NightShare,
    double MorningShare,
    string? FavoritePart);
