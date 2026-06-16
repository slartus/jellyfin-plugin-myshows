using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using MyShows.Configuration;
using MyShows.Journal;
using MyShows.MyShowsApi;
using MyShows.MyShowsApi.Api20;

namespace MyShows.Tasks
{
    public class PullWatchedFromMyShowsTask : IScheduledTask, IConfigurableScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;
        private readonly ILogger _logger;
        private readonly MyShowsApiFactory _apiFactory;

        public PullWatchedFromMyShowsTask(
            ILibraryManager libraryManager,
            IUserManager userManager,
            IUserDataManager userDataManager,
            ILoggerFactory loggerFactory,
            IHttpClientFactory httpClientFactory)
        {
            _libraryManager = libraryManager;
            _userManager = userManager;
            _userDataManager = userDataManager;
            _logger = loggerFactory.CreateLogger("MyShows.PullWatched");
            _apiFactory = new MyShowsApiFactory(_logger, httpClientFactory);
        }

        public string Name => "MyShows → Jellyfin: pull watched";
        public string Key => "MyShowsPullWatched";
        public string Description => "Backfill played state in Jellyfin from MyShows.me for users configured in the MyShows plugin.";
        public string Category => "MyShows";

        public bool IsHidden => false;
        public bool IsEnabled => true;
        public bool IsLogged => true;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var config = Plugin.Instance?.PluginConfiguration;
            if (config?.Users == null || config.Users.Length == 0)
            {
                _logger.LogInformation("No MyShows users configured, nothing to pull");
                progress?.Report(100);
                return;
            }

            var userConfigs = config.Users
                .Where(u => !string.IsNullOrEmpty(u.AccessToken) && u.PullWatchedFromMyShows)
                .ToList();
            if (userConfigs.Count == 0)
            {
                _logger.LogInformation("No MyShows users opted in to pull-sync (PullWatchedFromMyShows=true), nothing to do");
                progress?.Report(100);
                return;
            }

            for (var i = 0; i < userConfigs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var uc = userConfigs[i];
                await ProcessUser(uc, progress, i, userConfigs.Count, cancellationToken);
            }

