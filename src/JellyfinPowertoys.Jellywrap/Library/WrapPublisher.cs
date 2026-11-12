using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Jellyfin.Database.Implementations.Entities;

using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

using Microsoft.Extensions.Logging;

namespace JellyfinPowertoys.Jellywrap.Library;

public class WrapPublisher(
    JellywrapLibrary library,
    AccessGuard accessGuard,
    IUserManager userManager,
    ILogger<WrapPublisher> logger)
{
    internal async Task<Video> PublishUserWrapAsync(
        Folder root,
        int year,
        ProducedWrap wrap,
        User owner,
        CancellationToken cancellationToken)
    {
        var individualVisibleToAll = Plugin.Instance!.Configuration.IndividualVisibleToAll;
        var folder = library.EnsureFolder(root, JellywrapTags.GetUserTag(owner.Id), owner.Username);
        await accessGuard.ApplyAllAsync(individualVisibleToAll);

        var users = userManager.GetUsers().ToList();
        var savedOwner = users.Single(u => u.Id == owner.Id);

        var others = individualVisibleToAll ? [] : users.Where(u => u.Id != owner.Id).ToList();
        if (others.Any(folder.IsVisibleStandalone))
        {
            throw new InvalidOperationException($"The Jellywrap folder {folder.Path} is visible to other users");
        }

        var video = await library.PublishAsync(
            folder,
            $"{owner.Username}'s {year} Jellywrap",
            year,
            wrap,
            [savedOwner],
            cancellationToken);
        if (others.Any(video.IsVisibleStandalone))
        {
            library.DeleteItem(video);
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
        var folder = library.EnsureFolder(root, JellywrapTags.ServerTag, "Everyone");
        var users = userManager.GetUsers().ToList();

        var video = await library.PublishAsync(
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
}
