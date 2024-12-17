using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using StrmAssistant.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class PreferOriginalPoster
    {
        internal class ContextItem
        {
            public string TmdbId { get; set; }
            public string ImdbId { get; set; }
            public string TvdbId { get; set; }
            public string OriginalLanguage { get; set; }
        }

        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static Assembly _movieDbAssembly;
        private static MethodInfo _getMovieInfo;
        private static MethodInfo _ensureSeriesInfo;
        private static PropertyInfo _tmdbIdMovieDataTmdb;
        private static PropertyInfo _imdbIdMovieDataTmdb;
        private static PropertyInfo _originalLanguageMovieDataTmdb;
        private static PropertyInfo _tmdbIdSeriesDataTmdb;
        private static PropertyInfo _languagesSeriesDataTmdb;
        private static PropertyInfo _movieDataTmdbTaskResult;
        private static PropertyInfo _seriesDataTmdbTaskResult;

        private static Assembly _tvdbAssembly;
        private static MethodInfo _ensureMovieInfoTvdb;
        private static MethodInfo _ensureSeriesInfoTvdb;
        private static PropertyInfo _tvdbIdMovieDataTvdb;
        private static PropertyInfo _originalLanguageMovieDataTvdb;
        private static PropertyInfo _tvdbIdSeriesDataTvdb;
        private static PropertyInfo _originalLanguageSeriesDataTvdb;
        private static PropertyInfo _movieDataTvdbTaskResult;
        private static PropertyInfo _seriesDataTvdbTaskResult;

        private static MethodInfo _getAvailableRemoteImages;
        private static FieldInfo _remoteImageTaskResult;

        private static readonly ConcurrentDictionary<string, ContextItem> CurrentItemsByTmdbId =
            new ConcurrentDictionary<string, ContextItem>();
        private static readonly ConcurrentDictionary<string, ContextItem> CurrentItemsByImdbId =
            new ConcurrentDictionary<string, ContextItem>();
        private static readonly ConcurrentDictionary<string, ContextItem> CurrentItemsByTvdbId =
            new ConcurrentDictionary<string, ContextItem>();

        private static readonly AsyncLocal<ContextItem> CurrentLookupItem = new AsyncLocal<ContextItem>();

        private static readonly AsyncLocal<bool> WasCalledByImageProvider = new AsyncLocal<bool>();

        public static void Initialize()
        {
            try
            {
                _movieDbAssembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "MovieDb");

                if (_movieDbAssembly != null)
                {
                    var movieDbImageProvider = _movieDbAssembly.GetType("MovieDb.MovieDbImageProvider");
                    _getMovieInfo = movieDbImageProvider.GetMethod("GetMovieInfo",
                        BindingFlags.Instance | BindingFlags.NonPublic, null,
                        new[] { typeof(BaseItem), typeof(string), typeof(IJsonSerializer), typeof(CancellationToken) },
                        null);
                    var completeMovieData = _movieDbAssembly.GetType("MovieDb.MovieDbProvider")
                        .GetNestedType("CompleteMovieData", BindingFlags.NonPublic);
                    _tmdbIdMovieDataTmdb = completeMovieData.GetProperty("id");
                    _imdbIdMovieDataTmdb = completeMovieData.GetProperty("imdb_id");
                    _originalLanguageMovieDataTmdb = completeMovieData.GetProperty("original_language");

                    var movieDbSeriesProvider = _movieDbAssembly.GetType("MovieDb.MovieDbSeriesProvider");
                    _ensureSeriesInfo = movieDbSeriesProvider.GetMethod("EnsureSeriesInfo",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    var seriesRootObject = movieDbSeriesProvider.GetNestedType("SeriesRootObject", BindingFlags.Public);
                    _tmdbIdSeriesDataTmdb = seriesRootObject.GetProperty("id");
                    _languagesSeriesDataTmdb = seriesRootObject.GetProperty("languages");
                }
                else
                {
                    Plugin.Instance.Logger.Info("OriginalPoster - MovieDb plugin is not installed");
                }

                _tvdbAssembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Tvdb");
                if (_tvdbAssembly != null)
                {
                    var tvdbMovieProvider = _tvdbAssembly.GetType("Tvdb.TvdbMovieProvider");
                    _ensureMovieInfoTvdb = tvdbMovieProvider.GetMethod("EnsureMovieInfo",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    var tvdbSeriesProvider = _tvdbAssembly.GetType("Tvdb.TvdbSeriesProvider");
                    _ensureSeriesInfoTvdb = tvdbSeriesProvider.GetMethod("EnsureSeriesInfo",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    var movieData = _tvdbAssembly.GetType("Tvdb.MovieData");
                    _tvdbIdMovieDataTvdb = movieData.GetProperty("id");
                    _originalLanguageMovieDataTvdb = movieData.GetProperty("originalLanguage");
                    var seriesData = _tvdbAssembly.GetType("Tvdb.SeriesData");
                    _tvdbIdSeriesDataTvdb = seriesData.GetProperty("id");
                    _originalLanguageSeriesDataTvdb = seriesData.GetProperty("originalLanguage");
                }
                else
                {
                    Plugin.Instance.Logger.Info("OriginalPoster - Tvdb plugin is not installed");
                }

                var embyProvidersAssembly = Assembly.Load("Emby.Providers");
                var providerManager =
                    embyProvidersAssembly.GetType("Emby.Providers.Manager.ProviderManager");
                _getAvailableRemoteImages = providerManager.GetMethod("GetAvailableRemoteImages",
                    BindingFlags.Instance | BindingFlags.Public, null,
                    new[]
                    {
                        typeof(BaseItem), typeof(LibraryOptions), typeof(RemoteImageQuery),
                        typeof(IDirectoryService), typeof(CancellationToken)
                    }, null);
                _remoteImageTaskResult =
                    typeof(Task<IEnumerable<RemoteImageInfo>>).GetField("m_result",
                        BindingFlags.NonPublic | BindingFlags.Instance);
            }
            catch (Exception e)
            {
                Plugin.Instance.Logger.Warn("OriginalPoster - Patch Init Failed");
                Plugin.Instance.Logger.Debug(e.Message);
                Plugin.Instance.Logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.MetadataEnhanceStore.GetOptions().PreferOriginalPoster)
            {
                Patch();
            }
        }

        public static void Patch()
        {
            PatchMovieDb();
            PatchTvdb();
            PatchGetAvailableRemoteImages();
        }

        public static void Unpatch()
        {
            UnpatchMovieDb();
            UnpatchTvdb();
            UnpatchGetAvailableRemoteImages();
        }

        private static void PatchMovieDb()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony &&
                _movieDbAssembly != null)
            {
                try
                {
                    if (!IsPatched(_getMovieInfo, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Patch(_getMovieInfo,
                            postfix: new HarmonyMethod(typeof(PreferOriginalPoster).GetMethod(
                                "GetMovieInfoTmdbPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch MovieDbImageProvider.GetMovieInfo Success by Harmony");
                    }

                    if (!IsPatched(_ensureSeriesInfo, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Patch(_ensureSeriesInfo,
                            postfix: new HarmonyMethod(typeof(PreferOriginalPoster).GetMethod(
                                "EnsureSeriesInfoTmdbPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch MovieDbSeriesProvider.EnsureSeriesInfo Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Patch PreferOriginalPoster for MovieDb Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                    PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;
                }
            }
        }

        private static void UnpatchMovieDb()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony &&
                _movieDbAssembly != null)
            {
                try
                {
                    if (IsPatched(_ensureSeriesInfo, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Unpatch(_ensureSeriesInfo,
                            AccessTools.Method(typeof(PreferOriginalPoster), "EnsureSeriesInfoTmdbPostfix"));
                        Plugin.Instance.Logger.Debug(
                            "Unpatch MovieDbSeriesProvider.EnsureSeriesInfo Success by Harmony");
                    }

                    if (IsPatched(_getMovieInfo, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Unpatch(_getMovieInfo,
                            AccessTools.Method(typeof(PreferOriginalPoster), "GetMovieInfoTmdbPostfix"));
                        Plugin.Instance.Logger.Debug("Unpatch MovieDbImageProvider.GetMovieInfo Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Unpatch PreferOriginalPoster for MovieDb Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                }
            }
        }

        private static void PatchTvdb()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony &&
                _tvdbAssembly != null)
            {
                try
                {
                    if (!IsPatched(_ensureMovieInfoTvdb, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Patch(_ensureMovieInfoTvdb,
                            postfix: new HarmonyMethod(typeof(PreferOriginalPoster).GetMethod(
                                "EnsureMovieInfoTvdbPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch TvdbMovieProvider.EnsureMovieInfo Success by Harmony");
                    }

                    if (!IsPatched(_ensureSeriesInfoTvdb, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Patch(_ensureSeriesInfoTvdb,
                            postfix: new HarmonyMethod(typeof(PreferOriginalPoster).GetMethod(
                                "EnsureSeriesInfoTvdbPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch TvdbSeriesProvider.EnsureSeriesInfo Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Patch PreferOriginalPoster for Tvdb Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                    PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;
                }
            }
        }

        private static void UnpatchTvdb()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony &&
                _tvdbAssembly != null)
            {
                try
                {
                    if (IsPatched(_ensureMovieInfoTvdb, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Unpatch(_ensureMovieInfoTvdb,
                            AccessTools.Method(typeof(PreferOriginalPoster), "EnsureMovieInfoTvdbPostfix"));
                        Plugin.Instance.Logger.Debug(
                            "Unpatch TvdbMovieProvider.EnsureMovieInfo Success by Harmony");
                    }

                    if (IsPatched(_ensureSeriesInfoTvdb, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Unpatch(_ensureSeriesInfoTvdb,
                            AccessTools.Method(typeof(PreferOriginalPoster), "EnsureSeriesInfoTvdbPostfix"));
                        Plugin.Instance.Logger.Debug(
                            "Unpatch TvdbSeriesProvider.EnsureSeriesInfo Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Unpatch PreferOriginalPoster for Tvdb Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                }
            }
        }

        private static void PatchGetAvailableRemoteImages()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (!IsPatched(_getAvailableRemoteImages, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Patch(_getAvailableRemoteImages,
                            prefix: new HarmonyMethod(typeof(PreferOriginalPoster).GetMethod(
                                "GetAvailableRemoteImagesPrefix", BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(PreferOriginalPoster).GetMethod(
                                "GetAvailableRemoteImagesPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch GetAvailableRemoteImages Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Patch GetAvailableRemoteImages Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                    PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;
                }
            }
        }

        private static void UnpatchGetAvailableRemoteImages()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (IsPatched(_getAvailableRemoteImages, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Unpatch(_getAvailableRemoteImages,
                            AccessTools.Method(typeof(PreferOriginalPoster), "GetAvailableRemoteImagesPrefix"));
                        HarmonyMod.Unpatch(_getAvailableRemoteImages,
                            AccessTools.Method(typeof(PreferOriginalPoster), "GetAvailableRemoteImagesPostfix"));
                        Plugin.Instance.Logger.Debug("Unpatch GetAvailableRemoteImages Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Unpatch GetAvailableRemoteImages Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                }
            }
        }

        private static void AddContextItem(string tmdbId, string imdbId, string tvdbId)
        {
            if (tmdbId == null && imdbId == null && tvdbId == null) return;

            var item = new ContextItem { TmdbId = tmdbId, ImdbId = imdbId, TvdbId = tvdbId };

            if (tmdbId != null) CurrentItemsByTmdbId[tmdbId] = item;

            if (imdbId != null) CurrentItemsByImdbId[imdbId] = item;

            if (tvdbId != null) CurrentItemsByTvdbId[tvdbId] = item;

            CurrentLookupItem.Value = new ContextItem { TmdbId = tmdbId, ImdbId = imdbId, TvdbId = tvdbId };
        }

        private static void UpdateOriginalLanguage(string tmdbId, string imdbId, string tvdbId, string originalLanguage)
        {
            ContextItem itemToUpdate = null;

            if (tmdbId != null) CurrentItemsByTmdbId.TryGetValue(tmdbId, out itemToUpdate);

            if (itemToUpdate == null && imdbId != null) CurrentItemsByImdbId.TryGetValue(imdbId, out itemToUpdate);

            if (itemToUpdate == null && tvdbId != null) CurrentItemsByTvdbId.TryGetValue(tvdbId, out itemToUpdate);

            if (itemToUpdate != null) itemToUpdate.OriginalLanguage = originalLanguage;
        }

        private static ContextItem GetAndRemoveItem()
        {
            var lookupItem = CurrentLookupItem.Value;
            CurrentLookupItem.Value = null;

            if (lookupItem == null) return null;

            ContextItem foundItem = null;

            if (lookupItem.TmdbId != null)
            {
                CurrentItemsByTmdbId.TryRemove(lookupItem.TmdbId, out foundItem);
            }

            if (foundItem == null && lookupItem.ImdbId != null)
            {
                CurrentItemsByImdbId.TryRemove(lookupItem.ImdbId, out foundItem);
            }

            if (foundItem == null && lookupItem.TvdbId != null)
            {
                CurrentItemsByTvdbId.TryRemove(lookupItem.TvdbId, out foundItem);
            }

            return foundItem;
        }

        private static string GetOriginalLanguage(BaseItem item)
        {
            var itemLookup = GetAndRemoveItem();

            if (itemLookup != null && !string.IsNullOrEmpty(itemLookup.OriginalLanguage))
                return itemLookup.OriginalLanguage;

            var fallbackItem = item is Movie || item is Series ? item : item is Season season ? season.Series : null;

            if (fallbackItem != null)
            {
                return LanguageUtility.GetLanguageByTitle(fallbackItem.OriginalTitle);
            }

            if (item is BoxSet collection)
            {
                return Plugin.MetadataApi.GetCollectionOriginalLanguage(collection);
            }

            return null;
        }

        [HarmonyPostfix]
        private static void GetMovieInfoTmdbPostfix(BaseItem item, string language, IJsonSerializer jsonSerializer,
            CancellationToken cancellationToken, Task __result)
        {
            __result.ContinueWith(task =>
                {
                    if (task.IsCompletedSuccessfully)
                    {
                        if (_movieDataTmdbTaskResult == null)
                            _movieDataTmdbTaskResult = task.GetType().GetProperty("Result");

                        var movieData = _movieDataTmdbTaskResult?.GetValue(task);
                        if (movieData != null && _tmdbIdMovieDataTmdb != null && _imdbIdMovieDataTmdb != null && _originalLanguageMovieDataTmdb != null)
                        {
                            var tmdbId = _tmdbIdMovieDataTmdb.GetValue(movieData).ToString();
                            var imdbId = _imdbIdMovieDataTmdb.GetValue(movieData) as string;
                            var originalLanguage = _originalLanguageMovieDataTmdb.GetValue(movieData) as string;
                            if ((!string.IsNullOrEmpty(tmdbId) || !string.IsNullOrEmpty(imdbId)) &&
                                !string.IsNullOrEmpty(originalLanguage))
                            {
                                UpdateOriginalLanguage(tmdbId, imdbId, null, originalLanguage);
                            }
                        }
                    }
                }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                .ConfigureAwait(false);
        }

        [HarmonyPostfix]
        private static void EnsureSeriesInfoTmdbPostfix(string tmdbId, string language, CancellationToken cancellationToken,
            Task __result)
        {
            if (WasCalledByMethod(_movieDbAssembly, "FetchImages")) WasCalledByImageProvider.Value = true;

            __result.ContinueWith(task =>
                {
                    if (task.IsCompletedSuccessfully && WasCalledByImageProvider.Value)
                    {
                        if (_seriesDataTmdbTaskResult == null)
                            _seriesDataTmdbTaskResult = task.GetType().GetProperty("Result");

                        var seriesInfo = _seriesDataTmdbTaskResult?.GetValue(task);
                        if (seriesInfo != null && _tmdbIdSeriesDataTmdb != null &&
                            _languagesSeriesDataTmdb != null)
                        {
                            var id = _tmdbIdSeriesDataTmdb.GetValue(seriesInfo).ToString();
                            var originalLanguage =
                                (_languagesSeriesDataTmdb.GetValue(seriesInfo) as List<string>)
                                ?.FirstOrDefault();
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(originalLanguage))
                            {
                                UpdateOriginalLanguage(id, null, null, originalLanguage);
                            }
                        }
                    }
                }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                .ConfigureAwait(false);
        }

        [HarmonyPostfix]
        private static void EnsureMovieInfoTvdbPostfix(string tvdbId, IDirectoryService directoryService,
            CancellationToken cancellationToken, Task __result)
        {
            if (WasCalledByMethod(_tvdbAssembly, "GetImages")) WasCalledByImageProvider.Value = true;

            __result.ContinueWith(task =>
                {
                    if (task.IsCompletedSuccessfully && WasCalledByImageProvider.Value)
                    {
                        if (_movieDataTvdbTaskResult == null)
                            _movieDataTvdbTaskResult = task.GetType().GetProperty("Result");

                        var movieData = _movieDataTvdbTaskResult?.GetValue(task);
                        if (movieData != null && _tvdbIdMovieDataTvdb != null &&
                            _originalLanguageMovieDataTvdb != null)
                        {
                            var id = _tvdbIdMovieDataTvdb.GetValue(movieData).ToString();
                            var originalLanguage = _originalLanguageMovieDataTvdb.GetValue(movieData) as string;
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(originalLanguage))
                            {
                                var convertedLanguage = Plugin.MetadataApi.ConvertToServerLanguage(originalLanguage);
                                UpdateOriginalLanguage(null, null, id, convertedLanguage);
                            }
                        }
                    }
                }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                .ConfigureAwait(false);
        }

        [HarmonyPostfix]
        private static void EnsureSeriesInfoTvdbPostfix(string tvdbId, IDirectoryService directoryService,
            CancellationToken cancellationToken, Task __result)
        {
            if (WasCalledByMethod(_tvdbAssembly, "GetImages")) WasCalledByImageProvider.Value = true;

            __result.ContinueWith(task =>
                {
                    if (task.IsCompletedSuccessfully && WasCalledByImageProvider.Value)
                    {
                        if (_seriesDataTvdbTaskResult == null)
                            _seriesDataTvdbTaskResult = task.GetType().GetProperty("Result");

                        var seriesData = _seriesDataTvdbTaskResult?.GetValue(task);
                        if (seriesData != null && _tvdbIdSeriesDataTvdb != null &&
                            _originalLanguageSeriesDataTvdb != null)
                        {
                            var id = _tvdbIdSeriesDataTvdb.GetValue(seriesData).ToString();
                            var originalLanguage = _originalLanguageSeriesDataTvdb.GetValue(seriesData) as string;
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(originalLanguage))
                            {
                                var convertedLanguage = Plugin.MetadataApi.ConvertToServerLanguage(originalLanguage);
                                UpdateOriginalLanguage(null, null, id, convertedLanguage);
                            }
                        }
                    }
                }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                .ConfigureAwait(false);
        }

        [HarmonyPrefix]
        private static bool GetAvailableRemoteImagesPrefix(IHasProviderIds item, LibraryOptions libraryOptions,
            ref RemoteImageQuery query, IDirectoryService directoryService, CancellationToken cancellationToken)
        {
            query.IncludeAllLanguages = true;

            var tmdbId = item.GetProviderId(MetadataProviders.Tmdb);
            var imdbId = item.GetProviderId(MetadataProviders.Imdb);
            var tvdbId = item.GetProviderId(MetadataProviders.Tvdb);

            AddContextItem(tmdbId, imdbId, tvdbId);

            return true;
        }

        [HarmonyPostfix]
        private static void GetAvailableRemoteImagesPostfix(BaseItem item, LibraryOptions libraryOptions,
            ref RemoteImageQuery query, IDirectoryService directoryService, CancellationToken cancellationToken,
            Task<IEnumerable<RemoteImageInfo>> __result)
        {
            __result.ContinueWith(task =>
                {
                    if (task.IsCompletedSuccessfully)
                    {
                        var originalLanguage = GetOriginalLanguage(item);
                        var libraryPreferredImageLanguage = libraryOptions.PreferredImageLanguage?.Split('-')[0];

                        var remoteImages = task.Result;

                        var reorderedImages = remoteImages.OrderBy(i =>
                                !string.IsNullOrEmpty(libraryPreferredImageLanguage) && string.Equals(i.Language,
                                    libraryPreferredImageLanguage, StringComparison.OrdinalIgnoreCase) ? 0 :
                                !string.IsNullOrEmpty(originalLanguage) && string.Equals(i.Language, originalLanguage,
                                    StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                            .ToList();

                        _remoteImageTaskResult?.SetValue(__result, reorderedImages);
                    }
                }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                .ConfigureAwait(false);
        }
    }
}
