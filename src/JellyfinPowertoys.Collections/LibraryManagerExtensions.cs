using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;

namespace JellyfinPowertoys.Collections;

public static class LibraryManagerExtensions
{
    public static async Task<Folder> GetCustomCollectionsFolderAsync(this ILibraryManager libraryManager, string name, string appDataPath, ILibraryMonitor monitor)
    {
        var path = Path.Combine(appDataPath, $"{name.ToLower().Replace(" ", "-")}-collections");
        try
        {
            monitor.ReportFileSystemChangeBeginning(path);

            var folder = libraryManager.RootFolder.Children
                .OfType<Folder>()
                .FirstOrDefault(f => f.Path == path);
            if (folder is not null)
            {
                return folder;
            }
            var directory = Directory.CreateDirectory(path);
            folder = new()
            {
                Name = name,
                Path = path,
                DateCreated = directory.CreationTimeUtc,
                DateModified = directory.LastWriteTimeUtc,
            };
            libraryManager.RootFolder.AddChild(folder);

            await libraryManager.AddVirtualFolder(name, CollectionTypeOptions.boxsets, new()
            {
                PathInfos = [new MediaPathInfo(path)],
                EnableRealtimeMonitor = false,
                SaveLocalMetadata = false,
            }, true);

            return folder;
        }
        finally
        {
            monitor.ReportFileSystemChangeComplete(path, true);
        }
    }

    public static BoxSet CreateCustomCollection(this ILibraryManager libraryManager, string name, Folder collectionsFolder, ILibraryMonitor monitor)
    {
        var path = Path.Combine(collectionsFolder.Path, $"{name} [boxset]");
        try
        {
            monitor.ReportFileSystemChangeBeginning(path);

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            var collection = new BoxSet()
            {
                Name = name,
                Path = path,
                LibraryFolderIds = (
                    from f in libraryManager.GetVirtualFolders()
                    where f.CollectionType == CollectionTypeOptions.boxsets
                    where f.LibraryOptions.PathInfos.Any(p => p.Path == collectionsFolder.Path)
                    select Guid.Parse(f.ItemId)).ToArray(),
            };
            collectionsFolder.AddChild(collection);

            return collection;
        }
        finally
        {
            monitor.ReportFileSystemChangeComplete(path, true);
        }
    }

    public static async Task SyncMetadataAsync(
        this ILibraryManager libraryManager,
        BaseItem from,
        BaseItem to,
        IFileSystem fileSystem,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var updateType = ItemUpdateType.None;

        if (force && (from.Overview is null || from.ImageInfos.Length == 0))
        {
            var metadataUpdateType = await from.RefreshMetadata(new(new DirectoryService(fileSystem))
            {
                ForceSave = true,
                EnableRemoteContentProbe = true,
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh
            }, cancellationToken);
            await from.UpdateToRepositoryAsync(metadataUpdateType, cancellationToken);
        }
        for (var i = 0; i < Math.Max(from.ImageInfos.Length, to.ImageInfos.Length); i++)
        {
            var fromImage = from.ImageInfos.Length > i ? from.ImageInfos[i] : null;
            var toImage = to.ImageInfos.Length > i ? to.ImageInfos[i] : null;
            if (toImage is not null && fromImage is null && force)
            {
                to.RemoveImage(toImage);
                updateType |= ItemUpdateType.ImageUpdate;
            }
            else if (fromImage is not null && (toImage is null || (force && (fromImage.Type != toImage?.Type || fromImage.Path != toImage?.Path))))
            {
                to.SetImage(new() { Path = fromImage.Path, Type = fromImage.Type, }, i);
                updateType |= ItemUpdateType.ImageUpdate;
            }
        }
        if (to.Overview is null || (force && from.Overview != to.Overview))
        {
            to.Overview = from.Overview;
            updateType |= ItemUpdateType.MetadataEdit;
        }
        if (updateType != ItemUpdateType.None)
        {
            await libraryManager.UpdateItemAsync(to, to.GetParent(), updateType, cancellationToken);
        }
    }
}
