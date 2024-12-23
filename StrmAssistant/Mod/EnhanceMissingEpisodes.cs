using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using StrmAssistant.Provider;
using System;
using System.Linq;
using System.Reflection;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class EnhanceMissingEpisodes
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();
        private static MethodInfo _getEnabledMetadataProviders;

        public static void Initialize()
        {
            try
            {
                var embyProviders = Assembly.Load("Emby.Providers");
                var providerManager = embyProviders.GetType("Emby.Providers.Manager.ProviderManager");
                _getEnabledMetadataProviders=providerManager.GetMethod("GetEnabledMetadataProviders",
                    BindingFlags.Instance | BindingFlags.Public);
            }
            catch (Exception e)
            {
                Plugin.Instance.Logger.Warn("MissingEpisodes - Patch Init Failed");
                Plugin.Instance.Logger.Debug(e.Message);
                Plugin.Instance.Logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.ExperienceEnhanceStore.GetOptions().UIFunctionOptions.EnhanceMissingEpisodes)
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
                    if (!IsPatched(_getEnabledMetadataProviders, typeof(EnhanceMissingEpisodes)))
                    {
                        HarmonyMod.Patch(_getEnabledMetadataProviders,
                            postfix: new HarmonyMethod(typeof(EnhanceMissingEpisodes).GetMethod(
                                "GetEnabledMetadataProvidersPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug("Patch GetEnabledMetadataProviders Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Patch GetEnabledMetadataProviders Failed by Harmony");
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
                    if (IsPatched(_getEnabledMetadataProviders, typeof(EnhanceMissingEpisodes)))
                    {
                        HarmonyMod.Unpatch(_getEnabledMetadataProviders,
                            AccessTools.Method(typeof(EnhanceMissingEpisodes), "GetEnabledMetadataProvidersPostfix"));
                        Plugin.Instance.Logger.Debug("Unpatch GetEnabledMetadataProviders Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Unpatch GetEnabledMetadataProviders Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                }
            }
        }

        [HarmonyPostfix]
        private static void GetEnabledMetadataProvidersPostfix(BaseItem item, LibraryOptions libraryOptions,
            ref IMetadataProvider[] __result)
        {
            if (item is Series && item.ProviderIds.ContainsKey(MetadataProviders.Tmdb.ToString()))
            {
                var movieDbSeriesProvider =
                    __result.FirstOrDefault(p => p.GetType().FullName == "MovieDb.MovieDbSeriesProvider");
                var newResult = __result.Where(p => p.GetType().FullName != typeof(MovieDbSeriesProvider).FullName)
                    .ToList();
                var provider = Plugin.MetadataApi.GetMovieDbSeriesProvider();

                if (movieDbSeriesProvider != null)
                {
                    var index = newResult.IndexOf(movieDbSeriesProvider);
                    newResult.Insert(index, provider);
                }
                else if (!newResult.Any(p => p is ISeriesMetadataProvider))
                {
                    newResult.Add(provider);
                }

                __result = newResult.ToArray();
            }
        }
    }
}
