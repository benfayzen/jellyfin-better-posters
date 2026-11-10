using System;
using Jellyfin.Plugin.BetterPosters.Configuration;
using Jellyfin.Plugin.BetterPosters.Domain;
using Xunit;

namespace Jellyfin.Plugin.BetterPosters.Tests.Configuration;

public sealed class PluginConfigurationTests
{
    [Fact]
    public void NewConfiguration_HasExpectedProductDefaults()
    {
        var config = new PluginConfiguration();

        Assert.True(config.EnableTrendTags);
        Assert.False(config.EnableQualityTags);
        Assert.True(config.EnableGenre);
        Assert.True(config.EnableRating);
        Assert.False(config.EnableAgeRating);
        Assert.Equal(PosterRatingSource.Average, config.RatingSource);
        Assert.Equal(PosterLanguage.English, config.Language);
        Assert.True(config.EnableTmdbFallback);
        Assert.Equal(15, config.RequestTimeoutSeconds);
        Assert.Equal(2, config.MaxRetryAttempts);
    }

    [Fact]
    public void ValidateOrThrow_ValidConfiguration_Succeeds()
    {
        var config = new PluginConfiguration
        {
            Language = PosterLanguage.German,
            RatingSource = PosterRatingSource.RottenTomatoes,
            RequestTimeoutSeconds = 20,
            MaxRetryAttempts = 2
        };

        config.ValidateOrThrow();
        var options = config.ToPosterOptions();

        Assert.Equal(PosterLanguage.German, options.Language);
        Assert.Equal(PosterRatingSource.RottenTomatoes, options.RatingSource);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(61)]
    public void ValidateOrThrow_InvalidRequestTimeout_ThrowsArgumentOutOfRangeException(int invalidTimeout)
    {
        var config = new PluginConfiguration { RequestTimeoutSeconds = invalidTimeout };

        Assert.Throws<ArgumentOutOfRangeException>(() => config.ValidateOrThrow());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void ValidateOrThrow_InvalidMaxRetryAttempts_ThrowsArgumentOutOfRangeException(int invalidRetries)
    {
        var config = new PluginConfiguration { MaxRetryAttempts = invalidRetries };

        Assert.Throws<ArgumentOutOfRangeException>(() => config.ValidateOrThrow());
    }

    [Fact]
    public void ValidateOrThrow_InvalidEnumValue_ThrowsArgumentOutOfRangeException()
    {
        var config = new PluginConfiguration
        {
            RatingSource = (PosterRatingSource)123
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => config.ValidateOrThrow());
    }
}
