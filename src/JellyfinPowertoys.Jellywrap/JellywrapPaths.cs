using System.IO;

using MediaBrowser.Common.Configuration;

namespace JellyfinPowertoys.Jellywrap;

internal static class JellywrapPaths
{
    public static string Library(IApplicationPaths applicationPaths)
    {
        return Path.Combine(applicationPaths.DataPath, "jellywrap", "library");
    }

    public static string Work(IApplicationPaths applicationPaths)
    {
        return Path.Combine(applicationPaths.DataPath, "jellywrap", "work");
    }

    public static string Templates(IApplicationPaths applicationPaths, string? customTemplateFolder)
    {
        return string.IsNullOrWhiteSpace(customTemplateFolder)
            ? Path.Combine(applicationPaths.DataPath, "jellywrap", "templates")
            : customTemplateFolder.Trim();
    }
}
