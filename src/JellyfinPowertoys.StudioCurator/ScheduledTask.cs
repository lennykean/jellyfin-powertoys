using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Jellyfin.Data.Enums;

using JellyfinPowertoys.Collections;

using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;

using Microsoft.Extensions.Logging;

namespace JellyfinPowertoys.StudioCurator;

public class ScheduledTask(
    ILibraryManager libraryManager,
    ICollectionManager collectionManager,
    ILibraryMonitor libraryMonitor,
    IFileSystem fileSystem,
    IApplicationPaths appPaths,
    ILogger<ScheduledTask> logger) : IScheduledTask
{
    public string Name => "Studio Curator Sync";

    public string Key => typeof(ScheduledTask).FullName!;

    public string Description => "Syncs studio curator collections with the latest library data";

    public string Category => "Jellyfin Powertoys";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ValidateConfig();

        logger.LogInformation("Syncing studio curator collections");

        var errors = new List<Exception>();
        var studioCollectionsFolder = await libraryManager.GetCustomCollectionsFolderAsync("Studios", appPaths.DataPath, libraryMonitor);
        var studios = libraryManager.GetStudios(new() { }).Items.ToDictionary(k => k.Item.Name, v => (Studio)v.Item);
        var collections = libraryManager.GetItemList(new() { ParentId = studioCollectionsFolder.Id, })
            .OfType<BoxSet>()
            .ToDictionary(k => k.Name, v => v);
        var studioCollections = (
            from name in studios.Select(p => p.Key).Union(collections.Select(c => c.Key))
            let studio = studios.TryGetValue(name, out var p) ? p : null
            let collection = collections.TryGetValue(name, out var c) ? c : null
            select (name, studio, collection)).ToList();

        for (var i = 0; i < studioCollections.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("Task cancelled");
                return;
            }
            progress.Report(100d * (i + 1) / studioCollections.Count);

            var (name, studio, collection) = studioCollections[i];
            try
            {
                if (studio is null || !StudioMatchesFilters(studio))
                {
                    if (collection is not null)
                    {
                        logger.LogDebug(
                            "Studio {StudioName} does not match filters, deleting collection {CollectionId} ({CollectionName})",
                            name,
                            collection.Id,
                            collection.Name);
                        libraryManager.DeleteItem(collection, new() { DeleteFileLocation = true });
                    }
                    continue;
                }

                var studioItems = libraryManager
                    .GetItemList(new() { Recursive = true, StudioIds = [studio.Id], ExcludeItemTypes = [BaseItemKind.BoxSet] })
                    .ToDictionary(k => k.Id, v => v);
                var collectionItems = collection?.GetLinkedChildren().ToDictionary(k => k.Id, v => v) ?? [];

                foreach (var itemId in studioItems.Keys.Union(collectionItems.Keys))
                {
                    var item = studioItems.TryGetValue(itemId, out var p)
                        ? p
                        : collectionItems.TryGetValue(itemId, out var c)
                            ? c
                            : throw new InvalidOperationException($"Item {itemId} not found in studio or collection");

                    if (collection is not null && collectionItems.ContainsKey(itemId))
                    {
                        if (!studioItems.ContainsKey(itemId) || !ItemMatchesFilters(item))
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
                    else if (studioItems.ContainsKey(itemId))
                    {
                        if (ItemMatchesFilters(item))
                        {
                            if (collection is null)
                            {
                                logger.LogDebug("Collection for studio {StudioId} ({StudioName}) was not found, creating it", studio.Id, studio.Name);
                                collection = libraryManager.CreateCustomCollection(studio.Name, studioCollectionsFolder, libraryMonitor);
                            }
                            logger.LogDebug("Adding item {ItemId} ({ItemName}) to collection {CollectionId}", itemId, item.Name, collection.Id);
                            await collectionManager.AddToCollectionAsync(collection.Id, [item.Id]);
                            collectionItems[itemId] = item;
                        }
                    }
                }
                if (collection is not null && collectionItems.Count > 0)
                {
                    await libraryManager.SyncMetadataAsync(studio, collection, fileSystem, Plugin.Instance!.Configuration.FetchMissingMetadata, cancellationToken);
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
        if (!ValidateRegexPattern(config.StudioNameFilter))
        {
            logger.LogError("Invalid regex pattern in {Filter} ({Value})", nameof(config.StudioNameFilter), config.StudioNameFilter);
            configIsValid = false;
        }
        if (!ValidateRegexPattern(config.StudioOverviewFilter))
        {
            logger.LogError("Invalid regex pattern in {Filter} ({Value})", nameof(config.StudioOverviewFilter), config.StudioOverviewFilter);
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

    private static bool StudioMatchesFilters(Studio studio)
    {
        if (Plugin.Instance!.Configuration.AllStudios)
        {
            return true;
        }
        if (!Regex.IsMatch(studio.Name ?? string.Empty, Plugin.Instance!.Configuration.StudioNameFilter, RegexOptions.IgnoreCase))
        {
            return false;
        }
        if (!Regex.IsMatch(studio.Overview ?? string.Empty, Plugin.Instance!.Configuration.StudioOverviewFilter, RegexOptions.IgnoreCase))
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
}
