using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Jellyfin.Data.Events.Users;
using Jellyfin.Database.Implementations.Entities;

using JellyfinPowertoys.Jellywrap.Configuration;

using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellyfinPowertoys.Jellywrap.Library;

public class AccessGuard(
    IUserManager userManager,
    JellywrapLibrary library,
    OwnerFolders ownerFolders,
    PluginConfigurationStore configurationStore,
    ILogger<AccessGuard> logger) : IEventConsumer<UserCreatedEventArgs>, IEventConsumer<UserUpdatedEventArgs>, IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ApplyAllAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The Jellywrap tags could not be applied at startup");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task OnEvent(UserCreatedEventArgs eventArgs)
    {
        return ApplyAsync([eventArgs.Argument]);
    }

    public Task OnEvent(UserUpdatedEventArgs eventArgs)
    {
        return ApplyAsync([eventArgs.Argument]);
    }

    internal Task ApplyAllAsync()
    {
        return ApplyAsync(userManager.GetUsers());
    }

    internal async Task CleanupAsync()
    {
        foreach (var user in userManager.GetUsers())
        {
            await UpdatePolicyAsync(user, [], [], null);
        }
    }

    private async Task ApplyAsync(IEnumerable<User> users)
    {
        if (configurationStore.Current is not { } configuration
            || library.FindRoot() is not { } root
            || library.FindLibraryId() is not { } libraryId)
        {
            return;
        }

        var userTags = ownerFolders.GetFolders(root)
            .SelectMany(f => f.Tags)
            .Where(JellywrapTags.IsUserTag)
            .Distinct()
            .ToArray();
        foreach (var user in users)
        {
            var ownTag = JellywrapTags.GetUserTag(user.Id);
            if (configuration.IndividualVisibleToAll)
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
