using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.BetterPosters.Providers;
using MediaBrowser.Controller.BaseItemManager;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.BetterPosters.ScheduledTasks;

/// <summary>
/// Jellyfin 12 adapter implementing <see cref="IPosterRefreshGateway"/> via <see cref="ILibraryManager"/>,
/// <see cref="IBaseItemManager"/>, and <see cref="IProviderManager"/>.
/// </summary>
public sealed class JellyfinPosterRefreshGateway : IPosterRefreshGateway
{
    private readonly ILibraryManager _libraryManager;
    private readonly IBaseItemManager _baseItemManager;
    private readonly IProviderManager _providerManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinPosterRefreshGateway"/> class.
    /// </summary>
    public JellyfinPosterRefreshGateway(
        ILibraryManager libraryManager,
        IBaseItemManager baseItemManager,
        IProviderManager providerManager)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(baseItemManager);
        ArgumentNullException.ThrowIfNull(providerManager);

        _libraryManager = libraryManager;
        _baseItemManager = baseItemManager;
        _providerManager = providerManager;
    }

    /// <inheritdoc />
    public PosterRefreshCandidateSlice GetScopedCandidates(
        IReadOnlyCollection<Guid> scopedLibraryIds,
        int startIndex,
        int maxItems,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopedLibraryIds);
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxItems, 1);
        cancellationToken.ThrowIfCancellationRequested();

        if (scopedLibraryIds.Count == 0)
        {
            return new PosterRefreshCandidateSlice([], 0, 0);
        }

        var ancestorIds = scopedLibraryIds.OrderBy(id => id).ToArray();
        var effectiveStartIndex = startIndex;

        var firstResult = QueryBoundedPage(ancestorIds, effectiveStartIndex, maxItems, enableTotalRecordCount: true, cancellationToken);
        var totalRecordCount = Math.Max(firstResult.TotalRecordCount, firstResult.Items.Count);
        if (totalRecordCount == 0)
        {
            return new PosterRefreshCandidateSlice([], 0, 0);
        }

        if (firstResult.Items.Count == 0 && effectiveStartIndex > 0)
        {
            effectiveStartIndex = 0;
            firstResult = QueryBoundedPage(ancestorIds, 0, maxItems, enableTotalRecordCount: true, cancellationToken);
            totalRecordCount = Math.Max(firstResult.TotalRecordCount, firstResult.Items.Count);
            if (totalRecordCount == 0 || firstResult.Items.Count == 0)
            {
                return new PosterRefreshCandidateSlice([], 0, 0);
            }
        }

        var selectedItems = new List<BaseItem>(Math.Min(maxItems, totalRecordCount));
        selectedItems.AddRange(firstResult.Items.Take(maxItems));

        int nextStartIndex;
        if (effectiveStartIndex + selectedItems.Count < totalRecordCount)
        {
            nextStartIndex = effectiveStartIndex + selectedItems.Count;
        }
        else if (effectiveStartIndex > 0 && selectedItems.Count < maxItems)
        {
            var remainingBudget = Math.Min(effectiveStartIndex, maxItems - selectedItems.Count);
            var wrapResult = QueryBoundedPage(ancestorIds, 0, remainingBudget, enableTotalRecordCount: false, cancellationToken);
            var wrappedItems = wrapResult.Items.Take(remainingBudget).ToList();
            selectedItems.AddRange(wrappedItems);
            nextStartIndex = wrappedItems.Count < totalRecordCount ? wrappedItems.Count : 0;
        }
        else
        {
            nextStartIndex = 0;
        }

        var candidates = new List<PosterRefreshCandidate>(selectedItems.Count);
        foreach (var item in selectedItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!BetterPostersImageProvider.TryGetSupportedMediaType(item, out var mediaType))
            {
                continue;
            }

            var libraryOptions = _libraryManager.GetLibraryOptions(item);
            var typeOptions = libraryOptions?.GetTypeOptions(item.GetType().Name);
            var isFetcherEnabled = _baseItemManager.IsImageFetcherEnabled(item, typeOptions, Plugin.PluginName);

            candidates.Add(new PosterRefreshCandidate(
                ItemId: item.Id,
                Name: item.Name ?? string.Empty,
                MediaType: mediaType,
                ImdbId: item.GetProviderId(MetadataProvider.Imdb),
                TmdbId: item.GetProviderId(MetadataProvider.Tmdb),
                IsImageFetcherEnabled: isFetcherEnabled,
                HasExistingPrimaryImage: item.HasImage(ImageType.Primary, 0)));
        }

        var skippedOverflowCount = Math.Max(0, totalRecordCount - candidates.Count);
        return new PosterRefreshCandidateSlice(candidates, nextStartIndex, skippedOverflowCount);
    }

    private MediaBrowser.Model.Querying.QueryResult<BaseItem> QueryBoundedPage(
        Guid[] ancestorIds,
        int startIndex,
        int limit,
        bool enableTotalRecordCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            IsVirtualItem = false,
            Recursive = true,
            AncestorIds = ancestorIds,
            OrderBy =
            [
                (ItemSortBy.SortName, default),
                (ItemSortBy.ProductionYear, default)
            ],
            StartIndex = startIndex,
            Limit = limit,
            EnableTotalRecordCount = enableTotalRecordCount
        };

        return _libraryManager.GetItemsResult(query);
    }

    /// <inheritdoc />
    public async Task SaveValidatedPrimaryPosterAsync(
        Guid itemId,
        Stream validatedImageStream,
        string mimeType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validatedImageStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        cancellationToken.ThrowIfCancellationRequested();

        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            throw new InvalidOperationException($"Library item {itemId} was not found when saving validated poster.");
        }

        await _providerManager.SaveImage(
            item,
            validatedImageStream,
            mimeType,
            ImageType.Primary,
            0,
            cancellationToken).ConfigureAwait(false);

        await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);
    }
}
