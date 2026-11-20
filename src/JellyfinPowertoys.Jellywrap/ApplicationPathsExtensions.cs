using System.IO;

using MediaBrowser.Common.Configuration;

namespace JellyfinPowertoys.Jellywrap;

internal static class ApplicationPathsExtensions
{
    public static string GetJellywrapLibraryPath(this IApplicationPaths applicationPaths)
    {
        return Path.Combine(applicationPaths.DataPath, "jellywrap", "library");
    }

    public static string GetJellywrapWorkPath(this IApplicationPaths applicationPaths)
    {
        return Path.Combine(applicationPaths.DataPath, "jellywrap", "work");
    }

    public static string GetJellywrapTemplatesPath(this IApplicationPaths applicationPaths, string? customTemplateFolder)
    {
        return string.IsNullOrWhiteSpace(customTemplateFolder)
            ? Path.Combine(applicationPaths.DataPath, "jellywrap", "templates")
            : customTemplateFolder.Trim();
    }
}
