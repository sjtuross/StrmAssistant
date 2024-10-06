using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using System;
using System.Reflection;
using System.Threading;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    internal class RefreshContext
    {
        public BaseItem BaseItem { get; set; }
        public MetadataRefreshOptions MetadataRefreshOptions { get; set; }
    }

    public static class ExclusiveExtract
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static Assembly _mediaEncodingAssembly;
        private static MethodInfo _canRefreshMetadata;
        private static MethodInfo _canRefreshImage;
        private static MethodInfo _runFfProcess;

        private static MethodInfo _addVirtualFolder;
        private static MethodInfo _removeVirtualFolder;
        private static MethodInfo _addMediaPath;
        private static MethodInfo _removeMediaPath;

        private static AsyncLocal<BaseItem> CurrentItem { get; } = new AsyncLocal<BaseItem>();

        private static AsyncLocal<RefreshContext> CurrentRefreshContext { get; } = new AsyncLocal<RefreshContext>();

        public static void Initialize()
        {
            try
            {
                var embyProviders = Assembly.Load("Emby.Providers");
                var providerManager = embyProviders.GetType("Emby.Providers.Manager.ProviderManager");
                _canRefreshMetadata = providerManager.GetMethod("CanRefresh",
                    BindingFlags.Static | BindingFlags.NonPublic, null,
                    new Type[]
                    {
                        typeof(IMetadataProvider), typeof(BaseItem), typeof(LibraryOptions), typeof(bool),
                        typeof(bool), typeof(bool)
                    }, null);
                _canRefreshImage = providerManager.GetMethod("CanRefresh",
                    BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new Type[]
                    {
                        typeof(IImageProvider), typeof(BaseItem), typeof(LibraryOptions),
                        typeof(ImageRefreshOptions), typeof(bool), typeof(bool)
                    }, null);

                _mediaEncodingAssembly = Assembly.Load("Emby.Server.MediaEncoding");
                var mediaProbeManager =
                    _mediaEncodingAssembly.GetType("Emby.Server.MediaEncoding.Probing.MediaProbeManager");
                _runFfProcess =
                    mediaProbeManager.GetMethod("RunFfProcess", BindingFlags.Instance | BindingFlags.NonPublic);

                var embyApi = Assembly.Load("Emby.Api");
                Type[] addVirtualFolderType = { embyApi.GetType("Emby.Api.Library.AddVirtualFolder") };
                Type[] removeVirtualFolderType = { embyApi.GetType("Emby.Api.Library.RemoveVirtualFolder") };
                Type[] addMediaPathType = { embyApi.GetType("Emby.Api.Library.AddMediaPath") };
                Type[] removeMediaPathType = { embyApi.GetType("Emby.Api.Library.RemoveMediaPath") };
                var libraryStructureService = embyApi.GetType("Emby.Api.Library.LibraryStructureService");
                _addVirtualFolder = libraryStructureService.GetMethod("Post", addVirtualFolderType);
                _removeVirtualFolder = libraryStructureService.GetMethod("Any", removeVirtualFolderType);
                _addMediaPath = libraryStructureService.GetMethod("Post", addMediaPathType);
                _removeMediaPath = libraryStructureService.GetMethod("Any", removeMediaPathType);
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
                    if (!IsPatched(_runFfProcess, typeof(ExclusiveExtract)))
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
                    if (IsPatched(_runFfProcess, typeof(ExclusiveExtract)))
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
                    if (!IsPatched(_canRefreshMetadata, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Patch(_canRefreshMetadata,
                            prefix: new HarmonyMethod(typeof(ExclusiveExtract).GetMethod("CanRefreshMetadataPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch CanRefreshMetadata Success by Harmony");
                    }
                    if (!IsPatched(_canRefreshImage, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Patch(_canRefreshImage,
                            prefix: new HarmonyMethod(typeof(ExclusiveExtract).GetMethod("CanRefreshImagePrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch CanRefreshImage Success by Harmony");
                    }
                    if (!IsPatched(_addVirtualFolder, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Patch(_addVirtualFolder,
                            prefix: new HarmonyMethod(typeof(ExclusiveExtract).GetMethod("RefreshLibraryPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch AddVirtualFolder Success by Harmony");
                    }
                    if (!IsPatched(_removeVirtualFolder, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Patch(_removeVirtualFolder,
                            prefix: new HarmonyMethod(typeof(ExclusiveExtract).GetMethod("RefreshLibraryPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch RemoveVirtualFolder Success by Harmony");
                    }
                    if (!IsPatched(_addMediaPath, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Patch(_addMediaPath,
                            prefix: new HarmonyMethod(typeof(ExclusiveExtract).GetMethod("RefreshLibraryPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch AddMediaPath Success by Harmony");
                    }
                    if (!IsPatched(_removeMediaPath, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Patch(_removeMediaPath,
                            prefix: new HarmonyMethod(typeof(ExclusiveExtract).GetMethod("RefreshLibraryPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch RemoveMediaPath Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch ExclusiveExtract Failed by Harmony");
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
                    if (IsPatched(_canRefreshMetadata, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Unpatch(_canRefreshMetadata, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch CanRefreshMetadata Success by Harmony");
                    }
                    if (IsPatched(_canRefreshImage, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Unpatch(_canRefreshImage, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch CanRefreshImage Success by Harmony");
                    }
                    if (IsPatched(_addVirtualFolder, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Unpatch(_addVirtualFolder, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch AddVirtualFolder Success by Harmony");
                    }
                    if (IsPatched(_removeVirtualFolder, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Unpatch(_removeVirtualFolder, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch RemoveVirtualFolder Success by Harmony");
                    }
                    if (IsPatched(_addMediaPath, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Unpatch(_addMediaPath, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch AddMediaPath Success by Harmony");
                    }
                    if (IsPatched(_removeMediaPath, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Unpatch(_removeMediaPath, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch RemoveMediaPath Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch ExclusiveExtract Failed by Harmony");
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
        private static bool CanRefreshMetadataPrefix(IMetadataProvider provider, BaseItem item, LibraryOptions libraryOptions,
            bool includeDisabled, bool forceEnableInternetMetadata, bool ignoreMetadataLock, ref bool __result)
        {
            if (item.Parent is null || !(provider is IPreRefreshProvider) || !(provider is ICustomMetadataProvider<Video>)) return true;

            if (CurrentItem.Value != null && CurrentItem.Value.InternalId == item.InternalId) return true;

            if (item.DateLastRefreshed == DateTimeOffset.MinValue)
            {
                __result = false;
                return false;
            }

            if (CurrentRefreshContext.Value != null &&
                CurrentRefreshContext.Value.BaseItem.InternalId == item.InternalId &&
                CurrentRefreshContext.Value.MetadataRefreshOptions.MetadataRefreshMode <= MetadataRefreshMode.Default &&
                CurrentRefreshContext.Value.MetadataRefreshOptions.ImageRefreshMode <= MetadataRefreshMode.Default)
            {
                if (Plugin.SubtitleApi.HasExternalSubtitleChanged(item))
                    QueueManager.ExternalSubtitleItemQueue.Enqueue(item);

                __result = false;
                return false;
            }

            if (!item.IsShortcut && CurrentRefreshContext.Value != null &&
                CurrentRefreshContext.Value.MetadataRefreshOptions.ReplaceAllImages) return true;

            if (Plugin.LibraryApi.HasMediaStream(item) && !Plugin.LibraryApi.HasFileChanged(item))
            {
                if (Plugin.SubtitleApi.HasExternalSubtitleChanged(item))
                    QueueManager.ExternalSubtitleItemQueue.Enqueue(item);

                __result = false;
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool CanRefreshImagePrefix(IImageProvider provider, BaseItem item, LibraryOptions libraryOptions,
            ImageRefreshOptions refreshOptions, bool ignoreMetadataLock, bool ignoreLibraryOptions, ref bool __result)
        {
            if (item.Parent is null || !provider.Supports(item) || !(item is Video)) return true;

            if (CurrentItem.Value != null && CurrentItem.Value.InternalId == item.InternalId) return true;

            if (item.DateLastRefreshed == DateTimeOffset.MinValue) return true;

            if (CurrentRefreshContext.Value == null && refreshOptions is MetadataRefreshOptions options)
            {
                CurrentRefreshContext.Value = new RefreshContext
                {
                    BaseItem = item,
                    MetadataRefreshOptions = options
                };
            }

            if (!item.IsShortcut && provider is IDynamicImageProvider &&
                provider.GetType().Name == "VideoImageProvider" && refreshOptions is MetadataRefreshOptions &&
                !refreshOptions.ReplaceAllImages &&
                item is Episode && item.HasImage(ImageType.Primary))
            {
                __result = false;
                return false;
            }

            if (item.IsShortcut && (provider is ILocalImageProvider || provider is IRemoteImageProvider) &&
                refreshOptions is MetadataRefreshOptions && !refreshOptions.ReplaceAllImages && item is Episode &&
                item.HasImage(ImageType.Primary))
            {
                __result = false;
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool RefreshLibraryPrefix(object request)
        {
            if (request != null)
            {
                var requestType = request.GetType();
                var refreshLibraryProperty = requestType.GetProperty("RefreshLibrary");

                if (refreshLibraryProperty != null && refreshLibraryProperty.CanWrite)
                {
                    refreshLibraryProperty.SetValue(request, false);
                }
            }

            return true;
        }
    }
}
