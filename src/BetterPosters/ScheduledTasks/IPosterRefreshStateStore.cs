using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.BetterPosters.ScheduledTasks;

/// <summary>
/// Tracks per-item Better Posters URL/configuration fingerprints and per-scope traversal cursors across scheduled refresh runs.
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
    /// Attempts to get the next 0-based traversal start index for the specified library <paramref name="scopeKey"/>.
    /// </summary>
    bool TryGetScopeCursor(string scopeKey, out int startIndex);

    /// <summary>
    /// Records the next 0-based traversal start index for the specified library <paramref name="scopeKey"/>.
    /// </summary>
    void RecordScopeCursor(string scopeKey, int nextStartIndex);

    /// <summary>
    /// Flushes any pending fingerprint and cursor updates to durable storage.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken);
}
