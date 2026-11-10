using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.BetterPosters.Domain;

/// <summary>
/// Deterministic URL and cache-key builder for btttr.cc poster and metadata requests.
/// </summary>
public static class PosterUrlBuilder
{
    /// <summary>
    /// Base HTTPS origin for btttr.cc.
    /// </summary>
    public const string BaseOrigin = "https://btttr.cc";

    /// <summary>
    /// Builds a deterministic HTTPS poster URI for a validated IMDb ID and options.
    /// </summary>
    public static Uri BuildPosterUri(string imdbId, PosterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (!PosterIdValidator.TryNormalizeImdbId(imdbId, out var normalizedImdbId))
        {
            throw new ArgumentException("A valid IMDb ID (tt followed by 7-10 digits) is required.", nameof(imdbId));
        }

        var routeSegment = BuildRouteSegment(options);
        var path = string.Format(
            CultureInfo.InvariantCulture,
            "{0}/{1}/imdb/poster-default/{2}.jpg",
            BaseOrigin,
            routeSegment,
            Uri.EscapeDataString(normalizedImdbId));

        var queryParts = new List<string>(3);

        if (!options.EnableTrendTags)
        {
            queryParts.Add("tag=none");
        }

        var langCode = GetLanguageCode(options.Language);
        if (langCode is not null)
        {
            queryParts.Add(string.Concat("lang=", Uri.EscapeDataString(langCode)));
        }

        if (options.EnableRating)
        {
            var ratingSourceCode = GetRatingSourceCode(options.RatingSource);
            if (ratingSourceCode is not null)
            {
                queryParts.Add(string.Concat("rs=", Uri.EscapeDataString(ratingSourceCode)));
            }
        }

        var fullUrl = queryParts.Count == 0
            ? path
            : string.Concat(path, "?", string.Join("&", queryParts));

        return new Uri(fullUrl, UriKind.Absolute);
    }

    /// <summary>
    /// Builds the verified btttr.cc Stremio metadata lookup URI for resolving a TMDB ID to its canonical IMDb ID.
    /// </summary>
    public static Uri BuildTmdbMetaUri(SupportedMediaType mediaType, string tmdbId)
    {
        if (!Enum.IsDefined(mediaType))
        {
            throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, "Unsupported media type.");
        }

        if (!PosterIdValidator.TryNormalizeTmdbId(tmdbId, out var normalizedTmdbId))
        {
            throw new ArgumentException("A valid positive numeric TMDB ID is required.", nameof(tmdbId));
        }

        var typeSegment = mediaType == SupportedMediaType.Movie ? "movie" : "series";
        var url = string.Format(
            CultureInfo.InvariantCulture,
            "{0}/meta/{1}/tmdb:{2}.json",
            BaseOrigin,
            typeSegment,
            Uri.EscapeDataString(normalizedTmdbId));

        return new Uri(url, UriKind.Absolute);
    }

    /// <summary>
    /// Builds a deterministic, credential-free cache/fingerprint key for a media item and poster configuration.
    /// </summary>
    public static string BuildCacheKey(SupportedMediaType mediaType, string normalizedProviderId, PosterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedProviderId);

        var typePart = mediaType == SupportedMediaType.Movie ? "movie" : "series";
        var routePart = BuildRouteSegment(options);
        var trendPart = options.EnableTrendTags ? "auto" : "none";
        var langPart = GetLanguageCode(options.Language) ?? "en";
        var ratingPart = options.EnableRating ? (GetRatingSourceCode(options.RatingSource) ?? "avg") : "off";

        return string.Format(
            CultureInfo.InvariantCulture,
            "bp:v1:{0}:{1}:{2}:tag={3}:lang={4}:rs={5}",
            typePart,
            normalizedProviderId.Trim(),
            routePart,
            trendPart,
            langPart,
            ratingPart);
    }

    /// <summary>
    /// Gets the ISO/BCP-47 language code expected by btttr.cc, or <c>null</c> for the default (<c>en</c>).
    /// </summary>
    public static string? GetLanguageCode(PosterLanguage language) => language switch
    {
        PosterLanguage.English => null,
        PosterLanguage.SpanishSpain => "es-ES",
        PosterLanguage.SpanishLatinAmerica => "es-MX",
        PosterLanguage.Spanish => "es",
        PosterLanguage.French => "fr",
        PosterLanguage.German => "de",
        PosterLanguage.PortugueseBrazil => "pt-BR",
        PosterLanguage.PortuguesePortugal => "pt-PT",
        PosterLanguage.Italian => "it",
        PosterLanguage.Dutch => "nl",
        PosterLanguage.Polish => "pl",
        PosterLanguage.Russian => "ru",
        PosterLanguage.Turkish => "tr",
        PosterLanguage.Arabic => "ar",
        PosterLanguage.Japanese => "ja",
        PosterLanguage.Korean => "ko",
        PosterLanguage.Chinese => "zh",
        PosterLanguage.Hindi => "hi",
        PosterLanguage.Swedish => "sv",
        PosterLanguage.Czech => "cs",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported language.")
    };

    /// <summary>
    /// Gets the language code for Jellyfin <c>RemoteImageInfo.Language</c> metadata.
    /// </summary>
    public static string GetIsoLanguageTag(PosterLanguage language) => GetLanguageCode(language) ?? "en";

    /// <summary>
    /// Gets the rating source query code expected by btttr.cc, or <c>null</c> for the default (<c>avg</c>).
    /// </summary>
    public static string? GetRatingSourceCode(PosterRatingSource ratingSource) => ratingSource switch
    {
        PosterRatingSource.Average => null,
        PosterRatingSource.Imdb => "IM",
        PosterRatingSource.Tmdb => "TM",
        PosterRatingSource.RottenTomatoes => "RT",
        PosterRatingSource.Metacritic => "MC",
        PosterRatingSource.Trakt => "TR",
        PosterRatingSource.Letterboxd => "LB",
        PosterRatingSource.RogerEbert => "RE",
        _ => throw new ArgumentOutOfRangeException(nameof(ratingSource), ratingSource, "Unsupported rating source.")
    };

    private static string BuildRouteSegment(PosterOptions options)
    {
        var baseSegment = (options.EnableGenre, options.EnableRating) switch
        {
            (true, true) => "poster",
            (true, false) => "poster-g",
            (false, true) => "poster-r",
            (false, false) => "poster-n"
        };

        var suffix = string.Concat(
            options.EnableQualityTags ? "q" : string.Empty,
            options.EnableAgeRating ? "a" : string.Empty);

        if (suffix.Length == 0)
        {
            return baseSegment;
        }

        return baseSegment.Contains('-', StringComparison.Ordinal)
            ? string.Concat(baseSegment, suffix)
            : string.Concat(baseSegment, "-", suffix);
    }
}
