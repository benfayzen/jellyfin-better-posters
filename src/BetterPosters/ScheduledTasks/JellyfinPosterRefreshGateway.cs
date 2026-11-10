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
    public IReadOnlyList<PosterRefreshCandidate> GetScopedCandidates(IReadOnlyCollection<Guid> scopedLibraryIds)
    {
        ArgumentNullException.ThrowIfNull(scopedLibraryIds);
        if (scopedLibraryIds.Count == 0)
        {
            return [];
        }

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            IsVirtualItem = false,
            Recursive = true,
            AncestorIds = scopedLibraryIds.ToArray()
        };

        var items = _libraryManager.GetItemList(query);
        var candidates = new List<PosterRefreshCandidate>(items.Count);

        foreach (var item in items.OrderBy(i => i.Id))
        {
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

        return candidates;
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
