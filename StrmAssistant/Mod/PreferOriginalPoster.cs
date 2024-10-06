using HarmonyLib;
using MediaBrowser.Controller.Entities;
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
        private static MethodInfo _ensureSeriesInfo;
        private static MethodInfo _getAvailableRemoteImages;
        private static MethodInfo _indexOfIgnoreCase;

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
                    var movieDbSeriesProvider = _movieDbAssembly.GetType("MovieDb.MovieDbSeriesProvider");
                    _ensureSeriesInfo = movieDbSeriesProvider.GetMethod("EnsureSeriesInfo",
                        BindingFlags.Instance | BindingFlags.NonPublic);

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
                    _indexOfIgnoreCase =
                        providerManager.GetMethod("IndexOfIgnoreCase", BindingFlags.NonPublic | BindingFlags.Static);
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
                            "Patch MovieDbSeriesProvider.EnsureSeriesInfoPostfix Success by Harmony");
                    }
                    if (!IsPatched(_getAvailableRemoteImages, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Patch(_getAvailableRemoteImages,
                            prefix: new HarmonyMethod(typeof(PreferOriginalPoster).GetMethod(
                                "GetAvailableRemoteImagesPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch GetAvailableRemoteImages Success by Harmony");
                    }

                    if (!IsPatched(_indexOfIgnoreCase, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Patch(_indexOfIgnoreCase,
                            prefix: new HarmonyMethod(typeof(PreferOriginalPoster).GetMethod("IndexOfIgnoreCasePrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch IndexOfIgnoreCase Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch OriginalPoster Failed by Harmony");
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
                        HarmonyMod.Unpatch(_getAvailableRemoteImages, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch GetAvailableRemoteImages Success by Harmony");
                    }
                    if (IsPatched(_ensureSeriesInfo, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Unpatch(_ensureSeriesInfo, HarmonyPatchType.Postfix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeriesProvider.EnsureSeriesInfoPostfix Success by Harmony");
                    }
                    if (IsPatched(_getMovieInfo, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Unpatch(_getMovieInfo, HarmonyPatchType.Postfix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbImageProvider.GetMovieInfo Success by Harmony");
                    }
                    if (IsPatched(_indexOfIgnoreCase, typeof(PreferOriginalPoster)))
                    {
                        HarmonyMod.Unpatch(_indexOfIgnoreCase, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch IndexOfIgnoreCase Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch OriginalPoster Failed by Harmony");
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

        [HarmonyPostfix]
        private static void GetMovieInfoPostfix(BaseItem item, string language, IJsonSerializer jsonSerializer,
            CancellationToken cancellationToken, Task __result)
        {
            __result.ContinueWith(task =>
            {
                var result = task.GetType().GetProperty("Result")?.GetValue(task);
                if (result != null)
                {
                    var tmdbId = result.GetType().GetProperty("id")?.GetValue(result).ToString();
                    var imdbId = item.GetProviderId(MetadataProviders.Imdb);
                    var originalLanguage =
                        result.GetType().GetProperty("original_language")?.GetValue(result) as string;
                    UpdateOriginalLanguage(tmdbId, imdbId, originalLanguage);
                }
            }, cancellationToken);
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
                    var result = task.GetType().GetProperty("Result")?.GetValue(task);
                    if (result != null)
                    {
                        var id = result.GetType().GetProperty("id")?.GetValue(result).ToString();
                        var originalLanguage =
                            (result.GetType().GetProperty("languages")?.GetValue(result) as List<string>)
                            ?.FirstOrDefault();
                        UpdateOriginalLanguage(id, null, originalLanguage);
                    }
                }
            }, cancellationToken);
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

        [HarmonyPrefix]
        private static bool IndexOfIgnoreCasePrefix(List<string> list, ReadOnlySpan<char> value)
        {
            var lookupItem = GetAndRemoveItem();
            var originalLanguage = lookupItem?.OriginalLanguage;

            if (!string.IsNullOrEmpty(originalLanguage))
            {
                list.Remove(originalLanguage);
                list.Insert(0, originalLanguage);
            }

            return true;
        }

        [HarmonyPostfix]
        private static void GetAvailableRemoteImagesPostfix(BaseItem item, LibraryOptions libraryOptions,
            ref RemoteImageQuery query, IDirectoryService directoryService, CancellationToken cancellationToken,
            Task<IEnumerable<RemoteImageInfo>> __result)
        {
            __result.ContinueWith(task =>
            {
                var remoteImages = task.Result;
                var remoteImageList = remoteImages.ToList();

                return (IEnumerable<RemoteImageInfo>)remoteImageList;
            });
        }
    }
}
