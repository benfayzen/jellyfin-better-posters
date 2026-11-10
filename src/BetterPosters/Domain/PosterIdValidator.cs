using System;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.BetterPosters.Domain;

/// <summary>
/// Validates and normalizes external provider IDs used for btttr.cc poster lookups.
/// </summary>
public static partial class PosterIdValidator
{
    /// <summary>
    /// Canonical upstream host for btttr.cc.
    /// </summary>
    public const string AllowedUpstreamHost = "btttr.cc";

    [GeneratedRegex(@"^tt\d{7,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex ImdbIdRegex();

    [GeneratedRegex(@"^[1-9]\d{0,8}$", RegexOptions.CultureInvariant)]
    private static partial Regex TmdbIdRegex();

    [GeneratedRegex(@"^/poster/(?:movie|series)/(tt\d{7,10})/", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PosterPathImdbExtractRegex();

    /// <summary>
    /// Validates and normalizes an IMDb ID into lowercase <c>tt</c> followed by 7 to 10 digits.
    /// Rejects all-zero numeric parts.
    /// </summary>
    public static bool TryNormalizeImdbId(string? rawImdbId, out string normalizedImdbId)
    {
        normalizedImdbId = string.Empty;
        if (string.IsNullOrWhiteSpace(rawImdbId))
        {
            return false;
        }

        var trimmed = rawImdbId.Trim();
        if (trimmed.Length is < 9 or > 12)
        {
            return false;
        }

        var candidate = string.Concat("tt", trimmed.AsSpan(2));
        if (!trimmed.StartsWith("tt", StringComparison.OrdinalIgnoreCase) || !ImdbIdRegex().IsMatch(candidate))
        {
            return false;
        }

        // Reject degenerate all-zero IDs such as tt0000000.
        var digits = candidate.AsSpan(2);
        var hasNonZero = false;
        foreach (var ch in digits)
        {
            if (ch != '0')
            {
                hasNonZero = true;
                break;
            }
        }

        if (!hasNonZero)
        {
            return false;
        }

        normalizedImdbId = candidate;
        return true;
    }

    /// <summary>
    /// Validates and normalizes a TMDB ID into a positive integer string without leading zeros.
    /// Accepts an optional <c>tmdb:</c> prefix.
    /// </summary>
    public static bool TryNormalizeTmdbId(string? rawTmdbId, out string normalizedTmdbId)
    {
        normalizedTmdbId = string.Empty;
        if (string.IsNullOrWhiteSpace(rawTmdbId))
        {
            return false;
        }

        var span = rawTmdbId.AsSpan().Trim();
        if (span.StartsWith("tmdb:", StringComparison.OrdinalIgnoreCase))
        {
            span = span[5..].Trim();
        }

        var candidate = span.ToString();
        if (!TmdbIdRegex().IsMatch(candidate))
        {
            return false;
        }

        normalizedTmdbId = candidate;
        return true;
    }

    /// <summary>
    /// Extracts and validates a canonical IMDb ID from a btttr.cc poster URL returned by <c>/meta/{type}/tmdb:{id}.json</c>.
    /// </summary>
    public static bool TryExtractImdbIdFromPosterUrl(string? posterUrl, out string normalizedImdbId)
    {
        normalizedImdbId = string.Empty;
        if (string.IsNullOrWhiteSpace(posterUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(posterUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, AllowedUpstreamHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var match = PosterPathImdbExtractRegex().Match(uri.AbsolutePath);
        if (!match.Success)
        {
            return false;
        }

        return TryNormalizeImdbId(match.Groups[1].Value, out normalizedImdbId);
    }
}
