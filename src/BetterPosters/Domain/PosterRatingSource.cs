namespace Jellyfin.Plugin.BetterPosters.Domain;

/// <summary>
/// Rating sources supported by the btttr.cc poster service.
/// </summary>
public enum PosterRatingSource
{
    Average = 0,
    Imdb = 1,
    Tmdb = 2,
    RottenTomatoes = 3,
    Metacritic = 4,
    Trakt = 5,
    Letterboxd = 6,
    RogerEbert = 7
}
