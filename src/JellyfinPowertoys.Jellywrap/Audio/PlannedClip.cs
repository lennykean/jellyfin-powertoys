namespace JellyfinPowertoys.Jellywrap.Audio;

internal sealed record PlannedClip(
    string Path,
    double In,
    double Duration,
    string Title,
    ClipKind Kind);
