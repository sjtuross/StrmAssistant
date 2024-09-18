using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class ExclusiveExtract
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static MethodInfo _refreshWithProviders;
        private static AsyncLocal<BaseItem> CurrentItem { get; } = new AsyncLocal<BaseItem>();

        public static void Initialize()
        {
            try
            {
                var embyProvidersManager = Assembly.Load("Emby.Providers");
                var genericMetadataService = embyProvidersManager.GetType("Emby.Providers.Manager.MetadataService`2");
                var genericMetadataServiceEpisode =
                    genericMetadataService.MakeGenericType(typeof(Episode), typeof(ItemLookupInfo));
                _refreshWithProviders = genericMetadataServiceEpisode.GetMethod("RefreshWithProviders",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                Patch();
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("ExclusiveExtract - Patch Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null)
            {
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;
            }

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.GetPluginOptions().ModOptions.ExclusiveExtract)
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
                    if (!IsPatched(_refreshWithProviders))
                    {
                        HarmonyMod.Patch(_refreshWithProviders,
                            prefix: new HarmonyMethod(typeof(ExclusiveExtract).GetMethod("RefreshWithProvidersPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch RefreshWithProviders Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch RefreshWithProviders Failed by Harmony");
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
                    if (IsPatched(_refreshWithProviders))
                    {
                        HarmonyMod.Unpatch(_refreshWithProviders, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch RefreshWithProviders Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch RefreshWithProviders Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        public static void AllowExtractInstance(BaseItem item)
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                CurrentItem.Value = item;
            }
        }

        public static void DisallowExtractInstance(BaseItem item)
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                CurrentItem.Value = null;
            }
        }

        [HarmonyPrefix]
        private static bool RefreshWithProvidersPrefix(MetadataResult<BaseItem> metadata, ref List<IMetadataProvider> providers)
        {
            if (CurrentItem.Value != null && metadata.BaseItem.InternalId == CurrentItem.Value.InternalId)
            {
                return true;
            }

            providers.RemoveAll(p =>
                p is IPreRefreshProvider && p is ICustomMetadataProvider<Video>);

            return true;
        }
    }
}
