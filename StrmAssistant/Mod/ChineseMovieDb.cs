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
        private static MethodInfo _getTitleMovieData;

        private static Type _movieDbProviderBase;
        private static MethodInfo _getMovieDbMetadataLanguages;
        private static MethodInfo _mapLanguageToProviderLanguage;
        private static MethodInfo _getImageLanguagesParam;
        private static FieldInfo _cacheTime;

        private static MethodInfo _movieDbSeriesProviderIsComplete;
        private static MethodInfo _movieDbSeriesProviderImportData;
        private static MethodInfo _ensureSeriesInfo;
        private static MethodInfo _getTitleSeriesInfo;
        private static PropertyInfo _nameSeriesInfoProperty;
        private static PropertyInfo _alternativeTitleSeriesInfoProperty;
        private static PropertyInfo _alternativeTitleListProperty;
        private static PropertyInfo _alternativeTitle;
        private static PropertyInfo _alternativeTitleCountryCode;
        private static PropertyInfo _genresProperty;
        private static PropertyInfo _genreNameProperty;

        private static MethodInfo _movieDbSeasonProviderIsComplete;
        private static MethodInfo _movieDbSeasonProviderImportData;
        private static PropertyInfo _nameSeasonInfoProperty;
        private static PropertyInfo _overviewSeasonInfoProperty;

        private static MethodInfo _movieDbEpisodeProviderIsComplete;
        private static MethodInfo _movieDbEpisodeProviderImportData;
        private static PropertyInfo _nameEpisodeInfoProperty;
        private static PropertyInfo _overviewEpisodeInfoProperty;

        private static MethodInfo _movieDbPersonProviderImportData;
        private static PropertyInfo _nameProperty;
        private static PropertyInfo _alsoKnownAsProperty;
        private static PropertyInfo _biographyProperty;
        private static PropertyInfo _placeOfBirthProperty;

        private static PropertyInfo _seriesInfoTaskResultProperty;

        private static readonly AsyncLocal<bool> WasCalledByFetchImages = new AsyncLocal<bool>();
        private static readonly AsyncLocal<string> CurrentLookupCountryCode = new AsyncLocal<string>();
        private static readonly TimeSpan OriginalCacheTime = TimeSpan.FromHours(6);
        private static readonly TimeSpan NewCacheTime = TimeSpan.FromHours(48);

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
                    var completeMovieData = _movieDbAssembly.GetType("MovieDb.MovieDbProvider")
                        .GetNestedType("CompleteMovieData", BindingFlags.NonPublic);
                    _getTitleMovieData = completeMovieData.GetMethod("GetTitle");

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
                    _cacheTime = _movieDbProviderBase.GetField("CacheTime", BindingFlags.Public | BindingFlags.Static);

                    var movieDbSeriesProvider = _movieDbAssembly.GetType("MovieDb.MovieDbSeriesProvider");
                    _movieDbSeriesProviderIsComplete =
                        movieDbSeriesProvider.GetMethod("IsComplete", BindingFlags.NonPublic | BindingFlags.Instance);
                    _movieDbSeriesProviderImportData =
                        movieDbSeriesProvider.GetMethod("ImportData", BindingFlags.NonPublic | BindingFlags.Instance);
                    _ensureSeriesInfo = movieDbSeriesProvider.GetMethod("EnsureSeriesInfo",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    var seriesRootObject = movieDbSeriesProvider.GetNestedType("SeriesRootObject", BindingFlags.Public);
                    _getTitleSeriesInfo = seriesRootObject.GetMethod("GetTitle");
                    _nameSeriesInfoProperty = seriesRootObject.GetProperty("name");
                    _alternativeTitleSeriesInfoProperty = seriesRootObject.GetProperty("alternative_titles");
                    _alternativeTitleListProperty = _movieDbAssembly.GetType("MovieDb.TmdbAlternativeTitles")
                        .GetProperty("results");
                    var tmdbTitleType = _movieDbAssembly.GetType("MovieDb.TmdbTitle");
                    _alternativeTitle = tmdbTitleType.GetProperty("title");
                    _alternativeTitleCountryCode = tmdbTitleType.GetProperty("iso_3166_1");
                    _genresProperty = seriesRootObject.GetProperty("genres");
                    _genreNameProperty = _movieDbAssembly.GetType("MovieDb.TmdbGenre").GetProperty("name");

                    var movieDbSeasonProvider = _movieDbAssembly.GetType("MovieDb.MovieDbSeasonProvider");
                    _movieDbSeasonProviderIsComplete =
                        movieDbSeasonProvider.GetMethod("IsComplete", BindingFlags.NonPublic | BindingFlags.Instance);
                    _movieDbSeasonProviderImportData =
                        movieDbSeasonProvider.GetMethod("ImportData", BindingFlags.NonPublic | BindingFlags.Instance);
                    var seasonRootObject = movieDbSeasonProvider.GetNestedType("SeasonRootObject", BindingFlags.Public);
                    _nameSeasonInfoProperty=seasonRootObject.GetProperty("name");
                    _overviewSeasonInfoProperty = seasonRootObject.GetProperty("overview");

                    var movieDbEpisodeProvider = _movieDbAssembly.GetType("MovieDb.MovieDbEpisodeProvider");
                    _movieDbEpisodeProviderIsComplete =
                        movieDbEpisodeProvider.GetMethod("IsComplete", BindingFlags.NonPublic | BindingFlags.Instance);
                    _movieDbEpisodeProviderImportData =
                        movieDbEpisodeProvider.GetMethod("ImportData", BindingFlags.NonPublic | BindingFlags.Instance);
                    var episodeRootObject = _movieDbProviderBase.GetNestedType("RootObject", BindingFlags.Public);
                    _nameEpisodeInfoProperty=episodeRootObject.GetProperty("name");
                    _overviewEpisodeInfoProperty = episodeRootObject.GetProperty("overview");

                    var movieDbPersonProvider = _movieDbAssembly.GetType("MovieDb.MovieDbPersonProvider");
                    _movieDbPersonProviderImportData = movieDbPersonProvider.GetMethod("ImportData",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    var personResult = movieDbPersonProvider.GetNestedType("PersonResult", BindingFlags.Public);
                    _nameProperty = personResult.GetProperty("name");
                    _alsoKnownAsProperty = personResult.GetProperty("also_known_as");
                    _biographyProperty = personResult.GetProperty("biography");
                    _placeOfBirthProperty = personResult.GetProperty("place_of_birth");
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
                Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions.ChineseMovieDb)
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
                            "Patch MovieDbSeriesProvider.EnsureSeriesInfoSuccess by Harmony");
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
                        HarmonyMod.Unpatch(_genericMovieDbInfoIsCompleteMovie,
                            AccessTools.Method(typeof(ChineseMovieDb), "IsCompletePrefix"));
                        Plugin.Instance.logger.Debug("Unpatch GenericMovieDbInfo.IsComplete for Movie Success by Harmony");
                    }
                    if (IsPatched(_genericMovieDbInfoProcessMainInfoMovie, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_genericMovieDbInfoProcessMainInfoMovie,
                            AccessTools.Method(typeof(ChineseMovieDb), "ProcessMainInfoMoviePrefix"));
                        Plugin.Instance.logger.Debug("Unpatch GenericMovieDbInfo.ProcessMainInfo for Movie Success by Harmony");
                    }
                    //if (IsPatched(_genericMovieDbInfoIsCompleteSeries, typeof(ChineseMovieDb)))
                    //{
                    //    HarmonyMod.Unpatch(_genericMovieDbInfoIsCompleteSeries,
                    //        AccessTools.Method(typeof(ChineseMovieDb), "IsCompletePrefix"));
                    //    Plugin.Instance.logger.Debug("Unpatch GenericMovieDbInfo.IsComplete for Series Success by Harmony");
                    //}
                    //if (IsPatched(_genericMovieDbInfoProcessMainInfoSeries, typeof(ChineseMovieDb)))
                    //{
                    //    HarmonyMod.Unpatch(_genericMovieDbInfoProcessMainInfoSeries,
                    //        AccessTools.Method(typeof(ChineseMovieDb), "ProcessMainInfoSeriesPrefix"));
                    //    Plugin.Instance.logger.Debug("Unpatch GenericMovieDbInfo.ProcessMainInfo for Series Success by Harmony");
                    //}
                    if (IsPatched(_getMovieDbMetadataLanguages, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_getMovieDbMetadataLanguages,
                            AccessTools.Method(typeof(ChineseMovieDb), "MetadataLanguagesPostfix"));
                        Plugin.Instance.logger.Debug("Unpatch MovieDbProviderBase.GetMovieDbMetadataLanguages Success by Harmony");
                    }

                    if (IsPatched(_getImageLanguagesParam, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_getImageLanguagesParam,
                            AccessTools.Method(typeof(ChineseMovieDb), "GetImageLanguagesParamPostfix"));
                        Plugin.Instance.logger.Debug("Unpatch MovieDbProviderBase.GetImageLanguagesParam Success by Harmony");
                    }
                    if (IsPatched(_movieDbSeriesProviderIsComplete, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_movieDbSeriesProviderIsComplete,
                            AccessTools.Method(typeof(ChineseMovieDb), "IsCompletePrefix"));
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeriesProvider.IsComplete Success by Harmony");
                    }
                    if (IsPatched(_movieDbSeriesProviderImportData, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_movieDbSeriesProviderImportData,
                            AccessTools.Method(typeof(ChineseMovieDb), "SeriesImportDataPrefix"));
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeriesProvider.ImportData Success by Harmony");
                    }
                    if (IsPatched(_ensureSeriesInfo, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_ensureSeriesInfo,
                            AccessTools.Method(typeof(ChineseMovieDb), "EnsureSeriesInfoPostfix"));
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeriesProvider.EnsureSeriesInfo Success by Harmony");
                    }
                    if (IsPatched(_movieDbSeasonProviderIsComplete, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_movieDbSeasonProviderIsComplete,
                            AccessTools.Method(typeof(ChineseMovieDb), "IsCompletePrefix"));
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeasonProvider.IsComplete Success by Harmony");
                    }
                    if (IsPatched(_movieDbSeasonProviderImportData, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_movieDbSeasonProviderImportData,
                            AccessTools.Method(typeof(ChineseMovieDb), "SeasonImportDataPrefix"));
                        Plugin.Instance.logger.Debug("Unpatch MovieDbSeasonProvider.ImportData Success by Harmony");
                    }
                    if (IsPatched(_movieDbEpisodeProviderIsComplete, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_movieDbEpisodeProviderIsComplete,
                            AccessTools.Method(typeof(ChineseMovieDb), "IsCompletePrefix"));
                        Plugin.Instance.logger.Debug("Unpatch MovieDbEpisodeProvider.IsComplete Success by Harmony");
                    }
                    if (IsPatched(_movieDbEpisodeProviderImportData, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_movieDbEpisodeProviderImportData,
                            AccessTools.Method(typeof(ChineseMovieDb), "EpisodeImportDataPrefix"));
                        Plugin.Instance.logger.Debug("Unpatch MovieDbEpisodeProvider.ImportData Success by Harmony");
                    }
                    if (IsPatched(_movieDbPersonProviderImportData, typeof(ChineseMovieDb)))
                    {
                        HarmonyMod.Unpatch(_movieDbPersonProviderImportData,
                            AccessTools.Method(typeof(ChineseMovieDb), "PersonImportDataPrefix"));
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

        public static void PatchCacheTime()
        {
            _cacheTime?.SetValue(null, NewCacheTime);
            Plugin.Instance.logger.Debug("Patch CacheTime Success by Reflection");
        }

        public static void UnpatchCacheTime()
        {
            _cacheTime?.SetValue(null, OriginalCacheTime);
            Plugin.Instance.logger.Debug("Unpatch CacheTime Success by Reflection");
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
            var currentFallbackLanguages = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions.FallbackLanguages
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();

            return currentFallbackLanguages;
        }

        [HarmonyPrefix]
        private static bool ProcessMainInfoMoviePrefix(MetadataResult<Movie> resultItem, object settings,
            string preferredCountryCode, object movieData, bool isFirstLanguage)
        {
            var item = resultItem.Item;

            if (_getTitleMovieData != null && IsUpdateNeeded(item.Name, false))
            {
                item.Name = _getTitleMovieData.Invoke(movieData, null) as string;
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

            if (_getTitleMovieData != null && IsUpdateNeeded(item.Name, false))
            {
                item.Name = _getTitleMovieData.Invoke(movieData, null) as string;
            }

            return true;
        }
        
        [HarmonyPrefix]
        private static bool SeriesImportDataPrefix(MetadataResult<Series> seriesResult, object seriesInfo,
            string preferredCountryCode, object settings, bool isFirstLanguage)
        {
            var item = seriesResult.Item;

            if (_getTitleSeriesInfo != null && IsUpdateNeeded(item.Name, false))
            {
                item.Name = _getTitleSeriesInfo.Invoke(seriesInfo, null) as string;
            }

            if (_genresProperty != null && _genreNameProperty != null && isFirstLanguage &&
                string.Equals(CurrentLookupCountryCode.Value, "CN", StringComparison.OrdinalIgnoreCase))
            {
                if (_genresProperty.GetValue(seriesInfo) is IList genres)
                {
                    foreach (var genre in genres)
                    {
                        var genreValue = _genreNameProperty.GetValue(genre)?.ToString();
                        if (!string.IsNullOrEmpty(genreValue))
                        {
                            if (string.Equals(genreValue, "Sci-Fi & Fantasy",
                                    StringComparison.OrdinalIgnoreCase))
                                _genreNameProperty.SetValue(genre, "科幻奇幻");

                            if (string.Equals(genreValue, "War & Politics",
                                    StringComparison.OrdinalIgnoreCase))
                                _genreNameProperty.SetValue(genre, "战争政治");
                        }
                    }
                }
            }

            return true;
        }

        [HarmonyPostfix]
        private static void EnsureSeriesInfoPostfix(string tmdbId, string language, CancellationToken cancellationToken,
            Task __result)
        {
            if (WasCalledByMethod(_movieDbAssembly, "FetchImages")) WasCalledByFetchImages.Value = true;
            CurrentLookupCountryCode.Value = string.IsNullOrEmpty(language) ? null : language.Split('-')[1];

            __result.ContinueWith(task =>
                {
                    if (!WasCalledByFetchImages.Value)
                    {
                        var isJapaneseFallback =
                            GetFallbackLanguages().Contains("ja-jp", StringComparer.OrdinalIgnoreCase);

                        if (_seriesInfoTaskResultProperty == null)
                            _seriesInfoTaskResultProperty = task.GetType().GetProperty("Result");

                        var seriesInfo = _seriesInfoTaskResultProperty?.GetValue(task);
                        if (seriesInfo != null && _nameSeriesInfoProperty != null)
                        {
                            var name = _nameSeriesInfoProperty.GetValue(seriesInfo) as string;
                            if (!IsChinese(name) && (!isJapaneseFallback || !IsJapanese(name)) &&
                                _alternativeTitleSeriesInfoProperty != null && _alternativeTitleListProperty != null &&
                                _alternativeTitleCountryCode != null && _alternativeTitle != null)
                            {
                                var alternativeTitles = _alternativeTitleSeriesInfoProperty.GetValue(seriesInfo);
                                if (_alternativeTitleListProperty.GetValue(alternativeTitles) is IList altTitles)
                                {
                                    foreach (var altTitle in altTitles)
                                    {
                                        var iso3166Value = _alternativeTitleCountryCode.GetValue(altTitle)?.ToString();
                                        var titleValue = _alternativeTitle.GetValue(altTitle)?.ToString();
                                        if (!string.IsNullOrEmpty(iso3166Value) && !string.IsNullOrEmpty(titleValue) &&
                                            CurrentLookupCountryCode.Value != null &&
                                            string.Equals(iso3166Value, CurrentLookupCountryCode.Value,
                                                StringComparison.OrdinalIgnoreCase))
                                        {
                                            _nameSeriesInfoProperty.SetValue(seriesInfo, titleValue);
                                            break;
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
            if (_nameSeasonInfoProperty != null && IsUpdateNeeded(item.Name, false))
            {
                item.Name = _nameSeasonInfoProperty.GetValue(seasonInfo) as string;
            }

            if (_overviewSeasonInfoProperty != null && IsUpdateNeeded(item.Overview, false))
            {
                item.Overview = _overviewSeasonInfoProperty.GetValue(seasonInfo) as string;
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool EpisodeImportDataPrefix(MetadataResult<Episode> result, EpisodeInfo info, object response,
            object settings, bool isFirstLanguage)
        {
            var isJapaneseFallback =
                GetFallbackLanguages().Contains("ja-jp", StringComparer.OrdinalIgnoreCase);

            var item = result.Item;

            if (_nameEpisodeInfoProperty != null && IsUpdateNeeded(item.Name, true))
            {
                var nameValue = _nameEpisodeInfoProperty.GetValue(response) as string;
                if (string.IsNullOrEmpty(item.Name) || !isJapaneseFallback ||
                    (IsChinese(item.Name) && IsDefaultChineseEpisodeName(item.Name) && IsJapanese(nameValue) &&
                     !IsDefaultJapaneseEpisodeName(nameValue)))
                {
                    item.Name = nameValue;
                }
            }

            if (_overviewEpisodeInfoProperty != null && IsUpdateNeeded(item.Overview, true))
            {
                item.Overview = _overviewEpisodeInfoProperty.GetValue(response) as string;
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

        private static Tuple<string, bool> ProcessPersonInfoAsExpected(string input, string placeOfBirth)
        {
            var isJapaneseFallback = GetFallbackLanguages().Contains("ja-jp", StringComparer.OrdinalIgnoreCase);

            var considerJapanese = isJapaneseFallback && placeOfBirth != null &&
                                   placeOfBirth.IndexOf("Japan", StringComparison.Ordinal) >= 0;

            if (IsChinese(input))
            {
                input = ConvertTraditionalToSimplified(input);
            }

            if (IsChinese(input) || (considerJapanese && IsJapanese(input)))
            {
                return new Tuple<string, bool>(input, true);
            }

            return new Tuple<string, bool>(input, false);
        }

        [HarmonyPrefix]
        private static bool PersonImportDataPrefix(Person item, object info, bool isFirstLanguage)
        {
            var placeOfBirth = _placeOfBirthProperty?.GetValue(info) as string;

            if (_nameProperty?.GetValue(info) is string infoPersonName)
            {
                var updateNameResult = ProcessPersonInfoAsExpected(infoPersonName, placeOfBirth);

                if (updateNameResult.Item2)
                {
                    if (!string.Equals(infoPersonName, CleanPersonName(updateNameResult.Item1),
                            StringComparison.Ordinal))
                        _nameProperty.SetValue(info, updateNameResult.Item1);
                }
                else
                {
                    if (_alsoKnownAsProperty?.GetValue(info) is List<object> alsoKnownAsList)
                    {
                        foreach (var alias in alsoKnownAsList)
                        {
                            if (alias is string aliasString && !string.IsNullOrEmpty(aliasString))
                            {
                                var updateAliasResult = ProcessPersonInfoAsExpected(aliasString, placeOfBirth);
                                if (updateAliasResult.Item2)
                                {
                                    _nameProperty.SetValue(info, updateAliasResult.Item1);
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            if (_biographyProperty?.GetValue(info) is string infoBiography)
            {
                var updateBiographyResult = ProcessPersonInfoAsExpected(infoBiography, placeOfBirth);

                if (updateBiographyResult.Item2)
                {
                    if (!string.Equals(infoBiography, updateBiographyResult.Item1, StringComparison.Ordinal))
                        _biographyProperty.SetValue(info, updateBiographyResult.Item1);
                }
            }

            return true;
        }
    }
}
