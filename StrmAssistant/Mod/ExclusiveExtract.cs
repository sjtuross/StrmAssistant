using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using System;
using System.Reflection;
using System.Threading;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class ExclusiveExtract
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static Assembly _mediaEncodingAssembly;
        private static MethodInfo _canRefresh;
        private static MethodInfo _runFfProcess;

        private static AsyncLocal<BaseItem> CurrentItem { get; } = new AsyncLocal<BaseItem>();

        public static void Initialize()
        {
            try
            {
                var embyProvidersManager = Assembly.Load("Emby.Providers");
                var providerManager = embyProvidersManager.GetType("Emby.Providers.Manager.ProviderManager");
                _canRefresh = providerManager.GetMethod("CanRefresh", BindingFlags.Static | BindingFlags.NonPublic);
                
                _mediaEncodingAssembly = Assembly.Load("Emby.Server.MediaEncoding");
                var mediaProbeManager =
                    _mediaEncodingAssembly.GetType("Emby.Server.MediaEncoding.Probing.MediaProbeManager");
                _runFfProcess =
                    mediaProbeManager.GetMethod("RunFfProcess", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("ExclusiveExtract - Patch Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.GetPluginOptions().ModOptions.ExclusiveExtract)
            {
                Patch();
            }
        }

        public static void PatchFFProbeTimeout()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (!IsPatched(_runFfProcess))
                    {
                        HarmonyMod.Patch(_runFfProcess,
                            prefix: new HarmonyMethod(typeof(ExclusiveExtract).GetMethod("RunFfProcessPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch RunFfProcess Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch RunFfProcess Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        public static void UnpatchFFProbeTimeout()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (IsPatched(_runFfProcess))
                    {
                        HarmonyMod.Unpatch(_runFfProcess, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch RunFfProcess Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch RunFfProcess Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        public static void Patch()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (!IsPatched(_canRefresh))
                    {
                        HarmonyMod.Patch(_canRefresh,
                            prefix: new HarmonyMethod(typeof(ExclusiveExtract).GetMethod("CanRefreshPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch CanRefresh Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch CanRefresh Failed by Harmony");
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
                    if (IsPatched(_canRefresh))
                    {
                        HarmonyMod.Unpatch(_canRefresh, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch CanRefresh Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch CanRefresh Failed by Harmony");
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
        private static void RunFfProcessPrefix(ref int timeoutMs)
        {
            timeoutMs = 60000 * Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.MaxConcurrentCount;
        }

        [HarmonyPrefix]
        private static bool CanRefreshPrefix(IMetadataProvider provider, BaseItem item, LibraryOptions libraryOptions,
            bool includeDisabled, bool forceEnableInternetMetadata, bool ignoreMetadataLock, ref bool __result)
        {
            if (item.Parent is null || !(provider is IPreRefreshProvider) || !(provider is ICustomMetadataProvider<Video>)) return true;

            if (CurrentItem.Value != null && CurrentItem.Value.InternalId == item.InternalId) return true;

            if (item.DateLastRefreshed == DateTimeOffset.MinValue ||
                Plugin.LibraryApi.HasMediaStream(item) && !Plugin.LibraryApi.HasFileChanged(item))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}
