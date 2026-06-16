# jellyfin-plugin-myshows (slartus fork)

MyShows.me integration for Jellyfin.

This is a fork of [shemanaev/jellyfin-plugin-myshows](https://github.com/shemanaev/jellyfin-plugin-myshows) with extra features:

- **Movies scrobbling** via TMDb provider id (upstream only handles episodes).
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

Movies are matched by **TMDb id only** — MyShows does not accept other movie ID sources via `movies.AddExternalMovie`. Make sure Jellyfin's TMDb metadata provider is enabled and your movies have a TMDb provider id.

### Network access to MyShows

`myshows.me` may be blocked by DPI/SNI filtering at some ISPs. If Jellyfin can't reach the host, route the container through a proxy (e.g. xray, sing-box) via `HTTP_PROXY`/`HTTPS_PROXY` environment variables.

## Debugging

Define the `JellyfinHome` environment variable pointing to a Jellyfin distribution to be able to run the debug configuration.
