using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.LanguageUtility;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class ChineseMovieDb
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static Assembly _movieDbAssembly;
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
        private static MethodInfo _ensureSeriesInfo;

        private static MethodInfo _movieDbSeasonProviderIsComplete;
        private static MethodInfo _movieDbSeasonProviderImportData;

        private static MethodInfo _movieDbEpisodeProviderIsComplete;
        private static MethodInfo _movieDbEpisodeProviderImportData;

        private static MethodInfo _movieDbPersonProviderImportData;

        private static readonly AsyncLocal<bool> WasCalledByFetchImages = new AsyncLocal<bool>();
        private static readonly AsyncLocal<string> RequestCountryCode = new AsyncLocal<string>();

        public static void Initialize()
        {
            try
            {
                _movieDbAssembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "MovieDb");

                if (_movieDbAssembly != null)
                {
                    var genericMovieDbInfo = _movieDbAssembly.GetType("MovieDb.GenericMovieDbInfo`1");

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

                    _movieDbProviderBase = _movieDbAssembly.GetType("MovieDb.MovieDbProviderBase");
                    _getMovieDbMetadataLanguages = _movieDbProviderBase.GetMethod("GetMovieDbMetadataLanguages",
                        BindingFlags.Public | BindingFlags.Instance);
                    _mapLanguageToProviderLanguage = _movieDbProviderBase.GetMethod("MapLanguageToProviderLanguage",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _getImageLanguagesParam = _movieDbProviderBase.GetMethod("GetImageLanguagesParam",
                        BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string[]) }, null);

                    var movieDbSeriesProvider = _movieDbAssembly.GetType("MovieDb.MovieDbSeriesProvider");
                    _movieDbSeriesProviderIsComplete =
                        movieDbSeriesProvider.GetMethod("IsComplete", BindingFlags.NonPublic | BindingFlags.Instance);
                    _movieDbSeriesProviderImportData =
                        movieDbSeriesProvider.GetMethod("ImportData", BindingFlags.NonPublic | BindingFlags.Instance);
                    _ensureSeriesInfo = movieDbSeriesProvider.GetMethod("EnsureSeriesInfo",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                    var movieDbSeasonProvider = _movieDbAssembly.GetType("MovieDb.MovieDbSeasonProvider");
                    _movieDbSeasonProviderIsComplete =
                        movieDbSeasonProvider.GetMethod("IsComplete", BindingFlags.NonPublic | BindingFlags.Instance);
                    _movieDbSeasonProviderImportData =
                        movieDbSeasonProvider.GetMethod("ImportData", BindingFlags.NonPublic | BindingFlags.Instance);

                    var movieDbEpisodeProvider = _movieDbAssembly.GetType("MovieDb.MovieDbEpisodeProvider");
                    _movieDbEpisodeProviderIsComplete =
                        movieDbEpisodeProvider.GetMethod("IsComplete", BindingFlags.NonPublic | BindingFlags.Instance);
                    _movieDbEpisodeProviderImportData =
                        movieDbEpisodeProvider.GetMethod("ImportData", BindingFlags.NonPublic | BindingFlags.Instance);

                    var movieDbPersonProvider = _movieDbAssembly.GetType("MovieDb.MovieDbPersonProvider");
                    _movieDbPersonProviderImportData = movieDbPersonProvider.GetMethod("ImportData",
                        BindingFlags.NonPublic | BindingFlags.Instance);
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

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

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
                        if (!IsPatched(_genericMovieDbInfoIsCompleteMovie, typeof(ChineseMovieDb)))
                        {
                            HarmonyMod.Patch(_genericMovieDbInfoIsCompleteMovie,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "IsCompletePrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch GenericMovieDbInfo.IsComplete for Movie Success by Harmony");
                        }
                        if (!IsPatched(_genericMovieDbInfoProcessMainInfoMovie, typeof(ChineseMovieDb)))
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
                        if (!IsPatched(_genericMovieDbInfoIsCompleteSeries, typeof(ChineseMovieDb)))
                        {
                            HarmonyMod.Patch(_genericMovieDbInfoIsCompleteSeries,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "IsCompletePrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch GenericMovieDbInfo.IsComplete for Series Success by Harmony");
                        }
                        if (!IsPatched(_genericMovieDbInfoProcessMainInfoSeries, typeof(ChineseMovieDb)))
                        {
                            HarmonyMod.Patch(_genericMovieDbInfoProcessMainInfoSeries,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod("ProcessMainInfoSeriesPrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch GenericMovieDbInfo.ProcessMainInfo for Series Success by Harmony");
                        }
                        */
                        if (!IsPatched(_getMovieDbMetadataLanguages, typeof(ChineseMovieDb)))
                        {
                            HarmonyMod.Patch(_getMovieDbMetadataLanguages,
                                postfix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "MetadataLanguagesPostfix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbProviderBase.GetMovieDbMetadataLanguages Success by Harmony");
                        }
                        if (!IsPatched(_getImageLanguagesParam, typeof(ChineseMovieDb)))
                        {
                            HarmonyMod.Patch(_getImageLanguagesParam,
                                postfix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "GetImageLanguagesParamPostfix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbProviderBase.GetImageLanguagesParam Success by Harmony");
                        }
                        if (!IsPatched(_movieDbSeriesProviderIsComplete, typeof(ChineseMovieDb)))
                        {
                            HarmonyMod.Patch(_movieDbSeriesProviderIsComplete,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "IsCompletePrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbSeriesProvider.IsComplete Success by Harmony");
                        }
                        if (!IsPatched(_movieDbSeriesProviderImportData, typeof(ChineseMovieDb)))
                        {
                            HarmonyMod.Patch(_movieDbSeriesProviderImportData,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod("SeriesImportDataPrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbSeriesProvider.ImportData Success by Harmony");
                        }
                        if (!IsPatched(_ensureSeriesInfo, typeof(ChineseMovieDb)))
                        {
                            HarmonyMod.Patch(_ensureSeriesInfo,
                                postfix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "EnsureSeriesInfoPostfix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbSeriesProvider.EnsureSeriesInfoPostfix Success by Harmony");
                        }
                        if (!IsPatched(_movieDbSeasonProviderIsComplete, typeof(ChineseMovieDb)))
                        {
                            HarmonyMod.Patch(_movieDbSeasonProviderIsComplete,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "IsCompletePrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbSeasonProvider.IsComplete Success by Harmony");
                        }
                        if (!IsPatched(_movieDbSeasonProviderImportData, typeof(ChineseMovieDb)))
                        {
                            HarmonyMod.Patch(_movieDbSeasonProviderImportData,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod("SeasonImportDataPrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbSeasonProvider.ImportData Success by Harmony");
                        }
                        if (!IsPatched(_movieDbEpisodeProviderIsComplete, typeof(ChineseMovieDb)))
                        {
                            HarmonyMod.Patch(_movieDbEpisodeProviderIsComplete,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod(
                                    "IsCompletePrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbEpisodeProvider.IsComplete Success by Harmony");
                        }
                        if (!IsPatched(_movieDbEpisodeProviderImportData, typeof(ChineseMovieDb)))
                        {
                            HarmonyMod.Patch(_movieDbEpisodeProviderImportData,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod("EpisodeImportDataPrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbEpisodeProvider.ImportData Success by Harmony");
                        }
                        if (!IsPatched(_movieDbPersonProviderImportData, typeof(ChineseMovieDb)))
                        {
                            HarmonyMod.Patch(_movieDbPersonProviderImportData,
                                prefix: new HarmonyMethod(typeof(ChineseMovieDb).GetMethod("PersonImportDataPrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug(
                                "Patch MovieDbPersonProvider.ImportData Success by Harmony");
                        }
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch ChineseMovieDb Failed by Harmony");
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
                    if (IsPatched(_genericMovieDbInfoIsCompleteMovie, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_genericMovieDbInfoIsCompleteMovie, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch GenericMovieDbInfo.IsComplete for Movie Success by Harmony");
                    }
                    if (IsPatched(_genericMovieDbInfoProcessMainInfoMovie, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_genericMovieDbInfoProcessMainInfoMovie, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch GenericMovieDbInfo.ProcessMainInfo for Movie Success by Harmony");
                    }
                    //if (IsPatched(_genericMovieDbInfoIsCompleteSeries, typeof(ChineseMovieDb)))
                    //{
                    //    HarmonyMod.Unpatch(_genericMovieDbInfoIsCompleteSeries, HarmonyPatchType.Prefix);
                    //    Plugin.Instance.logger.Debug("Unpatch GenericMovieDbInfo.IsComplete for Series Success by Harmony");
                    //}

                    //if (IsPatched(_genericMovieDbInfoProcessMainInfoSeries, typeof(ChineseMovieDb)))
                    //{
                    //    HarmonyMod.Unpatch(_genericMovieDbInfoProcessMainInfoSeries, HarmonyPatchType.Prefix);
                    //    Plugin.Instance.logger.Debug("Unpatch GenericMovieDbInfo.ProcessMainInfo for Series Success by Harmony");
                    //}
                    if (IsPatched(_getMovieDbMetadataLanguages, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_getMovieDbMetadataLanguages, HarmonyPatchType.Postfix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbProviderBase.GetMovieDbMetadataLanguages Success by Harmony");
                    }

                    if (IsPatched(_getImageLanguagesParam, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_getImageLanguagesParam, HarmonyPatchType.Postfix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbProviderBase.GetImageLanguagesParam Success by Harmony");
                    }
                    if (IsPatched(_movieDbSeriesProviderIsComplete, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_movieDbSeriesProviderIsComplete, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeriesProvider.IsComplete Success by Harmony");
                    }
                    if (IsPatched(_movieDbSeriesProviderImportData, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_movieDbSeriesProviderImportData, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeriesProvider.ImportData Success by Harmony");
                    }
                    if (IsPatched(_ensureSeriesInfo, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_ensureSeriesInfo, HarmonyPatchType.Postfix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeriesProvider.EnsureSeriesInfoPostfix Success by Harmony");
                    }
                    if (IsPatched(_movieDbSeasonProviderIsComplete, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_movieDbSeasonProviderIsComplete, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeasonProvider.IsComplete Success by Harmony");
                    }
                    if (IsPatched(_movieDbSeasonProviderImportData, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_movieDbSeasonProviderImportData, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeasonProvider.ImportData Success by Harmony");
                    }
                    if (IsPatched(_movieDbEpisodeProviderIsComplete, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_movieDbEpisodeProviderIsComplete, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbEpisodeProvider.IsComplete Success by Harmony");
                    }
                    if (IsPatched(_movieDbEpisodeProviderImportData, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_movieDbEpisodeProviderImportData, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbEpisodeProvider.ImportData Success by Harmony");
                    }
                    if (IsPatched(_movieDbPersonProviderImportData, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_movieDbPersonProviderImportData, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch MovieDbPersonProvider.ImportData Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch ChineseMovieDb Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        private static bool IsUpdateNeeded(string name, bool isEpisode)
        {
            var isJapaneseFallback = GetFallbackLanguages().Contains("ja-jp", StringComparer.OrdinalIgnoreCase);

            return string.IsNullOrEmpty(name) || !isJapaneseFallback && !IsChinese(name) ||
                   isJapaneseFallback && !(IsChinese(name) && (!isEpisode || !IsDefaultChineseEpisodeName(name)) ||
                                           IsJapanese(name));
        }

        public static List<string> GetFallbackLanguages()
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
            if (item is Movie || item is Series || item is Season || item is Episode)
            {
                if (!GetFallbackLanguages().Contains("ja-jp", StringComparer.OrdinalIgnoreCase))
                {
                    if (!IsChinese(item.Name) || !IsChinese(item.Overview))
                    {
                        __result = false;
                        return false;
                    }
                    item.Name = ConvertTraditionalToSimplified(item.Name);
                    item.Overview = ConvertTraditionalToSimplified(item.Overview);
                    __result = true;
                    return false;
                }
                else
                {
                    if (IsChinese(item.Name) && IsChinese(item.Overview) ||
                        IsJapanese(item.Name) && IsJapanese(item.Overview))
                    {
                        item.Name = ConvertTraditionalToSimplified(item.Name);
                        item.Overview = ConvertTraditionalToSimplified(item.Overview);
                        __result = true;
                        return false;
                    }
                    __result = false;
                    return false;
                }
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

        [HarmonyPostfix]
        private static void EnsureSeriesInfoPostfix(string tmdbId, string language, CancellationToken cancellationToken,
            Task __result)
        {
            if (WasCalledByMethod(_movieDbAssembly, "FetchImages")) WasCalledByFetchImages.Value = true;
            RequestCountryCode.Value = string.IsNullOrEmpty(language) ? null : language.Split('-')[1];

            __result.ContinueWith(task =>
                {
                    if (!WasCalledByFetchImages.Value)
                    {
                        var isJapaneseFallback =
                            GetFallbackLanguages().Contains("ja-jp", StringComparer.OrdinalIgnoreCase);

                        var result = task.GetType().GetProperty("Result")?.GetValue(task);
                        if (result != null)
                        {
                            var nameProperty = result.GetType().GetProperty("name");
                            if (nameProperty != null)
                            {
                                var name = nameProperty.GetValue(result) as string;
                                if (!IsChinese(name) && (!isJapaneseFallback || !IsJapanese(name)))
                                {
                                    var alternativeTitlesProperty = result.GetType().GetProperty("alternative_titles");
                                    if (alternativeTitlesProperty != null)
                                    {
                                        var alternativeTitles = alternativeTitlesProperty.GetValue(result);
                                        var resultsProperty = alternativeTitles?.GetType().GetProperty("results");

                                        if (resultsProperty?.GetValue(alternativeTitles) is IList altTitles)
                                        {
                                            foreach (var altTitle in altTitles)
                                            {
                                                var iso3166Property = altTitle.GetType().GetProperty("iso_3166_1");
                                                var titleProperty = altTitle.GetType().GetProperty("title");

                                                if (iso3166Property != null && titleProperty != null)
                                                {
                                                    var iso3166Value = iso3166Property.GetValue(altTitle)?.ToString();
                                                    if (iso3166Value != null && RequestCountryCode.Value != null &&
                                                        string.Equals(iso3166Value, RequestCountryCode.Value,
                                                            StringComparison.OrdinalIgnoreCase))
                                                    {
                                                        var titleValue = titleProperty.GetValue(altTitle)?.ToString();
                                                        if (titleValue != null)
                                                        {
                                                            nameProperty.SetValue(result, titleValue);
                                                            break;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            if (string.Equals(RequestCountryCode.Value,"CN",StringComparison.OrdinalIgnoreCase))
                            {
                                var genresProperty = result.GetType().GetProperty("genres");
                                if (genresProperty != null)
                                {
                                    if (genresProperty.GetValue(result) is IList genres)
                                    {
                                        foreach (var genre in genres)
                                        {
                                            var genreNameProperty = genre.GetType().GetProperty("name");
                                            if (genreNameProperty != null)
                                            {
                                                var genreValue = genreNameProperty.GetValue(genre)?.ToString();
                                                if (genreValue != null)
                                                {
                                                    if (string.Equals(genreValue, "Sci-Fi & Fantasy",
                                                            StringComparison.OrdinalIgnoreCase))
                                                        genreNameProperty.SetValue(genre, "科幻奇幻");

                                                    if (string.Equals(genreValue, "War & Politics",
                                                            StringComparison.OrdinalIgnoreCase))
                                                        genreNameProperty.SetValue(genre, "战争政治");
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                .ConfigureAwait(false);
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

        [HarmonyPrefix]
        private static bool PersonImportDataPrefix(Person item, object info, bool isFirstLanguage)
        {
            var nameProperty = info.GetType().GetProperty("name");
            if (nameProperty?.GetValue(info) is string infoName)
            {
                var updateNameResult = Plugin.MetadataApi.UpdateAsExpected(infoName);

                if (updateNameResult.Item2)
                {
                    if (!string.Equals(infoName, Plugin.MetadataApi.CleanPersonName(updateNameResult.Item1),
                            StringComparison.Ordinal))
                        nameProperty.SetValue(info, updateNameResult.Item1);
                }
                else
                {
                    var alsoKnownAsProperty = info.GetType().GetProperty("also_known_as");
                    if (alsoKnownAsProperty?.GetValue(info) is List<object> alsoKnownAsList)
                    {
                        foreach (var alias in alsoKnownAsList)
                        {
                            if (alias is string aliasString && !string.IsNullOrEmpty(aliasString))
                            {
                                var updateAliasResult = Plugin.MetadataApi.UpdateAsExpected(aliasString);
                                if (updateAliasResult.Item2)
                                {
                                    nameProperty.SetValue(info, updateAliasResult.Item1);
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            var biographyProperty = info.GetType().GetProperty("biography");
            if (biographyProperty?.GetValue(info) is string infoBiography)
            {
                var updateBiographyResult = Plugin.MetadataApi.UpdateAsExpected(infoBiography);

                if (updateBiographyResult.Item2)
                {
                    if (!string.Equals(infoBiography, updateBiographyResult.Item1, StringComparison.Ordinal))
                        biographyProperty.SetValue(info, updateBiographyResult.Item1);
                }
            }

            return true;
        }
    }
}
