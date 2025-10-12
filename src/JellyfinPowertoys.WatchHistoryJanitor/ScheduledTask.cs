using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;

using Microsoft.Extensions.Logging;

namespace JellyfinPowertoys.WatchHistoryJanitor;

public class ScheduledTask(
    IUserManager userManager,
    IUserDataManager userDataManager,
    ILibraryManager libraryManager,
    ILogger<ScheduledTask> logger) : IScheduledTask
{
    public string Name => @"Watch History Janitor";

    public string Key => typeof(ScheduledTask).FullName!;

    public string Description => @"Clean up ""Continue Watching"" history after the configured time period";

    public string Category => "Jellyfin Powertoys";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (!Plugin.Instance!.Configuration.Enabled)
        {
            logger.LogInformation("Watch History Janitor is not enabled");
            return Task.CompletedTask;
        }

        var cutoff = DateTime.UtcNow - Plugin.Instance!.Configuration.ExpireAfter;
        var userFilter = new Regex(Plugin.Instance!.Configuration.UsernameFilter, RegexOptions.IgnoreCase);
        var users = (
            from user in userManager.Users
            where userFilter.IsMatch(user.Username)
            select user).ToList();

        logger.LogInformation("Cleaning up continue watching history older than {Cutoff}", cutoff);

        for (var i = 0; i < users.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("Task cancelled");
                break;
            }
            progress.Report(100d * (i + 1) / users.Count);

            var user = users[i];
            foreach (var item in libraryManager.GetItemList(new() { User = user, IsResumable = true }))
            {
                var userItemData = userDataManager.GetUserData(user, item);
                if (userItemData.LastPlayedDate < cutoff)
                {
                    logger.LogDebug(
                        "User {UserId} ({Username}) playback date {LastPlayedDate} for item {ItemId} ({ItemName}) is older than the cutoff, resetting it",
                        user.Id,
                        user.Username,
                        userItemData.LastPlayedDate,
                        item.Id,
                        item.Name);
                    userItemData.PlaybackPositionTicks = 0;
                    userDataManager.SaveUserData(user, item, userItemData, UserDataSaveReason.PlaybackProgress, cancellationToken);
                }
            }
        }
        return Task.CompletedTask;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new()
        {
            Type = TaskTriggerInfo.TriggerDaily,
            TimeOfDayTicks = 0,
        };
    }
}
