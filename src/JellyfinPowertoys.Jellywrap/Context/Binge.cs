using System;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record Binge(
    DateOnly Date,
    int Episodes,
    TitleRef Series,
    double Hours);
