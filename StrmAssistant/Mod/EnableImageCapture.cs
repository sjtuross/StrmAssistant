using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class EnableImageCapture
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static ConstructorInfo _staticConstructor;
        private static FieldInfo _resourcePoolField;
        private static MethodInfo _isShortcutGetter;
        private static PropertyInfo _isShortcutProperty;
        private static MethodInfo _getImage;
        private static MethodInfo _runExtraction;
        private static Type _quickSingleImageExtractor;
        private static PropertyInfo _totalTimeoutMs;

        private static AsyncLocal<BaseItem> CurrentItem { get; } = new AsyncLocal<BaseItem>();
        private static int _currentMaxConcurrentCount;

        private static SemaphoreSlim SemaphoreFFmpeg;

        public static void Initialize()
        {
            _currentMaxConcurrentCount = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.MaxConcurrentCount;

            try
            {
                var mediaEncodingAssembly = Assembly.Load("Emby.Server.MediaEncoding");
                var imageExtractorBaseType =
                    mediaEncodingAssembly.GetType("Emby.Server.MediaEncoding.ImageExtraction.ImageExtractorBase");
                _staticConstructor =
                    imageExtractorBaseType.GetConstructor(BindingFlags.Static | BindingFlags.NonPublic, null,
                        Type.EmptyTypes, null);
                _resourcePoolField =
                    imageExtractorBaseType.GetField("resourcePool", BindingFlags.NonPublic | BindingFlags.Static);
                _isShortcutGetter = typeof(BaseItem)
                    .GetProperty("IsShortcut", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetGetMethod();
                _isShortcutProperty =
                    typeof(BaseItem).GetProperty("IsShortcut", BindingFlags.Instance | BindingFlags.Public);
                _runExtraction =
                    imageExtractorBaseType.GetMethod("RunExtraction", BindingFlags.Instance | BindingFlags.Public);
                _quickSingleImageExtractor =
                    mediaEncodingAssembly.GetType(
                        "Emby.Server.MediaEncoding.ImageExtraction.QuickSingleImageExtractor");
                _totalTimeoutMs = _quickSingleImageExtractor.GetProperty("TotalTimeoutMs");

                var embyProvidersAssembly = Assembly.Load("Emby.Providers");
                var videoImageProvider = embyProvidersAssembly.GetType("Emby.Providers.MediaInfo.VideoImageProvider");
                _getImage = videoImageProvider.GetMethod("GetImage", BindingFlags.Public | BindingFlags.Instance);
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("EnableImageCapture - Patch Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None)
            {
                SemaphoreFFmpeg = new SemaphoreSlim(_currentMaxConcurrentCount);
                PatchResourcePool();
                var resourcePool = (SemaphoreSlim)_resourcePoolField.GetValue(null);
                Plugin.Instance.logger.Info(
                    "Current FFmpeg ResourcePool: " + resourcePool?.CurrentCount ?? string.Empty);

                if (Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.EnableImageCapture)
                {
                    Patch();
                }
            }
        }

        public static void Patch()
        {
            PatchIsShortcut();
            PatchGetImage();
        }

        public static void Unpatch()
        {
            UnpatchIsShortcut();
            UnpatchGetImage();
        }

        private static void PatchResourcePool()
        {
            switch (PatchApproachTracker.FallbackPatchApproach)
            {
                case PatchApproach.Harmony:
                    try
                    {
                        if (!IsPatched(_staticConstructor, typeof(EnableImageCapture)))
                        {
                            HarmonyMod.Patch(_staticConstructor,
                                prefix: new HarmonyMethod(typeof(EnableImageCapture).GetMethod("ResourcePoolPrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            //HarmonyMod.Patch(_staticConstructor,
                            //    transpiler: new HarmonyMethod(typeof(EnableImageCapture).GetMethod("ResourcePoolTranspiler",
                            //        BindingFlags.Static | BindingFlags.NonPublic)));

                            Plugin.Instance.logger.Debug("Patch FFmpeg ResourcePool Success by Harmony");
                        }
                    }
                    catch (Exception he)
                    {
                        Plugin.Instance.logger.Debug("Patch FFmpeg ResourcePool Failed by Harmony");
                        Plugin.Instance.logger.Debug(he.Message);
                        Plugin.Instance.logger.Debug(he.StackTrace);
                        PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

                        try
                        {
                            _resourcePoolField.SetValue(null, SemaphoreFFmpeg);
                            Plugin.Instance.logger.Debug("Patch FFmpeg ResourcePool Success by Reflection");
                        }
                        catch (Exception re)
                        {
                            Plugin.Instance.logger.Debug("Patch FFmpeg ResourcePool Failed by Reflection");
                            Plugin.Instance.logger.Debug(re.Message);
                            PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
                        }
                    }

                    break;

                case PatchApproach.Reflection:
                    try
                    {
                        _resourcePoolField.SetValue(null, SemaphoreFFmpeg);
                        Plugin.Instance.logger.Debug("Patch FFmpeg ResourcePool Success by Reflection");
                    }
                    catch (Exception re)
                    {
                        Plugin.Instance.logger.Debug("Patch FFmpeg ResourcePool Failed by Reflection");
                        Plugin.Instance.logger.Debug(re.Message);
                        PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
                    }
                    break;
            }
        }

        private static void PatchGetImage()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (!IsPatched(_getImage, typeof(EnableImageCapture)))
                    {
                        HarmonyMod.Patch(_getImage,
                            prefix: new HarmonyMethod(typeof(EnableImageCapture).GetMethod("GetImagePrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch VideoImageProvider.GetImage Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch VideoImageProvider.GetImage Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                    PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;
                }
            }
        }

        private static void PatchIsShortcut()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (!IsPatched(_isShortcutGetter, typeof(EnableImageCapture)))
                    {
                        HarmonyMod.Patch(_isShortcutGetter,
                            prefix: new HarmonyMethod(typeof(EnableImageCapture).GetMethod("IsShortcutPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch IsShortcut Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch IsShortcut Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                    PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;
                }
            }
        }

        public static void UpdateResourcePool(int maxConcurrentCount)
        {
            if (_currentMaxConcurrentCount != maxConcurrentCount)
            {
                _currentMaxConcurrentCount = maxConcurrentCount;
                SemaphoreSlim newSemaphoreFFmpeg;
                SemaphoreSlim oldSemaphoreFFmpeg;

                switch (PatchApproachTracker.FallbackPatchApproach)
                {
                    case PatchApproach.Harmony:
                        Plugin.Instance.ApplicationHost.NotifyPendingRestart();

                        /* un-patch and re-patch don't work for readonly static field
                        UnpatchResourcePool();

                        _currentMaxConcurrentCount = maxConcurrentCount;
                        newSemaphoreFFmpeg = new SemaphoreSlim(maxConcurrentCount);
                        oldSemaphoreFFmpeg = SemaphoreFFmpeg;
                        SemaphoreFFmpeg = newSemaphoreFFmpeg;

                        PatchResourcePool();

                        oldSemaphoreFFmpeg.Dispose();
                        */
                        break;

                    case PatchApproach.Reflection:
                        try
                        {
                            newSemaphoreFFmpeg = new SemaphoreSlim(maxConcurrentCount);
                            oldSemaphoreFFmpeg = SemaphoreFFmpeg;
                            SemaphoreFFmpeg = newSemaphoreFFmpeg;

                            _resourcePoolField.SetValue(null, SemaphoreFFmpeg); //works only with modded Emby.Server.MediaEncoding.dll

                            oldSemaphoreFFmpeg.Dispose();
                        }
                        catch (Exception re)
                        {
                            Plugin.Instance.logger.Debug("Patch FFmpeg ResourcePool Failed by Reflection");
                            Plugin.Instance.logger.Debug(re.Message);
                        }
                        break;
                }
            }

            var resourcePool = (SemaphoreSlim)_resourcePoolField.GetValue(null);
            Plugin.Instance.logger.Info("Current FFmpeg ResourcePool: " + resourcePool?.CurrentCount ?? String.Empty);
        }

        public static void PatchInstanceIsShortcut(BaseItem item)
        {
            switch (PatchApproachTracker.FallbackPatchApproach)
            {
                case PatchApproach.Harmony:
                    CurrentItem.Value = item;
                    break;

                case PatchApproach.Reflection:
                    try
                    {
                        _isShortcutProperty.SetValue(item, true); //special logic depending on modded MediaBrowser.Controller.dll
                        Plugin.Instance.logger.Debug("Patch IsShortcut Success by Reflection" + " - " + item.Name + " - " +
                                                     item.Path);
                    }
                    catch (Exception re)
                    {
                        Plugin.Instance.logger.Debug("Patch IsShortcut Failed by Reflection");
                        Plugin.Instance.logger.Debug(re.Message);
                        PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
                    }
                    break;
            }
        }

        public static void UnpatchInstanceIsShortcut(BaseItem item)
        {
            switch (PatchApproachTracker.FallbackPatchApproach)
            {
                case PatchApproach.Harmony:
                    CurrentItem.Value = null;
                    break;

                case PatchApproach.Reflection:
                    try
                    {
                        _isShortcutProperty.SetValue(item, false); //special logic depending on modded MediaBrowser.Controller.dll
                        Plugin.Instance.logger.Debug("Unpatch IsShortcut Success by Reflection" + " - " + item.Name + " - " +
                                                     item.Path);
                    }
                    catch (Exception re)
                    {
                        Plugin.Instance.logger.Debug("Unpatch IsShortcut Failed by Reflection");
                        Plugin.Instance.logger.Debug(re.Message);
                    }
                    break;
            }
        }

        public static void UnpatchResourcePool()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (IsPatched(_staticConstructor, typeof(EnableImageCapture)))
                    {
                        HarmonyMod.Unpatch(_staticConstructor,
                            AccessTools.Method(typeof(EnableImageCapture), "ResourcePoolPrefix"));
                        //HarmonyMod.Unpatch(_staticConstructor,
                        //    AccessTools.Method(typeof(EnableImageCapture), "ResourcePoolTranspiler"));
                        Plugin.Instance.logger.Debug("Unpatch FFmpeg ResourcePool Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch FFmpeg ResourcePool Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
                finally
                {
                    var resourcePool = (SemaphoreSlim)_resourcePoolField.GetValue(null);
                    Plugin.Instance.logger.Info("Current FFmpeg Resource Pool: " + resourcePool?.CurrentCount ??
                                                String.Empty);
                }
            }
        }

        public static void UnpatchIsShortcut()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (IsPatched(_isShortcutGetter, typeof(EnableImageCapture)))
                    {
                        HarmonyMod.Unpatch(_isShortcutGetter,
                            AccessTools.Method(typeof(EnableImageCapture), "IsShortcutPrefix"));
                        Plugin.Instance.logger.Debug("Unpatch IsShortcut Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch IsShortcut Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        public static void UnpatchGetImage()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (IsPatched(_getImage, typeof(EnableImageCapture)))
                    {
                        HarmonyMod.Unpatch(_getImage, AccessTools.Method(typeof(EnableImageCapture), "GetImagePrefix"));
                        Plugin.Instance.logger.Debug("Unpatch VideoImageProvider.GetImage Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch VideoImageProvider.GetImage Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        public static void PatchFFmpegTimeout()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (!IsPatched(_runExtraction, typeof(EnableImageCapture)))
                    {
                        HarmonyMod.Patch(_runExtraction,
                            prefix: new HarmonyMethod(typeof(EnableImageCapture).GetMethod("RunExtractionPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch RunExtraction Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch RunExtraction Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        public static void UnpatchFFmpegTimeout()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (IsPatched(_runExtraction, typeof(EnableImageCapture)))
                    {
                        HarmonyMod.Unpatch(_runExtraction,
                            AccessTools.Method(typeof(EnableImageCapture), "RunExtractionPrefix"));
                        Plugin.Instance.logger.Debug("Unpatch RunExtraction Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch RunExtraction Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        [HarmonyPrefix]
        private static bool ResourcePoolPrefix()
        {
            _resourcePoolField.SetValue(null, SemaphoreFFmpeg);
            return false;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> ResourcePoolTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_I4_1)
                {
                    codes[i] = new CodeInstruction(OpCodes.Ldc_I4_S,
                        (sbyte)Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.MaxConcurrentCount);
                    if (i + 1 < codes.Count && codes[i + 1].opcode == OpCodes.Ldc_I4_1)
                    {
                        codes[i + 1] = new CodeInstruction(OpCodes.Ldc_I4_S,
                            (sbyte)Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.MaxConcurrentCount);
                    }
                    break;
                }
            }
            return codes.AsEnumerable();
        }

        [HarmonyPrefix]
        private static bool IsShortcutPrefix(BaseItem __instance, ref bool __result)
        {
            if (CurrentItem.Value != null && __instance.InternalId == CurrentItem.Value.InternalId)
            {
                __result = false;
                return false;
            }

            return true;
        }
        
        [HarmonyPrefix]
        private static bool GetImagePrefix(ref BaseMetadataResult itemResult)
        {
            if (itemResult != null && itemResult.MediaStreams != null)
            {
                itemResult.MediaStreams = itemResult.MediaStreams
                    .Where(ms => ms.Type != MediaStreamType.EmbeddedImage)
                    .ToArray();
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool RunExtractionPrefix(object __instance)
        {
            if (_totalTimeoutMs != null && __instance.GetType() == _quickSingleImageExtractor)
            {
                var newValue =
                    60000 * Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.MaxConcurrentCount;
                _totalTimeoutMs.SetValue(__instance, newValue);
            }

            return true;
        }
    }
}
