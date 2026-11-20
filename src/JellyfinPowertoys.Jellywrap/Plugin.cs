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
    private readonly PluginConfigurationStore _configurationStore;
    private readonly JellywrapLibrary _library;
    private readonly WrapCollections _collections;
    private readonly AccessGuard _accessGuard;
    private readonly OwnerTagRestorer _ownerTagRestorer;

    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        PluginConfigurationStore configurationStore,
        JellywrapLibrary library,
        WrapCollections collections,
        AccessGuard accessGuard,
        OwnerTagRestorer ownerTagRestorer) : base(applicationPaths, xmlSerializer)
    {
        _configurationStore = configurationStore;
        _library = library;
        _collections = collections;
        _accessGuard = accessGuard;
        _ownerTagRestorer = ownerTagRestorer;
        _configurationStore.Open(this);
    }

    public override Guid Id => new Guid("79f37cbb-74db-44cb-a2ac-e95a5e7fbd9f");
    public override string Name => "Jellywrap";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo()
        {
            Name = "Jellywrap",
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        };
    }

    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        base.UpdateConfiguration(configuration);
        _accessGuard.ApplyAllAsync().GetAwaiter().GetResult();
    }

    public override void OnUninstalling()
    {
        _configurationStore.Close();
        _collections.DeleteAll();
        _library.RemoveAsync().GetAwaiter().GetResult();
        _ownerTagRestorer.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        _accessGuard.CleanupAsync().GetAwaiter().GetResult();
    }
}
