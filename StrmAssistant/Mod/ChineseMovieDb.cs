using HarmonyLib;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static StrmAssistant.PatchManager;

namespace StrmAssistant
{
    public static class ChineseMovieDb
    {
        private static MethodInfo _genericMovieDbInfoIsCompleteMovie;
        private static MethodInfo _genericMovieDbInfoProcessMainInfoMovie;
        private static MethodInfo _genericMovieDbInfoIsCompleteSeries;
        private static MethodInfo _genericMovieDbInfoProcessMainInfoSeries;

        private static Type _movieDbProviderBase;
        private static MethodInfo _getMovieDbMetadataLanguages;
        private static MethodInfo _getImageLanguagesParam;
        private static MethodInfo _movieDbSeriesProviderIsComplete;
        private static MethodInfo _movieDbSeriesProviderImportData;
        
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
                    _getImageLanguagesParam = _movieDbProviderBase.GetMethod("GetImageLanguagesParam",
                        BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string[]) }, null);

                    var movieDbSeriesProvider = movieDbAssembly.GetType("MovieDb.MovieDbSeriesProvider");
                    _movieDbSeriesProviderIsComplete =
                        movieDbSeriesProvider.GetMethod("IsComplete", BindingFlags.NonPublic | BindingFlags.Instance);
                    _movieDbSeriesProviderImportData =
                        movieDbSeriesProvider.GetMethod("ImportData", BindingFlags.NonPublic | BindingFlags.Instance);
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
                FallbackPatchApproach = PatchApproach.None;
            }

            if (FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.GetPluginOptions().ModOptions.ChineseMovieDb &&
                Plugin.Instance.GetPluginOptions().ModOptions.IsMovieDbPluginLoaded)
            {
                Patch();
            }
        }

        public static void Patch()
        {
            if (FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (Plugin.Instance.GetPluginOptions().ModOptions.IsMovieDbPluginLoaded)
                    {
                        if (!IsPatched(_genericMovieDbInfoIsCompleteMovie))
                        {
                            Mod.Patch(_genericMovieDbInfoIsCompleteMovie,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "GenericIsCompleteMoviePrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch GenericMovieDbInfo.IsComplete for Movie Success by Harmony");
                        }

                        if (!IsPatched(_genericMovieDbInfoProcessMainInfoMovie))
                        {
                            Mod.Patch(_genericMovieDbInfoProcessMainInfoMovie,
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
                            Mod.Patch(_genericMovieDbInfoIsCompleteSeries,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "GenericIsCompleteSeriesPrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch GenericMovieDbInfo.IsComplete for Series Success by Harmony");
                        }

                        if (!IsPatched(_genericMovieDbInfoProcessMainInfoSeries))
                        {
                            Mod.Patch(_genericMovieDbInfoProcessMainInfoSeries,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod("ProcessMainInfoSeriesPrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch GenericMovieDbInfo.ProcessMainInfo for Series Success by Harmony");
                        }
                        */

                        if (!IsPatched(_getMovieDbMetadataLanguages))
                        {
                            Mod.Patch(_getMovieDbMetadataLanguages,
                                postfix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "MetadataLanguagesPostfix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbProviderBase.GetMovieDbMetadataLanguages Success by Harmony");
                        }

                        if (!IsPatched(_getImageLanguagesParam))
                        {
                            Mod.Patch(_getImageLanguagesParam,
                                postfix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "GetImageLanguagesParamPostfix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbProviderBase.GetImageLanguagesParam Success by Harmony");
                        }

                        if (!IsPatched(_movieDbSeriesProviderIsComplete))
                        {
                            Mod.Patch(_movieDbSeriesProviderIsComplete,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "SeriesIsCompletePrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbSeriesProvider.IsComplete Success by Harmony");
                        }

                        if (!IsPatched(_movieDbSeriesProviderImportData))
                        {
                            Mod.Patch(_movieDbSeriesProviderImportData,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod("ImportDataPrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbSeriesProvider.ImportData Success by Harmony");
                        }
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch MovieDb Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                    FallbackPatchApproach = PatchApproach.Reflection;
                }
            }
        }

        public static void Unpatch()
        {
            if (FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (IsPatched(_genericMovieDbInfoIsCompleteMovie))
                    {
                        Mod.Unpatch(_genericMovieDbInfoIsCompleteMovie, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch GenericMovieDbInfo.IsComplete for Movie Success by Harmony");
                    }

                    if (IsPatched(_genericMovieDbInfoProcessMainInfoMovie))
                    {
                        Mod.Unpatch(_genericMovieDbInfoProcessMainInfoMovie, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch GenericMovieDbInfo.ProcessMainInfo for Movie Success by Harmony");
                    }

                    //if (IsPatched(_genericMovieDbInfoIsCompleteSeries))
                    //{
                    //    PatchManager.Mod.Unpatch(_genericMovieDbInfoIsCompleteSeries, HarmonyPatchType.Prefix);
                    //    Plugin.Instance.logger.Debug("Unpatch GenericMovieDbInfo.IsComplete for Series Success by Harmony");
                    //}

                    //if (PatchManager.IsPatched(_genericMovieDbInfoProcessMainInfoSeries))
                    //{
                    //    PatchManager.Mod.Unpatch(_genericMovieDbInfoProcessMainInfoSeries, HarmonyPatchType.Prefix);
                    //    Plugin.Instance.logger.Debug("Unpatch GenericMovieDbInfo.ProcessMainInfo for Series Success by Harmony");
                    //}

                    if (IsPatched(_getMovieDbMetadataLanguages))
                    {
                        Mod.Unpatch(_getMovieDbMetadataLanguages, HarmonyPatchType.Postfix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbProviderBase.GetMovieDbMetadataLanguages Success by Harmony");
                    }

                    if (IsPatched(_getImageLanguagesParam))
                    {
                        Mod.Unpatch(_getImageLanguagesParam, HarmonyPatchType.Postfix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbProviderBase.GetImageLanguagesParam Success by Harmony");
                    }

                    if (IsPatched(_movieDbSeriesProviderIsComplete))
                    {
                        Mod.Unpatch(_movieDbSeriesProviderIsComplete, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeriesProvider.IsComplete Success by Harmony");
                    }

                    if (IsPatched(_movieDbSeriesProviderImportData))
                    {
                        Mod.Unpatch(_movieDbSeriesProviderImportData, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeriesProvider.ImportData Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch MovieDb Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                    FallbackPatchApproach = PatchApproach.Reflection;
                }
            }
        }

        private static bool IsChinese(string title) => new Regex(@"[\u4e00-\u9fff]").IsMatch(title);

        [HarmonyPrefix]
        private static bool GenericIsCompleteMoviePrefix(Movie item, ref bool __result)
        {
            if (!IsChinese(item.Name))
            {
                __result = false;
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool ProcessMainInfoMoviePrefix(MetadataResult<Movie> resultItem, object settings,
            string preferredCountryCode, object movieData, bool isFirstLanguage)
        {
            var item = resultItem.Item;

            if (string.IsNullOrEmpty(item.Name) || !IsChinese(item.Name))
            {
                var getTitleMethod = movieData.GetType().GetMethod("GetTitle");
                if (getTitleMethod != null)
                {
                    var newTitle = getTitleMethod.Invoke(movieData, null);
                    item.Name = newTitle as string;
                }
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool GenericIsCompleteSeriesPrefix(Series item, ref bool __result)
        {
            if (!IsChinese(item.Name))
            {
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

            if (string.IsNullOrEmpty(item.Name) || !IsChinese(item.Name))
            {
                var getTitleMethod = movieData.GetType().GetMethod("GetTitle");
                if (getTitleMethod != null)
                {
                    var newTitle = getTitleMethod.Invoke(movieData, null);
                    item.Name = newTitle as string;
                }
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool SeriesIsCompletePrefix(Series item, ref bool __result)
        {
            if (!IsChinese(item.Name))
            {
                __result = false;
                return false;
            }

            return true;
        }
        
        [HarmonyPrefix]
        private static bool ImportDataPrefix(MetadataResult<Series> seriesResult, object seriesInfo,
            string preferredCountryCode, object settings, bool isFirstLanguage)
        {
            var item = seriesResult.Item;

            if (string.IsNullOrEmpty(item.Name) || !IsChinese(item.Name))
            {
                var getTitleMethod = seriesInfo.GetType().GetMethod("GetTitle");
                if (getTitleMethod != null)
                {
                    var newTitle = getTitleMethod.Invoke(seriesInfo, null);
                    item.Name = newTitle as string;
                }
            }

            return true;
        }

        [HarmonyPostfix]
        private static void MetadataLanguagesPostfix(object __instance, ItemLookupInfo searchInfo,
            string[] providerLanguages, ref string[] __result)
        {
            List<string> list = __result.ToList();

            if (list.Any(l => l.StartsWith("zh", StringComparison.OrdinalIgnoreCase)))
            {
                int index = list.FindIndex(l => string.Equals(l, "en", StringComparison.OrdinalIgnoreCase) ||
                                                string.Equals(l, "en-us", StringComparison.OrdinalIgnoreCase));

                var currentFallbackLanguages = Plugin.Instance.GetPluginOptions().ModOptions.FallbackLanguages
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();

                foreach (var fallbackLanguage in currentFallbackLanguages)
                {
                    if (!list.Contains(fallbackLanguage, StringComparer.OrdinalIgnoreCase))
                    {
                        MethodInfo mapLanguageToProviderLanguage = _movieDbProviderBase.GetMethod(
                            "MapLanguageToProviderLanguage",
                            BindingFlags.NonPublic | BindingFlags.Instance);

                        if (mapLanguageToProviderLanguage != null)
                        {
                            string mappedLanguage = (string)mapLanguageToProviderLanguage.Invoke(__instance,
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
