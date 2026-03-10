using MediaBrowser.Model.Plugins;

namespace JellyfinPowertoys.JellyTag.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string[] QuickTags { get; set; } = [];
}
