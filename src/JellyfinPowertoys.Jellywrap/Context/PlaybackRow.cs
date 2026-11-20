using System;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record PlaybackRow(
    Guid UserId,
    Guid ItemId,
    bool IsStart,
    bool IsAudio,
    DateTime Time,
    string Name);
