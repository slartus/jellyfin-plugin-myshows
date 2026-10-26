using System;
using System.Security.Claims;

namespace MyShows.Api
{
    /// <summary>
    /// Which Jellyfin user a request may act on: the caller itself, or anyone when the caller is an administrator
    /// (API keys authenticate as administrator with an empty user id).
    /// </summary>
    internal static class CallerScope
    {
        public static string ResolveTargetUserId(ClaimsPrincipal caller, string requested)
        {
            var callerId = CurrentUserId(caller);
            if (callerId == null) return null;

            if (string.IsNullOrWhiteSpace(requested)) return callerId;

            var requestedNorm = NormaliseUserId(requested);
            if (requestedNorm == null) return null;
            if (requestedNorm == callerId) return callerId;
            if (IsAdmin(caller)) return requestedNorm;
            return null;
        }

        public static string CurrentUserId(ClaimsPrincipal caller)
        {
            var raw = caller.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? caller.FindFirst("Jellyfin-UserId")?.Value;
            return NormaliseUserId(raw);
        }

        public static bool IsAdmin(ClaimsPrincipal caller) =>
            caller.IsInRole("Administrator")
            || string.Equals(caller.FindFirst("Jellyfin-IsAdmin")?.Value, "true", StringComparison.OrdinalIgnoreCase);

        public static string NormaliseUserId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return Guid.TryParse(raw, out var guid) ? guid.ToString("N").ToLowerInvariant() : null;
        }
    }
}
