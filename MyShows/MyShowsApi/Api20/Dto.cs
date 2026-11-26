using System;
using System.Collections.Generic;

namespace MyShows.MyShowsApi.Api20
{
    public class JsonRpcCall
    {
        public string jsonrpc { get; set; }
        public string method { get; set; }
        public int id { get; set; }
        public object @params { get; set; }
    }

    public class JsonRpcResult<T>
    {
        public string jsonrpc { get; set; }
        public T result { get; set; }
        public int id { get; set; }
        public JsonRpcError error { get; set; }
    }

    public class JsonRpcError
    {
        public int code { get; set; }
        public string message { get; set; }
    }

    public class MoviesGetCatalogArgs
    {
        public MoviesCatalogSearch search { get; set; }
        public int page { get; set; }
        public int pageSize { get; set; }
    }

    public class MoviesCatalogSearch
    {
        public string query { get; set; }
    }

    public class MovieCatalogEntry
    {
        public MovieCatalogMovie movie { get; set; }
    }

    public class MovieCatalogMovie
    {
        public int id { get; set; }
        public string title { get; set; }
        public int year { get; set; }
    }

    public class MoviesGetByIdArgs
    {
        public int movieId { get; set; }
    }

    public class MovieDetails
    {
        public int id { get; set; }
        public int? kinopoiskId { get; set; }
    }

    public class ShowsGetByIdArgs
    {
        public int showId { get; set; }
        public bool withEpisodes { get; set; }
    }

    public class ShowsGetByExternalIdArgs
    {
        public int id { get; set; }
        public string source { get; set; }
    }

    public class ManageSetShowStatusArgs
    {
        public int id { get; set; }
        public string status { get; set; }
    }

    public class ManageEpisodeArgs
    {
        public int id { get; set; }
    }

    public class ManageSyncEpisodesDeltaArgs
    {
        public int showId { get; set; }
        public int[] checkedIds { get; set; }
        public int[] unCheckedIds { get; set; }
    }

    public class ShowSummary
    {
        public int id { get; set; }
        public string title { get; set; }
        public string titleOriginal { get; set; }
        public string status { get; set; }
        public EpisodeSummary[] episodes { get; set; }
    }

    public class EpisodeSummary
    {
        public int id { get; set; }
        public int seasonNumber { get; set; }
        public int episodeNumber { get; set; }
    }

    public class ProfileShowStatuses
    {
        public int[] showIds { get; set; }
    }

    public class ShowStatus
    {
        public int showId { get; set; }
        public string watchStatus { get; set; }
    }

    public class MoviesAddExternalMovieArgs
    {
        public string externalId { get; set; }
        public string source { get; set; }
    }

    public class ManageSetMovieStatusArgs
    {
        public int movieId { get; set; }
        public string status { get; set; }
    }

    public class ProfileMovieStatusesArgs
    {
        public int[] movieIds { get; set; }
    }

    public class MovieStatus
    {
        public int id { get; set; }
        public string watchStatus { get; set; }
    }

    public class ProfileMovieSummary
    {
        public int id { get; set; }
        public string title { get; set; }
        public string titleOriginal { get; set; }
        public int? year { get; set; }
        public List<ProfileGenre> genres { get; set; }
        public double? imdbRating { get; set; }
        public double? kinopoiskRating { get; set; }
        public double? rating { get; set; }
        public ProfileMovieUser userMovie { get; set; }

        internal bool TryGetPersonalRating(out double? value)
        {
            var field = userMovie?.rating ?? default;
            if (field.ValueKind == System.Text.Json.JsonValueKind.Number && field.TryGetDouble(out var number))
            {
                value = number > 0 ? number : null;
                return true;
            }
            value = null;
            return field.ValueKind == System.Text.Json.JsonValueKind.Null;
        }
    }

    public class ProfileMovieUser
    {
        public int id { get; set; }
        public string watchStatus { get; set; }
        public System.Text.Json.JsonElement rating { get; set; }
        public string watchDate { get; set; }
    }

    public class ProfileGenre
    {
        public int id { get; set; }
        public string title { get; set; }
        public string alias { get; set; }
    }

    public class ProfileEpisodesArgs
    {
        public int showId { get; set; }
    }

    public class ProfileEpisode
    {
        public int id { get; set; }
        public string watchDate { get; set; }
        public double? rating { get; set; }
        public bool isFavorite { get; set; }
    }

    public class ProfileShowSummary
    {
        public ProfileShowSummaryInner show { get; set; }
        public string watchStatus { get; set; }
        public double? rating { get; set; }
        public int watchCount { get; set; }
        public int totalEpisodes { get; set; }
        public int watchedEpisodes { get; set; }
    }

    public class ProfileShowSummaryInner
    {
        public int id { get; set; }
        public string title { get; set; }
        public string titleOriginal { get; set; }
        public string status { get; set; }
        public int? year { get; set; }
        public int? totalSeasons { get; set; }
        public List<ProfileGenre> genres { get; set; }
        public double? imdbRating { get; set; }
        public double? kinopoiskRating { get; set; }
        public double? rating { get; set; }
    }

    public class EpisodePosition
    {
        public int seasonNumber { get; set; }
        public int episodeNumber { get; set; }
        public DateTimeOffset? watchedAt { get; set; }
    }
}
