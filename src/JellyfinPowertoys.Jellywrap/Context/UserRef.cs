using System;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record UserRef(
    Guid Id,
    string Name,
    ImageRef? Avatar,
    bool IsKid,
    Persona Persona,
    double Hours);
