using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using MediaBrowser.Controller.Chapters;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;

namespace JellyfinPowertoys.Jellywrap.Audio;

public sealed class ItemMarkerReader(
    ILibraryManager libraryManager,
    IMediaSegmentManager segmentManager,
    IChapterManager chapterManager)
{
    private const double TicksPerSecond = TimeSpan.TicksPerSecond;

    internal async Task<ItemMarkers> ReadAsync(Guid itemId)
    {
        var item = libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return new ItemMarkers(new Dictionary<string, (double, double)>(), []);
        }

        var segments = await segmentManager.GetSegmentsAsync(item, null, libraryManager.GetLibraryOptions(item));
        var chapters = chapterManager.GetChapters(item.Id);
        return new ItemMarkers(
            segments.GroupBy(s => s.Type.ToString()).ToDictionary(g => g.Key, g => (g.Last().StartTicks / TicksPerSecond, g.Last().EndTicks / TicksPerSecond)),
            [.. chapters.Select(c => new Chapter(c.Name ?? string.Empty, c.StartPositionTicks / TicksPerSecond))]);
    }
}
