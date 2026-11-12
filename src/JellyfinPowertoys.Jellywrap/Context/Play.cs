using System;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record Play(
    Guid UserId,
    Guid ItemId,
    MediaItem? Item,
    DateTime Start,
    DateTime Local,
    double Seconds,
    DateTime End,
    bool Completed)
{
    public ItemKind? Kind => Item?.Kind;

    public bool IsVideo => Kind is ItemKind.Movie or ItemKind.Episode or ItemKind.Video;
}
