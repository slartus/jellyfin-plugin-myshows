using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MyShows.MyShowsApi;

namespace MyShows.Configuration
{
    public class UserConfig
    {
        /// <summary>
        /// Jellyfin's user id (Guid).
        /// </summary>
        public string Id { get; set; }

        public string Name { get; set; }

        public MyShowsApiVersion ApiVersion { get; set; }

        /// <summary>
        /// OAuth access token. Or PHPSESSID cookie.
        /// </summary>
        public string AccessToken { get; set; }

        /// <summary>
        /// OAuth refresh token.
        /// </summary>
        public string RefreshToken { get; set; }

        /// <summary>
        /// OAuth expires_in.
        /// </summary>
        public DateTime ExpirationTime { get; set; }

        /// <summary>
        /// Percentage after which to scrobble.
        /// </summary>
        public int ScrobbleAt { get; set; } = 90;

        /// <summary>
        /// Opt-in: pull watched state from MyShows back into Jellyfin (via scheduled task).
        /// </summary>
        public bool PullWatchedFromMyShows { get; set; } = false;

        /// <summary>
        /// Why MyShows rejected the stored credentials (null while they are usable).
        /// Set only on a definitive OAuth rejection; cleared by re-linking the account.
        /// </summary>
        public string AuthError { get; set; }

        public DateTime? AuthErrorAt { get; set; }

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> RefreshLocks = new();

        /// <summary>
        /// Ensure OAuth access_token is valid and refresh it if it's not.
        /// The user is never removed here: transient failures are retried on the next call,
        /// a definitive rejection is persisted in <see cref="AuthError"/>.
        /// </summary>
        /// <returns>true if you can use token</returns>
        public async Task<bool> EnsureAccessTokenValid(HttpClient http, ILogger logger)
        {
            if (AuthError != null) return false;
            if (DateTime.Compare(ExpirationTime, DateTime.Now) >= 0) return true;

            var refreshLock = RefreshLocks.GetOrAdd(Id, _ => new SemaphoreSlim(1, 1));
            await refreshLock.WaitAsync();
            try
            {
                var current = Plugin.Instance.Configuration.Users.FirstOrDefault(u => u.Id == Id) ?? this;
                if (current != this) CopyAuthStateFrom(current);
                if (AuthError != null) return false;
                if (DateTime.Compare(ExpirationTime, DateTime.Now) >= 0) return true;

                var usedRefreshToken = RefreshToken;
                OAuthToken token;
                OAuthError error;
                try
                {
                    (token, error) = await OAuthHelper.RefreshToken(http, usedRefreshToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "MyShows: token refresh for {0} failed, will retry later", Name);
                    return false;
                }

                // The configuration may have been replaced (saved from the dashboard or re-linked) during the request.
                current = Plugin.Instance.Configuration.Users.FirstOrDefault(u => u.Id == Id) ?? current;
                if (current.RefreshToken != usedRefreshToken)
                {
                    CopyAuthStateFrom(current);
                    return AuthError == null && DateTime.Compare(ExpirationTime, DateTime.Now) >= 0;
                }

                if (error != null)
                {
                    if (error.error != InvalidGrant)
                    {
                        logger.LogWarning("MyShows: token refresh for {0} rejected ({1}: {2}), will retry later", Name, error.error, error.error_description);
                        return false;
                    }

                    current.AuthError = $"{error.error}: {error.error_description}";
                    current.AuthErrorAt = DateTime.Now;
                    CopyAuthStateFrom(current);
                    Plugin.Instance.SaveConfiguration();
                    logger.LogError("MyShows: credentials of {0} were revoked ({1}). Scrobbling is disabled until the account is re-linked in plugin settings", Name, AuthError);
                    return false;
                }

                if (string.IsNullOrEmpty(token?.access_token))
                {
                    logger.LogWarning("MyShows: token refresh for {0} returned no token, will retry later", Name);
                    return false;
                }

                current.AccessToken = token.access_token;
                current.RefreshToken = token.refresh_token;
                current.ExpirationTime = DateTime.Now.AddSeconds(token.expires_in);
                CopyAuthStateFrom(current);
                Plugin.Instance.SaveConfiguration();
                return true;
            }
            finally
            {
                refreshLock.Release();
            }
        }

        private const string InvalidGrant = "invalid_grant";

        private void CopyAuthStateFrom(UserConfig other)
        {
            if (other == this) return;
            AccessToken = other.AccessToken;
            RefreshToken = other.RefreshToken;
            ExpirationTime = other.ExpirationTime;
            AuthError = other.AuthError;
            AuthErrorAt = other.AuthErrorAt;
        }
    }
}
