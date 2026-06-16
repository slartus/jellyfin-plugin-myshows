using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging;
using MyShows.Configuration;

namespace MyShows.MyShowsApi.Api20
{
    internal class MyShowsApi20 : IMyShowsApi
    {
        private readonly ILogger _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private int _counter = 1;
        private static readonly TimeSpan CACHED_SHOW_STORAGE_INTERVAL = TimeSpan.FromHours(24);
        private readonly ExpireableCache<string, ShowSummary> _showsCache = new();
        private readonly List<Guid> _lastWatchedShows = new();
        private readonly ExpireableCache<string, int> _moviesCache = new();
        private static readonly TimeSpan CACHED_MOVIE_STORAGE_INTERVAL = TimeSpan.FromHours(24);

        public MyShowsApi20(
            ILogger logger,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<bool> SetShowStatusToWatching(UserConfig user, Series item)
        {
            if (_lastWatchedShows.Contains(item.Id)) return true;

            var show = await GetShow(user, item);
            if (show == default(ShowSummary)) return false;

            var showStatus = await Execute<ShowStatus[]>(user, "profile.ShowStatuses", new ProfileShowStatuses
            {
                showIds = new[] {show.id}
            });

            if (showStatus.FirstOrDefault()?.watchStatus == "watching")
            {
                _lastWatchedShows.Add(item.Id);
                return true;
            }

            var success = await Execute<bool>(user, "manage.SetShowStatus", new ManageSetShowStatusArgs
            {
                id = show.id,
                status = "watching"
            });

            if (success) _lastWatchedShows.Add(item.Id);
            return success;
        }

        public async Task<bool> CheckEpisode(UserConfig user, Episode item)
        {
            return await ToggleEpisode(user, item, true);
        }

        public async Task<bool> UnCheckEpisode(UserConfig user, Episode item)
        {
            return await ToggleEpisode(user, item, false);
        }

        private async Task<bool> ToggleEpisode(UserConfig user, Episode item, bool check)
        {
            var method = check ? "manage.CheckEpisode" : "manage.UnCheckEpisode";
            var show = await GetShow(user, item.Series);
            if (show == default(ShowSummary)) return false;
            var episode = show.episodes.First(e => e.seasonNumber == item.Season.IndexNumber && e.episodeNumber == item.IndexNumber);

            var success = await Execute<bool>(user, method, new ManageEpisodeArgs
            {
                id = episode.id
            });
            return success;
        }

        public async Task<bool> SyncEpisodes(UserConfig user, List<Episode> seen, List<Episode> unseen)
        {
            if (!seen.Any() && !unseen.Any()) return false;

            var firstEpisode = seen.Any() ? seen.First() : unseen.First();
            var show = await GetShow(user, firstEpisode.Series);
            if (show == default(ShowSummary)) return false;

            var seenIds = new List<int>();
            var unSeenIds = new List<int>();

            foreach (var ep in seen)
            {
                var episode = show.episodes.FirstOrDefault(e => e.seasonNumber == ep.Season.IndexNumber && e.episodeNumber == ep.IndexNumber);
                if (episode != default(EpisodeSummary))
                    seenIds.Add(episode.id);
            }
            foreach (var ep in unseen)
            {
                var episode = show.episodes.FirstOrDefault(e => e.seasonNumber == ep.Season.IndexNumber && e.episodeNumber == ep.IndexNumber);
                if (episode != default(EpisodeSummary))
                    unSeenIds.Add(episode.id);
            }

            var success = await Execute<bool>(user, "manage.SyncEpisodesDelta", new ManageSyncEpisodesDeltaArgs
            {
                showId = show.id,
                checkedIds = seenIds.ToArray(),
                unCheckedIds = unSeenIds.ToArray(),
            });
            return success;
        }

        protected async Task<ShowSummary> GetShow(UserConfig user, Series item)
        {
            var (id, source) = item.GetBestProviderId();
            if (source == null)
            {
                _logger.LogWarning("Not found any provider id for show '{0}'", item.Name);
                return default;
            }

            var cacheKey = id.ToString() + source;

            var show = _showsCache.Get(cacheKey);
            if (show != default(ShowSummary))
            {
                return show;
            }

            show = await Execute<ShowSummary>(user, "shows.GetByExternalId", new ShowsGetByExternalIdArgs
            {
                id = id,
                source = source
            });

            if (show == default(ShowSummary)) return show;

            show = await Execute<ShowSummary>(user, "shows.GetById", new ShowsGetByIdArgs
            {
                showId = show.id,
                withEpisodes = true
            });

            _showsCache.Store(cacheKey, show, CACHED_SHOW_STORAGE_INTERVAL);

            return show;
        }

        public async Task<bool> CheckMovie(UserConfig user, Movie item)
        {
            return await ToggleMovie(user, item, true);
        }

        public async Task<bool> UnCheckMovie(UserConfig user, Movie item)
        {
            return await ToggleMovie(user, item, false);
        }

        private async Task<bool> ToggleMovie(UserConfig user, Movie item, bool check)
        {
            var movieId = await GetMovieId(user, item);
            if (movieId <= 0) return false;

            var success = await Execute<bool>(user, "manage.SetMovieStatus", new ManageSetMovieStatusArgs
            {
                movieId = movieId,
                status = check ? "finished" : "remove"
            });
            return success;
        }

        protected async Task<int> GetMovieId(UserConfig user, Movie item)
        {
            var tmdbId = item.GetTmdbId();
            if (string.IsNullOrEmpty(tmdbId))
            {
                _logger.LogWarning("No TMDb id for movie '{0}' — MyShows only matches movies by TMDb id", item.Name);
                return 0;
            }

            var cacheKey = "tmdb:" + tmdbId;
            var cached = _moviesCache.Get(cacheKey);
            if (cached > 0) return cached;

            var movieId = await Execute<int>(user, "movies.AddExternalMovie", new MoviesAddExternalMovieArgs
            {
                externalId = tmdbId,
                source = "tmdb"
            });

            if (movieId <= 0)
            {
                _logger.LogWarning("MyShows did not return an id for TMDb={0} ('{1}')", tmdbId, item.Name);
                return 0;
            }

            _moviesCache.Store(cacheKey, movieId, CACHED_MOVIE_STORAGE_INTERVAL);
            return movieId;
        }

        public async Task<IReadOnlyDictionary<(int Season, int Episode), DateTimeOffset?>> GetWatchedEpisodes(UserConfig user, Series series)
        {
            var show = await GetShow(user, series);
            if (show == default(ShowSummary) || show.episodes == null) return new Dictionary<(int, int), DateTimeOffset?>();

            var byId = show.episodes
                .GroupBy(e => e.id)
                .ToDictionary(g => g.Key, g => g.First());

            var profileEpisodes = await Execute<ProfileEpisode[]>(user, "profile.Episodes", new ProfileEpisodesArgs
            {
                showId = show.id,
            });
            if (profileEpisodes == null) return new Dictionary<(int, int), DateTimeOffset?>();

            var result = new Dictionary<(int Season, int Episode), DateTimeOffset?>();
            foreach (var pe in profileEpisodes)
            {
                if (!byId.TryGetValue(pe.id, out var summary)) continue;
                var key = (summary.seasonNumber, summary.episodeNumber);
                DateTimeOffset? when = null;
                if (!string.IsNullOrEmpty(pe.watchDate)
                    && DateTimeOffset.TryParse(pe.watchDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    when = parsed;
                }
                result[key] = when;
            }
            return result;
        }

        public async Task<IReadOnlyDictionary<Guid, bool>> GetWatchedMovies(UserConfig user, IReadOnlyList<Movie> movies)
        {
            if (movies == null || movies.Count == 0) return new Dictionary<Guid, bool>();

            var myShowsToJellyfin = new Dictionary<int, Guid>();
            foreach (var movie in movies)
            {
                var myShowsMovieId = await GetMovieId(user, movie);
                if (myShowsMovieId <= 0) continue;
                myShowsToJellyfin[myShowsMovieId] = movie.Id;
            }
            if (myShowsToJellyfin.Count == 0) return new Dictionary<Guid, bool>();

            var statuses = await Execute<MovieStatus[]>(user, "profile.MovieStatuses", new ProfileMovieStatusesArgs
            {
                movieIds = myShowsToJellyfin.Keys.ToArray(),
            });

            var result = new Dictionary<Guid, bool>();
            if (statuses == null) return result;
            foreach (var status in statuses)
            {
                if (!myShowsToJellyfin.TryGetValue(status.id, out var jellyfinId)) continue;
                result[jellyfinId] = string.Equals(status.watchStatus, "finished", StringComparison.OrdinalIgnoreCase);
            }
            return result;
        }

        private async Task<T> Execute<T>(UserConfig user, string method, object args)
        {
            var isTokenValid = await user.EnsureAccessTokenValid(GetHttpClient());
            if (!isTokenValid)
            {
                _logger.LogWarning("AccessToken invalidated and RefreshToken isn't helped. Too bad.");
                return default;
            }

            var call = new JsonRpcCall
            {
                jsonrpc = "2.0",
                id = _counter++,
                method = method,
                @params = args,
            };
            var httpClient = GetHttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, ApiConstants.RpcUri)
            {
                Content = new StringContent(JsonSerializer.Serialize(call, JsonDefaults.Options), Encoding.UTF8, "application/json"),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Authorization2", "Bearer " + user.AccessToken);
            var response = await httpClient.SendAsync(request);

            var result = await Extensions.DeserializeFromHttp<JsonRpcResult<T>>(response);
            if (result.error != null)
            {
                _logger.LogWarning("JSON-RPC error: {0}", result.error.message);
            }
            return result.result;
        }

        private HttpClient GetHttpClient(string accessToken = null)
        {
            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (accessToken != null)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return client;
        }
    }
}
