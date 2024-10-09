using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
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
    internal class ContextItem
    {
        public string TmdbId { get; set; }
        public string ImdbId { get; set; }
        public string OriginalLanguage { get; set; }
    }

    public static class PreferOriginalPoster
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static Assembly _movieDbAssembly;
        private static MethodInfo _getMovieInfo;
        private static PropertyInfo _tmdbIdMovieDataProperty;
        private static PropertyInfo _imdbIdMovieDataProperty;
        private static PropertyInfo _originalLanguageMovieDataProperty;

        private static MethodInfo _ensureSeriesInfo;
        private static MethodInfo _getAvailableRemoteImages;
        private static PropertyInfo _tmdbIdSeriesInfoProperty;
        private static PropertyInfo _languagesSeriesInfoProperty;

        private static PropertyInfo _movieDataTaskResultProperty;
        private static PropertyInfo _seriesInfoTaskResultProperty;
        private static FieldInfo _remoteImageTaskResult;

        private static readonly ConcurrentDictionary<string, ContextItem> CurrentItemsByTmdbId =
            new ConcurrentDictionary<string, ContextItem>();
        private static readonly ConcurrentDictionary<string, ContextItem> CurrentItemsByImdbId =
            new ConcurrentDictionary<string, ContextItem>();
        private static readonly AsyncLocal<ContextItem> CurrentLookupItem = new AsyncLocal<ContextItem>();

        private static readonly AsyncLocal<bool> WasCalledByFetchImages = new AsyncLocal<bool>();

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
                    _tmdbIdMovieDataProperty = completeMovieData.GetProperty("id");
                    _imdbIdMovieDataProperty = completeMovieData.GetProperty("imdb_id");
                    _originalLanguageMovieDataProperty = completeMovieData.GetProperty("original_language");

                    var movieDbSeriesProvider = _movieDbAssembly.GetType("MovieDb.MovieDbSeriesProvider");
                    _ensureSeriesInfo = movieDbSeriesProvider.GetMethod("EnsureSeriesInfo",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    var seriesRootObject = movieDbSeriesProvider.GetNestedType("SeriesRootObject", BindingFlags.Public);
                    _tmdbIdSeriesInfoProperty = seriesRootObject.GetProperty("id");
                    _languagesSeriesInfoProperty = seriesRootObject.GetProperty("languages");

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
                    
                    _remoteImageTaskResult = typeof(Task<IEnumerable<RemoteImageInfo>>).GetField("m_result", BindingFlags.NonPublic | BindingFlags.Instance);
                }
                else
                {
                    Plugin.Instance.logger.Info("OriginalPoster - MovieDb plugin is not installed");
                }
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("OriginalPoster - Patch Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.GetPluginOptions().ModOptions.PreferOriginalPoster &&
                Plugin.Instance.GetPluginOptions().ModOptions.IsMovieDbPluginLoaded)
            {
                Patch();
            }
        }

        public static void Patch()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (!IsPatched(_getMovieInfo, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Patch(_getMovieInfo,
                            postfix: new HarmonyMethod(typeof(PreferOriginalPoster).GetMethod(
                                "GetMovieInfoPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch MovieDbImageProvider.GetMovieInfo Success by Harmony");
                    }
                    if (!IsPatched(_ensureSeriesInfo, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Patch(_ensureSeriesInfo,
                            postfix: new HarmonyMethod(typeof(PreferOriginalPoster).GetMethod(
                                "EnsureSeriesInfoPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch MovieDbSeriesProvider.EnsureSeriesInfo Success by Harmony");
                    }
                    if (!IsPatched(_getAvailableRemoteImages, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Patch(_getAvailableRemoteImages,
                            prefix: new HarmonyMethod(typeof(PreferOriginalPoster).GetMethod(
                                "GetAvailableRemoteImagesPrefix", BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(PreferOriginalPoster).GetMethod(
                                "GetAvailableRemoteImagesPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch GetAvailableRemoteImages Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch PreferOriginalPoster Failed by Harmony");
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
                    if (IsPatched(_getAvailableRemoteImages, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Unpatch(_getAvailableRemoteImages,
                            AccessTools.Method(typeof(PreferOriginalPoster), "GetAvailableRemoteImagesPrefix"));
                        HarmonyMod.Unpatch(_getAvailableRemoteImages,
                            AccessTools.Method(typeof(PreferOriginalPoster), "GetAvailableRemoteImagesPostfix"));
                        Plugin.Instance.logger.Debug("Unpatch GetAvailableRemoteImages Success by Harmony");
                    }
                    if (IsPatched(_ensureSeriesInfo, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Unpatch(_ensureSeriesInfo,
                            AccessTools.Method(typeof(PreferOriginalPoster), "EnsureSeriesInfoPostfix"));
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeriesProvider.EnsureSeriesInfo Success by Harmony");
                    }
                    if (IsPatched(_getMovieInfo, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Unpatch(_getMovieInfo,
                            AccessTools.Method(typeof(PreferOriginalPoster), "GetMovieInfoPostfix"));
                        Plugin.Instance.logger.Debug("Unpatch MovieDbImageProvider.GetMovieInfo Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch PreferOriginalPoster Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        private static void AddContextItem(string tmdbId, string imdbId)
        {
            if (tmdbId == null && imdbId == null) return;

            var item = new ContextItem { TmdbId = tmdbId, ImdbId = imdbId };

            if (tmdbId != null) CurrentItemsByTmdbId[tmdbId] = item;

            if (imdbId != null) CurrentItemsByImdbId[imdbId] = item;

            CurrentLookupItem.Value = new ContextItem { TmdbId = tmdbId, ImdbId = imdbId };
        }

        private static void UpdateOriginalLanguage(string tmdbId, string imdbId, string originalLanguage)
        {
            ContextItem itemToUpdate = null;

            if (tmdbId != null) CurrentItemsByTmdbId.TryGetValue(tmdbId, out itemToUpdate);

            if (itemToUpdate == null && imdbId != null) CurrentItemsByImdbId.TryGetValue(imdbId, out itemToUpdate);

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

            return foundItem;
        }

        private static string GetOriginalLanguage(BaseItem item)
        {
            if (item is Movie || item is Series)
            {
                var itemLookup = GetAndRemoveItem();
                return itemLookup?.OriginalLanguage;
            }

            if (item is Season season)
            {
                var series = season.Series;
                return LanguageUtility.IsChinese(series.OriginalTitle) ? "cn" :
                    LanguageUtility.IsJapanese(series.OriginalTitle) ? "jp" :
                    LanguageUtility.IsKorean(series.OriginalTitle) ? "ko" : "en";
            }

            return null;
        }

        [HarmonyPostfix]
        private static void GetMovieInfoPostfix(BaseItem item, string language, IJsonSerializer jsonSerializer,
            CancellationToken cancellationToken, Task __result)
        {
            __result.ContinueWith(task =>
                {
                    if (_movieDataTaskResultProperty == null)
                        _movieDataTaskResultProperty = task.GetType().GetProperty("Result");

                    var movieData = _movieDataTaskResultProperty?.GetValue(task);
                    if (movieData != null && _tmdbIdMovieDataProperty!=null && _imdbIdMovieDataProperty!=null && _originalLanguageMovieDataProperty!=null)
                    {
                        var tmdbId = _tmdbIdMovieDataProperty.GetValue(movieData).ToString();
                        var imdbId = _imdbIdMovieDataProperty.GetValue(movieData) as string;
                        var originalLanguage = _originalLanguageMovieDataProperty.GetValue(movieData) as string;
                        if ((!string.IsNullOrEmpty(tmdbId) || !string.IsNullOrEmpty(imdbId)) &&
                            !string.IsNullOrEmpty(originalLanguage))
                        {
                            UpdateOriginalLanguage(tmdbId, imdbId, originalLanguage);
                        }
                    }
                }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                .ConfigureAwait(false);
        }

        [HarmonyPostfix]
        private static void EnsureSeriesInfoPostfix(string tmdbId, string language, CancellationToken cancellationToken,
            Task __result)
        {
            if (WasCalledByMethod(_movieDbAssembly, "FetchImages")) WasCalledByFetchImages.Value = true;

            __result.ContinueWith(task =>
                {
                    if (WasCalledByFetchImages.Value)
                    {
                        if (_seriesInfoTaskResultProperty == null)
                            _seriesInfoTaskResultProperty = task.GetType().GetProperty("Result");

                        var seriesInfo = _seriesInfoTaskResultProperty?.GetValue(task);
                        if (seriesInfo != null && _tmdbIdSeriesInfoProperty != null &&
                            _languagesSeriesInfoProperty != null)
                        {
                            var id = _tmdbIdSeriesInfoProperty.GetValue(seriesInfo).ToString();
                            var originalLanguage =
                                (_languagesSeriesInfoProperty.GetValue(seriesInfo) as List<string>)
                                ?.FirstOrDefault();
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(originalLanguage))
                            {
                                UpdateOriginalLanguage(id, null, originalLanguage);
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
            query.IncludeAllLanguages=true;

            var tmdbId = item.GetProviderId(MetadataProviders.Tmdb);
            var imdbId = item.GetProviderId(MetadataProviders.Imdb);
            AddContextItem(tmdbId, imdbId);

            return true;
        }

        [HarmonyPostfix]
        private static void GetAvailableRemoteImagesPostfix(BaseItem item, LibraryOptions libraryOptions,
            ref RemoteImageQuery query, IDirectoryService directoryService, CancellationToken cancellationToken,
            Task<IEnumerable<RemoteImageInfo>> __result)
        {
            __result.ContinueWith(task =>
                {
                    if (task.IsCompleted)
                    {
                        var originalLanguage = GetOriginalLanguage(item);
                        var remoteImages = task.Result;

                        if (!string.IsNullOrEmpty(originalLanguage))
                        {
                            var orderedImages = remoteImages
                                .OrderBy(i =>
                                    string.Equals(i.Language, originalLanguage, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                                .ToList();
                            _remoteImageTaskResult?.SetValue(__result, orderedImages);
                        }
                    }
                }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                .ConfigureAwait(false);
        }
    }
}
