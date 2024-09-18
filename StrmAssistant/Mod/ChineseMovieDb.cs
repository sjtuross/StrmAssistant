using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class ChineseMovieDb
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static MethodInfo _genericMovieDbInfoIsCompleteMovie;
        private static MethodInfo _genericMovieDbInfoProcessMainInfoMovie;
        private static MethodInfo _genericMovieDbInfoIsCompleteSeries;
        private static MethodInfo _genericMovieDbInfoProcessMainInfoSeries;

        private static Type _movieDbProviderBase;
        private static MethodInfo _getMovieDbMetadataLanguages;
        private static MethodInfo _mapLanguageToProviderLanguage;
        private static MethodInfo _getImageLanguagesParam;

        private static MethodInfo _movieDbSeriesProviderIsComplete;
        private static MethodInfo _movieDbSeriesProviderImportData;

        private static MethodInfo _movieDbSeasonProviderIsComplete;
        private static MethodInfo _movieDbSeasonProviderImportData;

        private static MethodInfo _movieDbEpisodeProviderIsComplete;
        private static MethodInfo _movieDbEpisodeProviderImportData;

        public static void Initialize()
        {
            try
            {
                var movieDbAssembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "MovieDb");

                if (movieDbAssembly != null)
                {
                    var genericMovieDbInfo = movieDbAssembly.GetType("MovieDb.GenericMovieDbInfo`1");

                    var genericMovieDbInfoMovie = genericMovieDbInfo.MakeGenericType(typeof(Movie));
                    _genericMovieDbInfoIsCompleteMovie = genericMovieDbInfoMovie.GetMethod("IsComplete",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _genericMovieDbInfoProcessMainInfoMovie = genericMovieDbInfoMovie.GetMethod("ProcessMainInfo",
                        BindingFlags.NonPublic | BindingFlags.Instance);

                    var genericMovieDbInfoSeries = genericMovieDbInfo.MakeGenericType(typeof(Series));
                    _genericMovieDbInfoIsCompleteSeries = genericMovieDbInfoSeries.GetMethod("IsComplete",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _genericMovieDbInfoProcessMainInfoSeries = genericMovieDbInfoSeries.GetMethod("ProcessMainInfo",
                        BindingFlags.NonPublic | BindingFlags.Instance);

                    _movieDbProviderBase = movieDbAssembly.GetType("MovieDb.MovieDbProviderBase");
                    _getMovieDbMetadataLanguages = _movieDbProviderBase.GetMethod("GetMovieDbMetadataLanguages",
                        BindingFlags.Public | BindingFlags.Instance);
                    _mapLanguageToProviderLanguage = _movieDbProviderBase.GetMethod("MapLanguageToProviderLanguage",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _getImageLanguagesParam = _movieDbProviderBase.GetMethod("GetImageLanguagesParam",
                        BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string[]) }, null);

                    var movieDbSeriesProvider = movieDbAssembly.GetType("MovieDb.MovieDbSeriesProvider");
                    _movieDbSeriesProviderIsComplete =
                        movieDbSeriesProvider.GetMethod("IsComplete", BindingFlags.NonPublic | BindingFlags.Instance);
                    _movieDbSeriesProviderImportData =
                        movieDbSeriesProvider.GetMethod("ImportData", BindingFlags.NonPublic | BindingFlags.Instance);
                    
                    var movieDbSeasonProvider = movieDbAssembly.GetType("MovieDb.MovieDbSeasonProvider");
                    _movieDbSeasonProviderIsComplete =
                        movieDbSeasonProvider.GetMethod("IsComplete", BindingFlags.NonPublic | BindingFlags.Instance);
                    _movieDbSeasonProviderImportData =
                        movieDbSeasonProvider.GetMethod("ImportData", BindingFlags.NonPublic | BindingFlags.Instance);

                    var movieDbEpisodeProvider = movieDbAssembly.GetType("MovieDb.MovieDbEpisodeProvider");
                    _movieDbEpisodeProviderIsComplete =
                        movieDbEpisodeProvider.GetMethod("IsComplete", BindingFlags.NonPublic | BindingFlags.Instance);
                    _movieDbEpisodeProviderImportData =
                        movieDbEpisodeProvider.GetMethod("ImportData", BindingFlags.NonPublic | BindingFlags.Instance);
                }
                else
                {
                    Plugin.Instance.logger.Info("ChineseMovieDb - MovieDb plugin is not installed");
                }
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("ChineseMovieDb - Patch Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null)
            {
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;
            }

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.GetPluginOptions().ModOptions.ChineseMovieDb &&
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
                    if (Plugin.Instance.GetPluginOptions().ModOptions.IsMovieDbPluginLoaded)
                    {
                        if (!IsPatched(_genericMovieDbInfoIsCompleteMovie))
                        {
                            HarmonyMod.Patch(_genericMovieDbInfoIsCompleteMovie,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "IsCompletePrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch GenericMovieDbInfo.IsComplete for Movie Success by Harmony");
                        }

                        if (!IsPatched(_genericMovieDbInfoProcessMainInfoMovie))
                        {
                            HarmonyMod.Patch(_genericMovieDbInfoProcessMainInfoMovie,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod("ProcessMainInfoMoviePrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch GenericMovieDbInfo.ProcessMainInfo for Movie Success by Harmony");
                        }

                        /* Not actually needed because a non-generic class is only patched with the last generic concrete type
                         * It's fine here because the patch logic is the same and the intention is to patch the generic method
                         * https://github.com/pardeike/Harmony/issues/201#issuecomment-821980884
                        if (!IsPatched(_genericMovieDbInfoIsCompleteSeries))
                        {
                            HarmonyMod.Patch(_genericMovieDbInfoIsCompleteSeries,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "IsCompletePrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch GenericMovieDbInfo.IsComplete for Series Success by Harmony");
                        }

                        if (!IsPatched(_genericMovieDbInfoProcessMainInfoSeries))
                        {
                            HarmonyMod.Patch(_genericMovieDbInfoProcessMainInfoSeries,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod("ProcessMainInfoSeriesPrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch GenericMovieDbInfo.ProcessMainInfo for Series Success by Harmony");
                        }
                        */

                        if (!IsPatched(_getMovieDbMetadataLanguages))
                        {
                            HarmonyMod.Patch(_getMovieDbMetadataLanguages,
                                postfix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "MetadataLanguagesPostfix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbProviderBase.GetMovieDbMetadataLanguages Success by Harmony");
                        }

                        if (!IsPatched(_getImageLanguagesParam))
                        {
                            HarmonyMod.Patch(_getImageLanguagesParam,
                                postfix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "GetImageLanguagesParamPostfix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbProviderBase.GetImageLanguagesParam Success by Harmony");
                        }

                        if (!IsPatched(_movieDbSeriesProviderIsComplete))
                        {
                            HarmonyMod.Patch(_movieDbSeriesProviderIsComplete,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "IsCompletePrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbSeriesProvider.IsComplete Success by Harmony");
                        }

                        if (!IsPatched(_movieDbSeriesProviderImportData))
                        {
                            HarmonyMod.Patch(_movieDbSeriesProviderImportData,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod("SeriesImportDataPrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbSeriesProvider.ImportData Success by Harmony");
                        }

                        if (!IsPatched(_movieDbSeasonProviderIsComplete))
                        {
                            HarmonyMod.Patch(_movieDbSeasonProviderIsComplete,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "IsCompletePrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbSeasonProvider.IsComplete Success by Harmony");
                        }

                        if (!IsPatched(_movieDbSeasonProviderImportData))
                        {
                            HarmonyMod.Patch(_movieDbSeasonProviderImportData,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod("SeasonImportDataPrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbSeasonProvider.ImportData Success by Harmony");
                        }

                        if (!IsPatched(_movieDbEpisodeProviderIsComplete))
                        {
                            HarmonyMod.Patch(_movieDbEpisodeProviderIsComplete,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "IsCompletePrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbEpisodeProvider.IsComplete Success by Harmony");
                        }

                        if (!IsPatched(_movieDbEpisodeProviderImportData))
                        {
                            HarmonyMod.Patch(_movieDbEpisodeProviderImportData,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod("EpisodeImportDataPrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbEpisodeProvider.ImportData Success by Harmony");
                        }
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch MovieDb Failed by Harmony");
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
                    if (IsPatched(_genericMovieDbInfoIsCompleteMovie))
                    {
                        HarmonyMod.Unpatch(_genericMovieDbInfoIsCompleteMovie, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch GenericMovieDbInfo.IsComplete for Movie Success by Harmony");
                    }

                    if (IsPatched(_genericMovieDbInfoProcessMainInfoMovie))
                    {
                        HarmonyMod.Unpatch(_genericMovieDbInfoProcessMainInfoMovie, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch GenericMovieDbInfo.ProcessMainInfo for Movie Success by Harmony");
                    }

                    //if (IsPatched(_genericMovieDbInfoIsCompleteSeries))
                    //{
                    //    HarmonyMod.Unpatch(_genericMovieDbInfoIsCompleteSeries, HarmonyPatchType.Prefix);
                    //    Plugin.Instance.logger.Debug("Unpatch GenericMovieDbInfo.IsComplete for Series Success by Harmony");
                    //}

                    //if (IsPatched(_genericMovieDbInfoProcessMainInfoSeries))
                    //{
                    //    HarmonyMod.Unpatch(_genericMovieDbInfoProcessMainInfoSeries, HarmonyPatchType.Prefix);
                    //    Plugin.Instance.logger.Debug("Unpatch GenericMovieDbInfo.ProcessMainInfo for Series Success by Harmony");
                    //}

                    if (IsPatched(_getMovieDbMetadataLanguages))
                    {
                        HarmonyMod.Unpatch(_getMovieDbMetadataLanguages, HarmonyPatchType.Postfix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbProviderBase.GetMovieDbMetadataLanguages Success by Harmony");
                    }

                    if (IsPatched(_getImageLanguagesParam))
                    {
                        HarmonyMod.Unpatch(_getImageLanguagesParam, HarmonyPatchType.Postfix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbProviderBase.GetImageLanguagesParam Success by Harmony");
                    }

                    if (IsPatched(_movieDbSeriesProviderIsComplete))
                    {
                        HarmonyMod.Unpatch(_movieDbSeriesProviderIsComplete, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeriesProvider.IsComplete Success by Harmony");
                    }

                    if (IsPatched(_movieDbSeriesProviderImportData))
                    {
                        HarmonyMod.Unpatch(_movieDbSeriesProviderImportData, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeriesProvider.ImportData Success by Harmony");
                    }

                    if (IsPatched(_movieDbSeasonProviderIsComplete))
                    {
                        HarmonyMod.Unpatch(_movieDbSeasonProviderIsComplete, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeasonProvider.IsComplete Success by Harmony");
                    }

                    if (IsPatched(_movieDbSeasonProviderImportData))
                    {
                        HarmonyMod.Unpatch(_movieDbSeasonProviderImportData, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeasonProvider.ImportData Success by Harmony");
                    }

                    if (IsPatched(_movieDbEpisodeProviderIsComplete))
                    {
                        HarmonyMod.Unpatch(_movieDbEpisodeProviderIsComplete, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbEpisodeProvider.IsComplete Success by Harmony");
                    }

                    if (IsPatched(_movieDbEpisodeProviderImportData))
                    {
                        HarmonyMod.Unpatch(_movieDbEpisodeProviderImportData, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbEpisodeProvider.ImportData Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch MovieDb Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        private static bool IsChinese(string title) => new Regex(@"[\u4E00-\u9FFF]").IsMatch(title);

        private static bool IsJapanese(string title) => new Regex(@"[\u3040-\u309F\u30A0-\u30FF]").IsMatch(title);

        private static bool IsDefaultChineseEpisodeName(string name) => new Regex(@"第\s*\d+\s*集").IsMatch(name);

        private static bool IsUpdateNeeded(string name, bool isEpisode)
        {
            var isJapaneseFallback = GetFallbackLanguages().Contains("ja-jp", StringComparer.OrdinalIgnoreCase);

            return string.IsNullOrEmpty(name) || !isJapaneseFallback && !IsChinese(name) ||
                   isJapaneseFallback && !(IsChinese(name) && (!isEpisode || !IsDefaultChineseEpisodeName(name)) ||
                                           IsJapanese(name));
        }

        private static List<string> GetFallbackLanguages()
        {
            var currentFallbackLanguages = Plugin.Instance.GetPluginOptions().ModOptions.FallbackLanguages
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();

            return currentFallbackLanguages;
        }

        [HarmonyPrefix]
        private static bool ProcessMainInfoMoviePrefix(MetadataResult<Movie> resultItem, object settings,
            string preferredCountryCode, object movieData, bool isFirstLanguage)
        {
            var item = resultItem.Item;

            if (IsUpdateNeeded(item.Name, false))
            {
                var getTitleMethod = movieData.GetType().GetMethod("GetTitle");
                if (getTitleMethod != null)
                {
                    item.Name = getTitleMethod.Invoke(movieData, null) as string;
                }
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool IsCompletePrefix(BaseItem item, ref bool __result)
        {
            if (!GetFallbackLanguages().Contains("ja-jp", StringComparer.OrdinalIgnoreCase))
            {
                if (item is Season || item is Episode)
                {
                    if (!IsChinese(item.Name) || !IsChinese(item.Overview))
                    {
                        __result = false;
                        return false;
                    }
                }
                else
                {
                    if (!IsChinese(item.Name))
                    {
                        __result = false;
                        return false;
                    }
                }
            }
            else
            {
                if (item is Season || item is Episode)
                {
                    if (IsChinese(item.Name) && IsChinese(item.Overview) ||
                        IsJapanese(item.Name) && IsJapanese(item.Overview))
                    {
                        return true;
                    }
                }
                else
                {
                    if (IsChinese(item.Name) || IsJapanese(item.Name))
                    {
                        return true;
                    }
                }

                __result = false;
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool ProcessMainInfoSeriesPrefix(MetadataResult<Series> resultItem, object settings,
            string preferredCountryCode, object movieData, bool isFirstLanguage)
        {
            var item = resultItem.Item;

            if (IsUpdateNeeded(item.Name, false))
            {
                var getTitleMethod = movieData.GetType().GetMethod("GetTitle");
                if (getTitleMethod != null)
                {
                    item.Name = getTitleMethod.Invoke(movieData, null) as string;
                }
            }

            return true;
        }
        
        [HarmonyPrefix]
        private static bool SeriesImportDataPrefix(MetadataResult<Series> seriesResult, object seriesInfo,
            string preferredCountryCode, object settings, bool isFirstLanguage)
        {
            var item = seriesResult.Item;

            if (IsUpdateNeeded(item.Name, false))
            {
                var getTitleMethod = seriesInfo.GetType().GetMethod("GetTitle");
                if (getTitleMethod != null)
                {
                    item.Name = getTitleMethod.Invoke(seriesInfo, null) as string;
                }
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool SeasonImportDataPrefix(Season item, object seasonInfo, string name, int seasonNumber,
            bool isFirstLanguage)
        {
            if (IsUpdateNeeded(item.Name, false))
            {
                var nameProperty = seasonInfo.GetType().GetProperty("name");
                if (nameProperty != null)
                {
                    item.Name = nameProperty.GetValue(seasonInfo) as string;
                }
            }

            if (IsUpdateNeeded(item.Overview, false))
            {
                var overviewProperty = seasonInfo.GetType().GetProperty("overview");
                if (overviewProperty != null)
                {
                    item.Overview = overviewProperty.GetValue(seasonInfo) as string;
                }
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool EpisodeImportDataPrefix(MetadataResult<Episode> result, EpisodeInfo info, object response,
            object settings, bool isFirstLanguage)
        {
            var item = result.Item;

            if (IsUpdateNeeded(item.Name, true))
            {
                var nameProperty = response.GetType().GetProperty("name");
                if (nameProperty != null)
                {
                    item.Name = nameProperty.GetValue(response) as string;
                }
            }

            if (IsUpdateNeeded(item.Overview, true))
            {
                var overviewProperty = response.GetType().GetProperty("overview");
                if (overviewProperty != null)
                {
                    item.Overview = overviewProperty.GetValue(response) as string;
                }
            }

            return true;
        }

        [HarmonyPostfix]
        private static void MetadataLanguagesPostfix(object __instance, ItemLookupInfo searchInfo,
            string[] providerLanguages, ref string[] __result)
        {
            var list = __result.ToList();

            if (list.Any(l => l.StartsWith("zh", StringComparison.OrdinalIgnoreCase)))
            {
                var index = list.FindIndex(l => string.Equals(l, "en", StringComparison.OrdinalIgnoreCase) ||
                                                string.Equals(l, "en-us", StringComparison.OrdinalIgnoreCase));

                var currentFallbackLanguages = GetFallbackLanguages();

                foreach (var fallbackLanguage in currentFallbackLanguages)
                {
                    if (!list.Contains(fallbackLanguage, StringComparer.OrdinalIgnoreCase))
                    {
                        var mappedLanguage = (string)_mapLanguageToProviderLanguage.Invoke(__instance,
                            new object[] { fallbackLanguage, null, false, providerLanguages });

                        if (!string.IsNullOrEmpty(mappedLanguage))
                        {
                            if (index >= 0)
                            {
                                list.Insert(index, mappedLanguage);
                                index++;
                            }
                            else
                            {
                                list.Add(mappedLanguage);
                            }
                        }
                    }
                }
            }

            __result = list.ToArray();
        }

        [HarmonyPostfix]
        private static void GetImageLanguagesParamPostfix(ref string __result)
        {
            List<string> list = __result.Split(',').ToList();

            if (list.Any(i => i.StartsWith("zh")) && !list.Contains("zh"))
            {
                list.Insert(list.FindIndex(i => i.StartsWith("zh")) + 1, "zh");
            }

            __result = string.Join(",", list.ToArray());
        }
    }
}
