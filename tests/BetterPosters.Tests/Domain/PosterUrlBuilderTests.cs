using System;
using Jellyfin.Plugin.BetterPosters.Domain;
using Xunit;

namespace Jellyfin.Plugin.BetterPosters.Tests.Domain;

public sealed class PosterUrlBuilderTests
{
    [Fact]
    public void DefaultOptions_MatchProductRequirements()
    {
        var defaults = PosterOptions.Default;

        Assert.True(defaults.EnableTrendTags);
        Assert.False(defaults.EnableQualityTags);
        Assert.True(defaults.EnableGenre);
        Assert.True(defaults.EnableRating);
        Assert.False(defaults.EnableAgeRating);
        Assert.Equal(PosterRatingSource.Average, defaults.RatingSource);
        Assert.Equal(PosterLanguage.English, defaults.Language);
        Assert.True(defaults.EnableTmdbFallback);
    }

    [Fact]
    public void BuildPosterUri_WithDefaultOptions_ProducesCanonicalBaseUrlWithoutQueryString()
    {
        var uri = PosterUrlBuilder.BuildPosterUri("tt1375666", PosterOptions.Default);

        Assert.Equal("https://btttr.cc/poster/imdb/poster-default/tt1375666.jpg", uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(true, true, false, false, "poster")]
    [InlineData(true, false, false, false, "poster-g")]
    [InlineData(false, true, false, false, "poster-r")]
    [InlineData(false, false, false, false, "poster-n")]
    [InlineData(true, true, true, false, "poster-q")]
    [InlineData(true, true, false, true, "poster-a")]
    [InlineData(true, true, true, true, "poster-qa")]
    [InlineData(true, false, true, true, "poster-gqa")]
    [InlineData(false, true, true, false, "poster-rq")]
    [InlineData(false, false, false, true, "poster-na")]
    [InlineData(false, false, true, true, "poster-nqa")]
    public void BuildPosterUri_OverlayCombinations_BuildsExpectedRouteSegment(
        bool genre,
        bool rating,
        bool quality,
        bool ageRating,
        string expectedSegment)
    {
        var options = PosterOptions.Default with
        {
            EnableGenre = genre,
            EnableRating = rating,
            EnableQualityTags = quality,
            EnableAgeRating = ageRating
        };

        var uri = PosterUrlBuilder.BuildPosterUri("tt0903747", options);

        Assert.Equal($"https://btttr.cc/{expectedSegment}/imdb/poster-default/tt0903747.jpg", uri.AbsoluteUri);
    }

    [Fact]
    public void BuildPosterUri_WithTrendDisabledGermanAndImdbRating_BuildsDeterministicQueryString()
    {
        var options = PosterOptions.Default with
        {
            EnableTrendTags = false,
            EnableQualityTags = true,
            EnableAgeRating = true,
            Language = PosterLanguage.German,
            RatingSource = PosterRatingSource.Imdb
        };

        var uri = PosterUrlBuilder.BuildPosterUri("TT1375666", options);

        Assert.Equal(
            "https://btttr.cc/poster-qa/imdb/poster-default/tt1375666.jpg?tag=none&lang=de&rs=IM",
            uri.AbsoluteUri);
    }

    [Fact]
    public void BuildPosterUri_WhenRatingDisabled_OmitsRatingSourceQueryParameter()
    {
        var options = PosterOptions.Default with
        {
            EnableRating = false,
            RatingSource = PosterRatingSource.RottenTomatoes
        };

        var uri = PosterUrlBuilder.BuildPosterUri("tt1375666", options);

        Assert.Equal("https://btttr.cc/poster-g/imdb/poster-default/tt1375666.jpg", uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(PosterRatingSource.Average, null)]
    [InlineData(PosterRatingSource.Imdb, "IM")]
    [InlineData(PosterRatingSource.Tmdb, "TM")]
    [InlineData(PosterRatingSource.RottenTomatoes, "RT")]
    [InlineData(PosterRatingSource.Metacritic, "MC")]
    [InlineData(PosterRatingSource.Trakt, "TR")]
    [InlineData(PosterRatingSource.Letterboxd, "LB")]
    [InlineData(PosterRatingSource.RogerEbert, "RE")]
    public void BuildPosterUri_SupportedRatingSources_UsesVerifiedUpstreamCode(
        PosterRatingSource source,
        string? expectedCode)
    {
        var options = PosterOptions.Default with { RatingSource = source };
        var uri = PosterUrlBuilder.BuildPosterUri("tt1375666", options);

        if (expectedCode is null)
        {
            Assert.Equal("https://btttr.cc/poster/imdb/poster-default/tt1375666.jpg", uri.AbsoluteUri);
        }
        else
        {
            Assert.Equal($"https://btttr.cc/poster/imdb/poster-default/tt1375666.jpg?rs={expectedCode}", uri.AbsoluteUri);
        }
    }

    [Theory]
    [InlineData(PosterLanguage.English, null)]
    [InlineData(PosterLanguage.SpanishSpain, "es-ES")]
    [InlineData(PosterLanguage.SpanishLatinAmerica, "es-MX")]
    [InlineData(PosterLanguage.Spanish, "es")]
    [InlineData(PosterLanguage.French, "fr")]
    [InlineData(PosterLanguage.German, "de")]
    [InlineData(PosterLanguage.PortugueseBrazil, "pt-BR")]
    [InlineData(PosterLanguage.PortuguesePortugal, "pt-PT")]
    [InlineData(PosterLanguage.Italian, "it")]
    [InlineData(PosterLanguage.Dutch, "nl")]
    [InlineData(PosterLanguage.Polish, "pl")]
    [InlineData(PosterLanguage.Russian, "ru")]
    [InlineData(PosterLanguage.Turkish, "tr")]
    [InlineData(PosterLanguage.Arabic, "ar")]
    [InlineData(PosterLanguage.Japanese, "ja")]
    [InlineData(PosterLanguage.Korean, "ko")]
    [InlineData(PosterLanguage.Chinese, "zh")]
    [InlineData(PosterLanguage.Hindi, "hi")]
    [InlineData(PosterLanguage.Swedish, "sv")]
    [InlineData(PosterLanguage.Czech, "cs")]
    public void BuildPosterUri_SupportedLanguages_UsesVerifiedUpstreamCode(
        PosterLanguage language,
        string? expectedCode)
    {
        var options = PosterOptions.Default with { Language = language };
        var uri = PosterUrlBuilder.BuildPosterUri("tt1375666", options);

        if (expectedCode is null)
        {
            Assert.Equal("https://btttr.cc/poster/imdb/poster-default/tt1375666.jpg", uri.AbsoluteUri);
        }
        else
        {
            Assert.Equal($"https://btttr.cc/poster/imdb/poster-default/tt1375666.jpg?lang={expectedCode}", uri.AbsoluteUri);
        }
    }

    [Theory]
    [InlineData(SupportedMediaType.Movie, "27205", "https://btttr.cc/meta/movie/tmdb:27205.json")]
    [InlineData(SupportedMediaType.Series, "1396", "https://btttr.cc/meta/series/tmdb:1396.json")]
    public void BuildTmdbMetaUri_ValidMediaTypeAndId_BuildsVerifiedMetaEndpoint(
        SupportedMediaType mediaType,
        string tmdbId,
        string expectedUrl)
    {
        var uri = PosterUrlBuilder.BuildTmdbMetaUri(mediaType, tmdbId);

        Assert.Equal(expectedUrl, uri.AbsoluteUri);
    }

    [Fact]
    public void BuildPosterUri_InvalidImdbId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => PosterUrlBuilder.BuildPosterUri("invalid", PosterOptions.Default));
    }

    [Fact]
    public void BuildPosterUri_InvalidEnumValue_ThrowsArgumentOutOfRangeException()
    {
        var invalidOptions = PosterOptions.Default with
        {
            Language = (PosterLanguage)999
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => PosterUrlBuilder.BuildPosterUri("tt1375666", invalidOptions));
    }

    [Fact]
    public void BuildCacheKey_VariesByMediaTypeIdAndOptions_ContainsNoSecrets()
    {
        var key1 = PosterUrlBuilder.BuildCacheKey(SupportedMediaType.Movie, "tt1375666", PosterOptions.Default);
        var key2 = PosterUrlBuilder.BuildCacheKey(SupportedMediaType.Series, "tt1375666", PosterOptions.Default);
        var key3 = PosterUrlBuilder.BuildCacheKey(
            SupportedMediaType.Movie,
            "tt1375666",
            PosterOptions.Default with { Language = PosterLanguage.Japanese });

        Assert.NotEqual(key1, key2);
        Assert.NotEqual(key1, key3);
        Assert.DoesNotContain("token", key1, StringComparison.OrdinalIgnoreCase);
    }
}
