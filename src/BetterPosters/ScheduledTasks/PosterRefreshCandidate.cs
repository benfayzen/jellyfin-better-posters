using System;
using Jellyfin.Plugin.BetterPosters.Domain;

namespace Jellyfin.Plugin.BetterPosters.ScheduledTasks;

/// <summary>
/// Represents a Movie or Series candidate inspected during a scoped scheduled refresh run.
/// </summary>
public sealed record PosterRefreshCandidate(
    Guid ItemId,
    string Name,
    SupportedMediaType MediaType,
    string? ImdbId,
    string? TmdbId,
    bool IsImageFetcherEnabled,
    bool HasExistingPrimaryImage,
    string InitialPosterMarker = "");
