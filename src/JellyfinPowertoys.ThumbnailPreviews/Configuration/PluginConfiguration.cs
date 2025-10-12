using MediaBrowser.Model.Plugins;

namespace JellyfinPowertoys.ThumbnailPreviews.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool LoopPreview { get; set; } = false;
    public int PreviewDuration { get; set; } = 10000;
    public int FrameMinDuration { get; set; } = 400;
    public string? Resolutions { get; set; }
}
