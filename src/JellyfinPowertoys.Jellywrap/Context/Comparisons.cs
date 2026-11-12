namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record Comparisons(
    int RankByHours,
    int Of,
    double ShareOfServer,
    double VsAverageGrownup,
    double VsGroupAverage,
    UserRef? NextAbove,
    UserRef? NextBelow);
