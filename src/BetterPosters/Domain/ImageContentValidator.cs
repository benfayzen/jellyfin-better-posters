using System;

namespace Jellyfin.Plugin.BetterPosters.Domain;

/// <summary>
/// Validates downloaded image payloads by Content-Type header, byte length bounds, and magic-byte signatures.
/// </summary>
public static class ImageContentValidator
{
    /// <summary>
    /// Minimum byte size for a valid poster payload.
    /// </summary>
    public const int MinImageBytes = 32;

    /// <summary>
    /// Default maximum byte size for a valid poster payload (10 MiB).
    /// </summary>
    public const int DefaultMaxImageBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Validates that <paramref name="payload"/> and <paramref name="contentTypeHeader"/> represent a bounded, valid JPEG, PNG, or WebP image.
    /// </summary>
    public static bool TryValidateImage(
        ReadOnlySpan<byte> payload,
        string? contentTypeHeader,
        out string detectedMimeType,
        out string reasonCode,
        int maxBytes = DefaultMaxImageBytes)
    {
        detectedMimeType = string.Empty;

        if (payload.Length < MinImageBytes)
        {
            reasonCode = "payload_too_small";
            return false;
        }

        if (payload.Length > maxBytes)
        {
            reasonCode = "payload_too_large";
            return false;
        }

        if (!IsAllowedContentTypeHeader(contentTypeHeader))
        {
            reasonCode = "unsupported_content_type";
            return false;
        }

        // WebP: "RIFF" (0..3) + 4 byte size + "WEBP" (8..11)
        if (payload[0] == (byte)'R'
            && payload[1] == (byte)'I'
            && payload[2] == (byte)'F'
            && payload[3] == (byte)'F'
            && payload[8] == (byte)'W'
            && payload[9] == (byte)'E'
            && payload[10] == (byte)'B'
            && payload[11] == (byte)'P')
        {
            detectedMimeType = "image/webp";
            reasonCode = "ok";
            return true;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (payload[0] == 0x89
            && payload[1] == 0x50
            && payload[2] == 0x4E
            && payload[3] == 0x47
            && payload[4] == 0x0D
            && payload[5] == 0x0A
            && payload[6] == 0x1A
            && payload[7] == 0x0A)
        {
            detectedMimeType = "image/png";
            reasonCode = "ok";
            return true;
        }

        // JPEG: FF D8 FF
        if (payload[0] == 0xFF
            && payload[1] == 0xD8
            && payload[2] == 0xFF)
        {
            detectedMimeType = "image/jpeg";
            reasonCode = "ok";
            return true;
        }

        reasonCode = "invalid_magic_bytes";
        return false;
    }

    private static bool IsAllowedContentTypeHeader(string? contentTypeHeader)
    {
        if (string.IsNullOrWhiteSpace(contentTypeHeader))
        {
            return true;
        }

        var mediaType = contentTypeHeader.AsSpan();
        var semicolonIndex = mediaType.IndexOf(';');
        if (semicolonIndex >= 0)
        {
            mediaType = mediaType[..semicolonIndex];
        }

        mediaType = mediaType.Trim();
        return mediaType.Equals("image/webp", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("image/jpg", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase);
    }
}
