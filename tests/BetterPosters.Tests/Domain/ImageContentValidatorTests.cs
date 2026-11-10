using System;
using System.Text;
using Jellyfin.Plugin.BetterPosters.Domain;
using Xunit;

namespace Jellyfin.Plugin.BetterPosters.Tests.Domain;

public sealed class ImageContentValidatorTests
{
    [Fact]
    public void TryValidateImage_ValidWebpBytes_ReturnsTrueAndImageWebpMime()
    {
        var webp = CreateSyntheticWebp(64);

        var ok = ImageContentValidator.TryValidateImage(webp, "image/webp", out var mimeType, out var reason);

        Assert.True(ok);
        Assert.Equal("image/webp", mimeType);
        Assert.Equal("ok", reason);
    }

    [Fact]
    public void TryValidateImage_ValidPngBytes_ReturnsTrueAndImagePngMime()
    {
        var png = CreateSyntheticPng(64);

        var ok = ImageContentValidator.TryValidateImage(png, "image/png", out var mimeType, out var reason);

        Assert.True(ok);
        Assert.Equal("image/png", mimeType);
        Assert.Equal("ok", reason);
    }

    [Fact]
    public void TryValidateImage_ValidJpegBytes_ReturnsTrueAndImageJpegMime()
    {
        var jpeg = CreateSyntheticJpeg(64);

        var ok = ImageContentValidator.TryValidateImage(jpeg, "image/jpeg", out var mimeType, out var reason);

        Assert.True(ok);
        Assert.Equal("image/jpeg", mimeType);
        Assert.Equal("ok", reason);
    }

    [Fact]
    public void TryValidateImage_HtmlErrorPageMasqueradingAsImage_ReturnsFalse()
    {
        var htmlBytes = Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>Not Found - Error page padding to exceed 32 bytes</body></html>");

        var ok = ImageContentValidator.TryValidateImage(htmlBytes, "image/jpeg", out var mimeType, out var reason);

        Assert.False(ok);
        Assert.Equal(string.Empty, mimeType);
        Assert.Equal("invalid_magic_bytes", reason);
    }

    [Fact]
    public void TryValidateImage_TextContentTypeHeader_ReturnsFalse()
    {
        var webp = CreateSyntheticWebp(64);

        var ok = ImageContentValidator.TryValidateImage(webp, "text/html", out var mimeType, out var reason);

        Assert.False(ok);
        Assert.Equal(string.Empty, mimeType);
        Assert.Equal("unsupported_content_type", reason);
    }

    [Fact]
    public void TryValidateImage_TooSmallPayload_ReturnsFalse()
    {
        var tiny = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };

        var ok = ImageContentValidator.TryValidateImage(tiny, "image/jpeg", out _, out var reason);

        Assert.False(ok);
        Assert.Equal("payload_too_small", reason);
    }

    [Fact]
    public void TryValidateImage_ExceedsMaxBytes_ReturnsFalse()
    {
        var webp = CreateSyntheticWebp(128);

        var ok = ImageContentValidator.TryValidateImage(webp, "image/webp", out _, out var reason, maxBytes: 64);

        Assert.False(ok);
        Assert.Equal("payload_too_large", reason);
    }

    private static byte[] CreateSyntheticWebp(int length)
    {
        var bytes = new byte[Math.Max(length, 32)];
        // RIFF....WEBP
        bytes[0] = (byte)'R';
        bytes[1] = (byte)'I';
        bytes[2] = (byte)'F';
        bytes[3] = (byte)'F';
        bytes[8] = (byte)'W';
        bytes[9] = (byte)'E';
        bytes[10] = (byte)'B';
        bytes[11] = (byte)'P';
        return bytes;
    }

    private static byte[] CreateSyntheticPng(int length)
    {
        var bytes = new byte[Math.Max(length, 32)];
        ReadOnlySpan<byte> sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        sig.CopyTo(bytes);
        return bytes;
    }

    private static byte[] CreateSyntheticJpeg(int length)
    {
        var bytes = new byte[Math.Max(length, 32)];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        bytes[3] = 0xE0;
        return bytes;
    }
}
