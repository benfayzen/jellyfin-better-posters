using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.BetterPosters.Configuration;
using Jellyfin.Plugin.BetterPosters.Domain;
using Jellyfin.Plugin.BetterPosters.Http;
using Jellyfin.Plugin.BetterPosters.ScheduledTasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.BetterPosters.Tests.ScheduledTasks;

public sealed class PosterRefreshServiceTests
{
    [Fact]
    public void GetDefaultTriggers_ReturnsEmptyCollection()
    {
        var service = CreateService(
            new FakeCandidateSource(),
            new FakeBtttrClient(),
            new InMemoryRefreshStateStore(),
            new PluginConfiguration());
        var task = new BetterPostersRefreshTask(service, NullLogger<BetterPostersRefreshTask>.Instance);

        Assert.Empty(task.GetDefaultTriggers());
    }

    [Fact]
    public async Task ExecuteRefreshAsync_WhenOptInDisabled_PerformsZeroQueriesAndZeroWrites()
    {
        var candidateSource = new FakeCandidateSource();
        candidateSource.Items.Add(CreateMovieCandidate("tt1375666", hasExistingPoster: true));
        var client = new FakeBtttrClient();
        var config = new PluginConfiguration
        {
            EnableScheduledRefreshOptIn = false,
            ScopedLibraryIds = [Guid.NewGuid().ToString("N")]
        };

        var service = CreateService(candidateSource, client, new InMemoryRefreshStateStore(), config);
        var summary = await service.ExecuteRefreshAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal("opt_in_disabled", summary.StatusReason);
        Assert.Equal(0, summary.InspectedCount);
        Assert.Equal(0, summary.UpdatedCount);
        Assert.Equal(0, candidateSource.SaveCalls);
        Assert.Equal(0, client.FetchCalls);
    }

    [Fact]
    public async Task ExecuteRefreshAsync_WhenNoScopedLibrariesConfigured_PerformsZeroQueriesAndZeroWrites()
    {
        var candidateSource = new FakeCandidateSource();
        candidateSource.Items.Add(CreateMovieCandidate("tt1375666", hasExistingPoster: true));
        var client = new FakeBtttrClient();
        var config = new PluginConfiguration
        {
            EnableScheduledRefreshOptIn = true,
            ScopedLibraryIds = []
        };

        var service = CreateService(candidateSource, client, new InMemoryRefreshStateStore(), config);
        var summary = await service.ExecuteRefreshAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal("no_scoped_libraries", summary.StatusReason);
        Assert.Equal(0, summary.InspectedCount);
        Assert.Equal(0, candidateSource.SaveCalls);
        Assert.Equal(0, client.FetchCalls);
    }

    [Fact]
    public async Task ExecuteRefreshAsync_DryRunEnabled_ValidatesUpstreamButPerformsZeroWrites()
    {
        var candidateSource = new FakeCandidateSource();
        var item = CreateMovieCandidate("tt1375666", hasExistingPoster: true, initialPosterMarker: "original-poster");
        candidateSource.Items.Add(item);

        var client = new FakeBtttrClient();
        var config = new PluginConfiguration
        {
            EnableScheduledRefreshOptIn = true,
            ScheduledRefreshDryRun = true,
            ScopedLibraryIds = [Guid.NewGuid().ToString("N")]
        };

        var service = CreateService(candidateSource, client, new InMemoryRefreshStateStore(), config);
        var summary = await service.ExecuteRefreshAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal("completed_dry_run", summary.StatusReason);
        Assert.Equal(1, summary.InspectedCount);
        Assert.Equal(0, summary.UpdatedCount);
        Assert.Equal(1, summary.DryRunWouldUpdateCount);
        Assert.Equal(0, candidateSource.SaveCalls);
        Assert.Equal(1, client.FetchCalls);
        Assert.Equal("original-poster", candidateSource.CurrentPosterMarkers[item.ItemId]);
    }

