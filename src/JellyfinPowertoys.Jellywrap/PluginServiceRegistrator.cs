using Jellyfin.Data.Events.Users;

using JellyfinPowertoys.Jellywrap.Audio;
using JellyfinPowertoys.Jellywrap.Configuration;
using JellyfinPowertoys.Jellywrap.Context;
using JellyfinPowertoys.Jellywrap.Library;

using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Plugins;

using Microsoft.Extensions.DependencyInjection;

namespace JellyfinPowertoys.Jellywrap;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<PluginConfigurationStore>();
        serviceCollection.AddSingleton<WrapSourceReader>();
        serviceCollection.AddSingleton<ItemMarkerReader>();
        serviceCollection.AddSingleton<AudioProbe>();
        serviceCollection.AddSingleton<ClipFinder>();
        serviceCollection.AddSingleton<VideoEncoder>();
        serviceCollection.AddSingleton<WrapProducer>();
        serviceCollection.AddSingleton<JellywrapLibrary>();
        serviceCollection.AddSingleton<OwnerFolders>();
        serviceCollection.AddSingleton<WrapCollections>();
        serviceCollection.AddSingleton<WrapPublisher>();
        serviceCollection.AddSingleton<AccessGuard>();
        serviceCollection.AddSingleton<IEventConsumer<UserCreatedEventArgs>>(s => s.GetRequiredService<AccessGuard>());
        serviceCollection.AddSingleton<IEventConsumer<UserUpdatedEventArgs>>(s => s.GetRequiredService<AccessGuard>());
        serviceCollection.AddHostedService(s => s.GetRequiredService<AccessGuard>());
        serviceCollection.AddSingleton<OwnerTagRestorer>();
        serviceCollection.AddHostedService(s => s.GetRequiredService<OwnerTagRestorer>());
        serviceCollection.AddHostedService<GenerateTaskOnStartup>();
    }
}
