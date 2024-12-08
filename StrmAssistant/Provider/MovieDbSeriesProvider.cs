using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using StrmAssistant.Mod;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Provider
{
    public class MovieDbSeriesProvider : ISeriesMetadataProvider
    {
        private readonly ILogger _logger;
        private readonly IHttpClient _httpClient;
        private readonly IJsonSerializer _jsonSerializer;

        private const int RequestIntervalMs = 100;
        private static long _lastRequestTicks;

        public MovieDbSeriesProvider(IJsonSerializer jsonSerializer, IHttpClient httpClient)
        {
            _logger = Plugin.Instance.Logger;
            _httpClient = httpClient;
            _jsonSerializer = jsonSerializer;
        }

        public string Name => "TheMovieDb";

        public async Task<RemoteSearchResult[]> GetAllEpisodes(SeriesInfo seriesInfo,
            CancellationToken cancellationToken)
        {
            var tmdbId = seriesInfo.GetProviderId(MetadataProviders.Tmdb);
            var language = seriesInfo.MetadataLanguage;

            if (string.IsNullOrEmpty(tmdbId))
                return Array.Empty<RemoteSearchResult>();

            var seriesUrl = BuildApiUrl($"tv/{tmdbId}", language);
            var seriesInfoResponse = await GetMovieDbResponse<SeriesResponseInfo>(seriesUrl, cancellationToken)
                .ConfigureAwait(false);
            if (seriesInfoResponse?.seasons == null)
                return Array.Empty<RemoteSearchResult>();

            var episodesResult = new List<RemoteSearchResult>();

            var seasonTasks = seriesInfoResponse.seasons
                .Select(s => FetchSeasonEpisodesAsync(tmdbId, s.season_number, language, cancellationToken))
                .ToArray();

            var seasonResults = await Task.WhenAll(seasonTasks).ConfigureAwait(false);
            foreach (var episodes in seasonResults)
            {
                if (episodes != null)
                    episodesResult.AddRange(episodes);
            }

            return episodesResult.ToArray();
        }

        private async Task<List<RemoteSearchResult>> FetchSeasonEpisodesAsync(
            string tmdbId, int seasonNumber, string language, CancellationToken cancellationToken)
        {
            var seasonUrl = BuildApiUrl($"tv/{tmdbId}/season/{seasonNumber}", language);
            var seasonInfo = await GetMovieDbResponse<SeasonResponseInfo>(seasonUrl, cancellationToken);

            if (seasonInfo?.episodes == null)
                return new List<RemoteSearchResult>();

            return (from episode in seasonInfo.episodes
                let providerIds = new ProviderIdDictionary
                    { { MetadataProviders.Tmdb.ToString(), episode.id.ToString(CultureInfo.InvariantCulture) } }
                select new RemoteSearchResult
                {
                    SearchProviderName = Name,
                    IndexNumber = episode.episode_number,
                    ParentIndexNumber = episode.season_number,
                    Name = episode.name,
                    Overview = episode.overview,
                    PremiereDate = episode.air_date,
                    ProductionYear = episode.air_date.Year,
                    ProviderIds = providerIds
                }).ToList();
        }

        private async Task<T> GetMovieDbResponse<T>(string url, CancellationToken cancellationToken)
        {
            var num = Math.Min(
                (RequestIntervalMs * 10000 - (DateTimeOffset.UtcNow.Ticks - _lastRequestTicks)) / 10000L,
                RequestIntervalMs);
            if (num > 0L)
            {
                _logger.Debug("Throttling Tmdb by {0} ms", num);
                await Task.Delay(Convert.ToInt32(num)).ConfigureAwait(false);
            }

            _lastRequestTicks = DateTimeOffset.UtcNow.Ticks;

            var options = new HttpRequestOptions
            {
                Url = url,
                CancellationToken = cancellationToken,
                AcceptHeader = "application/json",
                BufferContent = true,
                UserAgent = Plugin.Instance.UserAgent
            };

            using var response = await _httpClient.SendAsync(options, "GET").ConfigureAwait(false);
            await using var contentStream = response.Content;

            return _jsonSerializer.DeserializeFromStream<T>(contentStream);
        }

        private static string BuildApiUrl(string endpoint, string language)
        {
            var url =
                $"{AltMovieDbConfig.CurrentMovieDbApiUrl}/3/{endpoint}?api_key={AltMovieDbConfig.CurrentMovieDbApiKey}";
            if (!string.IsNullOrEmpty(language))
                url += $"&language={language}";
            return url;
        }

        internal class SeriesResponseInfo
        {
            public List<SeasonResponseInfo> seasons { get; set; }
        }

        internal class SeasonResponseInfo
        {
            public int id { get; set; }

            public int season_number { get; set; }

            public List<EpisodeResponseInfo> episodes { get; set; }
        }

        internal class EpisodeResponseInfo
        {
            public DateTimeOffset air_date { get; set; }

            public int episode_number { get; set; }

            public string name { get; set; }

            public string overview { get; set; }

            public int id { get; set; }

            public int season_number { get; set; }
        }
    }
}