    [Fact]
    public async Task ExecuteRefreshAsync_WhenUpstreamFails_RetainsExistingPosterAndIsolatesItemErrors()
    {
        var candidateSource = new FakeCandidateSource();
        var failingItem = CreateMovieCandidate("tt9999999", hasExistingPoster: true, initialPosterMarker: "keep-existing-poster");
        var succeedingItem = CreateMovieCandidate("tt1375666", hasExistingPoster: true, initialPosterMarker: "old-poster-2");
        candidateSource.Items.Add(failingItem);
        candidateSource.Items.Add(succeedingItem);

        var client = new FakeBtttrClient
        {
            ResultSelector = uri => uri.AbsolutePath.Contains("tt9999999", StringComparison.Ordinal)
                ? BtttrImageResult.Failure("not_found")
                : BtttrImageResult.Success(CreateSyntheticWebp(64), "image/webp")
        };

        var config = new PluginConfiguration
        {
            EnableScheduledRefreshOptIn = true,
            ScheduledRefreshDryRun = false,
            ScopedLibraryIds = [Guid.NewGuid().ToString("N")]
        };

        var service = CreateService(candidateSource, client, new InMemoryRefreshStateStore(), config);
        var summary = await service.ExecuteRefreshAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal("completed", summary.StatusReason);
        Assert.Equal(2, summary.InspectedCount);
        Assert.Equal(1, summary.UpdatedCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.Equal(1, candidateSource.SaveCalls);

        // Failing item MUST retain its original poster untouched.
        Assert.Equal("keep-existing-poster", candidateSource.CurrentPosterMarkers[failingItem.ItemId]);
        // Succeeding item was updated.
        Assert.StartsWith("updated:image/webp:", candidateSource.CurrentPosterMarkers[succeedingItem.ItemId], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteRefreshAsync_RepeatedRunWithUnchangedConfig_SkipsNetworkAndWrites()
    {
        var candidateSource = new FakeCandidateSource();
        var item = CreateMovieCandidate("tt1375666", hasExistingPoster: true);
        candidateSource.Items.Add(item);

        var client = new FakeBtttrClient();
        var stateStore = new InMemoryRefreshStateStore();
        var config = new PluginConfiguration
        {
            EnableScheduledRefreshOptIn = true,
            ScheduledRefreshDryRun = false,
            ForceReplaceUnchanged = false,
            ScopedLibraryIds = [Guid.NewGuid().ToString("N")]
        };

        var service = CreateService(candidateSource, client, stateStore, config);

        var firstSummary = await service.ExecuteRefreshAsync(new Progress<double>(), CancellationToken.None);
        Assert.Equal(1, firstSummary.UpdatedCount);
        Assert.Equal(1, client.FetchCalls);
        Assert.Equal(1, candidateSource.SaveCalls);

        var secondSummary = await service.ExecuteRefreshAsync(new Progress<double>(), CancellationToken.None);
        Assert.Equal(0, secondSummary.UpdatedCount);
        Assert.Equal(1, secondSummary.SkippedCount);
        Assert.Equal(1, secondSummary.ReasonCounts["unchanged_fingerprint"]);
        Assert.Equal(1, client.FetchCalls);
        Assert.Equal(1, candidateSource.SaveCalls);
    }

    [Fact]
    public async Task ExecuteRefreshAsync_RespectsMaxItemsPerRunBoundAndAdvancesCursorAcrossRuns()
    {
        var candidateSource = new FakeCandidateSource();
        for (var i = 1; i <= 5; i++)
        {
            candidateSource.Items.Add(CreateMovieCandidate($"tt137566{i}", hasExistingPoster: true));
        }

        var client = new FakeBtttrClient();
        var stateStore = new InMemoryRefreshStateStore();
        var config = new PluginConfiguration
        {
            EnableScheduledRefreshOptIn = true,
            ScheduledRefreshDryRun = false,
            MaxItemsPerRun = 2,
            ScopedLibraryIds = [Guid.NewGuid().ToString("N")]
        };

        var service = CreateService(candidateSource, client, stateStore, config);

        // Run 1: inspects items [0, 1], updates both, advances cursor to 2
        var run1 = await service.ExecuteRefreshAsync(new Progress<double>(), CancellationToken.None);
        Assert.Equal(2, run1.InspectedCount);
        Assert.Equal(2, run1.UpdatedCount);
        Assert.Equal(3, run1.SkippedCount);
        Assert.Equal(3, run1.ReasonCounts["max_items_per_run_reached"]);
        Assert.Equal(2, candidateSource.SaveCalls);

        // Run 2: starts at cursor 2 -> inspects items [2, 3], updates both, advances cursor to 4
        var run2 = await service.ExecuteRefreshAsync(new Progress<double>(), CancellationToken.None);
        Assert.Equal(2, run2.InspectedCount);
        Assert.Equal(2, run2.UpdatedCount);
        Assert.Equal(3, run2.SkippedCount);
        Assert.Equal(4, candidateSource.SaveCalls);

        // Run 3: starts at cursor 4 -> inspects item [4] and wraps around to item [0] (which is skipped as unchanged_fingerprint)
        var run3 = await service.ExecuteRefreshAsync(new Progress<double>(), CancellationToken.None);
        Assert.Equal(2, run3.InspectedCount);
        Assert.Equal(1, run3.UpdatedCount);
        Assert.Equal(4, run3.SkippedCount);
        Assert.Equal(1, run3.ReasonCounts["unchanged_fingerprint"]);
        Assert.Equal(5, candidateSource.SaveCalls);
    }

    [Fact]
    public async Task ExecuteRefreshAsync_CancellationMidRun_StopsAndPreservesUntouchedPosters()
    {
        using var cts = new CancellationTokenSource();
        var candidateSource = new FakeCandidateSource();
        var item1 = CreateMovieCandidate("tt1375666", hasExistingPoster: true, initialPosterMarker: "poster-1");
        var item2 = CreateMovieCandidate("tt0903747", hasExistingPoster: true, initialPosterMarker: "poster-2");
        candidateSource.Items.Add(item1);
        candidateSource.Items.Add(item2);

        var client = new FakeBtttrClient
        {
            OnFetch = () => cts.Cancel()
        };

        var config = new PluginConfiguration
        {
            EnableScheduledRefreshOptIn = true,
            ScheduledRefreshDryRun = false,
            MaxConcurrency = 1,
            ScopedLibraryIds = [Guid.NewGuid().ToString("N")]
        };

        var service = CreateService(candidateSource, client, new InMemoryRefreshStateStore(), config);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExecuteRefreshAsync(new Progress<double>(), cts.Token));

        Assert.Equal("poster-1", candidateSource.CurrentPosterMarkers[item1.ItemId]);
        Assert.Equal("poster-2", candidateSource.CurrentPosterMarkers[item2.ItemId]);
        Assert.Equal(0, candidateSource.SaveCalls);
    }

    [Fact]
    public async Task ExecuteRefreshAsync_WhenCancelledWhileWaitingOnSemaphore_DrainsInFlightWorkersBeforeDisposingSemaphore()
    {
        using var cts = new CancellationTokenSource();
        var candidateSource = new FakeCandidateSource();
        var item1 = CreateMovieCandidate("tt1375666", hasExistingPoster: true, initialPosterMarker: "poster-1");
        var item2 = CreateMovieCandidate("tt0903747", hasExistingPoster: true, initialPosterMarker: "poster-2");
        candidateSource.Items.Add(item1);
        candidateSource.Items.Add(item2);

        var workerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowWorkerToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workerCompletedCleanup = false;

        var client = new FakeBtttrClient
        {
            AsyncResultSelector = async (_, _) =>
            {
                workerEntered.TrySetResult();
                await allowWorkerToFinish.Task.ConfigureAwait(false);
                workerCompletedCleanup = true;
                return BtttrImageResult.Failure("not_found");
            }
        };

        var config = new PluginConfiguration
        {
            EnableScheduledRefreshOptIn = true,
            ScheduledRefreshDryRun = false,
            MaxConcurrency = 1,
            ScopedLibraryIds = [Guid.NewGuid().ToString("N")]
        };

        var service = CreateService(candidateSource, client, new InMemoryRefreshStateStore(), config);

        var refreshTask = service.ExecuteRefreshAsync(new Progress<double>(), cts.Token);
        await workerEntered.Task;

        // Cancel while the second candidate is waiting on semaphore.WaitAsync(cancellationToken)
        cts.Cancel();

        // Allow the first in-flight worker to finish after cancellation was triggered
        allowWorkerToFinish.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refreshTask);
        Assert.True(workerCompletedCleanup);
    }

    [Fact]
    public void JellyfinPosterRefreshGateway_GetScopedCandidates_QueriesBoundedPagesAndWrapsAround()
    {
        var allMovies = new List<MediaBrowser.Controller.Entities.BaseItem>();
        for (var i = 1; i <= 5; i++)
        {
            var movie = new MediaBrowser.Controller.Entities.Movies.Movie
            {
                Id = Guid.NewGuid(),
                Name = $"Movie {i}",
                ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Imdb"] = $"tt137566{i}"
                }
            };
            allMovies.Add(movie);
        }

        var recordedQueries = new List<(int? StartIndex, int? Limit)>();
        var libraryOptionsCalls = 0;

        var libraryManager = DispatchProxyStub<MediaBrowser.Controller.Library.ILibraryManager>.Create((method, args) =>
        {
            if (method.Name == nameof(MediaBrowser.Controller.Library.ILibraryManager.GetItemsResult) && args?[0] is MediaBrowser.Controller.Entities.InternalItemsQuery q)
            {
                recordedQueries.Add((q.StartIndex, q.Limit));
                var start = q.StartIndex ?? 0;
                var limit = q.Limit ?? allMovies.Count;
                var page = new List<MediaBrowser.Controller.Entities.BaseItem>();
                for (var idx = start; idx < allMovies.Count && page.Count < limit; idx++)
                {
                    page.Add(allMovies[idx]);
                }

                return new MediaBrowser.Model.Querying.QueryResult<MediaBrowser.Controller.Entities.BaseItem>(start, allMovies.Count, page);
            }

            if (method.Name == nameof(MediaBrowser.Controller.Library.ILibraryManager.GetLibraryOptions))
            {
                libraryOptionsCalls++;
                return new MediaBrowser.Model.Configuration.LibraryOptions();
            }

            return null;
        });

        var baseItemManager = DispatchProxyStub<MediaBrowser.Controller.BaseItemManager.IBaseItemManager>.Create((method, _) =>
        {
            if (method.Name == nameof(MediaBrowser.Controller.BaseItemManager.IBaseItemManager.IsImageFetcherEnabled))
            {
                return true;
            }

            return null;
        });

        var providerManager = DispatchProxyStub<MediaBrowser.Controller.Providers.IProviderManager>.Create((_, _) => null);

        var gateway = new JellyfinPosterRefreshGateway(libraryManager, baseItemManager, providerManager);
        var scope = new[] { Guid.NewGuid() };

        // Query 2 items starting at index 4 in a 5-item library:
        // Should issue bounded query (StartIndex=4, Limit=2) -> 1 item, then wrap around (StartIndex=0, Limit=1) -> 1 item
        var slice = gateway.GetScopedCandidates(scope, startIndex: 4, maxItems: 2, CancellationToken.None);

        Assert.Equal(2, slice.Candidates.Count);
        Assert.Equal(allMovies[4].Id, slice.Candidates[0].ItemId);
        Assert.Equal(allMovies[0].Id, slice.Candidates[1].ItemId);
        Assert.Equal(1, slice.NextStartIndex);
        Assert.Equal(3, slice.SkippedOverflowCount);
        Assert.Equal([(4, 2), (0, 1)], recordedQueries);
        Assert.Equal(2, libraryOptionsCalls);
    }

