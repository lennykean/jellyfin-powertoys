using System;
using System.Collections.Generic;

using JellyfinPowertoys.JellyTag.Configuration;

using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace JellyfinPowertoys.JellyTag;

public class Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : BasePlugin<PluginConfiguration>(applicationPaths, xmlSerializer), IHasWebPages
{
    public override Guid Id => new("36eb87e0-0373-423b-a547-2acb96e33430");
    public override string Name => "JellyTag";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new()
        {
            Name = "JellyTag",
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        };
    }
}
