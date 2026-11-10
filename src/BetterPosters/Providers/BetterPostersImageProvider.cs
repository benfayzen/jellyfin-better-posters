using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.BetterPosters.Configuration;
using Jellyfin.Plugin.BetterPosters.Domain;
using Jellyfin.Plugin.BetterPosters.Http;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BetterPosters.Providers;

/// <summary>
/// Native Jellyfin 12 remote primary-image provider for Movies and Series backed by btttr.cc.
/// </summary>
public sealed class BetterPostersImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly IBtttrClient _btttrClient;
    private readonly Func<PluginConfiguration> _configurationProvider;
    private readonly ILogger<BetterPostersImageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BetterPostersImageProvider"/> class.
    /// </summary>
    public BetterPostersImageProvider(
        IBtttrClient btttrClient,
        Func<PluginConfiguration> configurationProvider,
        ILogger<BetterPostersImageProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(btttrClient);
        ArgumentNullException.ThrowIfNull(configurationProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _btttrClient = btttrClient;
        _configurationProvider = configurationProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => Plugin.PluginName;

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool Supports(BaseItem item)
    {
        return item is Movie or Series;
    }

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return Supports(item) ? [ImageType.Primary] : [];
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Supports(item) || !TryGetSupportedMediaType(item, out var mediaType))
        {
            return [];
        }

        var config = _configurationProvider();
        var options = config.ToPosterOptions();

        var imdbId = await ResolveValidatedImdbIdAsync(item, mediaType, options, cancellationToken).ConfigureAwait(false);
        if (imdbId is null)
        {
            _logger.LogDebug(
                "Skipping Better Posters candidate for item {ItemId}: no usable IMDb or verified TMDB ID (reason={ReasonCode}).",
                item.Id,
                "missing_id");
            return [];
        }

        var posterUri = PosterUrlBuilder.BuildPosterUri(imdbId, options);
        var language = PosterUrlBuilder.GetIsoLanguageTag(options.Language);

        return
        [
            new RemoteImageInfo
            {
                ProviderName = Name,
                Url = posterUri.AbsoluteUri,
                ThumbnailUrl = posterUri.AbsoluteUri,
                Type = ImageType.Primary,
                Width = 500,
                Height = 750,
                Language = language
            }
        ];
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return _btttrClient.GetImageResponseForJellyfinAsync(url, cancellationToken);
    }

    /// <summary>
    /// Resolves a validated IMDb ID for a supported item using IMDb-first and optional verified TMDB fallback.
    /// </summary>
    internal async Task<string?> ResolveValidatedImdbIdAsync(
        BaseItem item,
        SupportedMediaType mediaType,
        PosterOptions options,
        CancellationToken cancellationToken)
    {
        var rawImdb = item.GetProviderId(MetadataProvider.Imdb);
        if (PosterIdValidator.TryNormalizeImdbId(rawImdb, out var normalizedImdb))
        {
            return normalizedImdb;
        }

        if (!options.EnableTmdbFallback)
        {
            return null;
        }

        var rawTmdb = item.GetProviderId(MetadataProvider.Tmdb);
        if (!PosterIdValidator.TryNormalizeTmdbId(rawTmdb, out var normalizedTmdb))
        {
            return null;
        }

        return await _btttrClient.ResolveImdbIdFromTmdbAsync(mediaType, normalizedTmdb, cancellationToken).ConfigureAwait(false);
    }

    internal static bool TryGetSupportedMediaType(BaseItem item, out SupportedMediaType mediaType)
    {
        switch (item)
        {
            case Movie:
                mediaType = SupportedMediaType.Movie;
                return true;
            case Series:
                mediaType = SupportedMediaType.Series;
                return true;
            default:
                mediaType = default;
                return false;
        }
    }
}
