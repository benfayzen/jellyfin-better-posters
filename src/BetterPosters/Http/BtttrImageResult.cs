using System;

namespace Jellyfin.Plugin.BetterPosters.Http;

/// <summary>
/// Represents the outcome of downloading and validating a poster image from btttr.cc.
/// </summary>
public sealed record BtttrImageResult(
    bool IsSuccess,
    byte[] ImageBytes,
    string MimeType,
    string ReasonCode)
{
    /// <summary>
    /// Creates a successful image result.
    /// </summary>
    public static BtttrImageResult Success(byte[] imageBytes, string mimeType)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        return new BtttrImageResult(true, imageBytes, mimeType, "ok");
    }

    /// <summary>
    /// Creates a failed image result with a structured reason code.
    /// </summary>
    public static BtttrImageResult Failure(string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        return new BtttrImageResult(false, [], string.Empty, reasonCode);
    }
}
