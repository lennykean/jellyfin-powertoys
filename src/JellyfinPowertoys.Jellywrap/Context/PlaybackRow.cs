using System;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record PlaybackRow(
    Guid UserId,
    Guid ItemId,
    bool IsStart,
    DateTime Time,
    string Name);
