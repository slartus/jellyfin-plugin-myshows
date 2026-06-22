using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyShows.Journal;

namespace MyShows.Api
{
    [ApiController]
    [Authorize]
    [Route("MyShows/v1")]
    [Produces(MediaTypeNames.Application.Json)]
    public class HistoryController : ControllerBase
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;

        public HistoryController(ILibraryManager libraryManager, IUserManager userManager)
        {
            _libraryManager = libraryManager;
            _userManager = userManager;
        }

        [HttpGet("history/stats")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<HistoryStats>> GetStats(
            [FromQuery] int topShows = 10,
            [FromQuery] string userId = null)
        {
            var targetUser = ResolveTargetUserId(userId);
            if (targetUser == null) return Forbid();

            var store = JournalAccessor.TryOpen();
            if (store == null) return NotFound(new { error = "Journal not initialised. Run pull-task first." });

            var stats = await store.GetStats(targetUser, topShows);
            return Ok(stats);
        }

        [HttpGet("history/episodes/recent")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult> GetRecentEpisodes(
            [FromQuery] int limit = 20,
            [FromQuery] string userId = null)
        {
            var targetUser = ResolveTargetUserId(userId);
            if (targetUser == null) return Forbid();
            if (limit <= 0 || limit > 500) limit = 20;

            var store = JournalAccessor.TryOpen();
            if (store == null) return NotFound(new { error = "Journal not initialised. Run pull-task first." });

            var episodes = await store.GetRecentEpisodes(targetUser, limit);
            return Ok(new { items = episodes });
        }

        [HttpGet("history/shows/recent")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult> GetRecentShows(
            [FromQuery] int limit = 15,
            [FromQuery] string userId = null)
        {
            var targetUser = ResolveTargetUserId(userId);
            if (targetUser == null) return Forbid();
            if (limit <= 0 || limit > 100) limit = 15;

            var store = JournalAccessor.TryOpen();
            if (store == null) return NotFound(new { error = "Journal not initialised. Run pull-task first." });

            var shows = await store.GetRecentShows(targetUser, limit);
            return Ok(new { items = shows });
        }

        [HttpGet("history/movies")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult> GetMovies(
            [FromQuery] int limit = 30,
            [FromQuery] bool onlyFinished = true,
            [FromQuery] string userId = null)
        {
            var targetUser = ResolveTargetUserId(userId);
            if (targetUser == null) return Forbid();
            if (limit <= 0 || limit > 500) limit = 30;

            var store = JournalAccessor.TryOpen();
            if (store == null) return NotFound(new { error = "Journal not initialised. Run pull-task first." });

            var movies = await store.GetMovies(targetUser, limit, onlyFinished);
            return Ok(new { items = movies });
        }

        [HttpGet("library/movies/unscrobblable")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult GetUnscrobblableMovies([FromQuery] string userId = null)
        {
            var scopeUser = ResolveScopeUser(userId);

            var query = scopeUser != null
                ? new InternalItemsQuery(scopeUser)
                : new InternalItemsQuery();
            query.IncludeItemTypes = new[] { BaseItemKind.Movie };
            query.Recursive = true;
            query.IsVirtualItem = false;

            var allMovies = _libraryManager.GetItemList(query).OfType<Movie>().ToList();

            var missing = new List<UnscrobblableMovie>();
            var kpOnly = new List<UnscrobblableMovie>();

            foreach (var movie in allMovies)
            {
                var tmdb = movie.GetTmdbId();
                var kp = movie.GetKinopoiskId();
                if (!string.IsNullOrEmpty(tmdb)) continue;

                var dto = new UnscrobblableMovie
                {
                    ItemId = movie.Id.ToString("N"),
                    Title = movie.Name,
                    OriginalTitle = movie.OriginalTitle,
                    Year = movie.ProductionYear,
                    Path = movie.Path,
                    KinopoiskId = kp,
                };

                if (string.IsNullOrEmpty(kp)) missing.Add(dto);
                else kpOnly.Add(dto);
            }

            missing.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            kpOnly.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));

            return Ok(new
            {
                total = allMovies.Count,
                noExternalId = missing.Count,
                kinopoiskOnly = kpOnly.Count,
                noExternalIdItems = missing,
                kinopoiskOnlyItems = kpOnly,
            });
        }

        public class UnscrobblableMovie
        {
            public string ItemId { get; set; }
            public string Title { get; set; }
            public string OriginalTitle { get; set; }
            public int? Year { get; set; }
            public string Path { get; set; }
            public string KinopoiskId { get; set; }
        }

        [HttpGet("library/shows/unscrobblable")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult GetUnscrobblableShows([FromQuery] string userId = null)
        {
            var scopeUser = ResolveScopeUser(userId);

            var query = scopeUser != null
                ? new InternalItemsQuery(scopeUser)
                : new InternalItemsQuery();
            query.IncludeItemTypes = new[] { BaseItemKind.Series };
            query.Recursive = true;
            query.IsVirtualItem = false;

            var allShows = _libraryManager.GetItemList(query).OfType<Series>().ToList();

            var missing = new List<UnscrobblableShow>();
            var kpOnly = new List<UnscrobblableShow>();

            foreach (var show in allShows)
            {
                var imdb = show.GetProviderId(MediaBrowser.Model.Entities.MetadataProvider.Imdb);
                var tvdb = show.GetProviderId(MediaBrowser.Model.Entities.MetadataProvider.Tvdb);
                var tvrage = show.GetProviderId(MediaBrowser.Model.Entities.MetadataProvider.TvRage);
                var tvmaze = show.GetProviderId(MediaBrowser.Model.Entities.MetadataProvider.TvMaze);
                var hasUpstream =
                    !string.IsNullOrEmpty(imdb)
                    || !string.IsNullOrEmpty(tvdb)
                    || !string.IsNullOrEmpty(tvrage)
                    || !string.IsNullOrEmpty(tvmaze);
                if (hasUpstream) continue;

                var kp = show.GetKinopoiskId();
                var dto = new UnscrobblableShow
                {
                    ItemId = show.Id.ToString("N"),
                    Title = show.Name,
                    OriginalTitle = show.OriginalTitle,
                    Year = show.ProductionYear,
                    Path = show.Path,
                    KinopoiskId = kp,
                };

                if (string.IsNullOrEmpty(kp)) missing.Add(dto);
                else kpOnly.Add(dto);
            }

            missing.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            kpOnly.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));

            return Ok(new
            {
                total = allShows.Count,
                noExternalId = missing.Count,
                kinopoiskOnly = kpOnly.Count,
                noExternalIdItems = missing,
                kinopoiskOnlyItems = kpOnly,
            });
        }

        public class UnscrobblableShow
        {
            public string ItemId { get; set; }
            public string Title { get; set; }
            public string OriginalTitle { get; set; }
            public int? Year { get; set; }
            public string Path { get; set; }
            public string KinopoiskId { get; set; }
        }

        private Jellyfin.Database.Implementations.Entities.User ResolveScopeUser(string requested)
        {
            var requestedNorm = NormaliseUserId(requested);
            if (TryLookupUser(requestedNorm, out var requestedUser)) return requestedUser;

            var caller = CurrentUserId();
            if (TryLookupUser(caller, out var callerUser)) return callerUser;

            foreach (var uc in Plugin.Instance.PluginConfiguration.Users ?? Array.Empty<MyShows.Configuration.UserConfig>())
            {
                if (TryLookupUser(uc?.Id, out var configUser)) return configUser;
            }

            return null;
        }

        private bool TryLookupUser(string raw, out Jellyfin.Database.Implementations.Entities.User user)
        {
            user = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            if (!Guid.TryParseExact(raw, "N", out var guid) && !Guid.TryParse(raw, out guid)) return false;
            if (guid == Guid.Empty) return false;
            user = _userManager.GetUserById(guid);
            return user != null;
        }

        private string ResolveTargetUserId(string requested)
        {
            var caller = CurrentUserId();
            if (caller == null) return null;

            if (string.IsNullOrWhiteSpace(requested)) return caller;

            var requestedNorm = NormaliseUserId(requested);
            if (requestedNorm == null) return null;
            if (requestedNorm == caller) return caller;
            if (IsAdmin()) return requestedNorm;
            return null;
        }

        private string CurrentUserId()
        {
            var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? User.FindFirst("Jellyfin-UserId")?.Value;
            return NormaliseUserId(raw);
        }

        private bool IsAdmin() =>
            User.IsInRole("Administrator")
            || string.Equals(User.FindFirst("Jellyfin-IsAdmin")?.Value, "true", StringComparison.OrdinalIgnoreCase);

        private static string NormaliseUserId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return Guid.TryParse(raw, out var guid) ? guid.ToString("N").ToLowerInvariant() : null;
        }
    }
}
