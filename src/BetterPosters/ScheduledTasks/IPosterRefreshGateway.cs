using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.BetterPosters.ScheduledTasks;

/// <summary>
/// Abstraction over Jellyfin library querying and stream-based primary poster persistence.
/// </summary>
public interface IPosterRefreshGateway
{
    /// <summary>
    /// Queries a bounded slice of Movie and Series items belonging to the explicitly scoped library folder IDs.
    /// </summary>
    PosterRefreshCandidateSlice GetScopedCandidates(
        IReadOnlyCollection<Guid> scopedLibraryIds,
        int startIndex,
        int maxItems,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists an already-downloaded and validated primary poster stream for the specified item via Jellyfin's verified <c>IProviderManager.SaveImage</c> API.
    /// </summary>
    Task SaveValidatedPrimaryPosterAsync(
        Guid itemId,
        Stream validatedImageStream,
        string mimeType,
        CancellationToken cancellationToken);
}
