using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using StrmAssistant.Common;
using StrmAssistant.ScheduledTask;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Options.MediaInfoExtractOptions;
using static StrmAssistant.Options.Utility;

namespace StrmAssistant.Mod
{
    internal class RefreshContext
    {
        public long InternalId { get; set; }
        public MetadataRefreshOptions MetadataRefreshOptions { get; set; }
        public bool MediaInfoNeedsUpdate { get; set; }
    }

    public static class ExclusiveExtract
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static Assembly _mediaEncodingAssembly;
        private static MethodInfo _canRefreshMetadata;
        private static MethodInfo _canRefreshImage;
        private static MethodInfo _afterMetadataRefresh;
        private static MethodInfo _runFfProcess;

        private static MethodInfo _addVirtualFolder;
        private static MethodInfo _removeVirtualFolder;
        private static MethodInfo _addMediaPath;
        private static MethodInfo _removeMediaPath;

        private static readonly Dictionary<Type, PropertyInfo> RefreshLibraryPropertyCache =
            new Dictionary<Type, PropertyInfo>();

        private static AsyncLocal<long> ExclusiveItem { get; } = new AsyncLocal<long>();

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
                _afterMetadataRefresh =
                    typeof(BaseItem).GetMethod("AfterMetadataRefresh", BindingFlags.Instance | BindingFlags.Public);

                _mediaEncodingAssembly = Assembly.Load("Emby.Server.MediaEncoding");
                var mediaProbeManager =
                    _mediaEncodingAssembly.GetType("Emby.Server.MediaEncoding.Probing.MediaProbeManager");
                _runFfProcess =
                    mediaProbeManager.GetMethod("RunFfProcess", BindingFlags.Instance | BindingFlags.NonPublic);

