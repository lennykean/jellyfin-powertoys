using System;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record Rhythm(
    int ActiveDays,
    int Streak,
    DateOnly? StreakFrom,
    DateOnly? StreakTo);