    private static PosterRefreshService CreateService(
        IPosterRefreshGateway gateway,
        IBtttrClient client,
        IPosterRefreshStateStore stateStore,
        PluginConfiguration config)
    {
        return new PosterRefreshService(
            gateway,
            client,
            stateStore,
            () => config,
            NullLogger<PosterRefreshService>.Instance);
    }

    private static PosterRefreshCandidate CreateMovieCandidate(
        string imdbId,
        bool hasExistingPoster,
        string initialPosterMarker = "initial")
    {
        return new PosterRefreshCandidate(
            ItemId: Guid.NewGuid(),
            Name: "Movie " + imdbId,
            MediaType: SupportedMediaType.Movie,
            ImdbId: imdbId,
            TmdbId: null,
            IsImageFetcherEnabled: true,
            HasExistingPrimaryImage: hasExistingPoster,
            InitialPosterMarker: initialPosterMarker);
    }

    private static byte[] CreateSyntheticWebp(int length)
    {
        var bytes = new byte[Math.Max(length, 32)];
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

    private sealed class FakeCandidateSource : IPosterRefreshGateway
    {
        private int _saveCalls;

        public List<PosterRefreshCandidate> Items { get; } = [];

        public ConcurrentDictionary<Guid, string> CurrentPosterMarkers { get; } = new();

        public int SaveCalls => _saveCalls;

        public PosterRefreshCandidateSlice GetScopedCandidates(
            IReadOnlyCollection<Guid> scopedLibraryIds,
            int startIndex,
            int maxItems,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var item in Items)
            {
                CurrentPosterMarkers.TryAdd(item.ItemId, item.InitialPosterMarker);
            }

            if (Items.Count == 0)
            {
                return new PosterRefreshCandidateSlice([], 0, 0);
            }

            var effectiveStart = startIndex >= Items.Count ? 0 : startIndex;
            var selected = new List<PosterRefreshCandidate>(Math.Min(maxItems, Items.Count));
            for (var i = effectiveStart; i < Items.Count && selected.Count < maxItems; i++)
            {
                selected.Add(Items[i]);
            }

            int nextStart;
            if (effectiveStart + selected.Count < Items.Count)
            {
                nextStart = effectiveStart + selected.Count;
            }
            else if (effectiveStart > 0 && selected.Count < maxItems)
            {
                var remaining = Math.Min(effectiveStart, maxItems - selected.Count);
                for (var i = 0; i < remaining; i++)
                {
                    selected.Add(Items[i]);
                }

                nextStart = remaining < Items.Count ? remaining : 0;
            }
            else
            {
                nextStart = 0;
            }

            var overflow = Math.Max(0, Items.Count - selected.Count);
            return new PosterRefreshCandidateSlice(selected, nextStart, overflow);
        }

        public Task SaveValidatedPrimaryPosterAsync(
            Guid itemId,
            Stream validatedImageStream,
            string mimeType,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _saveCalls);
            CurrentPosterMarkers[itemId] = $"updated:{mimeType}:{validatedImageStream.Length}";
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBtttrClient : IBtttrClient
    {
        private int _fetchCalls;

        public int FetchCalls => _fetchCalls;

        public Func<Uri, BtttrImageResult>? ResultSelector { get; set; }

        public Func<Uri, CancellationToken, Task<BtttrImageResult>>? AsyncResultSelector { get; set; }

        public Action? OnFetch { get; set; }

        public async Task<BtttrImageResult> FetchValidatedPosterAsync(Uri posterUri, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _fetchCalls);
            OnFetch?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            if (AsyncResultSelector is not null)
            {
                return await AsyncResultSelector(posterUri, cancellationToken).ConfigureAwait(false);
            }

            if (ResultSelector is not null)
            {
                return ResultSelector(posterUri);
            }

            return BtttrImageResult.Success(CreateSyntheticWebp(64), "image/webp");
        }

        public Task<string?> ResolveImdbIdFromTmdbAsync(
            SupportedMediaType mediaType,
            string tmdbId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<HttpResponseMessage> GetImageResponseForJellyfinAsync(string url, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class InMemoryRefreshStateStore : IPosterRefreshStateStore
    {
        private readonly ConcurrentDictionary<Guid, string> _fingerprints = new();
        private readonly ConcurrentDictionary<string, int> _cursors = new(StringComparer.Ordinal);

        public bool TryGetRecordedFingerprint(Guid itemId, out string fingerprint)
        {
            return _fingerprints.TryGetValue(itemId, out fingerprint!);
        }

        public void RecordFingerprint(Guid itemId, string fingerprint)
        {
            _fingerprints[itemId] = fingerprint;
        }

        public bool TryGetScopeCursor(string scopeKey, out int startIndex)
        {
            return _cursors.TryGetValue(scopeKey, out startIndex);
        }

        public void RecordScopeCursor(string scopeKey, int nextStartIndex)
        {
            _cursors[scopeKey] = nextStartIndex;
        }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public class DispatchProxyStub<TInterface> : System.Reflection.DispatchProxy
        where TInterface : class
    {
        private Func<System.Reflection.MethodInfo, object?[]?, object?>? _handler;

        public static TInterface Create(Func<System.Reflection.MethodInfo, object?[]?, object?> handler)
        {
            var proxy = Create<TInterface, DispatchProxyStub<TInterface>>();
            ((DispatchProxyStub<TInterface>)(object)proxy)._handler = handler;
            return proxy;
        }

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod is not null && _handler is not null
                ? _handler(targetMethod, args)
                : null;
        }
    }
}
