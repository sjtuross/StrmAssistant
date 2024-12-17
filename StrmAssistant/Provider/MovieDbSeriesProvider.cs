using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
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
        public string Name => "TheMovieDb";

        public async Task<RemoteSearchResult[]> GetAllEpisodes(SeriesInfo seriesInfo,
            CancellationToken cancellationToken)
        {
            var tmdbId = seriesInfo.GetProviderId(MetadataProviders.Tmdb);
            var language = seriesInfo.MetadataLanguage;

            if (string.IsNullOrEmpty(tmdbId))
                return Array.Empty<RemoteSearchResult>();

            var seriesUrl = BuildApiUrl($"tv/{tmdbId}", language);
            var seriesInfoResponse = await Plugin.MetadataApi
                .GetMovieDbResponse<SeriesResponseInfo>(seriesUrl, cancellationToken).ConfigureAwait(false);
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
            var seasonInfo = await Plugin.MetadataApi
                .GetMovieDbResponse<SeasonResponseInfo>(seasonUrl, cancellationToken).ConfigureAwait(false);

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

        private static string BuildApiUrl(string endpoint, string language)
        {
            var url =
                $"{AltMovieDbConfig.CurrentMovieDbApiUrl}/3/{endpoint}?api_key={AltMovieDbConfig.CurrentMovieDbApiKey}";
            if (!string.IsNullOrEmpty(language))
                url += $"&language={language}";
            return url;
        }
    }
}
