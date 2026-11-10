using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.BetterPosters.Configuration;
using Jellyfin.Plugin.BetterPosters.Domain;
using Jellyfin.Plugin.BetterPosters.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BetterPosters.ScheduledTasks;

/// <summary>
/// Implements opt-in, library-scoped, dry-run-capable, bounded poster replacement with item-level error isolation.
/// </summary>
public sealed class PosterRefreshService : IPosterRefreshService
{
    private readonly IPosterRefreshGateway _gateway;
    private readonly IBtttrClient _btttrClient;
    private readonly IPosterRefreshStateStore _stateStore;
    private readonly Func<PluginConfiguration> _configurationProvider;
    private readonly ILogger<PosterRefreshService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PosterRefreshService"/> class.
    /// </summary>
    public PosterRefreshService(
        IPosterRefreshGateway gateway,
        IBtttrClient btttrClient,
        IPosterRefreshStateStore stateStore,
        Func<PluginConfiguration> configurationProvider,
        ILogger<PosterRefreshService> logger)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(btttrClient);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(configurationProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _gateway = gateway;
        _btttrClient = btttrClient;
        _stateStore = stateStore;
        _configurationProvider = configurationProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PosterRefreshSummary> ExecuteRefreshAsync(
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var config = _configurationProvider();
        var isDryRun = config.ScheduledRefreshDryRun;
        var reasonCounts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        if (!config.EnableScheduledRefreshOptIn)
        {
            IncrementReason(reasonCounts, "opt_in_disabled");
            _logger.LogInformation(
                "Skipping Better Posters scheduled refresh because explicit opt-in is disabled (reason={ReasonCode}).",
                "opt_in_disabled");
            progress?.Report(100);
            return new PosterRefreshSummary("opt_in_disabled", isDryRun, 0, 0, 0, 0, 0, reasonCounts);
        }

        var scopedLibraryGuids = ParseScopedLibraryGuids(config.ScopedLibraryIds);
        if (scopedLibraryGuids.Count == 0)
        {
            IncrementReason(reasonCounts, "no_scoped_libraries");
            _logger.LogInformation(
                "Skipping Better Posters scheduled refresh because no scoped libraries are configured (reason={ReasonCode}).",
                "no_scoped_libraries");
            progress?.Report(100);
            return new PosterRefreshSummary("no_scoped_libraries", isDryRun, 0, 0, 0, 0, 0, reasonCounts);
        }

        var options = config.ToPosterOptions();
        var maxItems = Math.Clamp(config.MaxItemsPerRun, 1, PluginConfiguration.MaxAllowedItemsPerRun);
        var maxConcurrency = Math.Clamp(config.MaxConcurrency, 1, PluginConfiguration.MaxAllowedConcurrency);
        var forceReplace = config.ForceReplaceUnchanged;

        var allCandidates = _gateway.GetScopedCandidates(scopedLibraryGuids);
        if (allCandidates.Count == 0)
        {
            IncrementReason(reasonCounts, "no_matching_items");
            _logger.LogInformation(
                "Better Posters scheduled refresh found 0 matching Movie/Series items in {LibraryCount} scoped libraries (reason={ReasonCode}).",
                scopedLibraryGuids.Count,
                "no_matching_items");
            progress?.Report(100);
            return new PosterRefreshSummary(isDryRun ? "completed_dry_run" : "completed", isDryRun, 0, 0, 0, 0, 0, reasonCounts);
        }

        var boundedCandidates = allCandidates.Take(maxItems).ToList();
        var skippedOverflowCount = Math.Max(0, allCandidates.Count - boundedCandidates.Count);
        if (skippedOverflowCount > 0)
        {
            reasonCounts.AddOrUpdate("max_items_per_run_reached", skippedOverflowCount, (_, current) => current + skippedOverflowCount);
        }

        var updatedCount = 0;
        var dryRunWouldUpdateCount = 0;
        var skippedCount = skippedOverflowCount;
        var failedCount = 0;
        var completedCount = 0;
        var totalToProcess = boundedCandidates.Count;

        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = new List<Task>(totalToProcess);

        foreach (var candidate in boundedCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            tasks.Add(Task.Run(
                async () =>
                {
                    try
                    {
                        var (outcomeCategory, reasonCode) = await ProcessSingleCandidateAsync(
                            candidate,
                            options,
                            isDryRun,
                            forceReplace,
                            cancellationToken).ConfigureAwait(false);

                        IncrementReason(reasonCounts, reasonCode);

                        switch (outcomeCategory)
                        {
                            case OutcomeCategory.Updated:
                                Interlocked.Increment(ref updatedCount);
                                break;
                            case OutcomeCategory.DryRunWouldUpdate:
                                Interlocked.Increment(ref dryRunWouldUpdateCount);
                                break;
                            case OutcomeCategory.Skipped:
                                Interlocked.Increment(ref skippedCount);
                                break;
                            case OutcomeCategory.Failed:
                                Interlocked.Increment(ref failedCount);
                                break;
                        }
                    }
                    finally
                    {
                        var done = Interlocked.Increment(ref completedCount);
                        progress?.Report((double)done / totalToProcess * 100.0);
                        semaphore.Release();
                    }
                },
                cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        await _stateStore.FlushAsync(cancellationToken).ConfigureAwait(false);

        var statusReason = isDryRun ? "completed_dry_run" : "completed";
        _logger.LogInformation(
            "Better Posters scheduled refresh finished (status={StatusReason}, dryRun={IsDryRun}, inspected={InspectedCount}, updated={UpdatedCount}, dryRunWouldUpdate={DryRunWouldUpdateCount}, skipped={SkippedCount}, failed={FailedCount}).",
            statusReason,
            isDryRun,
            totalToProcess,
            updatedCount,
            dryRunWouldUpdateCount,
            skippedCount,
            failedCount);

        return new PosterRefreshSummary(
            StatusReason: statusReason,
            IsDryRun: isDryRun,
            InspectedCount: totalToProcess,
            UpdatedCount: updatedCount,
            DryRunWouldUpdateCount: dryRunWouldUpdateCount,
            SkippedCount: skippedCount,
            FailedCount: failedCount,
            ReasonCounts: reasonCounts);
    }

    private static List<Guid> ParseScopedLibraryGuids(string[]? rawIds)
    {
        if (rawIds is null || rawIds.Length == 0)
        {
            return [];
        }

        var result = new List<Guid>(rawIds.Length);
        foreach (var raw in rawIds)
        {
            if (Guid.TryParse(raw, out var guid) && guid != Guid.Empty && !result.Contains(guid))
            {
                result.Add(guid);
            }
        }

        return result;
    }

    private static void IncrementReason(ConcurrentDictionary<string, int> counts, string reason)
    {
        counts.AddOrUpdate(reason, 1, (_, current) => current + 1);
    }

    private async Task<(OutcomeCategory Category, string ReasonCode)> ProcessSingleCandidateAsync(
        PosterRefreshCandidate candidate,
        PosterOptions options,
        bool isDryRun,
        bool forceReplace,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!candidate.IsImageFetcherEnabled)
        {
            return (OutcomeCategory.Skipped, "fetcher_disabled");
        }

        try
        {
            var imdbId = await ResolveCandidateImdbIdAsync(candidate, options, cancellationToken).ConfigureAwait(false);
            if (imdbId is null)
            {
                return (OutcomeCategory.Skipped, "missing_id");
            }

            var fingerprint = PosterUrlBuilder.BuildCacheKey(candidate.MediaType, imdbId, options);
            if (!forceReplace
                && candidate.HasExistingPrimaryImage
                && _stateStore.TryGetRecordedFingerprint(candidate.ItemId, out var recordedFingerprint)
                && string.Equals(recordedFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return (OutcomeCategory.Skipped, "unchanged_fingerprint");
            }

            var posterUri = PosterUrlBuilder.BuildPosterUri(imdbId, options);

            // Download and validate the new poster BEFORE any write operation so existing posters are never lost on failure.
            var downloadResult = await _btttrClient.FetchValidatedPosterAsync(posterUri, cancellationToken).ConfigureAwait(false);
            if (!downloadResult.IsSuccess)
            {
                _logger.LogWarning(
                    "Preserving existing poster for item {ItemId} ({ItemName}): upstream fetch/validation failed (reason={ReasonCode}).",
                    candidate.ItemId,
                    candidate.Name,
                    downloadResult.ReasonCode);
                return (OutcomeCategory.Failed, downloadResult.ReasonCode);
            }

            if (isDryRun)
            {
                return (OutcomeCategory.DryRunWouldUpdate, "dry_run_would_update");
            }

            using var stream = new MemoryStream(downloadResult.ImageBytes, 0, downloadResult.ImageBytes.Length, writable: false);
            await _gateway.SaveValidatedPrimaryPosterAsync(
                candidate.ItemId,
                stream,
                downloadResult.MimeType,
                cancellationToken).ConfigureAwait(false);

            _stateStore.RecordFingerprint(candidate.ItemId, fingerprint);
            return (OutcomeCategory.Updated, "updated");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Preserving existing poster for item {ItemId} ({ItemName}) after unexpected item error (reason={ReasonCode}).",
                candidate.ItemId,
                candidate.Name,
                "item_exception");
            return (OutcomeCategory.Failed, "item_exception");
        }
    }

    private async Task<string?> ResolveCandidateImdbIdAsync(
        PosterRefreshCandidate candidate,
        PosterOptions options,
        CancellationToken cancellationToken)
    {
        if (PosterIdValidator.TryNormalizeImdbId(candidate.ImdbId, out var normalizedImdb))
        {
            return normalizedImdb;
        }

        if (!options.EnableTmdbFallback)
        {
            return null;
        }

        if (!PosterIdValidator.TryNormalizeTmdbId(candidate.TmdbId, out var normalizedTmdb))
        {
            return null;
        }

        return await _btttrClient.ResolveImdbIdFromTmdbAsync(candidate.MediaType, normalizedTmdb, cancellationToken).ConfigureAwait(false);
    }

    private enum OutcomeCategory
    {
        Updated,
        DryRunWouldUpdate,
        Skipped,
        Failed
    }
}
