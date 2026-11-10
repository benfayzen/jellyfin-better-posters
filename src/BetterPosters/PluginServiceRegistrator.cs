using System;
using System.Net.Http;
using Jellyfin.Plugin.BetterPosters.Configuration;
using Jellyfin.Plugin.BetterPosters.Http;
using Jellyfin.Plugin.BetterPosters.Providers;
using Jellyfin.Plugin.BetterPosters.ScheduledTasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.BetterPosters;

/// <summary>
/// Registers Better Posters services, remote image provider, and opt-in scheduled task with the Jellyfin 12 DI container.
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

        serviceCollection.AddSingleton<IPosterRefreshGateway, JellyfinPosterRefreshGateway>();
        serviceCollection.AddSingleton<IPosterRefreshStateStore, JsonPosterRefreshStateStore>();
        serviceCollection.AddSingleton<IPosterRefreshService, PosterRefreshService>();
        serviceCollection.AddSingleton<IScheduledTask, BetterPostersRefreshTask>();
    }
}
