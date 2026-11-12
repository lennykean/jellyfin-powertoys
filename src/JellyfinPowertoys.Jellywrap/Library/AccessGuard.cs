using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Jellyfin.Data.Events.Users;
using Jellyfin.Database.Implementations.Entities;

using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;

using Microsoft.Extensions.Logging;

namespace JellyfinPowertoys.Jellywrap.Library;

public class AccessGuard(
    IUserManager userManager,
    JellywrapLibrary library,
    ILogger<AccessGuard> logger) : IEventConsumer<UserCreatedEventArgs>, IEventConsumer<UserUpdatedEventArgs>
{
    private bool _stopped;

    public Task OnEvent(UserCreatedEventArgs eventArgs)
    {
        return ApplyAsync([eventArgs.Argument], Plugin.Instance?.Configuration.IndividualVisibleToAll == true);
    }

    public Task OnEvent(UserUpdatedEventArgs eventArgs)
    {
        return ApplyAsync([eventArgs.Argument], Plugin.Instance?.Configuration.IndividualVisibleToAll == true);
    }

    internal Task ApplyAllAsync(bool individualVisibleToAll)
    {
        return ApplyAsync(userManager.GetUsers(), individualVisibleToAll);
    }

    internal async Task CleanupAsync()
    {
        _stopped = true;
        foreach (var user in userManager.GetUsers())
        {
            await UpdatePolicyAsync(user, [], [], null);
        }
    }

    private async Task ApplyAsync(IEnumerable<User> users, bool individualVisibleToAll)
    {
        if (_stopped || library.FindRoot() is not { } root || library.FindLibraryId() is not { } libraryId)
        {
            return;
        }

        var userTags = library.GetFolders(root)
            .SelectMany(f => f.Tags)
            .Where(JellywrapTags.IsUserTag)
            .Distinct()
            .ToArray();
        foreach (var user in users)
        {
            var ownTag = JellywrapTags.GetUserTag(user.Id);
            if (individualVisibleToAll)
            {
                await UpdatePolicyAsync(user, [], [.. userTags, JellywrapTags.ServerTag], libraryId);
            }
            else
            {
                await UpdatePolicyAsync(
                    user,
                    [.. userTags.Where(t => !string.Equals(t, ownTag, StringComparison.OrdinalIgnoreCase))],
                    [ownTag, JellywrapTags.ServerTag],
                    libraryId);
            }
        }
    }

    private async Task UpdatePolicyAsync(User user, string[] blockedTags, string[] allowedTags, Guid? libraryId)
    {
        var policy = userManager.GetUserDto(user).Policy;

        var newBlockedTags = policy.BlockedTags.Where(t => !JellywrapTags.IsJellywrapTag(t)).Concat(blockedTags).ToArray();
        var newAllowedTags = policy.AllowedTags.Where(t => !JellywrapTags.IsJellywrapTag(t)).ToArray();
        if (newAllowedTags.Length > 0)
        {
            newAllowedTags = [.. newAllowedTags, .. allowedTags];
        }
        var newEnabledFolders = policy.EnableAllFolders || libraryId is not { } id || policy.EnabledFolders.Contains(id)
            ? policy.EnabledFolders
            : [.. policy.EnabledFolders, id];

        if (SetEquals(newBlockedTags, policy.BlockedTags)
            && SetEquals(newAllowedTags, policy.AllowedTags)
            && SetEquals(newEnabledFolders, policy.EnabledFolders))
        {
            return;
        }

        logger.LogInformation("Updating the Jellywrap tags of user {Username}", user.Username);
        policy.BlockedTags = newBlockedTags;
        policy.AllowedTags = newAllowedTags;
        policy.EnabledFolders = newEnabledFolders;
        await userManager.UpdatePolicyAsync(user.Id, policy);
    }

    private static bool SetEquals<T>(IEnumerable<T> first, IEnumerable<T> second)
    {
        return first.ToHashSet().SetEquals(second);
    }
}
