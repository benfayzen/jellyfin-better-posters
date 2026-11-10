using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.BetterPosters.ScheduledTasks;

/// <summary>
/// Tracks per-item Better Posters URL/configuration fingerprints so repeated scheduled runs do not re-download or rewrite unchanged posters.
/// </summary>
public interface IPosterRefreshStateStore
{
    /// <summary>
    /// Attempts to get the last recorded poster fingerprint for <paramref name="itemId"/>.
    /// </summary>
    bool TryGetRecordedFingerprint(Guid itemId, out string fingerprint);

    /// <summary>
    /// Records the poster fingerprint for <paramref name="itemId"/> after a successful poster write.
    /// </summary>
    void RecordFingerprint(Guid itemId, string fingerprint);

    /// <summary>
    /// Flushes any pending fingerprint updates to durable storage.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken);
}
