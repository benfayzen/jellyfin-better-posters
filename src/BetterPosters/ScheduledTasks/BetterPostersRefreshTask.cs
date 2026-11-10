using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BetterPosters.ScheduledTasks;

/// <summary>
/// Unscheduled-by-default Jellyfin 12 task that updates primary posters in explicitly scoped libraries when opt-in is enabled.
/// </summary>
public sealed class BetterPostersRefreshTask : IScheduledTask
{
    private readonly IPosterRefreshService _refreshService;
    private readonly ILogger<BetterPostersRefreshTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BetterPostersRefreshTask"/> class.
    /// </summary>
    public BetterPostersRefreshTask(
        IPosterRefreshService refreshService,
        ILogger<BetterPostersRefreshTask> logger)
    {
        ArgumentNullException.ThrowIfNull(refreshService);
        ArgumentNullException.ThrowIfNull(logger);

        _refreshService = refreshService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Update Better Posters";

    /// <inheritdoc />
    public string Key => "JellyfinPluginBetterPostersRefresh";

    /// <inheritdoc />
    public string Description => "Opt-in task that updates Movie and Series primary posters in explicitly scoped libraries using validated btttr.cc images (supports dry-run).";

    /// <inheritdoc />
    public string Category => Plugin.PluginName;

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Explicitly unscheduled by default per safety requirements.
        return [];
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Better Posters scheduled refresh task.");
        _ = await _refreshService.ExecuteRefreshAsync(progress, cancellationToken).ConfigureAwait(false);
    }
}
