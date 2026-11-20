using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Jellyfin.Data.Enums;

using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace JellyfinPowertoys.Jellywrap.Library;

public sealed class WrapCollections(
    ILibraryManager libraryManager,
    ICollectionManager collectionManager,
    IProviderManager providerManager)
{
    private const string NamePrefix = "Jellywrap";
    private const string LogoResourceName = "logo.png";

    internal async Task AddAsync(
        int year,
        IEnumerable<Video> videos,
        string? imagePath,
        CancellationToken cancellationToken)
    {
        var name = $"{NamePrefix} {year}";
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            Name = name,
            Recursive = true,
        };
        var collection = libraryManager.GetItemList(query).OfType<BoxSet>().FirstOrDefault();
        if (collection is null)
        {
            var options = new CollectionCreationOptions
            {
                Name = name,
                IsLocked = true,
            };
            collection = await collectionManager.CreateCollectionAsync(options);
        }

        await collectionManager.AddToCollectionAsync(collection.Id, videos.Select(v => v.Id));

        await using var image = imagePath is null
            ? typeof(WrapCollections).Assembly.GetManifestResourceStream(LogoResourceName)!
            : File.OpenRead(imagePath);

        await providerManager.SaveImage(
            collection,
            image,
            imagePath is null ? "image/png" : "image/jpeg",
            ImageType.Primary,
            0,
            cancellationToken);
        await collection.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken);
    }

    internal void DeleteAll()
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            Recursive = true,
        };
        var collections = libraryManager.GetItemList(query).Where(c => Regex.IsMatch(c.Name, $@"^{NamePrefix} \d+$"));
        foreach (var collection in collections)
        {
            libraryManager.DeleteItem(collection, new DeleteOptions { DeleteFileLocation = true });
        }
    }
}
