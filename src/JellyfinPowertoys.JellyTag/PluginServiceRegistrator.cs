using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;

using Microsoft.Extensions.DependencyInjection;

namespace JellyfinPowertoys.JellyTag;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddResponseTransformer(config => config
            .TransformDocument(injectPage => injectPage
                .When(ctx => ctx.Request.Path.Equals("/web/") || ctx.Request.Path.Equals("/web/index.html"))
                .InjectScript(script => script
                    .FromEmbeddedResource($"{GetType().Namespace}.assets.jellytag.js", GetType().Assembly)
                    .Inline())
                .InjectStyleSheet(styleSheet => styleSheet
                    .FromEmbeddedResource($"{GetType().Namespace}.assets.jellytag.css", GetType().Assembly))));
    }
}
