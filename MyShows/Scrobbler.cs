using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyShows.Configuration;
using MyShows.MyShowsApi;

namespace MyShows
{
    public class Scrobbler : IHostedService
    {
        private readonly ILogger _logger;
        private readonly ISessionManager _sessionManager;
        private readonly IUserDataManager _userDataManager;
        private readonly List<Guid> _lastScrobbled;
        private MyShowsApiFactory _client;
        private UserDataHelper _userDataHelper;
        private DateTime _nextTry;

        public Scrobbler(
            ISessionManager sessionManager,
            IUserDataManager userDataManager,
            ILoggerFactory logger,
            IHttpClientFactory httpClientFactory
            )
        {
            _sessionManager = sessionManager;
            _userDataManager = userDataManager;
            _logger = logger.CreateLogger("MyShows");

            _client = new MyShowsApiFactory(_logger, httpClientFactory);
            _userDataHelper = new UserDataHelper(_logger, _client);

            _nextTry = DateTime.UtcNow;
            _lastScrobbled = new List<Guid>();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _userDataManager.UserDataSaved -= OnUserDataSaved;
            _sessionManager.PlaybackStart -= OnPlaybackStart;
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
            _sessionManager.PlaybackProgress -= OnPlaybackProgress;

            _client = null;
            _userDataHelper = null;

            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _userDataManager.UserDataSaved += OnUserDataSaved;
            _sessionManager.PlaybackStart += OnPlaybackStart;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            _sessionManager.PlaybackProgress += OnPlaybackProgress;

            return Task.CompletedTask;
        }

        private async void OnPlaybackProgress(object sender, PlaybackProgressEventArgs e)
        {
            if (DateTime.UtcNow < _nextTry) return; // postpone
            _nextTry = DateTime.UtcNow.AddSeconds(30);

            if (!CheckConstraintsAndGetUser(e.Session.UserId, e.Item, out var user)) return;

            if (e.Session.PlayState.PositionTicks == null || e.Session.NowPlayingItem.RunTimeTicks == null) return;

            // don't scrobble if percentage watched is below 90%
            float percentageWatched = (float)e.Session.PlayState.PositionTicks / (float)e.Session.NowPlayingItem.RunTimeTicks * 100f;
            if (percentageWatched < user.ScrobbleAt) return;

            if (_lastScrobbled.Contains(e.Item.Id)) return;

            try
            {
                _logger.LogInformation("Item is played 90%. Scrobble");

                switch (e.Item)
                {
                    case Episode episode:
                    {
                        var result = await _client.GetApi(user.ApiVersion).CheckEpisode(user, episode);
                        _logger.LogInformation("Checked episode '{0}' S{1}E{2} {3}", episode.Series.Name,
                            episode.Season.IndexNumber, episode.IndexNumber, result ? "successfully" : "failed");
                        if (result) _lastScrobbled.Add(episode.Id);
                        break;
                    }
                    case Movie movie:
                    {
                        var result = await _client.GetApi(user.ApiVersion).CheckMovie(user, movie);
                        _logger.LogInformation("Checked movie '{0}' {1}", movie.Name, result ? "successfully" : "failed");
                        if (result) _lastScrobbled.Add(movie.Id);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending watching status update");
            }
        }

        private async void OnUserDataSaved(object sender, UserDataSaveEventArgs e)
        {
            // ignore change events for any reason other than manually toggling played.
            if (e.SaveReason != UserDataSaveReason.TogglePlayed)
            {
                return;
            }

            if (e.Item == null) return;

            if (ScrobbleSuppressor.TryConsume(e.UserId, e.Item.Id))
            {
                _logger.LogDebug("Suppressed echo scrobble for item {0} (mark came from pull-task)", e.Item.Name);
                return;
            }

            var user = Plugin.Instance.Configuration.GetUserById(e.UserId);

            // Can't progress
            if (user == null || !CanSync(e.Item))
            {
                return;
            }

            if (e.Item is Movie movie)
            {
                try
                {
                    var api = _client.GetApi(user.ApiVersion);
                    var result = e.UserData.Played
                        ? await api.CheckMovie(user, movie)
                        : await api.UnCheckMovie(user, movie);
                    _logger.LogInformation("Toggled movie '{0}' played={1} {2}",
                        movie.Name, e.UserData.Played, result ? "successfully" : "failed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error syncing movie watch state");
                }
                return;
            }

            await _userDataHelper.AddEvent(user, e);
        }

        private async void OnPlaybackStart(object sender, PlaybackProgressEventArgs e)
        {
            _logger.LogInformation("Playback Started");

            if (!CheckConstraintsAndGetUser(e.Session.UserId, e.Item, out var user)) return;

            try
            {
                if (e.Item is Episode episode)
                {
                    var result = await _client.GetApi(user.ApiVersion).SetShowStatusToWatching(user, episode.Series);
                    _logger.LogDebug("Started watching show '{0}' {1}", episode.Series.Name, result ? "successfully" : "failed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending watching status update");
            }

        }

        private async void OnPlaybackStopped(object sender, PlaybackStopEventArgs e)
        {
            _logger.LogInformation("Playback Stopped");

            if (!CheckConstraintsAndGetUser(e.Session.UserId, e.Item, out var user)) return;

            if (!e.PlayedToCompletion) return;

            if (_lastScrobbled.Contains(e.Item.Id)) return;

            try
            {
                _logger.LogInformation("Item is played. Scrobble");

                switch (e.Item)
                {
                    case Episode episode:
                    {
                        var result = await _client.GetApi(user.ApiVersion).CheckEpisode(user, episode);
                        _logger.LogInformation("Checked episode '{0}' S{1}E{2} {3}", episode.Series.Name,
                            episode.Season.IndexNumber, episode.IndexNumber, result ? "successfully" : "failed");
                        if (result) _lastScrobbled.Add(episode.Id);
                        break;
                    }
                    case Movie movie:
                    {
                        var result = await _client.GetApi(user.ApiVersion).CheckMovie(user, movie);
                        _logger.LogInformation("Checked movie '{0}' {1}", movie.Name, result ? "successfully" : "failed");
                        if (result) _lastScrobbled.Add(movie.Id);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending watching status update");
            }
        }

        private bool CheckConstraintsAndGetUser(Guid userId, BaseItem item, out UserConfig user)
        {
            user = Plugin.Instance.PluginConfiguration.GetUserById(userId);

            if (user == null)
            {
                _logger.LogInformation("Could not match user with any stored credentials");
                return false;
            }

            if (!CanSync(item))
            {
                _logger.LogDebug("Can not sync this type of items: {0}", item?.MediaType);
                return false;
            }

            return true;
        }

        public bool CanSync(BaseItem item)
        {
            if (item?.Path == null || item.LocationType == LocationType.Virtual)
            {
                return false;
            }

            if (item is Episode episode
                && episode.Series != null
                && episode.Season != null
                && episode.Season.IndexNumber.HasValue
                && episode.IndexNumber.HasValue
                && !episode.IsMissingEpisode
                )
            {
                var (_, source) = episode.Series.GetBestProviderId();
                return source != null;
            }

            if (item is Movie movie)
            {
                return !string.IsNullOrEmpty(movie.GetTmdbId());
            }

            return false;
        }
    }
}
