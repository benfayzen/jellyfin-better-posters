using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.BetterPosters.Configuration;
using Jellyfin.Plugin.BetterPosters.Domain;
using Jellyfin.Plugin.BetterPosters.Http;
using Jellyfin.Plugin.BetterPosters.Providers;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.BetterPosters.Tests.Providers;

public sealed class BetterPostersImageProviderTests
{
    [Fact]
    public void Supports_MovieAndSeriesOnly_IgnoresUnsupportedTypes()
    {
        var provider = CreateProvider(new FakeBtttrClient(), new PluginConfiguration());

        Assert.True(provider.Supports(new Movie()));
        Assert.True(provider.Supports(new Series()));
        Assert.False(provider.Supports(new Season()));
        Assert.False(provider.Supports(new Episode()));
        Assert.False(provider.Supports(new Audio()));

        Assert.Equal([ImageType.Primary], provider.GetSupportedImages(new Movie()));
        Assert.Equal([ImageType.Primary], provider.GetSupportedImages(new Series()));
        Assert.Empty(provider.GetSupportedImages(new Season()));
    }

    [Fact]
    public async Task GetImages_MovieWithValidImdbId_ReturnsDeterministicCandidateWithoutTmdbLookup()
    {
        var fakeClient = new FakeBtttrClient();
        var config = new PluginConfiguration
        {
            EnableTrendTags = true,
            EnableQualityTags = true,
            EnableGenre = true,
            EnableRating = true,
            RatingSource = PosterRatingSource.RottenTomatoes,
            Language = PosterLanguage.French
        };
        var provider = CreateProvider(fakeClient, config);

        var movie = new Movie { Name = "Inception" };
        movie.SetProviderId(MetadataProvider.Imdb, "tt1375666");
        movie.SetProviderId(MetadataProvider.Tmdb, "27205");

        var images = (await provider.GetImages(movie, CancellationToken.None)).ToList();

        var image = Assert.Single(images);
        Assert.Equal("Better Posters", image.ProviderName);
        Assert.Equal(ImageType.Primary, image.Type);
        Assert.Equal("fr", image.Language);
        Assert.Equal("https://btttr.cc/poster-q/imdb/poster-default/tt1375666.jpg?lang=fr&rs=RT", image.Url);
        Assert.Equal(0, fakeClient.TmdbResolveCalls);
    }

    [Fact]
    public async Task GetImages_SeriesWithoutImdbId_UsesVerifiedTmdbFallbackWhenEnabled()
    {
        var fakeClient = new FakeBtttrClient
        {
            ResolvedImdbId = "tt0903747"
        };
        var config = new PluginConfiguration
        {
            EnableTmdbFallback = true,
            Language = PosterLanguage.German
        };
        var provider = CreateProvider(fakeClient, config);

        var series = new Series { Name = "Breaking Bad" };
        series.SetProviderId(MetadataProvider.Tmdb, "1396");

        var images = (await provider.GetImages(series, CancellationToken.None)).ToList();

        var image = Assert.Single(images);
        Assert.Equal("https://btttr.cc/poster/imdb/poster-default/tt0903747.jpg?lang=de", image.Url);
        Assert.Equal(1, fakeClient.TmdbResolveCalls);
        Assert.Equal(SupportedMediaType.Series, fakeClient.LastMediaType);
        Assert.Equal("1396", fakeClient.LastTmdbId);
    }

    [Fact]
    public async Task GetImages_SeriesWithoutImdbId_WhenTmdbFallbackDisabled_ReturnsEmpty()
    {
        var fakeClient = new FakeBtttrClient
        {
            ResolvedImdbId = "tt0903747"
        };
        var config = new PluginConfiguration
        {
            EnableTmdbFallback = false
        };
        var provider = CreateProvider(fakeClient, config);

        var series = new Series { Name = "Breaking Bad" };
        series.SetProviderId(MetadataProvider.Tmdb, "1396");

        var images = await provider.GetImages(series, CancellationToken.None);

        Assert.Empty(images);
        Assert.Equal(0, fakeClient.TmdbResolveCalls);
    }

    [Fact]
    public async Task GetImages_ItemWithInvalidImdbAndUnresolvableTmdb_ReturnsEmptyGracefully()
    {
        var fakeClient = new FakeBtttrClient
        {
            ResolvedImdbId = null
        };
        var provider = CreateProvider(fakeClient, new PluginConfiguration());

        var movie = new Movie { Name = "Unknown" };
        movie.SetProviderId(MetadataProvider.Imdb, "invalid-id");
        movie.SetProviderId(MetadataProvider.Tmdb, "99999999");

        var images = await provider.GetImages(movie, CancellationToken.None);

        Assert.Empty(images);
        Assert.Equal(1, fakeClient.TmdbResolveCalls);
    }

    [Fact]
    public async Task GetImageResponse_DelegatesToValidatedBtttrClient()
    {
        var fakeClient = new FakeBtttrClient();
        var provider = CreateProvider(fakeClient, new PluginConfiguration());

        using var response = await provider.GetImageResponse(
            "https://btttr.cc/poster/imdb/poster-default/tt1375666.jpg",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fakeClient.GetImageResponseCalls);
    }

    private static BetterPostersImageProvider CreateProvider(IBtttrClient client, PluginConfiguration config)
    {
        return new BetterPostersImageProvider(
            client,
            () => config,
            NullLogger<BetterPostersImageProvider>.Instance);
    }

    private sealed class FakeBtttrClient : IBtttrClient
    {
        public string? ResolvedImdbId { get; set; }

        public int TmdbResolveCalls { get; private set; }

        public int GetImageResponseCalls { get; private set; }

        public SupportedMediaType? LastMediaType { get; private set; }

        public string? LastTmdbId { get; private set; }

        public Task<BtttrImageResult> FetchValidatedPosterAsync(Uri posterUri, CancellationToken cancellationToken)
        {
            return Task.FromResult(BtttrImageResult.Success([1, 2, 3], "image/webp"));
        }

        public Task<string?> ResolveImdbIdFromTmdbAsync(
            SupportedMediaType mediaType,
            string tmdbId,
            CancellationToken cancellationToken)
        {
            TmdbResolveCalls++;
            LastMediaType = mediaType;
            LastTmdbId = tmdbId;
            return Task.FromResult(ResolvedImdbId);
        }

        public Task<HttpResponseMessage> GetImageResponseForJellyfinAsync(string url, CancellationToken cancellationToken)
        {
            GetImageResponseCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
