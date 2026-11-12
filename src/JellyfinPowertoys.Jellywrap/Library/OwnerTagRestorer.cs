using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.Library;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellyfinPowertoys.Jellywrap.Library;

public sealed class OwnerTagRestorer(
    ILibraryManager libraryManager,
    JellywrapLibrary library,
    ILogger<OwnerTagRestorer> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        libraryManager.ItemAdded += OnItemChanged;
        libraryManager.ItemUpdated += OnItemChanged;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        libraryManager.ItemAdded -= OnItemChanged;
        libraryManager.ItemUpdated -= OnItemChanged;
        return Task.CompletedTask;
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs eventArgs)
    {
        var item = eventArgs.Item;
        var ownerTag = item.Path is null ? null : library.GetOwnerTag(item.Path);
        if (ownerTag is null || item.Tags.Contains(ownerTag))
        {
            return;
        }

        logger.LogInformation("Restoring the Jellywrap tag of {Path}", item.Path);
        item.Tags = [.. item.Tags, ownerTag];
        item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).GetAwaiter().GetResult();
    }
}
