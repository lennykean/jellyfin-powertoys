using System;

using MediaBrowser.Model.Plugins;

namespace JellyfinPowertoys.WatchHistoryJanitor.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; } = true;
    public TimeSpan ExpireAfter { get; set; } = TimeSpan.FromDays(30);
    public bool AllUsers { get; set; } = true;
    public string UsernameFilter { get; set; } = ".*";
}
