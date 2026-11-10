using System;
using Jellyfin.Plugin.BetterPosters.Domain;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.BetterPosters.Configuration;

/// <summary>
/// Typed configuration for the Better Posters Jellyfin 12 plugin.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Minimum allowed per-request timeout in seconds.
    /// </summary>
    public const int MinTimeoutSeconds = 3;

    /// <summary>
    /// Maximum allowed per-request timeout in seconds.
    /// </summary>
    public const int MaxTimeoutSeconds = 60;

    /// <summary>
    /// Maximum allowed retry attempts for transient upstream errors.
    /// </summary>
    public const int MaxAllowedRetries = 5;

    /// <summary>
    /// Gets or sets a value indicating whether trend tags (e.g., Trending, New, IMDb top rank) are enabled.
    /// </summary>
    public bool EnableTrendTags { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether quality badges (e.g., 4K, Dolby Vision, Atmos) are enabled.
    /// </summary>
    public bool EnableQualityTags { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether the bottom genre label is enabled.
    /// </summary>
    public bool EnableGenre { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the bottom rating overlay is enabled.
    /// </summary>
    public bool EnableRating { get; set; } = true;

    /// <summary>
    /// Gets or sets the rating source used when <see cref="EnableRating"/> is enabled.
    /// </summary>
    public PosterRatingSource RatingSource { get; set; } = PosterRatingSource.Average;

    /// <summary>
    /// Gets or sets a value indicating whether the age rating badge (e.g., PG-13, TV-MA, R) is enabled.
    /// </summary>
    public bool EnableAgeRating { get; set; } = false;

    /// <summary>
    /// Gets or sets the poster text and badge language.
    /// </summary>
    public PosterLanguage Language { get; set; } = PosterLanguage.English;

    /// <summary>
    /// Gets or sets a value indicating whether items lacking an IMDb ID may resolve their canonical IMDb ID
    /// via the verified btttr.cc Stremio metadata endpoint using their TMDB ID.
    /// </summary>
    public bool EnableTmdbFallback { get; set; } = true;

    /// <summary>
    /// Gets or sets the HTTP request timeout in seconds (bounded between 3 and 60).
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Gets or sets the maximum retry attempts for 429/5xx responses (bounded between 0 and 5).
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>
    /// Validates all configuration properties and throws if any value is outside supported bounds.
    /// </summary>
    public void ValidateOrThrow()
    {
        if (!Enum.IsDefined(RatingSource))
        {
            throw new ArgumentOutOfRangeException(nameof(RatingSource), RatingSource, "Unsupported rating source.");
        }

        if (!Enum.IsDefined(Language))
        {
            throw new ArgumentOutOfRangeException(nameof(Language), Language, "Unsupported language.");
        }

        if (RequestTimeoutSeconds is < MinTimeoutSeconds or > MaxTimeoutSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RequestTimeoutSeconds),
                RequestTimeoutSeconds,
                $"RequestTimeoutSeconds must be between {MinTimeoutSeconds} and {MaxTimeoutSeconds}.");
        }

        if (MaxRetryAttempts is < 0 or > MaxAllowedRetries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxRetryAttempts),
                MaxRetryAttempts,
                $"MaxRetryAttempts must be between 0 and {MaxAllowedRetries}.");
        }
    }

    /// <summary>
    /// Converts this configuration into a validated <see cref="PosterOptions"/> value object.
    /// If an in-memory field was mutated to an invalid enum value, falls back to safe defaults.
    /// </summary>
    public PosterOptions ToPosterOptions()
    {
        var ratingSource = Enum.IsDefined(RatingSource) ? RatingSource : PosterRatingSource.Average;
        var language = Enum.IsDefined(Language) ? Language : PosterLanguage.English;

        return new PosterOptions(
            EnableTrendTags: EnableTrendTags,
            EnableQualityTags: EnableQualityTags,
            EnableGenre: EnableGenre,
            EnableRating: EnableRating,
            EnableAgeRating: EnableAgeRating,
            RatingSource: ratingSource,
            Language: language,
            EnableTmdbFallback: EnableTmdbFallback);
    }
}
