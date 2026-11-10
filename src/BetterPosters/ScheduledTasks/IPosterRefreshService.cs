using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.BetterPosters.ScheduledTasks;

/// <summary>
/// Service that executes opt-in, scoped, bounded library poster refreshes.
/// </summary>
public interface IPosterRefreshService
{
    /// <summary>
    /// Executes a scoped poster refresh run according to the current plugin configuration.
    /// </summary>
    Task<PosterRefreshSummary> ExecuteRefreshAsync(
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}
