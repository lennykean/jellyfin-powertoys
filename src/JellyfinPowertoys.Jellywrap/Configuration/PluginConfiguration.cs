using System.Collections.Generic;

using MediaBrowser.Model.Plugins;

namespace JellyfinPowertoys.Jellywrap.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public int GenerationMonth { get; set; } = 12;
    public int GenerationDay { get; set; } = 1;
    public bool IndividualVisibleToAll { get; set; }
    public List<UserSettings> UserSettings { get; set; } = [];
    public bool GenerateServerVideo { get; set; } = true;
    public string? ServerTemplateId { get; set; }
    public string? CustomTemplateFolder { get; set; }
    public int? LastGeneratedYear { get; set; }
    public int? ForceYear { get; set; }
}
