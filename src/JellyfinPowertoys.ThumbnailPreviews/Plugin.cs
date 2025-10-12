using System;
using System.Collections.Generic;

using JellyfinPowertoys.ThumbnailPreviews.Configuration;

using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace JellyfinPowertoys.ThumbnailPreviews;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override Guid Id => new("6a65ce4e-fb35-4e99-8f37-02bc3979fe7e");
    public override string Name => "Thumbnail Previews";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new()
        {
            Name = "Thumbnail Previews",
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        };
    }
}
