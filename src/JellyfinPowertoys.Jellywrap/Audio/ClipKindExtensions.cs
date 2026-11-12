namespace JellyfinPowertoys.Jellywrap.Audio;

internal static class ClipKindExtensions
{
    public static string ToName(this ClipKind kind)
    {
        return kind.ToString().ToLowerInvariant();
    }
}
