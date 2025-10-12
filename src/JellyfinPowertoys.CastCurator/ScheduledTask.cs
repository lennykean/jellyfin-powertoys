using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using JellyfinPowertoys.Collections;

using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;

using Microsoft.Extensions.Logging;

namespace JellyfinPowertoys.CastCurator;

public class ScheduledTask(
    ILibraryManager libraryManager,
    ICollectionManager collectionManager,
    ILibraryMonitor libraryMonitor,
    IFileSystem fileSystem,
    IApplicationPaths appPaths,
    ILogger<ScheduledTask> logger) : IScheduledTask
{
    public string Name => "Cast Curator Sync";

    public string Key => typeof(ScheduledTask).FullName!;

    public string Description => "Syncs cast curator collections with the latest library data";

    public string Category => "Jellyfin Powertoys";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ValidateConfig();

        logger.LogInformation("Syncing cast curator collections");

        var errors = new List<Exception>();
        var itemRoleCache = new Dictionary<Guid, ILookup<string, PersonInfo>>();

        var peopleCollectionsFolder = await libraryManager.GetCustomCollectionsFolderAsync("People", appPaths.DataPath, libraryMonitor);
        var people = libraryManager.GetPeopleItems(new() { }).ToDictionary(k => k.Name, v => v);
        var collections = libraryManager.GetItemList(new() { ParentId = peopleCollectionsFolder.Id, })
            .OfType<BoxSet>()
            .ToDictionary(k => k.Name, v => v);
        var personCollections = (
            from name in people.Select(p => p.Key).Union(collections.Select(c => c.Key))
            let person = people.TryGetValue(name, out var p) ? p : null
            let collection = collections.TryGetValue(name, out var c) ? c : null
            select (name, person, collection)).ToList();

        for (var i = 0; i < personCollections.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("Task cancelled");
                return;
            }
            progress.Report(100d * (i + 1) / personCollections.Count);

            var (name, person, collection) = personCollections[i];
            try
            {
                if (person is null || !PersonMatchesFilters(person))
                {
                    if (collection is not null)
                    {
                        logger.LogDebug(
                            "Person {PersonName} does not match filters, deleting collection {CollectionId} ({CollectionName})",
                            name,
                            collection.Id,
                            collection.Name);
                        libraryManager.DeleteItem(collection, new() { DeleteFileLocation = true });
                    }
                    continue;
                }

                var personItems = libraryManager.GetItemList(new() { Recursive = true, PersonIds = [person.Id] }).ToDictionary(k => k.Id, v => v);
                var collectionItems = collection?.GetLinkedChildren().ToDictionary(k => k.Id, v => v) ?? [];

                foreach (var itemId in personItems.Keys.Union(collectionItems.Keys))
                {
                    var item = personItems.TryGetValue(itemId, out var p)
                        ? p
                        : collectionItems.TryGetValue(itemId, out var c)
                            ? c
                            : throw new InvalidOperationException($"Item {itemId} not found in person or collection");

                    if (!itemRoleCache.TryGetValue(itemId, out var itemRoles))
                    {
                        itemRoles = libraryManager.GetPeople(item).ToLookup(k => k.Name, v => v);
                        itemRoleCache[itemId] = itemRoles;
                    }
                    if (collection is not null && collectionItems.ContainsKey(itemId))
                    {
                        if (!personItems.ContainsKey(itemId) || !(ItemMatchesFilters(item) && itemRoles.Contains(name) && itemRoles[name].Any(RoleMatchesFilters)))
                        {
                            logger.LogDebug(
                                "Item {ItemId} ({ItemName}) does not match filters, deleting it from collection {CollectionId} ({CollectionName})",
                                itemId,
                                item.Name,
                                collection.Id,
                                collection.Name);
                            await collectionManager.RemoveFromCollectionAsync(collection.Id, [item.Id]);
                            collectionItems.Remove(itemId);
                        }
                    }
                    else if (personItems.ContainsKey(itemId))
                    {
                        if (ItemMatchesFilters(item) && itemRoles.Contains(name) && itemRoles[name].Any(RoleMatchesFilters))
                        {
                            if (collection is null)
                            {
                                logger.LogDebug("Collection for person {PersonId} ({PersonName}) was not found, creating it", person.Id, person.Name);
                                collection = libraryManager.CreateCustomCollection(person.Name, peopleCollectionsFolder, libraryMonitor);
                            }
                            logger.LogDebug("Adding item {ItemId} ({ItemName}) to collection {CollectionId}", itemId, item.Name, collection.Id);
                            await collectionManager.AddToCollectionAsync(collection.Id, [item.Id]);
                            collectionItems[itemId] = item;
                        }
                    }
                }
                if (collection is not null && collectionItems.Count > 0)
                {
                    await libraryManager.SyncMetadataAsync(person, collection, fileSystem, Plugin.Instance!.Configuration.FetchMissingMetadata, cancellationToken);
                }
                else if (collection is not null)
                {
                    logger.LogDebug("Collection {CollectionId} ({CollectionName}) has no items, deleting it", collection.Id, collection.Name);
                    libraryManager.DeleteItem(collection, new() { DeleteFileLocation = true });
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while syncing collections for {Name}", name);
                errors.Add(ex);
            }
        }
        if (errors.Count > 0)
        {
            throw new AggregateException(errors);
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new()
        {
            Type = TaskTriggerInfo.TriggerDaily,
            TimeOfDayTicks = 0,
        };
    }

    private void ValidateConfig()
    {
        var config = Plugin.Instance!.Configuration;

        var configIsValid = true;
        if (!ValidateRegexPattern(config.PersonNameFilter))
        {
            logger.LogError("Invalid regex pattern in {Filter} ({Value})", nameof(config.PersonNameFilter), config.PersonNameFilter);
            configIsValid = false;
        }
        if (!ValidateRegexPattern(config.PersonRoleTypeFilter))
        {
            logger.LogError("Invalid regex pattern in {Filter} ({Value})", nameof(config.PersonRoleTypeFilter), config.PersonRoleTypeFilter);
            configIsValid = false;
        }
        if (!ValidateRegexPattern(config.PersonRoleFilter))
        {
            logger.LogError("Invalid regex pattern in {Filter} ({Value})", nameof(config.PersonRoleFilter), config.PersonRoleFilter);
            configIsValid = false;
        }
        if (!ValidateRegexPattern(config.PersonOverviewFilter))
        {
            logger.LogError("Invalid regex pattern in {Filter} ({Value})", nameof(config.PersonOverviewFilter), config.PersonOverviewFilter);
            configIsValid = false;
        }
        if (!ValidateRegexPattern(config.ItemNameFilter))
        {
            logger.LogError("Invalid regex pattern in {Filter} ({Value})", nameof(config.ItemNameFilter), config.ItemNameFilter);
            configIsValid = false;
        }
        if (!ValidateRegexPattern(config.ItemTypeFilter))
        {
            logger.LogError("Invalid regex pattern in {Filter} ({Value})", nameof(config.ItemTypeFilter), config.ItemTypeFilter);
            configIsValid = false;
        }
        if (!ValidateRegexPattern(config.ItemGenreFilter))
        {
            logger.LogError("Invalid regex pattern in {Filter} ({Value})", nameof(config.ItemGenreFilter), config.ItemGenreFilter);
            configIsValid = false;
        }
        if (!ValidateRegexPattern(config.ItemOverviewFilter))
        {
            logger.LogError("Invalid regex pattern in {Filter} ({Value})", nameof(config.ItemOverviewFilter), config.ItemOverviewFilter);
            configIsValid = false;
        }
        if (!configIsValid)
        {
            throw new ArgumentException("Configuration is invalid.");
        }
    }

    private static bool ValidateRegexPattern(string pattern)
    {
        try
        {
            Regex.IsMatch(string.Empty, pattern);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool PersonMatchesFilters(Person person)
    {
        if (Plugin.Instance!.Configuration.AllPeople)
        {
            return true;
        }
        if (!Regex.IsMatch(person.Name ?? string.Empty, Plugin.Instance!.Configuration.PersonNameFilter, RegexOptions.IgnoreCase))
        {
            return false;
        }
        if (!Regex.IsMatch(person.Overview ?? string.Empty, Plugin.Instance!.Configuration.PersonOverviewFilter, RegexOptions.IgnoreCase))
        {
            return false;
        }
        return true;
    }

    private static bool ItemMatchesFilters(BaseItem item)
    {
        if (Plugin.Instance!.Configuration.AllItems)
        {
            return true;
        }
        if (!Regex.IsMatch(item.Name ?? string.Empty, Plugin.Instance!.Configuration.ItemNameFilter, RegexOptions.IgnoreCase))
        {
            return false;
        }
        if (!Regex.IsMatch(item.GetBaseItemKind().ToString(), Plugin.Instance!.Configuration.ItemTypeFilter, RegexOptions.IgnoreCase))
        {
            return false;
        }
        if (!(item.Genres ?? [""]).Any(g => Regex.IsMatch(g, Plugin.Instance!.Configuration.ItemGenreFilter, RegexOptions.IgnoreCase)))
        {
            return false;
        }
        if (!Regex.IsMatch(item.Overview ?? string.Empty, Plugin.Instance!.Configuration.ItemOverviewFilter, RegexOptions.IgnoreCase))
        {
            return false;
        }
        return true;
    }

    private static bool RoleMatchesFilters(PersonInfo role)
    {
        if (Plugin.Instance!.Configuration.AllPeople)
        {
            return true;
        }
        if (!Regex.IsMatch(role.Type.ToString(), Plugin.Instance!.Configuration.PersonRoleTypeFilter, RegexOptions.IgnoreCase))
        {
            return false;
        }
        if (!Regex.IsMatch(role.Role ?? string.Empty, Plugin.Instance!.Configuration.PersonRoleFilter, RegexOptions.IgnoreCase))
        {
            return false;
        }
        return true;
    }
}
