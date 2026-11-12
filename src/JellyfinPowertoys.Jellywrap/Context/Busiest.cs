using System;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record Busiest(
    string Month,
    double MonthHours,
    DateOnly Day,
    double DayHours,
    int Hour);
