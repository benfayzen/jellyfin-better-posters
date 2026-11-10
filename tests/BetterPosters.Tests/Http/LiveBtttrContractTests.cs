using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.BetterPosters.Configuration;
using Jellyfin.Plugin.BetterPosters.Domain;
using Jellyfin.Plugin.BetterPosters.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.BetterPosters.Tests.Http;

public sealed class LiveBtttrContractTests
{
    private static bool IsLiveEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("BETTER_POSTERS_RUN_LIVE_TESTS"), "1", StringComparison.Ordinal);

    [Fact]
    public async Task LiveUpstream_FetchMoviePosterAndResolveTmdb_SucceedsWhenOptInEnabled()
    {
        if (!IsLiveEnabled)
        {
            return;
        }

        var client = new BtttrClient(
            new SimpleHttpClientFactory(),
            () => new PluginConfiguration(),
            NullLogger<BtttrClient>.Instance);

        var resolvedImdb = await client.ResolveImdbIdFromTmdbAsync(
            SupportedMediaType.Movie,
            "27205",
            CancellationToken.None);

        Assert.Equal("tt1375666", resolvedImdb);

        var uri = PosterUrlBuilder.BuildPosterUri(resolvedImdb!, PosterOptions.Default);
        var posterResult = await client.FetchValidatedPosterAsync(uri, CancellationToken.None);

        Assert.True(posterResult.IsSuccess);
        Assert.Equal("image/webp", posterResult.MimeType);
        Assert.True(posterResult.ImageBytes.Length > 1024);

        var missingUri = PosterUrlBuilder.BuildPosterUri("tt999999999", PosterOptions.Default);
        var missingResult = await client.FetchValidatedPosterAsync(missingUri, CancellationToken.None);

        Assert.False(missingResult.IsSuccess);
        Assert.Equal("not_found", missingResult.ReasonCode);
    }

    private sealed class SimpleHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = false
            });
        }
    }
}
