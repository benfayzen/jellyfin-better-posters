using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.BetterPosters.Configuration;
using Jellyfin.Plugin.BetterPosters.Domain;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BetterPosters.Http;

/// <summary>
/// Bounded, redirect-validated, rate-limit-aware HTTP client adapter for btttr.cc.
/// </summary>
public sealed class BtttrClient : IBtttrClient
{
    /// <summary>
    /// Named HttpClient identifier registered with DI.
    /// </summary>
    public const string HttpClientName = "Jellyfin.Plugin.BetterPosters.BtttrClient";

    /// <summary>
    /// Dedicated User-Agent header sent on all upstream requests.
    /// </summary>
    public const string UserAgentValue = "Jellyfin-BetterPosters/1.0 (+https://github.com/benfayzen/jellyfin-better-posters)";

    private const int MaxRedirects = 3;
    private const int MaxMetaBytes = 256 * 1024;
    private const int MaxTmdbCacheEntries = 2048;
    private static readonly TimeSpan TmdbCacheTtl = TimeSpan.FromHours(12);
    private static readonly TimeSpan MaxRetryDelayCap = TimeSpan.FromSeconds(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<PluginConfiguration> _configurationProvider;
    private readonly ILogger<BtttrClient> _logger;
    private readonly TimeSpan? _requestTimeoutOverride;
    private readonly TimeSpan _retryBaseDelay;
    private readonly int _maxImageBytes;
    private readonly ConcurrentDictionary<string, (string? ImdbId, DateTimeOffset ExpiresAt)> _tmdbResolutionCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="BtttrClient"/> class.
    /// </summary>
    public BtttrClient(
        IHttpClientFactory httpClientFactory,
        Func<PluginConfiguration> configurationProvider,
        ILogger<BtttrClient> logger,
        TimeSpan? requestTimeoutOverride = null,
        TimeSpan? retryBaseDelayOverride = null,
        int maxImageBytesOverride = ImageContentValidator.DefaultMaxImageBytes)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(configurationProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _configurationProvider = configurationProvider;
        _logger = logger;
        _requestTimeoutOverride = requestTimeoutOverride;
        _retryBaseDelay = retryBaseDelayOverride ?? TimeSpan.FromMilliseconds(250);
        _maxImageBytes = maxImageBytesOverride > 0 ? maxImageBytesOverride : ImageContentValidator.DefaultMaxImageBytes;
    }

    /// <inheritdoc />
    public async Task<BtttrImageResult> FetchValidatedPosterAsync(Uri posterUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(posterUri);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsAllowedUpstreamUri(posterUri))
        {
            _logger.LogWarning("Rejected poster request to untrusted URI host or scheme (reason={ReasonCode}).", "untrusted_host");
            return BtttrImageResult.Failure("untrusted_host");
        }

        var (response, failureReason) = await SendWithRedirectsAndRetriesAsync(posterUri, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return BtttrImageResult.Failure(failureReason);
        }

        using (response)
        {
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > _maxImageBytes)
            {
                _logger.LogWarning(
                    "Rejected oversized poster response header ({ContentLength} bytes > {MaxBytes} bytes, reason={ReasonCode}).",
                    contentLength.Value,
                    _maxImageBytes,
                    "payload_too_large");
                return BtttrImageResult.Failure("payload_too_large");
            }

            var (bytes, readError) = await ReadBoundedBytesAsync(response.Content, _maxImageBytes, GetRequestTimeout(), cancellationToken).ConfigureAwait(false);
            if (bytes is null)
            {
                return BtttrImageResult.Failure(readError);
            }

            var contentTypeHeader = response.Content.Headers.ContentType?.ToString();
            if (!ImageContentValidator.TryValidateImage(bytes, contentTypeHeader, out var detectedMimeType, out var validationReason, _maxImageBytes))
            {
                _logger.LogWarning(
                    "Rejected invalid poster payload from upstream (reason={ReasonCode}, contentType={ContentType}, length={ByteLength}).",
                    validationReason,
                    contentTypeHeader ?? "none",
                    bytes.Length);
                return BtttrImageResult.Failure(validationReason);
            }

            return BtttrImageResult.Success(bytes, detectedMimeType);
        }
    }

    /// <inheritdoc />
    public async Task<string?> ResolveImdbIdFromTmdbAsync(
        SupportedMediaType mediaType,
        string tmdbId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!PosterIdValidator.TryNormalizeTmdbId(tmdbId, out var normalizedTmdbId))
        {
            return null;
        }

        var cacheKey = $"{(mediaType == SupportedMediaType.Movie ? "movie" : "series")}:{normalizedTmdbId}";
        var now = DateTimeOffset.UtcNow;
        if (_tmdbResolutionCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > now)
        {
            return cached.ImdbId;
        }

        var metaUri = PosterUrlBuilder.BuildTmdbMetaUri(mediaType, normalizedTmdbId);
        var (response, _) = await SendWithRedirectsAndRetriesAsync(metaUri, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        using (response)
        {
            var (bytes, _) = await ReadBoundedBytesAsync(response.Content, MaxMetaBytes, GetRequestTimeout(), cancellationToken).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(bytes);
                if (!doc.RootElement.TryGetProperty("meta", out var metaElement)
                    || metaElement.ValueKind != JsonValueKind.Object)
                {
                    CacheTmdbResolution(cacheKey, null, now);
                    return null;
                }

                string? resolvedImdb = null;
                if (metaElement.TryGetProperty("imdb_id", out var imdbProp)
                    && imdbProp.ValueKind == JsonValueKind.String
                    && PosterIdValidator.TryNormalizeImdbId(imdbProp.GetString(), out var directImdb))
                {
                    resolvedImdb = directImdb;
                }
                else if (metaElement.TryGetProperty("poster", out var posterProp)
                    && posterProp.ValueKind == JsonValueKind.String
                    && PosterIdValidator.TryExtractImdbIdFromPosterUrl(posterProp.GetString(), out var extractedImdb))
                {
                    resolvedImdb = extractedImdb;
                }

                CacheTmdbResolution(cacheKey, resolvedImdb, now);
                return resolvedImdb;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse btttr.cc TMDB metadata response (reason={ReasonCode}).", "invalid_meta_json");
                return null;
            }
        }
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> GetImageResponseForJellyfinAsync(string url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var result = await FetchValidatedPosterAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var content = new ByteArrayContent(result.ImageBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(result.MimeType);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        };
    }

    private static bool IsAllowedUpstreamUri(Uri? uri)
    {
        return uri is not null
            && uri.IsAbsoluteUri
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, PosterIdValidator.AllowedUpstreamHost, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(uri.UserInfo);
    }

    private void CacheTmdbResolution(string cacheKey, string? imdbId, DateTimeOffset now)
    {
        if (_tmdbResolutionCache.Count >= MaxTmdbCacheEntries)
        {
            _tmdbResolutionCache.Clear();
        }

        _tmdbResolutionCache[cacheKey] = (imdbId, now.Add(TmdbCacheTtl));
    }

    private TimeSpan GetRequestTimeout()
    {
        if (_requestTimeoutOverride.HasValue)
        {
            return _requestTimeoutOverride.Value;
        }

        var config = _configurationProvider();
        var seconds = Math.Clamp(config.RequestTimeoutSeconds, PluginConfiguration.MinTimeoutSeconds, PluginConfiguration.MaxTimeoutSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private int GetMaxRetries()
    {
        var config = _configurationProvider();
        return Math.Clamp(config.MaxRetryAttempts, 0, PluginConfiguration.MaxAllowedRetries);
    }

    private async Task<(HttpResponseMessage? Response, string FailureReason)> SendWithRedirectsAndRetriesAsync(
        Uri initialUri,
        CancellationToken cancellationToken)
    {
        var maxRetries = GetMaxRetries();
        var timeout = GetRequestTimeout();
        var client = _httpClientFactory.CreateClient(HttpClientName);

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (response, reason, shouldRetry, retryDelay) = await SendSingleAttemptWithRedirectsAsync(
                client,
                initialUri,
                timeout,
                attempt,
                cancellationToken).ConfigureAwait(false);

            if (response is not null)
            {
                return (response, "ok");
            }

            if (!shouldRetry || attempt == maxRetries)
            {
                return (null, reason);
            }

            if (retryDelay > TimeSpan.Zero)
            {
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        return (null, "upstream_error");
    }

    private async Task<(HttpResponseMessage? Response, string Reason, bool ShouldRetry, TimeSpan RetryDelay)> SendSingleAttemptWithRedirectsAsync(
        HttpClient client,
        Uri initialUri,
        TimeSpan timeout,
        int attempt,
        CancellationToken cancellationToken)
    {
        var currentUri = initialUri;

        for (var redirectHop = 0; redirectHop <= MaxRedirects; redirectHop++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsAllowedUpstreamUri(currentUri))
            {
                return (null, "redirect_rejected", false, TimeSpan.Zero);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
                request.Headers.UserAgent.ParseAdd(UserAgentValue);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/webp"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json", 0.9));

                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Upstream request to btttr.cc timed out after {TimeoutMs} ms (reason={ReasonCode}).", timeout.TotalMilliseconds, "timeout");
                return (null, "timeout", attempt > 0, CalculateBackoff(attempt));
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Upstream network error communicating with btttr.cc (reason={ReasonCode}).", "network_error");
                return (null, "network_error", true, CalculateBackoff(attempt));
            }

            // Verify final request URI in case an underlying handler automatically followed a redirect.
            if (response.RequestMessage?.RequestUri is { } finalUri && !IsAllowedUpstreamUri(finalUri))
            {
                response.Dispose();
                _logger.LogWarning("Rejected automatic redirect to untrusted URI (reason={ReasonCode}).", "redirect_rejected");
                return (null, "redirect_rejected", false, TimeSpan.Zero);
            }

            if (IsRedirectStatusCode(response.StatusCode))
            {
                var isNoArtFallback = response.Headers.TryGetValues("x-bp-fallback", out var fallbackValues)
                    && System.Linq.Enumerable.Any(fallbackValues, v => string.Equals(v?.Trim(), "noart", StringComparison.OrdinalIgnoreCase));
                var location = response.Headers.Location;
                response.Dispose();

                if (isNoArtFallback)
                {
                    _logger.LogDebug("Upstream btttr.cc indicated no artwork via x-bp-fallback=noart redirect (reason={ReasonCode}).", "not_found");
                    return (null, "not_found", false, TimeSpan.Zero);
                }

                if (location is null || redirectHop == MaxRedirects)
                {
                    return (null, "redirect_rejected", false, TimeSpan.Zero);
                }

                var nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                if (!IsAllowedUpstreamUri(nextUri))
                {
                    _logger.LogWarning("Rejected redirect hop to untrusted target (reason={ReasonCode}).", "redirect_rejected");
                    return (null, "redirect_rejected", false, TimeSpan.Zero);
                }

                currentUri = nextUri;
                continue;
            }

            if (response.StatusCode == HttpStatusCode.OK)
            {
                return (response, "ok", false, TimeSpan.Zero);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                response.Dispose();
                return (null, "not_found", false, TimeSpan.Zero);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter;
                response.Dispose();
                var canRetryWithinBudget = TryCalculateRetryDelay(attempt, retryAfter, out var delay);
                _logger.LogWarning("Rate limited by btttr.cc (HTTP 429, reason={ReasonCode}).", "rate_limited");
                return (null, "rate_limited", canRetryWithinBudget, delay);
            }

            if ((int)response.StatusCode >= 500)
            {
                var statusCode = (int)response.StatusCode;
                response.Dispose();
                _logger.LogWarning("Transient upstream error from btttr.cc (HTTP {StatusCode}, reason={ReasonCode}).", statusCode, "upstream_error");
                return (null, "upstream_error", true, CalculateBackoff(attempt));
            }

            response.Dispose();
            return (null, "upstream_error", false, TimeSpan.Zero);
        }

        return (null, "redirect_rejected", false, TimeSpan.Zero);
    }

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }

    private bool TryCalculateRetryDelay(int attempt, RetryConditionHeaderValue? retryAfter, out TimeSpan delay)
    {
        if (retryAfter?.Delta is { } delta)
        {
            if (delta <= TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
                return true;
            }

            if (delta > MaxRetryDelayCap)
            {
                delay = TimeSpan.Zero;
                return false;
            }

            delay = delta;
            return true;
        }

        if (retryAfter?.Date is { } date)
        {
            var diff = date - DateTimeOffset.UtcNow;
            if (diff <= TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
                return true;
            }

            if (diff > MaxRetryDelayCap)
            {
                delay = TimeSpan.Zero;
                return false;
            }

            delay = diff;
            return true;
        }

        delay = CalculateBackoff(attempt);
        return true;
    }

    private TimeSpan CalculateBackoff(int attempt)
    {
        var factor = 1 << Math.Clamp(attempt, 0, 4);
        var computed = TimeSpan.FromMilliseconds(_retryBaseDelay.TotalMilliseconds * factor);
        return computed <= MaxRetryDelayCap ? computed : MaxRetryDelayCap;
    }

    private static async Task<(byte[]? Bytes, string FailureReason)> ReadBoundedBytesAsync(
        HttpContent content,
        int maxBytes,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            var stream = await content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var ms = new MemoryStream();
                var buffer = new byte[8192];
                var totalRead = 0;

                while (true)
                {
                    var read = await stream.ReadAsync(buffer, timeoutCts.Token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    totalRead += read;
                    if (totalRead > maxBytes)
                    {
                        return (null, "payload_too_large");
                    }

                    ms.Write(buffer, 0, read);
                }

                return (ms.ToArray(), "ok");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "timeout");
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            return (null, "network_error");
        }
    }
}
