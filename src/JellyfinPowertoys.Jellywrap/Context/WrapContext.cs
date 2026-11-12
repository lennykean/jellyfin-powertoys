using System;
using System.Collections.Generic;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record WrapContext(
    int Year,
    DateTimeOffset GeneratedAt,
    DateOnly DataStart,
    bool DataStartsLate,
    ServerStats Server,
    IReadOnlyList<UserStats> Users,
    GroupStats Groups,
    UserStats? Me,
    Comparisons? Compare);
