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
    public async Task ExecuteRefreshAsync_RespectsMaxItemsPerRunBound()
    {
        var candidateSource = new FakeCandidateSource();
        for (var i = 1; i <= 5; i++)
        {
            candidateSource.Items.Add(CreateMovieCandidate($"tt137566{i}", hasExistingPoster: false));
        }

        var client = new FakeBtttrClient();
        var config = new PluginConfiguration
        {
            EnableScheduledRefreshOptIn = true,
            ScheduledRefreshDryRun = false,
            MaxItemsPerRun = 2,
            ScopedLibraryIds = [Guid.NewGuid().ToString("N")]
        };

        var service = CreateService(candidateSource, client, new InMemoryRefreshStateStore(), config);
        var summary = await service.ExecuteRefreshAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(2, summary.InspectedCount);
        Assert.Equal(2, summary.UpdatedCount);
        Assert.Equal(3, summary.SkippedCount);
        Assert.Equal(3, summary.ReasonCounts["max_items_per_run_reached"]);
        Assert.Equal(2, candidateSource.SaveCalls);
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

        public IReadOnlyList<PosterRefreshCandidate> GetScopedCandidates(IReadOnlyCollection<Guid> scopedLibraryIds)
        {
            foreach (var item in Items)
            {
                CurrentPosterMarkers.TryAdd(item.ItemId, item.InitialPosterMarker);
            }

            return Items;
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

        public Action? OnFetch { get; set; }

        public Task<BtttrImageResult> FetchValidatedPosterAsync(Uri posterUri, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _fetchCalls);
            OnFetch?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            if (ResultSelector is not null)
            {
                return Task.FromResult(ResultSelector(posterUri));
            }

            return Task.FromResult(BtttrImageResult.Success(CreateSyntheticWebp(64), "image/webp"));
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

        public bool TryGetRecordedFingerprint(Guid itemId, out string fingerprint)
        {
            return _fingerprints.TryGetValue(itemId, out fingerprint!);
        }

        public void RecordFingerprint(Guid itemId, string fingerprint)
        {
            _fingerprints[itemId] = fingerprint;
        }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
