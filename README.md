# jellyfin-plugin-myshows (slartus fork)

MyShows.me integration for Jellyfin.

This is a fork of [shemanaev/jellyfin-plugin-myshows](https://github.com/shemanaev/jellyfin-plugin-myshows) with extra features:

- **Movies scrobbling** via TMDb id **or** KinoPoisk id (upstream only handles episodes; only TMDb in pre-5.4.2).
- **MyShows API v3 RPC** endpoint (`myshows.me/v3/rpc/`) with `Authorization2: Bearer ...` header. Upstream still uses v2 (`api.myshows.me/v2/rpc/`).
- **Pull-sync scheduled task** — backfills `Played` state in Jellyfin from MyShows.me for opted-in users (`UserConfig.PullWatchedFromMyShows = true`). Runs manually from Dashboard → Scheduled Tasks → category "MyShows".
- Built against **.NET 9** / **Jellyfin 10.11.x** (upstream is on .NET 8 / Jellyfin 10.9.x).

## Installation

Build locally (`dotnet build -c Release`) and drop `MyShows.dll` into a new folder under `<jellyfin-config>/plugins/MyShows_<version>/` together with `meta.json` and `myshows.png`.

The upstream `jellyfin-plugin-repo` does not host this fork; install manually.

## Configuration

Plugin configuration is tied to a Jellyfin user — each user has their own MyShows credentials. Admin rights are required to configure the plugin from the dashboard.

To opt-in to pull-sync, set `<PullWatchedFromMyShows>true</PullWatchedFromMyShows>` for the desired `UserConfig` in `<jellyfin-config>/plugins/configurations/MyShows.xml`. Default is `false` to avoid surprises on upgrade.

### Movies — requirements

Movies are matched by **TMDb id** first, then **KinoPoisk id** as fallback (since 5.4.2). MyShows `movies.AddExternalMovie` accepts `source` values `tmdb`, `imdb`, `kinopoisk` (this fork uses `tmdb` and `kinopoisk`). Make sure at least one of TMDb / KinoPoisk metadata providers is enabled and matches your movies.

If neither provider id is present, the plugin silently skips scrobbling (since both `OnPlaybackProgress` and `OnPlaybackStopped` need an external id to map the Jellyfin movie to a MyShows.me record). Use the REST endpoint below to find such movies.

### REST endpoints

All under `Authorization` (Jellyfin `X-Emby-Token` or session token).

- `GET /MyShows/v1/history/stats?topShows=N&userId=<guid>` — stats from the local SQLite journal (see [Pull-sync](#pull-sync)).
- `GET /MyShows/v1/history/episodes/recent?limit=N&userId=<guid>` — latest watched episodes.
- `GET /MyShows/v1/history/shows/recent?limit=N&userId=<guid>` — latest watched shows.
- `GET /MyShows/v1/history/movies?limit=N&onlyFinished=true&userId=<guid>` — watched movies.
- `GET /MyShows/v1/library/movies/unscrobblable` — movies in your library that MyShows/Trakt cannot scrobble (no TMDb id) plus movies that only have a KinoPoisk id (scrobble-able since 5.4.2 but Trakt will still skip them). Useful for monitoring identification gaps.

### Network access to MyShows

`myshows.me` may be blocked by DPI/SNI filtering at some ISPs. If Jellyfin can't reach the host, route the container through a proxy (e.g. xray, sing-box) via `HTTP_PROXY`/`HTTPS_PROXY` environment variables.

## Debugging

Define the `JellyfinHome` environment variable pointing to a Jellyfin distribution to be able to run the debug configuration.
