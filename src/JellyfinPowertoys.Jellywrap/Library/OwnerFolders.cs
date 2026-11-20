using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;

using Microsoft.Extensions.Logging;

namespace JellyfinPowertoys.Jellywrap.Library;

public sealed class OwnerFolders(
    ILibraryManager libraryManager,
    IFileSystem fileSystem,
    IApplicationPaths applicationPaths,
    ILogger<OwnerFolders> logger)
{
    private const string MarkerFileName = ".jellywrap";
    private const int TokenLength = 16;

    private static readonly Regex _tokenSuffix = new Regex($" [0-9a-f]{{{TokenLength}}}$", RegexOptions.Compiled);

    private string RootPath => applicationPaths.GetJellywrapLibraryPath();

    internal IReadOnlyList<Folder> GetFolders(Folder root)
    {
        var query = new InternalItemsQuery
        {
            ParentId = root.Id,
            IsFolder = true,
        };
        return [.. libraryManager.GetItemList(query).OfType<Folder>()];
    }

    internal void AdoptFoldersOnDisk(Folder root)
    {
        var paths = GetFolders(root).Select(f => f.Path).ToHashSet();
        foreach (var path in Directory.GetDirectories(RootPath))
        {
            if (paths.Contains(path) || GetOwnerTag(path) is not { } tag || !JellywrapTags.IsJellywrapTag(tag))
            {
                continue;
            }

            logger.LogInformation("Adopting the Jellywrap folder {Path}", path);
            var folder = new Folder
            {
                Name = _tokenSuffix.Replace(Path.GetFileName(path), string.Empty),
                Path = path,
                Tags = [tag],
                LockedFields = [MetadataField.Tags],
            };
            root.AddChild(folder);
        }
    }

    internal void DeleteFoldersOfMissingUsers(Folder root, IReadOnlyCollection<string> userTags)
    {
        foreach (var folder in GetFolders(root).Where(f => f.Tags.Any(JellywrapTags.IsUserTag) && !f.Tags.Any(userTags.Contains)))
        {
            logger.LogInformation("Deleting the Jellywrap folder {Path} of a deleted user", folder.Path);
            DeleteFolder(folder);
        }
    }

    internal Folder EnsureFolder(Folder root, string tag, string name)
    {
        var folders = GetFolders(root).Where(f => f.Tags.Contains(tag)).ToList();
        foreach (var guessable in folders.Where(f => !HasToken(f)))
        {
            logger.LogInformation("Deleting the Jellywrap folder {Path}, whose name has no random token", guessable.Path);
            DeleteFolder(guessable);
        }

        var folder = folders.FirstOrDefault(HasToken);
        if (folder is not null)
        {
            return folder;
        }

        var token = RandomNumberGenerator.GetHexString(TokenLength, lowercase: true);
        var path = Path.Combine(RootPath, $"{fileSystem.GetValidFilename(name)} {token}");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, MarkerFileName), tag);

        folder = new Folder
        {
            Name = name,
            Path = path,
            Tags = [tag],
            LockedFields = [MetadataField.Tags],
        };
        root.AddChild(folder);
        return folder;
    }

    internal Folder? FindFolder(Folder root, string tag)
    {
        return GetFolders(root).FirstOrDefault(f => f.Tags.Contains(tag) && HasToken(f));
    }

    internal string? GetOwnerTag(string path)
    {
        if (!path.StartsWith(RootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }
        var folderName = path[(RootPath.Length + 1)..].Split(Path.DirectorySeparatorChar)[0];
        var marker = Path.Combine(RootPath, folderName, MarkerFileName);
        return File.Exists(marker) ? File.ReadAllText(marker) : null;
    }

    private void DeleteFolder(Folder folder)
    {
        libraryManager.DeleteItem(folder, new DeleteOptions { DeleteFileLocation = true });
    }

    private static bool HasToken(Folder folder)
    {
        return _tokenSuffix.IsMatch(Path.GetFileName(folder.Path));
    }
}
