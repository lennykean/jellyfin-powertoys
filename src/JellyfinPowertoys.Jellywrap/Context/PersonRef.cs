using System;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record PersonRef(
    Guid Id,
    string Name,
    ImageRef? Image,
    double Hours);
