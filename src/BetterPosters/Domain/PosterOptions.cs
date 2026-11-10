using System;

namespace Jellyfin.Plugin.BetterPosters.Domain;

/// <summary>
/// Immutable value object representing validated poster generation options.
/// </summary>
public sealed record PosterOptions(
    bool EnableTrendTags,
    bool EnableQualityTags,
    bool EnableGenre,
    bool EnableRating,
    bool EnableAgeRating,
    PosterRatingSource RatingSource,
    PosterLanguage Language,
    bool EnableTmdbFallback)
{
    /// <summary>
    /// Gets the product default poster options:
    /// trend on, quality off, genre on, ratings on, age rating off, average rating source, English, TMDB fallback on.
    /// </summary>
    public static PosterOptions Default { get; } = new(
        EnableTrendTags: true,
        EnableQualityTags: false,
        EnableGenre: true,
        EnableRating: true,
        EnableAgeRating: false,
        RatingSource: PosterRatingSource.Average,
        Language: PosterLanguage.English,
        EnableTmdbFallback: true);

    /// <summary>
    /// Validates that all enum fields in this instance have defined values.
    /// </summary>
    public void Validate()
    {
        if (!Enum.IsDefined(RatingSource))
        {
            throw new ArgumentOutOfRangeException(nameof(RatingSource), RatingSource, "Unsupported rating source value.");
        }

        if (!Enum.IsDefined(Language))
        {
            throw new ArgumentOutOfRangeException(nameof(Language), Language, "Unsupported poster language value.");
        }
    }
}
