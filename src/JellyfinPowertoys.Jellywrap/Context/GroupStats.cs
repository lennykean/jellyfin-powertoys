namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record GroupStats(
    Group Kids,
    Group Grownups,
    bool HasSplit,
    bool OneKid,
    string? MoreHoursPerPerson,
    string? MoreEpisodes);
