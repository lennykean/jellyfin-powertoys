using System;

namespace JellyfinPowertoys.Jellywrap.Audio;

internal sealed record Clip(Guid TitleId, string Title, ClipKind Kind, Guid SourceId, string Path, double In, double Length)
{
    public const double MinLength = 15;
    public (string Path, double In) MusicId => (Path, In);
}
