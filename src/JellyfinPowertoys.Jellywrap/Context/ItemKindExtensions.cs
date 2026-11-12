namespace JellyfinPowertoys.Jellywrap.Context;

internal static class ItemKindExtensions
{
    public static string ToContextKind(this ItemKind kind)
    {
        return kind switch
        {
            ItemKind.Movie => "movie",
            ItemKind.Series => "series",
            ItemKind.Episode => "episode",
            ItemKind.Song => "song",
            _ => "video",
        };
    }
}
