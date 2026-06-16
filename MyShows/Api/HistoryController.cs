using System;
using System.Net.Mime;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyShows.Journal;

namespace MyShows.Api
{
    [ApiController]
    [Authorize]
    [Route("MyShows/v1/history")]
    [Produces(MediaTypeNames.Application.Json)]
    public class HistoryController : ControllerBase
    {
        [HttpGet("stats")]
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

        [HttpGet("episodes/recent")]
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

        [HttpGet("shows/recent")]
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

        [HttpGet("movies")]
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
