using System;
using System.Collections.Generic;
using System.Linq;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed class ItemIndex
{
    private readonly Dictionary<Guid, MediaItem> _items;
    private readonly Dictionary<Guid, IReadOnlyList<Guid>> _copies;
    private readonly ILookup<Guid, MediaItem> _episodes;

    public ItemIndex(IReadOnlyList<MediaItem> items)
    {
        _items = items.ToDictionary(i => i.Id);
        _copies = items
            .Where(i => i is { Kind: ItemKind.Series or ItemKind.Movie, ProviderKey: not null })
            .GroupBy(i => (i.Kind, i.ProviderKey))
            .Select(g => g.Select(i => i.Id).OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToList())
            .SelectMany(ids => ids.Select(id => (Id: id, Copies: ids)))
            .ToDictionary(copy => copy.Id, copy => (IReadOnlyList<Guid>)copy.Copies);
        _episodes = items
            .Where(i => i is { Kind: ItemKind.Episode, SeriesId: not null, Path: not null })
            .ToLookup(i => CanonicalId(i.SeriesId!.Value));
    }

    public MediaItem? Get(Guid id)
    {
        return _items.GetValueOrDefault(id);
    }

    public Guid CanonicalId(Guid id)
    {
        return _copies.TryGetValue(id, out var copies) ? copies[0] : id;
    }

    public TitleKey TitleOf(MediaItem item)
    {
        return item is { Kind: ItemKind.Episode, SeriesId: { } seriesId }
            ? new TitleKey(CanonicalId(seriesId), item.SeriesName ?? item.Name, ItemKind.Series.ToContextKind())
            : new TitleKey(CanonicalId(item.Id), item.Name, item.Kind.ToContextKind());
    }

    public IReadOnlyList<string> GenresOf(MediaItem item)
    {
        return item is { Kind: ItemKind.Episode, SeriesId: { } seriesId } && Get(seriesId) is { Genres.Count: > 0 } series
            ? series.Genres
            : item.Genres;
    }

    public IReadOnlyList<Credit> ActorsOf(MediaItem item)
    {
        return item is { Actors.Count: 0, SeriesId: { } seriesId } && Get(seriesId) is { } series
            ? series.Actors
            : item.Actors;
    }

    public int? YearOf(Guid id)
    {
        return Get(id)?.Year;
    }

    public ImageRef? Image(Guid id)
    {
        var copies = _copies.GetValueOrDefault(CanonicalId(id), [id]).Select(Get).OfType<MediaItem>().ToList();
        var item = Get(id);
        return copies.Select(c => c.Primary).FirstOrDefault(i => i is not null)
            ?? copies.Select(c => c.Thumb).FirstOrDefault(i => i is not null)
            ?? copies.Select(c => c.Backdrop).FirstOrDefault(i => i is not null)
            ?? item?.ParentImage
            ?? (item?.SeriesId is { } seriesId && seriesId != id ? Image(seriesId) : null);
    }

    public ImageRef? PosterFor(MediaItem item)
    {
        return item is { Kind: ItemKind.Episode, SeriesId: { } seriesId }
            ? Image(seriesId) ?? Image(item.Id)
            : Image(item.Id);
    }

    public IEnumerable<MediaItem> Episodes(Guid seriesId)
    {
        return _episodes[seriesId];
    }
}