                var embyApi = Assembly.Load("Emby.Api");
                var libraryStructureService = embyApi.GetType("Emby.Api.Library.LibraryStructureService");
                _addVirtualFolder = libraryStructureService.GetMethod("Post",
                    new[] { embyApi.GetType("Emby.Api.Library.AddVirtualFolder") });
                _removeVirtualFolder = libraryStructureService.GetMethod("Any",
                    new[] { embyApi.GetType("Emby.Api.Library.RemoveVirtualFolder") });
                _addMediaPath = libraryStructureService.GetMethod("Post",
                    new[] { embyApi.GetType("Emby.Api.Library.AddMediaPath") });
                _removeMediaPath = libraryStructureService.GetMethod("Any",
                    new[] { embyApi.GetType("Emby.Api.Library.RemoveMediaPath") });
            }
            catch (Exception e)
            {
                Plugin.Instance.Logger.Warn("ExclusiveExtract - Patch Init Failed");
                Plugin.Instance.Logger.Debug(e.Message);
                Plugin.Instance.Logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None)
            {
                PatchFFProbeTimeout();

                if (Plugin.Instance.MediaInfoExtractStore.GetOptions().ExclusiveExtract)
                {
                    UpdateExclusiveControlFeatures(Plugin.Instance.MediaInfoExtractStore.GetOptions()
                        .ExclusiveControlFeatures);
                    Patch();
                }
            }
        }

        private static void PatchFFProbeTimeout()
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
                        Plugin.Instance.Logger.Debug(
                            "Patch RunFfProcess Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Patch RunFfProcess Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                }
            }
        }

        private static void UnpatchFFProbeTimeout()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (IsPatched(_runFfProcess, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Unpatch(_runFfProcess, HarmonyPatchType.Prefix);
                        Plugin.Instance.Logger.Debug("Unpatch RunFfProcess Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Unpatch RunFfProcess Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
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
                        Plugin.Instance.Logger.Debug(
                            "Patch CanRefreshMetadata Success by Harmony");
                    }
                    if (!IsPatched(_canRefreshImage, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Patch(_canRefreshImage,
                            prefix: new HarmonyMethod(typeof(ExclusiveExtract).GetMethod("CanRefreshImagePrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch CanRefreshImage Success by Harmony");
                    }
                    if (!IsPatched(_afterMetadataRefresh, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Patch(_afterMetadataRefresh,
                            prefix: new HarmonyMethod(typeof(ExclusiveExtract).GetMethod("AfterMetadataRefreshPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug("Patch AfterMetadataRefresh Success by Harmony");
                    }
                    if (!IsPatched(_addVirtualFolder, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Patch(_addVirtualFolder,
                            prefix: new HarmonyMethod(typeof(ExclusiveExtract).GetMethod("RefreshLibraryPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch AddVirtualFolder Success by Harmony");
                    }
                    if (!IsPatched(_removeVirtualFolder, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Patch(_removeVirtualFolder,
                            prefix: new HarmonyMethod(typeof(ExclusiveExtract).GetMethod("RefreshLibraryPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch RemoveVirtualFolder Success by Harmony");
                    }
                    if (!IsPatched(_addMediaPath, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Patch(_addMediaPath,
                            prefix: new HarmonyMethod(typeof(ExclusiveExtract).GetMethod("RefreshLibraryPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch AddMediaPath Success by Harmony");
                    }
                    if (!IsPatched(_removeMediaPath, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Patch(_removeMediaPath,
                            prefix: new HarmonyMethod(typeof(ExclusiveExtract).GetMethod("RefreshLibraryPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch RemoveMediaPath Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Patch ExclusiveExtract Failed by Harmony");
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
                    if (IsPatched(_canRefreshMetadata, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Unpatch(_canRefreshMetadata,
                            AccessTools.Method(typeof(ExclusiveExtract), "CanRefreshMetadataPrefix"));
                        Plugin.Instance.Logger.Debug("Unpatch CanRefreshMetadata Success by Harmony");
                    }
                    if (IsPatched(_canRefreshImage, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Unpatch(_canRefreshImage,
                            AccessTools.Method(typeof(ExclusiveExtract), "CanRefreshImagePrefix"));
                        Plugin.Instance.Logger.Debug("Unpatch CanRefreshImage Success by Harmony");
                    }
                    if (IsPatched(_afterMetadataRefresh, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Unpatch(_afterMetadataRefresh,
                            AccessTools.Method(typeof(ExclusiveExtract), "AfterMetadataRefreshPrefix"));
                        Plugin.Instance.Logger.Debug("Unpatch AfterMetadataRefresh Success by Harmony");
                    }
                    if (IsPatched(_addVirtualFolder, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Unpatch(_addVirtualFolder,
                            AccessTools.Method(typeof(ExclusiveExtract), "RefreshLibraryPrefix"));
                        Plugin.Instance.Logger.Debug("Unpatch AddVirtualFolder Success by Harmony");
                    }
                    if (IsPatched(_removeVirtualFolder, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Unpatch(_removeVirtualFolder,
                            AccessTools.Method(typeof(ExclusiveExtract), "RefreshLibraryPrefix"));
                        Plugin.Instance.Logger.Debug("Unpatch RemoveVirtualFolder Success by Harmony");
                    }
                    if (IsPatched(_addMediaPath, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Unpatch(_addMediaPath,
                            AccessTools.Method(typeof(ExclusiveExtract), "RefreshLibraryPrefix"));
                        Plugin.Instance.Logger.Debug("Unpatch AddMediaPath Success by Harmony");
                    }
                    if (IsPatched(_removeMediaPath, typeof(ExclusiveExtract)))
                    {
                        HarmonyMod.Unpatch(_removeMediaPath,
                            AccessTools.Method(typeof(ExclusiveExtract), "RefreshLibraryPrefix"));
                        Plugin.Instance.Logger.Debug("Unpatch RemoveMediaPath Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Unpatch ExclusiveExtract Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                }
            }
        }

        public static void AllowExtractInstance(BaseItem item)
        {
            ExclusiveItem.Value = item.InternalId;
        }

        [HarmonyPrefix]
        private static void RunFfProcessPrefix(ref int timeoutMs)
        {
            if (ExtractMediaInfoTask.IsRunning || QueueManager.IsMediaInfoProcessTaskRunning)
            {
                timeoutMs = 60000 * Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.MaxConcurrentCount;
            }
        }

        [HarmonyPrefix]
        private static bool CanRefreshMetadataPrefix(IMetadataProvider provider, BaseItem item,
            LibraryOptions libraryOptions, bool includeDisabled, bool forceEnableInternetMetadata,
            bool ignoreMetadataLock, ref bool __result)
        {
            if ((item.Parent is null && item.ExtraType is null) || !(provider is IPreRefreshProvider) ||
                !(provider is ICustomMetadataProvider<Video>))
                return true;

            ChapterChangeTracker.BypassInstance(item);

            if (ExclusiveItem.Value != 0 && ExclusiveItem.Value == item.InternalId) return true;

            if (item.DateLastRefreshed == DateTimeOffset.MinValue)
            {
                __result = false;
                return false;
            }

            if (!IsExclusiveFeatureSelected(ExclusiveControl.IgnoreFileChange) && CurrentRefreshContext.Value != null &&
                Plugin.LibraryApi.HasFileChanged(item))
            {
                CurrentRefreshContext.Value.MediaInfoNeedsUpdate = true;
                return true;
            }

            if (CurrentRefreshContext.Value != null && CurrentRefreshContext.Value.InternalId == item.InternalId &&
                (CurrentRefreshContext.Value.MetadataRefreshOptions.MetadataRefreshMode <=
                    MetadataRefreshMode.Default &&
                    CurrentRefreshContext.Value.MetadataRefreshOptions.ImageRefreshMode <=
                    MetadataRefreshMode.Default || !IsExclusiveFeatureSelected(ExclusiveControl.CatchAllAllow) &&
                    CurrentRefreshContext.Value.MetadataRefreshOptions.SearchResult != null))
            {
                if (item is Video && Plugin.SubtitleApi.HasExternalSubtitleChanged(item))
                    QueueManager.ExternalSubtitleItemQueue.Enqueue(item);

                __result = false;
                return false;
            }

            if (!IsExclusiveFeatureSelected(ExclusiveControl.CatchAllBlock) && !item.IsShortcut &&
                CurrentRefreshContext.Value != null &&
                CurrentRefreshContext.Value.MetadataRefreshOptions.ReplaceAllImages)
            {
                CurrentRefreshContext.Value.MediaInfoNeedsUpdate = true;
                return true;
            }

            if (!IsExclusiveFeatureSelected(ExclusiveControl.CatchAllAllow) && Plugin.LibraryApi.HasMediaInfo(item))
            {
                if (item is Video && Plugin.SubtitleApi.HasExternalSubtitleChanged(item))
                    QueueManager.ExternalSubtitleItemQueue.Enqueue(item);

                __result = false;
                return false;
            }

            if (CurrentRefreshContext.Value != null && (IsExclusiveFeatureSelected(ExclusiveControl.CatchAllAllow) ||
                                                        !IsExclusiveFeatureSelected(ExclusiveControl.CatchAllBlock) &&
                                                        !item.IsShortcut))
            {
                CurrentRefreshContext.Value.MediaInfoNeedsUpdate = true;
                return true;
            }

            if (IsExclusiveFeatureSelected(ExclusiveControl.CatchAllBlock) &&
                !(CurrentRefreshContext.Value != null && CurrentRefreshContext.Value.InternalId == item.InternalId &&
                  CurrentRefreshContext.Value.MetadataRefreshOptions.MetadataRefreshMode ==
                  MetadataRefreshMode.FullRefresh &&
                  CurrentRefreshContext.Value.MetadataRefreshOptions.ImageRefreshMode == MetadataRefreshMode.Default &&
                  !CurrentRefreshContext.Value.MetadataRefreshOptions.ReplaceAllMetadata &&
                  !CurrentRefreshContext.Value.MetadataRefreshOptions.ReplaceAllImages))
            {
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool CanRefreshImagePrefix(IImageProvider provider, BaseItem item, LibraryOptions libraryOptions,
            ImageRefreshOptions refreshOptions, bool ignoreMetadataLock, bool ignoreLibraryOptions, ref bool __result)
        {
            if ((item.Parent is null && item.ExtraType is null) || !provider.Supports(item) ||
                !(item is Video || item is Audio))
                return true;

            if (ExclusiveItem.Value != 0 && ExclusiveItem.Value == item.InternalId) return true;

            if (CurrentRefreshContext.Value == null && refreshOptions is MetadataRefreshOptions options)
            {
                CurrentRefreshContext.Value = new RefreshContext
                {
                    InternalId = item.InternalId,
                    MetadataRefreshOptions = options,
                    MediaInfoNeedsUpdate = false
                };

                if (IsExclusiveFeatureSelected(ExclusiveControl.CatchAllAllow))
                {
                    options.EnableRemoteContentProbe = true;
                }
            }

            if (item.DateLastRefreshed == DateTimeOffset.MinValue) return true;

            if (!item.IsShortcut &&
                item.HasImage(ImageType.Primary) && provider is IDynamicImageProvider &&
                provider.GetType().Name == "VideoImageProvider" &&
                (IsExclusiveFeatureSelected(ExclusiveControl.CatchAllBlock) ||
                 !IsExclusiveFeatureSelected(ExclusiveControl.CatchAllAllow) &&
                 refreshOptions is MetadataRefreshOptions && !refreshOptions.ReplaceAllImages))
            {
                __result = false;
                return false;
            }

            if (item.IsShortcut &&
                item.HasImage(ImageType.Primary) &&
                (provider is ILocalImageProvider || provider is IRemoteImageProvider) &&
                (IsExclusiveFeatureSelected(ExclusiveControl.CatchAllBlock) ||
                 !IsExclusiveFeatureSelected(ExclusiveControl.CatchAllAllow) &&
                 refreshOptions is MetadataRefreshOptions && !refreshOptions.ReplaceAllImages))
            {
                __result = false;
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool AfterMetadataRefreshPrefix(BaseItem __instance)
        {
            if (Plugin.Instance.MediaInfoExtractStore.GetOptions().PersistMediaInfo &&
                (__instance is Video || __instance is Audio) && Plugin.LibraryApi.IsLibraryInScope(__instance) &&
                CurrentRefreshContext.Value != null &&
                CurrentRefreshContext.Value.InternalId == __instance.InternalId && ExclusiveItem.Value == 0)
            {
                if (CurrentRefreshContext.Value.MediaInfoNeedsUpdate)
                {
                    if (__instance.IsShortcut)
                    {
                        Task.Run(() => Plugin.LibraryApi.DeleteMediaInfoJson(__instance, CancellationToken.None));
                    }
                    else
                    {
                        Task.Run(() =>
                            Plugin.LibraryApi.SerializeMediaInfo(__instance, true, "Exclusive Overwrite",
                                CancellationToken.None));
                    }
                }
                else if (!Plugin.LibraryApi.HasMediaInfo(__instance))
                {
                    Task.Run(() =>
                        Plugin.LibraryApi.DeserializeMediaInfo(__instance, "Exclusive Restore",
                            CancellationToken.None));
                }
                else
                {
                    Task.Run(() =>
                        Plugin.LibraryApi.SerializeMediaInfo(__instance, false, "Exclusive Non-existence",
                            CancellationToken.None));
                }

                CurrentRefreshContext.Value = null;
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool RefreshLibraryPrefix(object request)
        {
            var requestType = request.GetType();

            if (!RefreshLibraryPropertyCache.TryGetValue(requestType, out var refreshLibraryProperty))
            {
                refreshLibraryProperty = requestType.GetProperty("RefreshLibrary");
                RefreshLibraryPropertyCache[requestType] = refreshLibraryProperty;
            }

            if (refreshLibraryProperty != null && refreshLibraryProperty.CanWrite)
            {
                refreshLibraryProperty.SetValue(request, false);
            }

            return true;
        }
    }
}
