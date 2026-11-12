using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;

using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;

using Microsoft.Extensions.Logging;

namespace JellyfinPowertoys.Jellywrap.Library;

public class JellywrapLibrary(
    ILibraryManager libraryManager,
    ICollectionManager collectionManager,
    IProviderManager providerManager,
    IFileSystem fileSystem,
    IApplicationPaths applicationPaths,
    ILogger<JellywrapLibrary> logger)
{
    private const string LibraryName = "Jellywrap";
    private const string MarkerFileName = ".jellywrap";
    private const string LogoResourceName = "logo.png";
    private const int TokenLength = 16;

    private static readonly Regex _tokenSuffix = new($" [0-9a-f]{{{TokenLength}}}$", RegexOptions.Compiled);

    internal string RootPath => JellywrapPaths.Library(applicationPaths);

    internal Folder? FindRoot()
    {
        return libraryManager.FindByPath(RootPath, true) as Folder;
    }

    internal Guid? FindLibraryId()
    {
        var virtualFolder = FindVirtualFolder();
        return virtualFolder is null ? null : Guid.Parse(virtualFolder.ItemId);
    }

    internal async Task RemoveAsync()
    {
        var collections = libraryManager.GetItemList(new()
        {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            Recursive = true,
        }).Where(c => Regex.IsMatch(c.Name, $@"^{LibraryName} \d+$"));
        foreach (var collection in collections)
        {
            libraryManager.DeleteItem(collection, new DeleteOptions { DeleteFileLocation = true });
        }

        var virtualFolder = FindVirtualFolder();
        if (virtualFolder is not null)
        {
            await libraryManager.RemoveVirtualFolder(virtualFolder.Name, false);
            await libraryManager.ValidateTopLibraryFolders(CancellationToken.None, true);
        }
    }

    private VirtualFolderInfo? FindVirtualFolder()
    {
        return libraryManager.GetVirtualFolders().FirstOrDefault(f => f.Locations.Contains(RootPath));
    }

    internal async Task<Folder> EnsureLibraryAsync()
    {
        if (FindLibraryId() is null)
        {
            // A library whose path is empty is skipped as "inaccessible or empty" until a full scan.
            Directory.CreateDirectory(RootPath);
            await File.WriteAllTextAsync(Path.Combine(RootPath, MarkerFileName), string.Empty);

            var options = new LibraryOptions
            {
                PathInfos = [new MediaPathInfo(RootPath)],
                EnablePhotos = false,
                EnableRealtimeMonitor = false,
                EnableChapterImageExtraction = false,
                ExtractChapterImagesDuringLibraryScan = false,
                EnableTrickplayImageExtraction = false,
                ExtractTrickplayImagesDuringLibraryScan = false,
                SaveLocalMetadata = false,
                MetadataSavers = [],
                TypeOptions =
                [
                    new TypeOptions
                    {
                        Type = nameof(Video),
                        MetadataFetchers = [],
                        ImageFetchers = [],
                    },
                ],
            };
            await libraryManager.AddVirtualFolder(LibraryName, CollectionTypeOptions.homevideos, options, false);
        }
        return FindRoot() ?? throw new InvalidOperationException($"The {LibraryName} library has no folder item for {RootPath}");
    }

    internal async Task ValidateAsync(CancellationToken cancellationToken)
    {
        if (FindLibraryId() is not { } id || libraryManager.GetItemById(id) is not Folder libraryFolder)
        {
            return;
        }

        await libraryFolder.ValidateChildren(
            new Progress<double>(),
            new MetadataRefreshOptions(new DirectoryService(fileSystem)),
            recursive: true,
            allowRemoveRoot: false,
            cancellationToken);
    }

    internal IReadOnlyList<Folder> GetFolders(Folder root)
    {
        return [.. libraryManager.GetItemList(new()
        {
            ParentId = root.Id,
            IsFolder = true,
        }).OfType<Folder>()];
    }

    internal void DeleteFoldersOfMissingUsers(Folder root, IReadOnlyCollection<string> userTags)
    {
        foreach (var folder in GetFolders(root).Where(f => f.Tags.Any(JellywrapTags.IsUserTag) && !f.Tags.Any(userTags.Contains)))
        {
            logger.LogInformation("Deleting the Jellywrap folder {Path} of a deleted user", folder.Path);
            DeleteItem(folder);
        }
    }

    internal Folder EnsureFolder(Folder root, string tag, string name)
    {
        var folders = GetFolders(root).Where(f => f.Tags.Contains(tag)).ToList();
        foreach (var guessable in folders.Where(f => !HasToken(f)))
        {
            logger.LogInformation("Deleting the Jellywrap folder {Path}, whose name has no random token", guessable.Path);
            DeleteItem(guessable);
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

    internal Video? FindVideo(Folder root, string tag, int year)
    {
        var folder = GetFolders(root).FirstOrDefault(f => f.Tags.Contains(tag) && HasToken(f));
        return folder is not null && File.Exists(VideoPath(folder, year))
            ? libraryManager.GetItemById(libraryManager.GetNewItemId(VideoPath(folder, year), typeof(Video))) as Video
            : null;
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

    internal void DeleteItem(BaseItem item)
    {
        libraryManager.DeleteItem(item, new DeleteOptions { DeleteFileLocation = true });
    }

    internal async Task<Video> PublishAsync(
        Folder folder,
        string name,
        int year,
        ProducedWrap wrap,
        IEnumerable<User> viewers,
        CancellationToken cancellationToken)
    {
        var path = VideoPath(folder, year);
        var imagePath = Path.ChangeExtension(path, null) + "-poster.jpg";

        File.Move(wrap.ImagePath, imagePath, true);
        File.Move(wrap.VideoPath, path, true);

        var id = libraryManager.GetNewItemId(path, typeof(Video));
        var video = libraryManager.GetItemById(id) as Video;
        var isNew = video is null;
        video ??= new Video
        {
            Id = id,
            Path = path,
            IsInMixedFolder = true,
        };
        video.Name = name;
        video.Overview = wrap.Overview;
        video.ProductionYear = year;
        video.Tags = [.. folder.Tags.Where(JellywrapTags.IsJellywrapTag)];
        video.OfficialRating = "0";
        video.LockedFields = [MetadataField.Name, MetadataField.Overview, MetadataField.Tags, MetadataField.OfficialRating];
        video.SetImagePath(ImageType.Primary, imagePath);

        if (isNew)
        {
            folder.AddChild(video);
        }
        else
        {
            await video.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken);
            foreach (var viewer in viewers)
            {
                video.MarkUnplayed(viewer);
            }
        }

        await video.RefreshMetadata(
            new MetadataRefreshOptions(new DirectoryService(fileSystem))
            {
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
            },
            cancellationToken);
        return video;
    }

    internal async Task AddToCollectionAsync(
        int year,
        IEnumerable<Video> videos,
        string? imagePath,
        CancellationToken cancellationToken)
    {
        var name = $"{LibraryName} {year}";
        var collection = libraryManager.GetItemList(new()
        {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            Name = name,
            Recursive = true,
        }).OfType<BoxSet>().FirstOrDefault();
        collection ??= await collectionManager.CreateCollectionAsync(new()
        {
            Name = name,
            IsLocked = true,
        });

        await collectionManager.AddToCollectionAsync(collection.Id, videos.Select(v => v.Id));

        await using var image = imagePath is null
            ? typeof(JellywrapLibrary).Assembly.GetManifestResourceStream(LogoResourceName)!
            : File.OpenRead(imagePath);

        await providerManager.SaveImage(
            collection,
            image,
            imagePath is null ? "image/png" : "image/jpeg",
            ImageType.Primary,
            0,
            cancellationToken);
        await collection.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken);
    }

    private static bool HasToken(Folder folder)
    {
        return _tokenSuffix.IsMatch(Path.GetFileName(folder.Path));
    }

    private static string VideoPath(Folder folder, int year)
    {
        return Path.Combine(folder.Path, $"{LibraryName} {year}.mp4");
    }
}
