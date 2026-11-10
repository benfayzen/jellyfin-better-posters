using System;
using System.Net.Http;
using Jellyfin.Plugin.BetterPosters.Configuration;
using Jellyfin.Plugin.BetterPosters.Http;
using Jellyfin.Plugin.BetterPosters.Providers;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.BetterPosters;

/// <summary>
/// Registers Better Posters services and remote image provider with the Jellyfin 12 DI container.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection
            .AddHttpClient(BtttrClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        serviceCollection.AddSingleton<Func<PluginConfiguration>>(
            _ => () => Plugin.Instance?.Configuration ?? new PluginConfiguration());

        serviceCollection.AddSingleton<IBtttrClient, BtttrClient>();
        serviceCollection.AddSingleton<IRemoteImageProvider, BetterPostersImageProvider>();
    }
}
