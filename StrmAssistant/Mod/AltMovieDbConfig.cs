using HarmonyLib;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using static StrmAssistant.Common.CommonUtility;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class AltMovieDbConfig
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static Assembly _movieDbAssembly;
        private static MethodInfo _getMovieDbResponse;
        private static MethodInfo _saveImageFromRemoteUrl;
        private static MethodInfo _downloadImage;

        private static readonly string DefaultMovieDbApiUrl = "https://api.themoviedb.org";
        private static readonly string DefaultAltMovieDbApiUrl = "https://api.tmdb.org";
        private static readonly string DefaultMovieDbImageUrl = "https://image.tmdb.org";
        private static string SystemDefaultMovieDbApiKey;

        public static void Initialize()
        {
            try
            {
                _movieDbAssembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "MovieDb");

                if (_movieDbAssembly != null)
                {
                    var movieDbProviderBase = _movieDbAssembly.GetType("MovieDb.MovieDbProviderBase");
                    _getMovieDbResponse = movieDbProviderBase.GetMethod("GetMovieDbResponse",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    var apiKey = movieDbProviderBase.GetField("ApiKey", BindingFlags.Static | BindingFlags.NonPublic);
                    SystemDefaultMovieDbApiKey = apiKey?.GetValue(null) as string;

                    var embyProviders = Assembly.Load("Emby.Providers");
                    var providerManager = embyProviders.GetType("Emby.Providers.Manager.ProviderManager");
                    _saveImageFromRemoteUrl = providerManager.GetMethod("SaveImageFromRemoteUrl",
                        BindingFlags.NonPublic | BindingFlags.Instance);

                    var embyApi = Assembly.Load("Emby.Api");
                    var remoteImageService = embyApi.GetType("Emby.Api.Images.RemoteImageService");
                    _downloadImage = remoteImageService.GetMethod("DownloadImage",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                }
            }
            catch (Exception e)
            {
                Plugin.Instance.Logger.Warn("AltMovieDbConfig - Patch Init Failed");
                Plugin.Instance.Logger.Debug(e.Message);
                Plugin.Instance.Logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.MetadataEnhanceStore.GetOptions().AltMovieDbConfig)
            {
                PatchApiUrl();

                if (!string.IsNullOrEmpty(Plugin.Instance.MetadataEnhanceStore.GetOptions().AltMovieDbImageUrl))
                {
                    PatchImageUrl();
                }
            }
        }

        public static void PatchApiUrl()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony && _movieDbAssembly != null)
            {
                try
                {
                    if (!IsPatched(_getMovieDbResponse, typeof(AltMovieDbConfig)))
                    {
                        HarmonyMod.Patch(_getMovieDbResponse,
                            prefix: new HarmonyMethod(typeof(AltMovieDbConfig).GetMethod(
                                "GetMovieDbResponsePrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch GetMovieDbResponse Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Patch GetMovieDbResponse Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                    PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;
                }
            }
        }

        public static void UnpatchApiUrl()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (IsPatched(_getMovieDbResponse, typeof(AltMovieDbConfig)))
                    {
                        HarmonyMod.Unpatch(_getMovieDbResponse,
                            AccessTools.Method(typeof(AltMovieDbConfig), "GetMovieDbResponsePrefix"));
                        Plugin.Instance.Logger.Debug("Unpatch GetMovieDbResponse Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Unpatch GetMovieDbResponse Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                }
            }
        }

        public static void PatchImageUrl()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony && _movieDbAssembly != null)
            {
                try
                {
                    if (!IsPatched(_saveImageFromRemoteUrl, typeof(AltMovieDbConfig)))
                    {
                        HarmonyMod.Patch(_saveImageFromRemoteUrl,
                            prefix: new HarmonyMethod(typeof(AltMovieDbConfig).GetMethod(
                                "SaveImageFromRemoteUrlPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch SaveImageFromRemoteUrl Success by Harmony");
                    }

                    if (!IsPatched(_downloadImage, typeof(AltMovieDbConfig)))
                    {
                        HarmonyMod.Patch(_downloadImage,
                            prefix: new HarmonyMethod(typeof(AltMovieDbConfig).GetMethod(
                                "DownloadImagePrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch DownloadImage Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Patch AltMovieDbImageUrl Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                    PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;
                }
            }
        }

        public static void UnpatchImageUrl()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (IsPatched(_saveImageFromRemoteUrl, typeof(AltMovieDbConfig)))
                    {
                        HarmonyMod.Unpatch(_saveImageFromRemoteUrl,
                            AccessTools.Method(typeof(AltMovieDbConfig), "SaveImageFromRemoteUrlPrefix"));
                        Plugin.Instance.Logger.Debug("Unpatch SaveImageFromRemoteUrl Success by Harmony");
                    }

                    if (IsPatched(_downloadImage, typeof(AltMovieDbConfig)))
                    {
                        HarmonyMod.Unpatch(_downloadImage,
                            AccessTools.Method(typeof(AltMovieDbConfig), "DownloadImagePrefix"));
                        Plugin.Instance.Logger.Debug("Unpatch DownloadImage Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Unpatch AltMovieDbImageUrl Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                }
            }
        }

        [HarmonyPrefix]
        private static bool GetMovieDbResponsePrefix(HttpRequestOptions options)
        {
            var metadataEnhanceOptions = Plugin.Instance.MetadataEnhanceStore.GetOptions();
            var apiUrl = metadataEnhanceOptions.AltMovieDbApiUrl;
            var apiKey = metadataEnhanceOptions.AltMovieDbApiKey;

            var requestUrl = options.Url;

            if (requestUrl.StartsWith(DefaultMovieDbApiUrl + "/3/configuration", StringComparison.Ordinal))
            {
                requestUrl = requestUrl.Replace(DefaultMovieDbApiUrl, DefaultAltMovieDbApiUrl);
            }
            else if (IsValidHttpUrl(apiUrl))
            {
                requestUrl = requestUrl.Replace(DefaultMovieDbApiUrl, apiUrl);
            }

            if (IsValidMovieDbApiKey(apiKey))
            {
                requestUrl = requestUrl.Replace(SystemDefaultMovieDbApiKey, apiKey);
            }

            if (!string.Equals(requestUrl, options.Url, StringComparison.Ordinal))
            {
                options.Url = requestUrl;
            }

            return true;
        }

        private static void ReplaceMovieDbImageUrl(ref string url)
        {
            var imageUrl = Plugin.Instance.MetadataEnhanceStore.GetOptions().AltMovieDbImageUrl;

            if (IsValidHttpUrl(imageUrl))
            {
                url = url.Replace(DefaultMovieDbImageUrl, imageUrl);
            }
        }

        [HarmonyPrefix]
        private static bool SaveImageFromRemoteUrlPrefix(BaseItem item, LibraryOptions libraryOptions, ref string url,
            ImageType type, int? imageIndex, long[] generatedFromItemIds, IDirectoryService directoryService,
            bool updateImageCache, CancellationToken cancellationToken)
        {
            ReplaceMovieDbImageUrl(ref url);

            return true;
        }

        [HarmonyPrefix]
        private static bool DownloadImagePrefix(ref string url, Guid urlHash, string pointerCachePath,
            CancellationToken cancellationToken)
        {
            ReplaceMovieDbImageUrl(ref url);

            return true;
        }
    }
}
