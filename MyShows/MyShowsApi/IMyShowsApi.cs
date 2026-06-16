using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MyShows.Configuration;
using MyShows.MyShowsApi.Api20;

namespace MyShows.MyShowsApi
{
    internal interface IMyShowsApi
    {
        Task<bool> SetShowStatusToWatching(UserConfig user, Series item);
        Task<bool> CheckEpisode(UserConfig user, Episode item);
        Task<bool> UnCheckEpisode(UserConfig user, Episode item);
        Task<bool> SyncEpisodes(UserConfig user, List<Episode> seen, List<Episode> unseen);

        Task<bool> CheckMovie(UserConfig user, Movie item);
        Task<bool> UnCheckMovie(UserConfig user, Movie item);

        Task<int> ResolveMyShowsShowId(UserConfig user, Series series);

        Task<IReadOnlyList<ProfileShowSummary>> GetProfileShows(UserConfig user);
        Task<IReadOnlyList<ProfileEpisode>> GetEpisodesByShowId(UserConfig user, int showId);
        Task<ShowSummary> GetShowWithEpisodesById(UserConfig user, int showId);
        Task<int> GetMyShowsMovieId(UserConfig user, Movie movie);
        Task<IReadOnlyList<MovieStatus>> GetMovieStatusesByIds(UserConfig user, IReadOnlyList<int> movieIds);
    }
}
