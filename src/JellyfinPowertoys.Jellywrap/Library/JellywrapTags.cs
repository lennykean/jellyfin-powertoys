using System;

namespace JellyfinPowertoys.Jellywrap.Library;

internal static class JellywrapTags
{
    public const string Prefix = "jellywrap-";

    public const string ServerTag = Prefix + "server";

    public static string GetUserTag(Guid userId)
    {
        return $"{Prefix}{userId:N}";
    }

    public static bool IsJellywrapTag(string tag)
    {
        return tag.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsUserTag(string tag)
    {
        return IsJellywrapTag(tag) && !tag.Equals(ServerTag, StringComparison.OrdinalIgnoreCase);
    }
}
