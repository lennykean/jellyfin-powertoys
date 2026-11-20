using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;

namespace JellyfinPowertoys.Jellywrap.Library;

public class JellywrapLibrary(
    ILibraryManager libraryManager,
    IFileSystem fileSystem,
    IApplicationPaths applicationPaths)
{
    private const string LibraryName = "Jellywrap";
    private const string PlaceholderFileName = ".jellywrap";

    internal string RootPath => applicationPaths.GetJellywrapLibraryPath();

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
            await File.WriteAllTextAsync(Path.Combine(RootPath, PlaceholderFileName), string.Empty);

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
}
