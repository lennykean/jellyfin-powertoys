using MediaBrowser.Model.Plugins;

namespace JellyfinPowertoys.StudioCurator.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool OverwriteMetadata { get; set; } = true;
    public bool FetchMissingMetadata { get; set; } = true;
    public bool AllStudios { get; set; } = true;
    public bool AllItems { get; set; } = true;
    public string StudioNameFilter { get; set; } = ".*";
    public string StudioOverviewFilter { get; set; } = ".*";
    public string ItemNameFilter { get; set; } = ".*";
    public string ItemTypeFilter { get; set; } = ".*";
    public string ItemGenreFilter { get; set; } = ".*";
    public string ItemOverviewFilter { get; set; } = ".*";
}
