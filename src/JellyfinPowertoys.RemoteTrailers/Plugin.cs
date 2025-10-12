using System;
using System.Collections.Generic;

using JellyfinPowertoys.RemoteTrailers.Configuration;

using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace JellyfinPowertoys.RemoteTrailers;

public class Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) :
    BasePlugin<PluginConfiguration>(applicationPaths, xmlSerializer), IHasWebPages
{
    public override Guid Id => new("f0194bb3-788d-43b8-96c9-bb32f996db4a");
    public override string Name => "Remote Trailers";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new()
        {
            Name = "Remote Trailers",
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        };
    }
}
