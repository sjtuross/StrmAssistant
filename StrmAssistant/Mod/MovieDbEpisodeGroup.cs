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
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class MovieDbEpisodeGroup
    {
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
        private static MethodInfo _episodeGetMetadata;
        private static MethodInfo _episodeGetImages;

        private static readonly AsyncLocal<SeasonEpisodeMapping> CurrentLookupMapping = new AsyncLocal<SeasonEpisodeMapping>();

        public static void Initialize()
        {
            try
            {
                _movieDbAssembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "MovieDb");

                if (_movieDbAssembly != null)
                {
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
        private static bool EpisodeGetMetadataPrefix(RemoteMetadataFetchOptions<EpisodeInfo> options,
            CancellationToken cancellationToken, Task<MetadataResult<Episode>> __result)
        {
            var episode = options.SearchInfo;

            if (CurrentLookupMapping.Value == null &&
                episode.SeriesProviderIds.TryGetValue(MetadataProviders.Tmdb.ToString(), out var seriesTmdbId) &&
                episode.SeriesProviderIds.TryGetValue(MovieDbEpisodeGroupExternalId.StaticName, out var episodeGroupId))
            {
                episodeGroupId = episodeGroupId?.Trim();
                var matchingEpisode = Task.Run(() => MapSeasonEpisode(seriesTmdbId, episodeGroupId,
                        episode.ParentIndexNumber, episode.IndexNumber, cancellationToken))
                    .GetAwaiter()
                    .GetResult();

                if (matchingEpisode != null)
                {
                    CurrentLookupMapping.Value = matchingEpisode;
                    episode.ParentIndexNumber = matchingEpisode.MappedSeasonNumber;
                    episode.IndexNumber = matchingEpisode.MappedEpisodeNumber;
                }
            }

            return true;
        }

        [HarmonyPostfix]
        private static void EpisodeGetMetadataPostfix(RemoteMetadataFetchOptions<EpisodeInfo> options,
            CancellationToken cancellationToken, Task<MetadataResult<Episode>> __result)
        {
            __result.ContinueWith(task =>
                {
                    if (!task.IsCompletedSuccessfully || CurrentLookupMapping.Value == null) return;

                    var metadataResult = task.Result;
                    if (!metadataResult.HasMetadata || metadataResult.Item == null) return;

                    var episode = metadataResult.Item;
                    if (episode != null)
                    {
                        var mapping = CurrentLookupMapping.Value;
                        CurrentLookupMapping.Value = null;
                        episode.ParentIndexNumber = mapping.LookupSeasonNumber;
                        episode.IndexNumber = mapping.LookupEpisodeNumber;
                    }
                }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                .ConfigureAwait(false);
        }

        [HarmonyPrefix]
        private static bool EpisodeGetImagesPrefix(RemoteImageFetchOptions options, CancellationToken cancellationToken,
            Task<IEnumerable<RemoteImageInfo>> __result)
        {
            var episode = options.Item as Episode;
            var seriesTmdbId = episode?.Series.GetProviderId(MetadataProviders.Tmdb);
            var episodeGroupId = episode?.Series.GetProviderId(MovieDbEpisodeGroupExternalId.StaticName)?.Trim();

            if (CurrentLookupMapping.Value is null && !string.IsNullOrEmpty(seriesTmdbId) &&
                !string.IsNullOrEmpty(episodeGroupId))
            {
                var matchingEpisode = Task.Run(() => MapSeasonEpisode(seriesTmdbId, episodeGroupId,
                        episode.ParentIndexNumber, episode.IndexNumber, cancellationToken))
                    .GetAwaiter()
                    .GetResult();

                if (matchingEpisode != null)
                {
                    CurrentLookupMapping.Value = matchingEpisode;
                    episode.ParentIndexNumber = matchingEpisode.MappedSeasonNumber;
                    episode.IndexNumber = matchingEpisode.MappedEpisodeNumber;
                }
            }

            return true;
        }

        [HarmonyPostfix]
        private static void EpisodeGetImagesPostfix(RemoteImageFetchOptions options,
            CancellationToken cancellationToken, Task<IEnumerable<RemoteImageInfo>> __result)
        {
            __result.ContinueWith(task =>
                {
                    if (!task.IsCompletedSuccessfully || CurrentLookupMapping.Value == null) return;

                    if (options.Item is Episode episode)
                    {
                        var mapping = CurrentLookupMapping.Value;
                        CurrentLookupMapping.Value = null;
                        episode.ParentIndexNumber = mapping.LookupSeasonNumber;
                        episode.IndexNumber = mapping.LookupEpisodeNumber;
                    }
                }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                .ConfigureAwait(false);
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
