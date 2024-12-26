using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
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
using static StrmAssistant.Common.LanguageUtility;
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
        }

        internal class SeasonEpisodeMapping
        {
            public string SeriesTmdbId { get; set; }
            public string EpisodeGroupId { get; set; }
            public int? LookupSeasonNumber { get; set; }
            public int? LookupEpisodeNumber { get; set; }
            public int? MappedSeasonNumber { get; set; }
            public int? MappedEpisodeNumber { get; set; }
        }

        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static Assembly _movieDbAssembly;
        private static MethodInfo _seriesGetMetadata;
        private static MethodInfo _seasonGetMetadata;
        private static MethodInfo _episodeGetMetadata;
        private static MethodInfo _episodeGetImages;
        private static MethodInfo _canRefreshMetadata;

        private static AsyncLocal<Series> CurrentSeries { get; } = new AsyncLocal<Series>();

        public const string LocalEpisodeGroupFileName = "episodegroup.json";

        public static void Initialize()
        {
            try
            {
                _movieDbAssembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "MovieDb");

                if (_movieDbAssembly != null)
                {
                    var movieDbSeriesProvider = _movieDbAssembly.GetType("MovieDb.MovieDbSeriesProvider");
                    _seriesGetMetadata =
                        movieDbSeriesProvider.GetMethod("GetMetadata", BindingFlags.Public | BindingFlags.Instance);
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

                    var embyProviders = Assembly.Load("Emby.Providers");
                    var providerManager = embyProviders.GetType("Emby.Providers.Manager.ProviderManager");
                    _canRefreshMetadata = providerManager.GetMethod("CanRefresh",
                        BindingFlags.Static | BindingFlags.NonPublic, null,
                        new Type[]
                        {
                            typeof(IMetadataProvider), typeof(BaseItem), typeof(LibraryOptions), typeof(bool),
                            typeof(bool), typeof(bool)
                        }, null);
                }
                else
                {
                    Plugin.Instance.Logger.Info("MovieDbEpisodeGroup - MovieDb plugin is not installed");
                }
            }
            catch (Exception e)
            {
                Plugin.Instance.Logger.Warn("MovieDbEpisodeGroup - Patch Init Failed");
                Plugin.Instance.Logger.Debug(e.Message);
                Plugin.Instance.Logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.MetadataEnhanceStore.GetOptions().MovieDbEpisodeGroup)
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
                    if (!IsPatched(_seriesGetMetadata, typeof(MovieDbEpisodeGroup)))
                    {
                        HarmonyMod.Patch(_seriesGetMetadata,
                            prefix: new HarmonyMethod(typeof(MovieDbEpisodeGroup).GetMethod("SeriesGetMetadataPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(MovieDbEpisodeGroup).GetMethod("SeriesGetMetadataPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch MovieDbSeriesProvider.GetMetadata Success by Harmony");
                    }
                    if (!IsPatched(_seasonGetMetadata, typeof(MovieDbEpisodeGroup)))
                    {
                        HarmonyMod.Patch(_seasonGetMetadata,
                            prefix: new HarmonyMethod(typeof(MovieDbEpisodeGroup).GetMethod("SeasonGetMetadataPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(MovieDbEpisodeGroup).GetMethod("SeasonGetMetadataPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch MovieDbSeasonProvider.GetMetadata Success by Harmony");
                    }
                    if (!IsPatched(_episodeGetMetadata, typeof(MovieDbEpisodeGroup)))
                    {
                        HarmonyMod.Patch(_episodeGetMetadata,
                            prefix: new HarmonyMethod(typeof(MovieDbEpisodeGroup).GetMethod("EpisodeGetMetadataPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(MovieDbEpisodeGroup).GetMethod("EpisodeGetMetadataPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch MovieDbEpisodeProvider.GetMetadata Success by Harmony");
                    }
                    if (!IsPatched(_episodeGetImages, typeof(MovieDbEpisodeGroup)))
                    {
                        HarmonyMod.Patch(_episodeGetImages,
                            prefix: new HarmonyMethod(typeof(MovieDbEpisodeGroup).GetMethod("EpisodeGetImagesPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(MovieDbEpisodeGroup).GetMethod("EpisodeGetImagesPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch MovieDbEpisodeImageProvider.GetImages Success by Harmony");
                    }
                    if (!IsPatched(_canRefreshMetadata, typeof(MovieDbEpisodeGroup)))
                    {
                        HarmonyMod.Patch(_canRefreshMetadata,
                            prefix: new HarmonyMethod(typeof(MovieDbEpisodeGroup).GetMethod("CanRefreshMetadataPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch CanRefreshMetadata Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Warn("MovieDbEpisodeGroup - Patch Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
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
                    if (IsPatched(_seriesGetMetadata, typeof(MovieDbEpisodeGroup)))
                    {
                        HarmonyMod.Unpatch(_seriesGetMetadata,
                            AccessTools.Method(typeof(MovieDbEpisodeGroup), "SeriesGetMetadataPrefix"));
                        HarmonyMod.Unpatch(_seriesGetMetadata,
                            AccessTools.Method(typeof(MovieDbEpisodeGroup), "SeriesGetMetadataPostfix"));
                        Plugin.Instance.Logger.Debug("Unpatch MovieDbSeriesProvider.GetMetadata Success by Harmony");
                    }
                    if (IsPatched(_seasonGetMetadata, typeof(MovieDbEpisodeGroup)))
                    {
                        HarmonyMod.Unpatch(_seasonGetMetadata,
                            AccessTools.Method(typeof(MovieDbEpisodeGroup), "SeasonGetMetadataPrefix"));
                        HarmonyMod.Unpatch(_seasonGetMetadata,
                            AccessTools.Method(typeof(MovieDbEpisodeGroup), "SeasonGetMetadataPostfix"));
                        Plugin.Instance.Logger.Debug("Unpatch MovieDbSeasonProvider.GetMetadata Success by Harmony");
                    }
                    if (IsPatched(_episodeGetMetadata, typeof(MovieDbEpisodeGroup)))
                    {
                        HarmonyMod.Unpatch(_episodeGetMetadata,
                            AccessTools.Method(typeof(MovieDbEpisodeGroup), "EpisodeGetMetadataPrefix"));
                        HarmonyMod.Unpatch(_episodeGetMetadata,
                            AccessTools.Method(typeof(MovieDbEpisodeGroup), "EpisodeGetMetadataPostfix"));
                        Plugin.Instance.Logger.Debug("Unpatch MovieDbEpisodeProvider.GetMetadata Success by Harmony");
                    }
                    if (IsPatched(_episodeGetImages, typeof(MovieDbEpisodeGroup)))
                    {
                        HarmonyMod.Unpatch(_episodeGetImages,
                            AccessTools.Method(typeof(MovieDbEpisodeGroup), "EpisodeGetImagesPrefix"));
                        HarmonyMod.Unpatch(_episodeGetImages,
                            AccessTools.Method(typeof(MovieDbEpisodeGroup), "EpisodeGetImagesPostfix"));
                        Plugin.Instance.Logger.Debug("Unpatch MovieDbEpisodeImageProvider.GetImages Success by Harmony");
                    }
                    if (IsPatched(_canRefreshMetadata, typeof(MovieDbEpisodeGroup)))
                    {
                        HarmonyMod.Unpatch(_canRefreshMetadata,
                            AccessTools.Method(typeof(MovieDbEpisodeGroup), "CanRefreshMetadataPrefix"));
                        Plugin.Instance.Logger.Debug("Unpatch CanRefreshMetadata Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Unpatch MovieDbEpisodeGroup Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                }
            }
        }

        [HarmonyPrefix]
        private static bool CanRefreshMetadataPrefix(IMetadataProvider provider, BaseItem item,
            LibraryOptions libraryOptions, bool includeDisabled, bool forceEnableInternetMetadata,
            bool ignoreMetadataLock, ref bool __result)
        {
            if (item.Parent is null && item.ExtraType is null) return true;

            if (Plugin.Instance.MetadataEnhanceStore.GetOptions().LocalEpisodeGroup)
            {
                var providerName = provider.GetType().FullName;

                if (item is Episode episode && providerName == "MovieDb.MovieDbEpisodeProvider")
                {
                    CurrentSeries.Value = episode.Series;
                }
                else if (item is Season season && providerName == "MovieDb.MovieDbSeasonProvider")
                {
                    CurrentSeries.Value = season.Series;
                }
                else if (item is Series series && providerName == "MovieDb.MovieDbSeriesProvider")
                {
                    CurrentSeries.Value = series;
                }
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool SeriesGetMetadataPrefix(SeriesInfo info, CancellationToken cancellationToken,
            Task<MetadataResult<Series>> __result, out string __state)
        {
            __state = null;

            if (Plugin.Instance.MetadataEnhanceStore.GetOptions().LocalEpisodeGroup &&
                CurrentSeries.Value?.ContainingFolderPath != null)
            {
                var series = CurrentSeries.Value;
                CurrentSeries.Value = null;

                var localEpisodeGroupPath = Path.Combine(series.ContainingFolderPath, LocalEpisodeGroupFileName);
                var episodeGroupInfo = Task.Run(
                        () => Plugin.MetadataApi.FetchLocalEpisodeGroup(localEpisodeGroupPath),
                        cancellationToken)
                    .GetAwaiter()
                    .GetResult();

                if (episodeGroupInfo != null && !string.IsNullOrEmpty(episodeGroupInfo.id))
                {
                    __state = episodeGroupInfo.id;
                }
            }

            return true;
        }

        [HarmonyPostfix]
        private static void SeriesGetMetadataPostfix(SeriesInfo info, CancellationToken cancellationToken,
            Task<MetadataResult<Series>> __result, string __state)
        {
            var metadataResult = __result.Result;

            if (metadataResult.HasMetadata && metadataResult.Item != null && __state != null)
            {
                metadataResult.Item.SetProviderId(MovieDbEpisodeGroupExternalId.StaticName, __state);
            }
        }

        [HarmonyPrefix]
        private static bool SeasonGetMetadataPrefix(RemoteMetadataFetchOptions<SeasonInfo> options,
            CancellationToken cancellationToken, Task<MetadataResult<Season>> __result, out SeasonGroupName __state)
        {
            __state = null;

            var season = options.SearchInfo;
            season.SeriesProviderIds.TryGetValue(MovieDbEpisodeGroupExternalId.StaticName, out var episodeGroupId);
            episodeGroupId = episodeGroupId?.Trim();
            EpisodeGroupResponse episodeGroupInfo = null;
            string localEpisodeGroupPath = null;

            if (Plugin.Instance.MetadataEnhanceStore.GetOptions().LocalEpisodeGroup &&
                CurrentSeries.Value?.ContainingFolderPath != null)
            {
                var series = CurrentSeries.Value;
                CurrentSeries.Value = null;

                localEpisodeGroupPath = Path.Combine(series.ContainingFolderPath, LocalEpisodeGroupFileName);
                episodeGroupInfo = Task.Run(() => Plugin.MetadataApi.FetchLocalEpisodeGroup(localEpisodeGroupPath),
                        cancellationToken)
                    .GetAwaiter()
                    .GetResult();

                if (episodeGroupInfo != null && !string.IsNullOrEmpty(episodeGroupInfo.id) &&
                    string.IsNullOrEmpty(episodeGroupId))
                {
                    series.SetProviderId(MovieDbEpisodeGroupExternalId.StaticName, episodeGroupInfo.id);
                }
            }

            if (episodeGroupInfo is null && season.IndexNumber.HasValue && season.IndexNumber > 0 &&
                season.SeriesProviderIds.TryGetValue(MetadataProviders.Tmdb.ToString(), out var seriesTmdbId) &&
                !string.IsNullOrEmpty(episodeGroupId))
            {
                episodeGroupInfo = Task
                    .Run(
                        () => Plugin.MetadataApi.FetchOnlineEpisodeGroup(seriesTmdbId, episodeGroupId, localEpisodeGroupPath,
                            cancellationToken), cancellationToken)
                    .GetAwaiter()
                    .GetResult();
            }

            if (episodeGroupInfo != null)
            {
                var matchingSeason = episodeGroupInfo.groups.FirstOrDefault(g => g.order == season.IndexNumber);

                if (matchingSeason != null)
                {
                    __state = new SeasonGroupName
                    {
                        LookupSeasonNumber = season.IndexNumber,
                        LookupLanguage = season.MetadataLanguage,
                        GroupName = matchingSeason.name
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
            
            if (__state is null) return;

            var isZh = __state.LookupLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            var isJapaneseFallback = Plugin.Instance.MetadataEnhanceStore.GetOptions().ChineseMovieDb &&
                                     GetFallbackLanguages().Contains("ja-jp", StringComparer.OrdinalIgnoreCase);

            var shouldAssignSeasonName = !isZh || !isJapaneseFallback && IsChinese(__state.GroupName) ||
                                       isJapaneseFallback && (IsChinese(__state.GroupName) || IsJapanese(__state.GroupName));

            if (metadataResult.Item is null) metadataResult.Item = new Season();

            metadataResult.Item.IndexNumber = __state.LookupSeasonNumber;

            if (shouldAssignSeasonName) metadataResult.Item.Name = __state.GroupName;

            metadataResult.Item.PremiereDate = null;
            metadataResult.Item.ProductionYear = null;

            metadataResult.HasMetadata = true;
        }

        [HarmonyPrefix]
        private static bool EpisodeGetMetadataPrefix(RemoteMetadataFetchOptions<EpisodeInfo> options,
            CancellationToken cancellationToken, Task<MetadataResult<Episode>> __result, out SeasonEpisodeMapping __state)
        {
            __state = null;

            var episode = options.SearchInfo;
            string localEpisodeGroupPath = null;
            Series series = null;

            if (Plugin.Instance.MetadataEnhanceStore.GetOptions().LocalEpisodeGroup &&
                CurrentSeries.Value?.ContainingFolderPath != null)
            {
                series = CurrentSeries.Value;
                CurrentSeries.Value = null;

                localEpisodeGroupPath = Path.Combine(series.ContainingFolderPath, LocalEpisodeGroupFileName);
            }

            if (episode.SeriesProviderIds.TryGetValue(MetadataProviders.Tmdb.ToString(), out var seriesTmdbId))
            {
                episode.SeriesProviderIds.TryGetValue(MovieDbEpisodeGroupExternalId.StaticName, out var episodeGroupId);
                episodeGroupId = episodeGroupId?.Trim();

                var matchingEpisode = Task.Run(() => MapSeasonEpisode(seriesTmdbId, episodeGroupId,
                            episode.ParentIndexNumber, episode.IndexNumber, localEpisodeGroupPath, cancellationToken),
                        cancellationToken)
                    .GetAwaiter()
                    .GetResult();

                if (matchingEpisode != null)
                {
                    __state = matchingEpisode;
                    episode.ParentIndexNumber = matchingEpisode.MappedSeasonNumber;
                    episode.IndexNumber = matchingEpisode.MappedEpisodeNumber;

                    if (series != null && string.IsNullOrEmpty(episodeGroupId) &&
                        !string.IsNullOrEmpty(matchingEpisode.EpisodeGroupId))
                    {
                        series.SetProviderId(MovieDbEpisodeGroupExternalId.StaticName, matchingEpisode.EpisodeGroupId);
                    }
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

            metadataResult.Item.ParentIndexNumber = __state.LookupSeasonNumber;
            metadataResult.Item.IndexNumber = __state.LookupEpisodeNumber;
        }

        [HarmonyPrefix]
        private static bool EpisodeGetImagesPrefix(RemoteImageFetchOptions options, CancellationToken cancellationToken,
            Task<IEnumerable<RemoteImageInfo>> __result, out SeasonEpisodeMapping __state)
        {
            __state = null;

            if (options.Item is Episode episode)
            {
                var seriesTmdbId = episode.Series.GetProviderId(MetadataProviders.Tmdb);
                var localEpisodeGroupPath = Plugin.Instance.MetadataEnhanceStore.GetOptions().LocalEpisodeGroup
                    ? Path.Combine(episode.Series.ContainingFolderPath, LocalEpisodeGroupFileName)
                    : null;

                if (!string.IsNullOrEmpty(seriesTmdbId))
                {
                    var episodeGroupId = episode.Series.GetProviderId(MovieDbEpisodeGroupExternalId.StaticName)?.Trim();
                    var matchingEpisode = Task.Run(() => MapSeasonEpisode(seriesTmdbId, episodeGroupId,
                            episode.ParentIndexNumber, episode.IndexNumber, localEpisodeGroupPath, cancellationToken), cancellationToken)
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
            int? lookupSeasonNumber, int? lookupEpisodeNumber, string localEpisodeGroupPath, CancellationToken cancellationToken)
        {
            if (!lookupSeasonNumber.HasValue || !lookupEpisodeNumber.HasValue) return null;

            EpisodeGroupResponse episodeGroupInfo = null;

            if (Plugin.Instance.MetadataEnhanceStore.GetOptions().LocalEpisodeGroup &&
                !string.IsNullOrEmpty(localEpisodeGroupPath))
            {
                episodeGroupInfo = await Plugin.MetadataApi.FetchLocalEpisodeGroup(localEpisodeGroupPath).ConfigureAwait(false);
            }

            if (episodeGroupInfo is null && !string.IsNullOrEmpty(episodeGroupId))
            {
                episodeGroupInfo =
                    await Plugin.MetadataApi.FetchOnlineEpisodeGroup(seriesTmdbId, episodeGroupId, localEpisodeGroupPath, cancellationToken);
            }
            
            var matchingEpisode = episodeGroupInfo?.groups.Where(g => g.order == lookupSeasonNumber)
                .SelectMany(g => g.episodes)
                .FirstOrDefault(e => e.order + 1 == lookupEpisodeNumber.Value);

            if (matchingEpisode != null)
            {
                return new SeasonEpisodeMapping
                {
                    SeriesTmdbId = seriesTmdbId,
                    EpisodeGroupId = episodeGroupInfo.id,
                    LookupSeasonNumber = lookupSeasonNumber,
                    LookupEpisodeNumber = lookupEpisodeNumber,
                    MappedSeasonNumber = matchingEpisode.season_number,
                    MappedEpisodeNumber = matchingEpisode.episode_number
                };
            }

            return null;
        }
    }
}
