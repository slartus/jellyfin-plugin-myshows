using System;
using System.Collections.Generic;
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
using MyShows.MyShowsApi;

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

            var userOffset = (double)userIndex / userCount * 100.0;
            var userSlot = 100.0 / userCount;

            await PullEpisodes(api, uc, jellyfinUser, progress, userOffset, userSlot * 0.7, ct);
            await PullMovies(api, uc, jellyfinUser, progress, userOffset + userSlot * 0.7, userSlot * 0.3, ct);
        }

        private async Task PullEpisodes(IMyShowsApi api, UserConfig uc, Jellyfin.Database.Implementations.Entities.User user,
            IProgress<double> progress, double slotStart, double slotSize, CancellationToken ct)
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

                IReadOnlyDictionary<(int Season, int Episode), DateTimeOffset?> watched;
                try
                {
                    watched = await api.GetWatchedEpisodes(uc, series);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch watched episodes for '{0}'", series.Name);
                    continue;
                }

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
            IProgress<double> progress, double slotStart, double slotSize, CancellationToken ct)
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

            IReadOnlyDictionary<Guid, bool> watched;
            try
            {
                watched = await api.GetWatchedMovies(uc, movies);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch watched movies for {0}", user.Username);
                progress?.Report(slotStart + slotSize);
                return;
            }

            ct.ThrowIfCancellationRequested();
            var marked = 0;
            foreach (var movie in movies)
            {
                if (!watched.TryGetValue(movie.Id, out var isWatched) || !isWatched) continue;
                if (TryMarkPlayed(movie, user, null)) marked++;
            }

            _logger.LogInformation("Marked {0} movies as played for {1}", marked, user.Username);
            progress?.Report(slotStart + slotSize);
        }

        private bool TryMarkPlayed(BaseItem item, Jellyfin.Database.Implementations.Entities.User user, DateTimeOffset? when)
        {
            var existing = _userDataManager.GetUserData(user, item);
            if (existing != null && existing.Played) return false;

            var datePlayed = when?.UtcDateTime ?? DateTime.UtcNow;
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
