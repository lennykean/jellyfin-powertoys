using MediaBrowser.Model.Plugins;

namespace JellyfinPowertoys.ThumbnailPreviews.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool LoopPreview { get; set; } = false;
    public int PreviewDuration { get; set; } = 10000;
    public int FrameMinDuration { get; set; } = 400;
    public string? Resolutions { get; set; }
    public bool ShowTrailerPreview { get; set; } = true;
    public bool EnableTrailerLengthLimit { get; set; } = true;
    public int TrailerMaxLengthSeconds { get; set; } = 60;
    public bool OnlyShowSilentTrailers { get; set; } = false;
    public bool PlayTrailerAudio { get; set; } = false;
    public bool EnableHoverPlay { get; set; } = false;
    public int MouseLingerDelay { get; set; } = 800;
}