            progress?.Report(100);
        }

        private async Task ProcessUser(UserConfig uc, IProgress<double> progress, int userIndex, int userCount, CancellationToken ct)
        {
            var jellyfinUser = ResolveJellyfinUser(uc);
            if (jellyfinUser == null)
            {
                _logger.LogWarning("Cannot resolve Jellyfin user for UserConfig id={0}", uc.Id);
                return;
            }

            _logger.LogInformation("Pulling watched state for user {0}", jellyfinUser.Username);
            var api = _apiFactory.GetApi(uc.ApiVersion);
            var journal = OpenJournal();

            var userOffset = (double)userIndex / userCount * 100.0;
            var userSlot = 100.0 / userCount;

            await SyncJournal(api, uc, journal, progress, userOffset, userSlot * 0.5, ct);
            await PullEpisodes(api, uc, jellyfinUser, journal, progress, userOffset + userSlot * 0.5, userSlot * 0.35, ct);
            await PullMovies(api, uc, jellyfinUser, journal, progress, userOffset + userSlot * 0.85, userSlot * 0.15, ct);
        }

        private JournalStore OpenJournal() => JournalAccessor.Open();

        private async Task SyncJournal(IMyShowsApi api, UserConfig uc, JournalStore journal,
            IProgress<double> progress, double slotStart, double slotSize, CancellationToken ct)
        {
            IReadOnlyList<ProfileShowSummary> profileShows;
            try
            {
                profileShows = await api.GetProfileShows(uc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Journal: failed to fetch profile.Shows for {0}", uc.Name);
                progress?.Report(slotStart + slotSize);
                return;
            }

            var journalShows = profileShows
                .Where(s => s.show != null)
                .Select(s => new JournalShow
                {
                    ShowId = s.show.id,
                    Title = s.show.title,
                    TitleOriginal = s.show.titleOriginal,
                    Year = s.show.year,
                    Status = s.show.status,
                    WatchStatus = s.watchStatus,
                    WatchedEpisodes = s.watchedEpisodes,
                    TotalEpisodes = s.totalEpisodes,
                    Rating = s.rating,
                })
                .ToList();

            await journal.UpsertShows(uc.Id, journalShows);
            _logger.LogInformation("Journal: stored {0} shows for {1}", journalShows.Count, uc.Name);

            var withEpisodes = profileShows.Where(s => s.show != null && s.watchedEpisodes > 0).ToList();
            for (var i = 0; i < withEpisodes.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var s = withEpisodes[i];
                IReadOnlyList<ProfileEpisode> episodes;
                try
                {
                    episodes = await api.GetEpisodesByShowId(uc, s.show.id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Journal: failed to fetch episodes for show {0} ({1})", s.show.id, s.show.title);
                    continue;
                }

                var seByEpisodeId = new Dictionary<int, (int Season, int Episode)>();
                try
                {
                    var full = await api.GetShowWithEpisodesById(uc, s.show.id);
                    if (full?.episodes != null)
                    {
                        foreach (var fe in full.episodes)
                        {
                            seByEpisodeId[fe.id] = (fe.seasonNumber, fe.episodeNumber);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Journal: failed to fetch full show {0} ({1}) for S/E mapping", s.show.id, s.show.title);
                }

                var journalEpisodes = episodes
                    .Where(e => seByEpisodeId.ContainsKey(e.id))
                    .Select(e => new JournalEpisode
                    {
                        EpisodeId = e.id,
                        ShowId = s.show.id,
                        SeasonNumber = seByEpisodeId[e.id].Season,
                        EpisodeNumber = seByEpisodeId[e.id].Episode,
                        WatchDate = ParseWatchDate(e.watchDate),
                        Rating = e.rating,
                        IsFavorite = e.isFavorite,
                    })
                    .ToList();

                await journal.UpsertEpisodes(uc.Id, s.show.id, journalEpisodes);
                progress?.Report(slotStart + slotSize * ((double)(i + 1) / withEpisodes.Count));
            }

            _logger.LogInformation("Journal: synced episodes for {0} shows of {1}", withEpisodes.Count, uc.Name);
            progress?.Report(slotStart + slotSize);
        }

        private static DateTimeOffset? ParseWatchDate(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
                ? dt
                : (DateTimeOffset?)null;
        }

        private async Task PullEpisodes(IMyShowsApi api, UserConfig uc, Jellyfin.Database.Implementations.Entities.User user,
            JournalStore journal, IProgress<double> progress, double slotStart, double slotSize, CancellationToken ct)
        {
            var episodes = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                Recursive = true,
                IsVirtualItem = false,
            }).OfType<Episode>().Where(IsEpisodeMappable).ToList();

            var bySeries = episodes
                .Where(e => e.Series != null)
                .GroupBy(e => e.Series.Id)
                .ToList();

            _logger.LogInformation("Found {0} episodes across {1} series for {2}", episodes.Count, bySeries.Count, user.Username);

            var marked = 0;
            for (var i = 0; i < bySeries.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var group = bySeries[i];
                var series = group.First().Series;

                int msShowId;
                try
                {
                    msShowId = await api.ResolveMyShowsShowId(uc, series);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to resolve MyShows id for series '{0}'", series.Name);
                    continue;
                }
                if (msShowId <= 0) continue;

                var watched = await journal.GetWatchedEpisodesForShow(uc.Id, msShowId);
                if (watched.Count == 0) continue;

                foreach (var episode in group)
                {
                    if (episode.Season?.IndexNumber == null || !episode.IndexNumber.HasValue) continue;
                    var key = (episode.Season.IndexNumber.Value, episode.IndexNumber.Value);
                    if (!watched.TryGetValue(key, out var when)) continue;

                    if (TryMarkPlayed(episode, user, when))
                    {
                        marked++;
                    }
                }

                progress?.Report(slotStart + slotSize * ((double)(i + 1) / bySeries.Count));
            }

            _logger.LogInformation("Marked {0} episodes as played for {1}", marked, user.Username);
        }

        private async Task PullMovies(IMyShowsApi api, UserConfig uc, Jellyfin.Database.Implementations.Entities.User user,
            JournalStore journal, IProgress<double> progress, double slotStart, double slotSize, CancellationToken ct)
        {
            var movies = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                Recursive = true,
                IsVirtualItem = false,
            }).OfType<Movie>().Where(m => !string.IsNullOrEmpty(m.GetTmdbId())).ToList();

            _logger.LogInformation("Found {0} movies with TMDb id for {1}", movies.Count, user.Username);
            if (movies.Count == 0)
            {
                progress?.Report(slotStart + slotSize);
                return;
            }

            var myShowsIdToMovie = new Dictionary<int, Movie>();
            foreach (var movie in movies)
            {
                ct.ThrowIfCancellationRequested();
                int msId;
                try
                {
                    msId = await api.GetMyShowsMovieId(uc, movie);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to resolve MyShows id for movie '{0}'", movie.Name);
                    continue;
                }
                if (msId > 0) myShowsIdToMovie[msId] = movie;
            }

            if (myShowsIdToMovie.Count == 0)
            {
                progress?.Report(slotStart + slotSize);
                return;
            }

            IReadOnlyList<MovieStatus> statuses;
            try
            {
                statuses = await api.GetMovieStatusesByIds(uc, myShowsIdToMovie.Keys.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch movie statuses for {0}", user.Username);
                progress?.Report(slotStart + slotSize);
                return;
            }

            var journalMovies = new List<JournalMovie>(statuses.Count);
            var marked = 0;
            foreach (var status in statuses)
            {
                if (!myShowsIdToMovie.TryGetValue(status.id, out var movie)) continue;
                var isWatched = string.Equals(status.watchStatus, "finished", StringComparison.OrdinalIgnoreCase);

                journalMovies.Add(new JournalMovie
                {
                    MovieId = status.id,
                    TmdbId = movie.GetTmdbId(),
                    Title = movie.Name,
                    WatchStatus = status.watchStatus,
                    WatchDate = null,
                });

                if (isWatched && TryMarkPlayed(movie, user, null)) marked++;
            }

            await journal.UpsertMovies(uc.Id, journalMovies);

            _logger.LogInformation("Marked {0} movies as played for {1}; journaled {2}",
                marked, user.Username, journalMovies.Count);
            progress?.Report(slotStart + slotSize);
        }

        private bool TryMarkPlayed(BaseItem item, Jellyfin.Database.Implementations.Entities.User user, DateTimeOffset? when)
        {
            var existing = _userDataManager.GetUserData(user, item);
            if (existing != null && existing.Played) return false;

            var datePlayed = when?.UtcDateTime ?? DateTime.UtcNow;
            ScrobbleSuppressor.Suppress(user.Id, item.Id);
            item.MarkPlayed(user, datePlayed, true);
            _logger.LogDebug("Marked played: {0}", item.Name);
            return true;
        }

        private Jellyfin.Database.Implementations.Entities.User ResolveJellyfinUser(UserConfig uc)
        {
            if (string.IsNullOrEmpty(uc.Id)) return null;
            if (!Guid.TryParseExact(uc.Id, "N", out var guid)) return null;
            return _userManager.GetUserById(guid);
        }

        private static bool IsEpisodeMappable(Episode episode)
        {
            if (episode == null || episode.Series == null) return false;
            if (episode.Season?.IndexNumber == null) return false;
            if (!episode.IndexNumber.HasValue) return false;
            var (_, source) = episode.Series.GetBestProviderId();
            return source != null;
        }
    }
}
