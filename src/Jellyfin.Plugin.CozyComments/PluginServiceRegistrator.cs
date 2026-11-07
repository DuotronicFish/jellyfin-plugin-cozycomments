using Jellyfin.Plugin.CozyComments.Data;
using Jellyfin.Plugin.CozyComments.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.CozyComments;

/// <summary>Registers the plugin's services with Jellyfin's dependency injection.</summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<CommentStore>();
        serviceCollection.AddSingleton<CoverStore>();
        serviceCollection.AddSingleton<WriteRateLimiter>();
        serviceCollection.AddHostedService<TransformationRegistrar>();
    }
}
