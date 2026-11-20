using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Jellyfin.Database.Implementations.Entities;

using JellyfinPowertoys.Jellywrap.Configuration;

using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;

using Microsoft.Extensions.Logging;

namespace JellyfinPowertoys.Jellywrap.Library;

public class WrapPublisher(
    OwnerFolders ownerFolders,
    AccessGuard accessGuard,
    PluginConfigurationStore configurationStore,
    IUserManager userManager,
    ILibraryManager libraryManager,
    IFileSystem fileSystem,
    ILogger<WrapPublisher> logger)
{
    private const string VideoNamePrefix = "Jellywrap";

    internal Video? FindVideo(Folder root, string tag, int year)
    {
        var folder = ownerFolders.FindFolder(root, tag);
        return folder is not null && File.Exists(VideoPath(folder, year))
            ? libraryManager.GetItemById(libraryManager.GetNewItemId(VideoPath(folder, year), typeof(Video))) as Video
            : null;
    }

    internal async Task<Video> PublishUserWrapAsync(
        Folder root,
        int year,
        ProducedWrap wrap,
        User owner,
        CancellationToken cancellationToken)
    {
        var individualVisibleToAll = configurationStore.Current?.IndividualVisibleToAll == true;
        var folder = ownerFolders.EnsureFolder(root, JellywrapTags.GetUserTag(owner.Id), owner.Username);
        await accessGuard.ApplyAllAsync();

        var users = userManager.GetUsers().ToList();
        var savedOwner = users.Single(u => u.Id == owner.Id);

        var others = individualVisibleToAll ? [] : users.Where(u => u.Id != owner.Id).ToList();
        if (others.Any(folder.IsVisibleStandalone))
        {
            throw new InvalidOperationException($"The Jellywrap folder {folder.Path} is visible to other users");
        }

        var video = await PublishAsync(
            folder,
            $"{owner.Username}'s {year} Jellywrap",
            year,
            wrap,
            [savedOwner],
            cancellationToken);
        if (others.Any(video.IsVisibleStandalone))
        {
            libraryManager.DeleteItem(video, new DeleteOptions { DeleteFileLocation = true });
            throw new InvalidOperationException($"The Jellywrap video {video.Path} was visible to other users and was deleted");
        }

        if (!video.IsVisibleStandalone(savedOwner))
        {
            logger.LogWarning("User {Username} cannot see their Jellywrap video {Path}", owner.Username, video.Path);
        }

        return video;
    }

    internal async Task<Video> PublishServerWrapAsync(
        Folder root,
        int year,
        ProducedWrap wrap,
        CancellationToken cancellationToken)
    {
        var folder = ownerFolders.EnsureFolder(root, JellywrapTags.ServerTag, "Everyone");
        var users = userManager.GetUsers().ToList();

        var video = await PublishAsync(
            folder,
            $"Our {year} Jellywrap",
            year,
            wrap,
            users,
            cancellationToken);
        foreach (var user in users.Where(u => !video.IsVisibleStandalone(u)))
        {
            logger.LogWarning("User {Username} cannot see the server Jellywrap video {Path}", user.Username, video.Path);
        }

        return video;
    }

    private async Task<Video> PublishAsync(
        Folder folder,
        string name,
        int year,
        ProducedWrap wrap,
        IEnumerable<User> viewers,
        CancellationToken cancellationToken)
    {
        var path = VideoPath(folder, year);
        var imagePath = Path.ChangeExtension(path, null) + "-poster.jpg";

        // A video that someone is watching cannot be replaced, and its poster must then stay too.
        File.Move(wrap.VideoPath, path, true);
        File.Move(wrap.ImagePath, imagePath, true);

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

    private static string VideoPath(Folder folder, int year)
    {
        return Path.Combine(folder.Path, $"{VideoNamePrefix} {year}.mp4");
    }
}
