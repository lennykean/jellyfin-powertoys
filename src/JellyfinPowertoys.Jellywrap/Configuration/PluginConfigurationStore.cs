using MediaBrowser.Common.Plugins;

namespace JellyfinPowertoys.Jellywrap.Configuration;

public sealed class PluginConfigurationStore
{
    private volatile BasePlugin<PluginConfiguration>? _plugin;

    public PluginConfiguration? Current => _plugin?.Configuration;

    public void Save()
    {
        _plugin?.SaveConfiguration();
    }

    internal void Open(BasePlugin<PluginConfiguration> plugin)
    {
        _plugin = plugin;
    }

    internal void Close()
    {
        _plugin = null;
    }
}
