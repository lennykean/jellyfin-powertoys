using System;
using System.Collections.Generic;

using JellyfinPowertoys.PrivacyMode.Configuration;

using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace JellyfinPowertoys.PrivacyMode;

public class Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : BasePlugin<PluginConfiguration>(applicationPaths, xmlSerializer), IHasWebPages
{
    public override Guid Id => new("c03a2469-eed0-4107-95b1-6dfda14fb0fc");
    public override string Name => "Privacy Mode";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new()
        {
            Name = "Privacy Mode",
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        };
    }
}
