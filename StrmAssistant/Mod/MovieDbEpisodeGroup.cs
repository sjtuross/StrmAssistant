using HarmonyLib;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using StrmAssistant.Provider;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.LanguageUtility;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class MovieDbEpisodeGroup
    {
        internal class SeasonGroupName
        {
            public int? LookupSeasonNumber { get; set; }
            public string LookupLanguage { get; set; }
            public string GroupName { get; set; }
            public DateTimeOffset? PremiereDate { get; set; }
        }

        internal class SeasonEpisodeMapping
        {
            public string SeriesTmdbId { get; set; }
            public int? LookupSeasonNumber { get; set; }
            public int? LookupEpisodeNumber { get; set; }
            public int? MappedSeasonNumber { get; set; }
            public int? MappedEpisodeNumber { get; set; }
        }

        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static Assembly _movieDbAssembly;
        private static MethodInfo _seasonGetMetadata;
        private static MethodInfo _episodeGetMetadata;
        private static MethodInfo _episodeGetImages;

        public static void Initialize()
        {
            try
            {
                _movieDbAssembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "MovieDb");

                if (_movieDbAssembly != null)
                {
                    var movieDbSeasonProvider = _movieDbAssembly.GetType("MovieDb.MovieDbSeasonProvider");
                    _seasonGetMetadata = movieDbSeasonProvider.GetMethod("GetMetadata",
                        BindingFlags.Public | BindingFlags.Instance, null,
                        new[] { typeof(RemoteMetadataFetchOptions<SeasonInfo>), typeof(CancellationToken) }, null);
                    var movieDbEpisodeProvider = _movieDbAssembly.GetType("MovieDb.MovieDbEpisodeProvider");
                    _episodeGetMetadata = movieDbEpisodeProvider.GetMethod("GetMetadata",
                        BindingFlags.Public | BindingFlags.Instance, null,
                        new[] { typeof(RemoteMetadataFetchOptions<EpisodeInfo>), typeof(CancellationToken) }, null);
                    var movieDbEpisodeImageProvider = _movieDbAssembly.GetType("MovieDb.MovieDbEpisodeImageProvider");
                    _episodeGetImages = movieDbEpisodeImageProvider.GetMethod("GetImages",
                        BindingFlags.Public | BindingFlags.Instance, null,
                        new[] { typeof(RemoteImageFetchOptions), typeof(CancellationToken) }, null);
                }
                else
                {
                    Plugin.Instance.logger.Info("MovieDbEpisodeGroup - MovieDb plugin is not installed");
                }
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("MovieDbEpisodeGroup - Patch Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions.MovieDbEpisodeGroup)
            {
                Patch();
            }
        }

        public static void Patch()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony && _movieDbAssembly != null)
            {
                try
                {
                    if (!IsPatched(_seasonGetMetadata, typeof(MovieDbEpisodeGroup)))
                    {
                        HarmonyMod.Patch(_seasonGetMetadata,
                            prefix: new HarmonyMethod(typeof(MovieDbEpisodeGroup).GetMethod("SeasonGetMetadataPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(MovieDbEpisodeGroup).GetMethod("SeasonGetMetadataPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch MovieDbSeasonProvider.GetMetadata Success by Harmony");
                    }
                    if (!IsPatched(_episodeGetMetadata, typeof(MovieDbEpisodeGroup)))
                    {
                        HarmonyMod.Patch(_episodeGetMetadata,
                            prefix: new HarmonyMethod(typeof(MovieDbEpisodeGroup).GetMethod("EpisodeGetMetadataPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(MovieDbEpisodeGroup).GetMethod("EpisodeGetMetadataPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch MovieDbEpisodeProvider.GetMetadata Success by Harmony");
                    }
                    if (!IsPatched(_episodeGetImages, typeof(MovieDbEpisodeGroup)))
                    {
                        HarmonyMod.Patch(_episodeGetImages,
                            prefix: new HarmonyMethod(typeof(MovieDbEpisodeGroup).GetMethod("EpisodeGetImagesPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(MovieDbEpisodeGroup).GetMethod("EpisodeGetImagesPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch MovieDbEpisodeImageProvider.GetImages Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Warn("MovieDbEpisodeGroup - Patch Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                    PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;
                }
            }
        }

        public static void Unpatch()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (IsPatched(_seasonGetMetadata, typeof(MovieDbEpisodeGroup)))
                    {
                        HarmonyMod.Unpatch(_seasonGetMetadata,
                            AccessTools.Method(typeof(MovieDbEpisodeGroup), "SeasonGetMetadataPrefix"));
                        HarmonyMod.Unpatch(_seasonGetMetadata,
                            AccessTools.Method(typeof(MovieDbEpisodeGroup), "SeasonGetMetadataPostfix"));
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeasonProvider.GetMetadata Success by Harmony");
                    }
                    if (IsPatched(_episodeGetMetadata, typeof(MovieDbEpisodeGroup)))
                    {
                        HarmonyMod.Unpatch(_episodeGetMetadata,
                            AccessTools.Method(typeof(MovieDbEpisodeGroup), "EpisodeGetMetadataPrefix"));
                        HarmonyMod.Unpatch(_episodeGetMetadata,
                            AccessTools.Method(typeof(MovieDbEpisodeGroup), "EpisodeGetMetadataPostfix"));
                        Plugin.Instance.logger.Debug("Unpatch MovieDbEpisodeProvider.GetMetadata Success by Harmony");
                    }
                    if (IsPatched(_episodeGetImages, typeof(MovieDbEpisodeGroup)))
                    {
                        HarmonyMod.Unpatch(_episodeGetImages,
                            AccessTools.Method(typeof(MovieDbEpisodeGroup), "EpisodeGetImagesPrefix"));
                        HarmonyMod.Unpatch(_episodeGetImages,
                            AccessTools.Method(typeof(MovieDbEpisodeGroup), "EpisodeGetImagesPostfix"));
                        Plugin.Instance.logger.Debug("Unpatch MovieDbEpisodeImageProvider.GetImages Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch MovieDbEpisodeGroup Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        [HarmonyPrefix]
        private static bool SeasonGetMetadataPrefix(RemoteMetadataFetchOptions<SeasonInfo> options,
            CancellationToken cancellationToken, Task<MetadataResult<Season>> __result, out SeasonGroupName __state)
        {
            __state = null;

            var season = options.SearchInfo;
            if (season.IndexNumber.HasValue &&
                season.SeriesProviderIds.TryGetValue(MetadataProviders.Tmdb.ToString(), out var seriesTmdbId) &&
                season.SeriesProviderIds.TryGetValue(MovieDbEpisodeGroupExternalId.StaticName, out var episodeGroupId))
            {
                episodeGroupId = episodeGroupId?.Trim();
                var episodeGroupInfo = Task
                    .Run(() => FetchEpisodeGroupInfo(seriesTmdbId, episodeGroupId, cancellationToken),
                        cancellationToken)
                    .GetAwaiter()
                    .GetResult();

                var matchingSeason = episodeGroupInfo?.groups.FirstOrDefault(g => g.order == season.IndexNumber);
                var firstEpisodeInfo = matchingSeason?.episodes.FirstOrDefault(e => e.order == 0);

                if (matchingSeason != null)
                {
                    __state = new SeasonGroupName
                    {
                        LookupSeasonNumber = season.IndexNumber,
                        LookupLanguage = season.MetadataLanguage,
                        GroupName = matchingSeason.name,
                        PremiereDate = firstEpisodeInfo?.air_date
                    };
                }
            }

            return true;
        }

        [HarmonyPostfix]
        private static void SeasonGetMetadataPostfix(RemoteMetadataFetchOptions<SeasonInfo> options,
            CancellationToken cancellationToken, Task<MetadataResult<Season>> __result, SeasonGroupName __state)
        {
            var metadataResult = __result.Result;

            var isJapaneseFallback = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions.ChineseMovieDb &&
                                     GetFallbackLanguages().Contains("ja-jp", StringComparer.OrdinalIgnoreCase);

            if (__state != null && __state.LookupLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase) &&
                (!isJapaneseFallback && IsChinese(__state.GroupName) ||
                 isJapaneseFallback && (IsChinese(__state.GroupName) || IsJapanese(__state.GroupName))))
            {
                if (metadataResult.Item is null)
                {
                    metadataResult.Item = new Season();
                }

                metadataResult.Item.IndexNumber = __state.LookupSeasonNumber;
                metadataResult.Item.Name = __state.GroupName;
                metadataResult.Item.PremiereDate = __state.PremiereDate;
                if (__state.PremiereDate != null)
                    metadataResult.Item.ProductionYear = __state.PremiereDate.Value.Year;

                metadataResult.HasMetadata = true;
            }
        }

        [HarmonyPrefix]
        private static bool EpisodeGetMetadataPrefix(RemoteMetadataFetchOptions<EpisodeInfo> options,
            CancellationToken cancellationToken, Task<MetadataResult<Episode>> __result, out SeasonEpisodeMapping __state)
        {
            __state = null;

            var episode = options.SearchInfo;
            if (episode.ParentIndexNumber.HasValue && episode.IndexNumber.HasValue &&
                episode.SeriesProviderIds.TryGetValue(MetadataProviders.Tmdb.ToString(), out var seriesTmdbId) &&
                episode.SeriesProviderIds.TryGetValue(MovieDbEpisodeGroupExternalId.StaticName, out var episodeGroupId))
            {
                episodeGroupId = episodeGroupId?.Trim();
                var matchingEpisode = Task.Run(() => MapSeasonEpisode(seriesTmdbId, episodeGroupId,
                        episode.ParentIndexNumber, episode.IndexNumber, cancellationToken), cancellationToken)
                    .GetAwaiter()
                    .GetResult();

                if (matchingEpisode != null)
                {
                    __state = matchingEpisode;
                    episode.ParentIndexNumber = matchingEpisode.MappedSeasonNumber;
                    episode.IndexNumber = matchingEpisode.MappedEpisodeNumber;
                }
            }

            return true;
        }

        [HarmonyPostfix]
        private static void EpisodeGetMetadataPostfix(RemoteMetadataFetchOptions<EpisodeInfo> options,
            CancellationToken cancellationToken, Task<MetadataResult<Episode>> __result, SeasonEpisodeMapping __state)
        {
            var metadataResult = __result.Result;
            if (!metadataResult.HasMetadata || metadataResult.Item is null || __state is null) return;

            var episode = metadataResult.Item;
            if (episode != null)
            {
                episode.ParentIndexNumber = __state.LookupSeasonNumber;
                episode.IndexNumber = __state.LookupEpisodeNumber;
            }
        }

        [HarmonyPrefix]
        private static bool EpisodeGetImagesPrefix(RemoteImageFetchOptions options, CancellationToken cancellationToken,
            Task<IEnumerable<RemoteImageInfo>> __result, out SeasonEpisodeMapping __state)
        {
            __state = null;

            if (options.Item is Episode episode && episode.ParentIndexNumber.HasValue && episode.IndexNumber.HasValue)
            {
                var seriesTmdbId = episode.Series.GetProviderId(MetadataProviders.Tmdb);
                var episodeGroupId = episode.Series.GetProviderId(MovieDbEpisodeGroupExternalId.StaticName)?.Trim();

                if (!string.IsNullOrEmpty(seriesTmdbId) && !string.IsNullOrEmpty(episodeGroupId))
                {
                    var matchingEpisode = Task.Run(() => MapSeasonEpisode(seriesTmdbId, episodeGroupId,
                            episode.ParentIndexNumber, episode.IndexNumber, cancellationToken), cancellationToken)
                        .GetAwaiter()
                        .GetResult();

                    if (matchingEpisode != null)
                    {
                        __state = matchingEpisode;
                        episode.ParentIndexNumber = matchingEpisode.MappedSeasonNumber;
                        episode.IndexNumber = matchingEpisode.MappedEpisodeNumber;
                    }
                }
            }

            return true;
        }

        [HarmonyPostfix]
        private static void EpisodeGetImagesPostfix(RemoteImageFetchOptions options,
            CancellationToken cancellationToken, Task<IEnumerable<RemoteImageInfo>> __result, SeasonEpisodeMapping __state)
        {
            if (options.Item is Episode episode && __state != null)
            {
                episode.ParentIndexNumber = __state.LookupSeasonNumber;
                episode.IndexNumber = __state.LookupEpisodeNumber;
            }
        }

        private static async Task<SeasonEpisodeMapping> MapSeasonEpisode(string seriesTmdbId, string episodeGroupId,
            int? lookupSeasonNumber, int? lookupEpisodeNumber, CancellationToken cancellationToken)
        {
            if (!lookupSeasonNumber.HasValue || !lookupEpisodeNumber.HasValue) return null;

            var episodeGroupInfo =
                await FetchEpisodeGroupInfo(seriesTmdbId, episodeGroupId, cancellationToken);

            var matchingEpisode = episodeGroupInfo?.groups.Where(g => g.order == lookupSeasonNumber)
                .SelectMany(g => g.episodes)
                .FirstOrDefault(e => e.order + 1 == lookupEpisodeNumber.Value);

            if (matchingEpisode != null)
            {
                return new SeasonEpisodeMapping
                {
                    SeriesTmdbId = seriesTmdbId,
                    LookupSeasonNumber = lookupSeasonNumber,
                    LookupEpisodeNumber = lookupEpisodeNumber,
                    MappedSeasonNumber = matchingEpisode.season_number,
                    MappedEpisodeNumber = matchingEpisode.episode_number
                };
            }

            return null;
        }

        private static async Task<EpisodeGroupResponse> FetchEpisodeGroupInfo(string seriesTmdbId,
            string episodeGroupId, CancellationToken cancellationToken)
        {
            var url =
                $"{AltMovieDbConfig.CurrentMovieDbApiUrl}/3/tv/episode_group/{episodeGroupId}?api_key={AltMovieDbConfig.CurrentMovieDbApiKey}";

            var cacheKey = "tmdb_episode_group_" + seriesTmdbId + "_" + episodeGroupId;

            var cachePath = Path.Combine(Plugin.Instance.ApplicationPaths.CachePath, "tmdb-tv", seriesTmdbId,
                episodeGroupId + ".json");

            var episodeGroupResponse = await Plugin.MetadataApi
                .GetMovieDbResponse<EpisodeGroupResponse>(url, cacheKey, cachePath, cancellationToken)
                .ConfigureAwait(false);

            return episodeGroupResponse;
        }
    }
}
