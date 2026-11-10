using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.BetterPosters.Domain;

namespace Jellyfin.Plugin.BetterPosters.Http;

/// <summary>
/// Safe HTTP adapter for communicating with https://btttr.cc.
/// </summary>
public interface IBtttrClient
{
    /// <summary>
    /// Downloads and validates a poster image from an allowed btttr.cc HTTPS URI.
    /// </summary>
    Task<BtttrImageResult> FetchValidatedPosterAsync(Uri posterUri, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a numeric TMDB ID to its canonical IMDb ID via the verified btttr.cc Stremio meta endpoint.
    /// </summary>
    Task<string?> ResolveImdbIdFromTmdbAsync(
        SupportedMediaType mediaType,
        string tmdbId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Downloads and validates a poster image and wraps it in an <see cref="HttpResponseMessage"/> for Jellyfin's <c>IRemoteImageProvider.GetImageResponse</c>.
    /// </summary>
    Task<HttpResponseMessage> GetImageResponseForJellyfinAsync(string url, CancellationToken cancellationToken);
}
