using System;
using System.Collections.Generic;

namespace MyShows.Journal
{
    internal class JournalShow
    {
        public int ShowId { get; set; }
        public string Title { get; set; }
        public string TitleOriginal { get; set; }
        public int? Year { get; set; }
        public string Status { get; set; }
        public string WatchStatus { get; set; }
        public int WatchedEpisodes { get; set; }
        public int TotalEpisodes { get; set; }
        public double? Rating { get; set; }
    }

    internal class JournalEpisode
    {
        public int EpisodeId { get; set; }
        public int ShowId { get; set; }
        public int SeasonNumber { get; set; }
        public int EpisodeNumber { get; set; }
        public DateTimeOffset? WatchDate { get; set; }
        public double? Rating { get; set; }
        public bool IsFavorite { get; set; }
    }

    internal class JournalMovie
    {
        public int MovieId { get; set; }
        public string TmdbId { get; set; }
        public string Title { get; set; }
        public string WatchStatus { get; set; }
        public DateTimeOffset? WatchDate { get; set; }
        public double? Rating { get; set; }
    }

    public class HistoryStats
    {
        public string JellyfinUserId { get; set; }
        public int ShowsTotal { get; set; }
        public int ShowsFinished { get; set; }
        public int ShowsWatching { get; set; }
        public int EpisodesWatched { get; set; }
        public int EpisodesAired { get; set; }
        public int MoviesWatched { get; set; }
        public long LastSyncedAtUnix { get; set; }
        public IReadOnlyList<HistoryStatsTopShow> TopShows { get; set; }
    }

    public class HistoryStatsTopShow
    {
        public int ShowId { get; set; }
        public string Title { get; set; }
        public int? Year { get; set; }
        public int WatchedEpisodes { get; set; }
        public int TotalEpisodes { get; set; }
        public string WatchStatus { get; set; }
    }

    public class RecentEpisode
    {
        public int ShowId { get; set; }
        public string ShowTitle { get; set; }
        public int SeasonNumber { get; set; }
        public int EpisodeNumber { get; set; }
        public string WatchDate { get; set; }
    }
}
