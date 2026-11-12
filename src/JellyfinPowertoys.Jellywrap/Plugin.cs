using System;
using System.Collections.Generic;
using System.Threading;

using JellyfinPowertoys.Jellywrap.Configuration;
using JellyfinPowertoys.Jellywrap.Library;

using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace JellyfinPowertoys.Jellywrap;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly JellywrapLibrary _library;
    private readonly AccessGuard _accessGuard;
    private readonly OwnerTagRestorer _ownerTagRestorer;

    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        JellywrapLibrary library,
        AccessGuard accessGuard,
        OwnerTagRestorer ownerTagRestorer) : base(applicationPaths, xmlSerializer)
    {
        _library = library;
        _accessGuard = accessGuard;
        _ownerTagRestorer = ownerTagRestorer;
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override Guid Id => new("79f37cbb-74db-44cb-a2ac-e95a5e7fbd9f");
    public override string Name => "Jellywrap";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new()
        {
            Name = "Jellywrap",
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        };
    }

    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        base.UpdateConfiguration(configuration);
        _accessGuard.ApplyAllAsync(Configuration.IndividualVisibleToAll).GetAwaiter().GetResult();
    }

    public override void OnUninstalling()
    {
        _library.RemoveAsync().GetAwaiter().GetResult();
        _ownerTagRestorer.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        _accessGuard.CleanupAsync().GetAwaiter().GetResult();
    }
}
