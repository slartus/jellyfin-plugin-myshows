using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace MyShows.Journal
{
    internal class JournalStore
    {
        private readonly string _connectionString;

        public JournalStore(string dbPath)
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _connectionString = $"Data Source={dbPath}";
            EnsureSchema();
        }

        private void EnsureSchema()
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
PRAGMA journal_mode=WAL;

CREATE TABLE IF NOT EXISTS myshows_shows (
    show_id           INTEGER NOT NULL,
    jellyfin_user_id  TEXT    NOT NULL,
    title             TEXT    NOT NULL,
    title_original    TEXT,
    year              INTEGER,
    status            TEXT,
    watch_status      TEXT,
    watched_episodes  INTEGER NOT NULL DEFAULT 0,
    total_episodes    INTEGER NOT NULL DEFAULT 0,
    rating            REAL,
    last_synced_at    INTEGER NOT NULL,
    PRIMARY KEY (show_id, jellyfin_user_id)
);

CREATE TABLE IF NOT EXISTS myshows_episodes (
    episode_id        INTEGER NOT NULL,
    jellyfin_user_id  TEXT    NOT NULL,
    show_id           INTEGER NOT NULL,
    season_number     INTEGER NOT NULL,
    episode_number    INTEGER NOT NULL,
    watch_date        TEXT,
    rating            REAL,
    is_favorite       INTEGER NOT NULL DEFAULT 0,
    last_synced_at    INTEGER NOT NULL,
    PRIMARY KEY (episode_id, jellyfin_user_id)
);

CREATE TABLE IF NOT EXISTS myshows_movies (
    movie_id          INTEGER NOT NULL,
    jellyfin_user_id  TEXT    NOT NULL,
    tmdb_id           TEXT,
    title             TEXT,
    watch_status      TEXT,
    watch_date        TEXT,
    rating            REAL,
    last_synced_at    INTEGER NOT NULL,
    PRIMARY KEY (movie_id, jellyfin_user_id)
);

CREATE INDEX IF NOT EXISTS idx_eps_show_user
    ON myshows_episodes(show_id, jellyfin_user_id);
CREATE INDEX IF NOT EXISTS idx_eps_user_watch
    ON myshows_episodes(jellyfin_user_id, watch_date DESC);
";
            cmd.ExecuteNonQuery();
        }

        public async Task UpsertShows(string userId, IReadOnlyList<JournalShow> shows)
        {
            if (shows.Count == 0) return;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await using var conn = Open();
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO myshows_shows (show_id, jellyfin_user_id, title, title_original, year, status,
                            watch_status, watched_episodes, total_episodes, rating, last_synced_at)
VALUES ($show_id, $user, $title, $title_original, $year, $status,
        $watch_status, $we, $te, $rating, $now)
ON CONFLICT(show_id, jellyfin_user_id) DO UPDATE SET
    title            = excluded.title,
    title_original   = excluded.title_original,
    year             = excluded.year,
    status           = excluded.status,
    watch_status     = excluded.watch_status,
    watched_episodes = excluded.watched_episodes,
    total_episodes   = excluded.total_episodes,
    rating           = excluded.rating,
    last_synced_at   = excluded.last_synced_at;";

            var pShow = cmd.CreateParameter(); pShow.ParameterName = "$show_id"; cmd.Parameters.Add(pShow);
            var pUser = cmd.CreateParameter(); pUser.ParameterName = "$user"; cmd.Parameters.Add(pUser);
            var pTitle = cmd.CreateParameter(); pTitle.ParameterName = "$title"; cmd.Parameters.Add(pTitle);
            var pTitleOrig = cmd.CreateParameter(); pTitleOrig.ParameterName = "$title_original"; cmd.Parameters.Add(pTitleOrig);
            var pYear = cmd.CreateParameter(); pYear.ParameterName = "$year"; cmd.Parameters.Add(pYear);
            var pStatus = cmd.CreateParameter(); pStatus.ParameterName = "$status"; cmd.Parameters.Add(pStatus);
            var pWatchStatus = cmd.CreateParameter(); pWatchStatus.ParameterName = "$watch_status"; cmd.Parameters.Add(pWatchStatus);
            var pWe = cmd.CreateParameter(); pWe.ParameterName = "$we"; cmd.Parameters.Add(pWe);
            var pTe = cmd.CreateParameter(); pTe.ParameterName = "$te"; cmd.Parameters.Add(pTe);
            var pRating = cmd.CreateParameter(); pRating.ParameterName = "$rating"; cmd.Parameters.Add(pRating);
            var pNow = cmd.CreateParameter(); pNow.ParameterName = "$now"; cmd.Parameters.Add(pNow);
            pNow.Value = now;
            pUser.Value = userId;

            foreach (var s in shows)
            {
                pShow.Value = s.ShowId;
                pTitle.Value = s.Title ?? string.Empty;
                pTitleOrig.Value = (object)s.TitleOriginal ?? DBNull.Value;
                pYear.Value = (object)s.Year ?? DBNull.Value;
                pStatus.Value = (object)s.Status ?? DBNull.Value;
                pWatchStatus.Value = (object)s.WatchStatus ?? DBNull.Value;
                pWe.Value = s.WatchedEpisodes;
                pTe.Value = s.TotalEpisodes;
                pRating.Value = (object)s.Rating ?? DBNull.Value;
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }

        public async Task UpsertEpisodes(string userId, int showId, IReadOnlyList<JournalEpisode> episodes)
        {
            if (episodes.Count == 0) return;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await using var conn = Open();
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO myshows_episodes (episode_id, jellyfin_user_id, show_id, season_number,
                              episode_number, watch_date, rating, is_favorite, last_synced_at)
VALUES ($eid, $user, $show, $season, $ep, $when, $rating, $fav, $now)
ON CONFLICT(episode_id, jellyfin_user_id) DO UPDATE SET
    show_id        = excluded.show_id,
    season_number  = excluded.season_number,
    episode_number = excluded.episode_number,
    watch_date     = excluded.watch_date,
    rating         = excluded.rating,
    is_favorite    = excluded.is_favorite,
    last_synced_at = excluded.last_synced_at;";

            var pEid = cmd.CreateParameter(); pEid.ParameterName = "$eid"; cmd.Parameters.Add(pEid);
            var pUser = cmd.CreateParameter(); pUser.ParameterName = "$user"; cmd.Parameters.Add(pUser);
            var pShow = cmd.CreateParameter(); pShow.ParameterName = "$show"; cmd.Parameters.Add(pShow);
            var pSeason = cmd.CreateParameter(); pSeason.ParameterName = "$season"; cmd.Parameters.Add(pSeason);
            var pEp = cmd.CreateParameter(); pEp.ParameterName = "$ep"; cmd.Parameters.Add(pEp);
            var pWhen = cmd.CreateParameter(); pWhen.ParameterName = "$when"; cmd.Parameters.Add(pWhen);
            var pRating = cmd.CreateParameter(); pRating.ParameterName = "$rating"; cmd.Parameters.Add(pRating);
            var pFav = cmd.CreateParameter(); pFav.ParameterName = "$fav"; cmd.Parameters.Add(pFav);
            var pNow = cmd.CreateParameter(); pNow.ParameterName = "$now"; cmd.Parameters.Add(pNow);
            pNow.Value = now;
            pUser.Value = userId;
            pShow.Value = showId;

            foreach (var e in episodes)
            {
                pEid.Value = e.EpisodeId;
                pSeason.Value = e.SeasonNumber;
                pEp.Value = e.EpisodeNumber;
                pWhen.Value = e.WatchDate.HasValue
                    ? (object)e.WatchDate.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                    : DBNull.Value;
                pRating.Value = (object)e.Rating ?? DBNull.Value;
                pFav.Value = e.IsFavorite ? 1 : 0;
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }

        public async Task UpsertMovies(string userId, IReadOnlyList<JournalMovie> movies)
        {
            if (movies.Count == 0) return;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await using var conn = Open();
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO myshows_movies (movie_id, jellyfin_user_id, tmdb_id, title, watch_status,
                            watch_date, rating, last_synced_at)
VALUES ($mid, $user, $tmdb, $title, $ws, $when, $rating, $now)
ON CONFLICT(movie_id, jellyfin_user_id) DO UPDATE SET
    tmdb_id        = excluded.tmdb_id,
    title          = excluded.title,
    watch_status   = excluded.watch_status,
    watch_date     = excluded.watch_date,
    rating         = excluded.rating,
    last_synced_at = excluded.last_synced_at;";

            var pMid = cmd.CreateParameter(); pMid.ParameterName = "$mid"; cmd.Parameters.Add(pMid);
            var pUser = cmd.CreateParameter(); pUser.ParameterName = "$user"; cmd.Parameters.Add(pUser);
            var pTmdb = cmd.CreateParameter(); pTmdb.ParameterName = "$tmdb"; cmd.Parameters.Add(pTmdb);
            var pTitle = cmd.CreateParameter(); pTitle.ParameterName = "$title"; cmd.Parameters.Add(pTitle);
            var pWs = cmd.CreateParameter(); pWs.ParameterName = "$ws"; cmd.Parameters.Add(pWs);
            var pWhen = cmd.CreateParameter(); pWhen.ParameterName = "$when"; cmd.Parameters.Add(pWhen);
            var pRating = cmd.CreateParameter(); pRating.ParameterName = "$rating"; cmd.Parameters.Add(pRating);
            var pNow = cmd.CreateParameter(); pNow.ParameterName = "$now"; cmd.Parameters.Add(pNow);
            pNow.Value = now;
            pUser.Value = userId;

            foreach (var m in movies)
            {
                pMid.Value = m.MovieId;
                pTmdb.Value = (object)m.TmdbId ?? DBNull.Value;
                pTitle.Value = (object)m.Title ?? DBNull.Value;
                pWs.Value = (object)m.WatchStatus ?? DBNull.Value;
                pWhen.Value = m.WatchDate.HasValue
                    ? (object)m.WatchDate.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                    : DBNull.Value;
                pRating.Value = (object)m.Rating ?? DBNull.Value;
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }

        public async Task<HistoryStats> GetStats(string userId, int topShows = 10)
        {
            await using var conn = Open();

            var stats = new HistoryStats { JellyfinUserId = userId };

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT
    COUNT(*) AS shows_total,
    SUM(CASE WHEN watch_status = 'finished' THEN 1 ELSE 0 END) AS shows_finished,
    SUM(CASE WHEN watch_status = 'watching' THEN 1 ELSE 0 END) AS shows_watching,
    COALESCE(SUM(watched_episodes), 0) AS eps_watched,
    COALESCE(SUM(total_episodes), 0)   AS eps_aired,
    COALESCE(MAX(last_synced_at), 0)   AS last_sync
FROM myshows_shows
WHERE jellyfin_user_id = $user;";
                cmd.Parameters.AddWithValue("$user", userId);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    stats.ShowsTotal = r.GetInt32(0);
                    stats.ShowsFinished = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                    stats.ShowsWatching = r.IsDBNull(2) ? 0 : r.GetInt32(2);
                    stats.EpisodesWatched = r.GetInt32(3);
                    stats.EpisodesAired = r.GetInt32(4);
                    stats.LastSyncedAtUnix = r.GetInt64(5);
                }
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT COUNT(*) FROM myshows_movies
WHERE jellyfin_user_id = $user AND watch_status = 'finished';";
                cmd.Parameters.AddWithValue("$user", userId);
                stats.MoviesWatched = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
            }

            var top = new List<HistoryStatsTopShow>(topShows);
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT show_id, title, year, watched_episodes, total_episodes, watch_status
FROM myshows_shows
WHERE jellyfin_user_id = $user
ORDER BY watched_episodes DESC, title ASC
LIMIT $limit;";
                cmd.Parameters.AddWithValue("$user", userId);
                cmd.Parameters.AddWithValue("$limit", topShows);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    top.Add(new HistoryStatsTopShow
                    {
                        ShowId = r.GetInt32(0),
                        Title = r.GetString(1),
                        Year = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
                        WatchedEpisodes = r.GetInt32(3),
                        TotalEpisodes = r.GetInt32(4),
                        WatchStatus = r.IsDBNull(5) ? null : r.GetString(5),
                    });
                }
            }
            stats.TopShows = top;
            return stats;
        }

        public async Task<IReadOnlyDictionary<(int Season, int Episode), DateTimeOffset?>> GetWatchedEpisodesForShow(string userId, int showId)
        {
            var result = new Dictionary<(int, int), DateTimeOffset?>();
            await using var conn = Open();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT season_number, episode_number, watch_date
FROM myshows_episodes
WHERE jellyfin_user_id = $user AND show_id = $show;";
            cmd.Parameters.AddWithValue("$user", userId);
            cmd.Parameters.AddWithValue("$show", showId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var key = (r.GetInt32(0), r.GetInt32(1));
                DateTimeOffset? when = null;
                if (!r.IsDBNull(2)
                    && DateTimeOffset.TryParse(r.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                {
                    when = dt;
                }
                result[key] = when;
            }
            return result;
        }

        public async Task<IReadOnlyList<RecentEpisode>> GetRecentEpisodes(string userId, int limit)
        {
            var result = new List<RecentEpisode>(limit);
            await using var conn = Open();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT e.show_id, s.title, e.season_number, e.episode_number, e.watch_date
FROM myshows_episodes e
LEFT JOIN myshows_shows s
       ON s.show_id = e.show_id AND s.jellyfin_user_id = e.jellyfin_user_id
WHERE e.jellyfin_user_id = $user AND e.watch_date IS NOT NULL
ORDER BY e.watch_date DESC
LIMIT $limit;";
            cmd.Parameters.AddWithValue("$user", userId);
            cmd.Parameters.AddWithValue("$limit", limit);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                result.Add(new RecentEpisode
                {
                    ShowId = r.GetInt32(0),
                    ShowTitle = r.IsDBNull(1) ? "?" : r.GetString(1),
                    SeasonNumber = r.GetInt32(2),
                    EpisodeNumber = r.GetInt32(3),
                    WatchDate = r.IsDBNull(4) ? null : r.GetString(4),
                });
            }
            return result;
        }

        private SqliteConnection Open()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            return conn;
        }
    }
}
