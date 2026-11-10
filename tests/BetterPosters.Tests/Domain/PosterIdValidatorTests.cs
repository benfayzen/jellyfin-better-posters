using Jellyfin.Plugin.BetterPosters.Domain;
using Xunit;

namespace Jellyfin.Plugin.BetterPosters.Tests.Domain;

public sealed class PosterIdValidatorTests
{
    [Theory]
    [InlineData("tt1375666", "tt1375666")]
    [InlineData("TT0903747", "tt0903747")]
    [InlineData("  tt35538033  ", "tt35538033")]
    [InlineData("Tt0111161", "tt0111161")]
    [InlineData("tt1234567890", "tt1234567890")]
    public void TryNormalizeImdbId_ValidIds_ReturnsTrueAndNormalizedId(string input, string expected)
    {
        var ok = PosterIdValidator.TryNormalizeImdbId(input, out var normalized);

        Assert.True(ok);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1375666")]
    [InlineData("tt12345")]
    [InlineData("tt12345678901")]
    [InlineData("tt0000000")]
    [InlineData("tt00000000")]
    [InlineData("tt1375666/../../etc/passwd")]
    [InlineData("tt1375666?tag=none")]
    [InlineData("nm0000138")]
    [InlineData("tmdb:27205")]
    public void TryNormalizeImdbId_InvalidIds_ReturnsFalse(string? input)
    {
        var ok = PosterIdValidator.TryNormalizeImdbId(input, out var normalized);

        Assert.False(ok);
        Assert.Equal(string.Empty, normalized);
    }

    [Theory]
    [InlineData("27205", "27205")]
    [InlineData("  1396  ", "1396")]
    [InlineData("tmdb:27205", "27205")]
    [InlineData("TMDB:157336", "157336")]
    [InlineData("1", "1")]
    public void TryNormalizeTmdbId_ValidIds_ReturnsTrueAndNormalizedDigits(string input, string expected)
    {
        var ok = PosterIdValidator.TryNormalizeTmdbId(input, out var normalized);

        Assert.True(ok);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("027205")]
    [InlineData("-27205")]
    [InlineData("tt1375666")]
    [InlineData("tmdb:")]
    [InlineData("tmdb:0")]
    [InlineData("1234567890123")]
    [InlineData("27205/evil")]
    public void TryNormalizeTmdbId_InvalidIds_ReturnsFalse(string? input)
    {
        var ok = PosterIdValidator.TryNormalizeTmdbId(input, out var normalized);

        Assert.False(ok);
        Assert.Equal(string.Empty, normalized);
    }

    [Theory]
    [InlineData("https://btttr.cc/poster/movie/tt1375666/Oscar%20Nominee.png", "tt1375666")]
    [InlineData("https://btttr.cc/poster/series/tt0903747/Emmy%20Winner.png", "tt0903747")]
    [InlineData("https://btttr.cc/poster/movie/tt35538033/auto~gr.png?r=2", "tt35538033")]
    public void TryExtractImdbIdFromPosterUrl_ValidBtttrUrl_ExtractsNormalizedImdbId(string url, string expected)
    {
        var ok = PosterIdValidator.TryExtractImdbIdFromPosterUrl(url, out var imdbId);

        Assert.True(ok);
        Assert.Equal(expected, imdbId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://btttr.cc/poster/movie/tt1375666/Oscar.png")]
    [InlineData("https://evil.example.com/poster/movie/tt1375666/Oscar.png")]
    [InlineData("https://btttr.cc/poster/movie/tmdb:27205/Oscar.png")]
    [InlineData("https://btttr.cc/poster/movie/tt0000000/Oscar.png")]
    public void TryExtractImdbIdFromPosterUrl_InvalidOrUntrustedUrl_ReturnsFalse(string? url)
    {
        var ok = PosterIdValidator.TryExtractImdbIdFromPosterUrl(url, out var imdbId);

        Assert.False(ok);
        Assert.Equal(string.Empty, imdbId);
    }
}
