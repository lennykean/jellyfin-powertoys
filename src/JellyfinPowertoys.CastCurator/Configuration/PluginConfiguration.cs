using MediaBrowser.Model.Plugins;

namespace JellyfinPowertoys.CastCurator.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool OverwriteMetadata { get; set; } = true;
    public bool FetchMissingMetadata { get; set; } = true;
    public bool AllPeople { get; set; } = true;
    public bool AllItems { get; set; } = true;
    public string PersonNameFilter { get; set; } = ".*";
    public string PersonRoleTypeFilter { get; set; } = ".*";
    public string PersonRoleFilter { get; set; } = ".*";
    public string PersonOverviewFilter { get; set; } = ".*";
    public string ItemNameFilter { get; set; } = ".*";
    public string ItemTypeFilter { get; set; } = ".*";
    public string ItemGenreFilter { get; set; } = ".*";
    public string ItemOverviewFilter { get; set; } = ".*";
}
