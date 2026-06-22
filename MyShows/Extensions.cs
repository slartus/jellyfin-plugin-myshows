using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Model.Entities;

namespace MyShows
{
    public static class Extensions
    {
        public static (int, string) GetBestProviderId(this IHasProviderIds item)
        {
            var imdb = item.GetProviderId(MetadataProvider.Imdb);
            if (!string.IsNullOrEmpty(imdb)) return (int.Parse(imdb.Replace("tt", "")), "imdb");

            var tvrage = item.GetProviderId(MetadataProvider.TvRage);
            if (!string.IsNullOrEmpty(tvrage)) return (int.Parse(tvrage), "tvrage");

            var tvdb = item.GetProviderId(MetadataProvider.Tvdb);
            if (!string.IsNullOrEmpty(tvdb)) return (int.Parse(tvdb), "thetvdb");

            var tvmaze = item.GetProviderId(MetadataProvider.TvMaze);
            if (!string.IsNullOrEmpty(tvmaze)) return (int.Parse(tvmaze), "tvmaze");

            return (-1, null);
        }

        public static string GetTmdbId(this IHasProviderIds item)
        {
            var tmdb = item.GetProviderId(MetadataProvider.Tmdb);
            return string.IsNullOrEmpty(tmdb) ? null : tmdb;
        }

        public static string GetKinopoiskId(this IHasProviderIds item)
        {
            var kp = item.GetProviderId("kinopoisk");
            return string.IsNullOrEmpty(kp) ? null : kp;
        }

        public static (string id, string source) GetBestMovieExternalId(this IHasProviderIds item)
        {
            var tmdb = item.GetTmdbId();
            if (!string.IsNullOrEmpty(tmdb)) return (tmdb, "tmdb");

            var kp = item.GetKinopoiskId();
            if (!string.IsNullOrEmpty(kp)) return (kp, "kinopoisk");

            return (null, null);
        }

        public static async Task<T> DeserializeFromHttp<T>(HttpResponseMessage response)
        {
            var contentStream = await response.Content.ReadAsStreamAsync();
            var result = await JsonSerializer.DeserializeAsync<T>(contentStream, JsonDefaults.Options);
            return result;
        }
    }
}
