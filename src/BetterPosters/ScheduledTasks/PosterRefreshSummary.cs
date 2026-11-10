using System.Collections.Generic;

namespace Jellyfin.Plugin.BetterPosters.ScheduledTasks;

/// <summary>
/// Structured outcome summary of a scheduled poster refresh execution.
/// </summary>
public sealed record PosterRefreshSummary(
    string StatusReason,
    bool IsDryRun,
    int InspectedCount,
    int UpdatedCount,
    int DryRunWouldUpdateCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyDictionary<string, int> ReasonCounts);
