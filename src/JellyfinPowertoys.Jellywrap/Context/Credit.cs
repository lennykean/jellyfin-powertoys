using System;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record Credit(
    Guid Id,
    string Name,
    ImageRef? Image);
