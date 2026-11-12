using System;
using System.IO;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class StarterTemplate
{
    private const string ResourceRoot = "Templates/Starter/";

    public static bool IsIn(string folder)
    {
        return Directory.Exists(Path.Combine(folder, "starter"));
    }

    public static void AddTo(string folder)
    {
        if (IsIn(folder))
        {
            return;
        }

        var assembly = typeof(StarterTemplate).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            var path = resourceName.Replace('\\', '/');
            if (!path.StartsWith(ResourceRoot, StringComparison.Ordinal))
            {
                continue;
            }

            var target = Path.Combine(folder, path[ResourceRoot.Length..].Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var resource = assembly.GetManifestResourceStream(resourceName)!;
            using var file = File.Create(target);
            resource.CopyTo(file);
        }
    }
}
