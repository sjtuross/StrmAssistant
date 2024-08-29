using HarmonyLib;
using MediaBrowser.Controller.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace StrmAssistant
{
    public static class Patch
    {
        private static Assembly _mediaEncodingAssembly;
        private static Type _imageExtractorBaseType;
        private static ConstructorInfo _staticConstructor;
        private static FieldInfo _resourcePoolField;
        private static MethodInfo _isShortcutGetter;
        private static PropertyInfo _isShortcutProperty;

        private static AsyncLocal<BaseItem> CurrentItem { get; } = new();
        private static int _currentMaxConcurrentCount;
        private static bool _enableImageCapture;

        private enum PatchApproach
        {
            Harmony = 0,
            Reflection = 1,
            None = 2
        };
        private static PatchApproach _fallbackPatchApproach = PatchApproach.Harmony;

        public static SemaphoreSlim SemaphoreFFmpeg;
        public static Harmony Mod;

        public static void Initialize()
        {
            try
            {
                _mediaEncodingAssembly = Assembly.Load("Emby.Server.MediaEncoding");
                _imageExtractorBaseType =
                    _mediaEncodingAssembly.GetType("Emby.Server.MediaEncoding.ImageExtraction.ImageExtractorBase");
                _staticConstructor =
                    _imageExtractorBaseType.GetConstructor(BindingFlags.Static | BindingFlags.NonPublic, null,
                        Type.EmptyTypes, null);
                _resourcePoolField =
                    _imageExtractorBaseType.GetField("resourcePool", BindingFlags.NonPublic | BindingFlags.Static);
                _isShortcutGetter = typeof(BaseItem)
                    .GetProperty("IsShortcut", BindingFlags.Instance | BindingFlags.Public)
                    .GetGetMethod();
                _isShortcutProperty =
                    typeof(BaseItem).GetProperty("IsShortcut", BindingFlags.Instance | BindingFlags.Public);
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("Patch Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
                _fallbackPatchApproach = PatchApproach.None;
            }

            _enableImageCapture = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.EnableImageCapture;
            _currentMaxConcurrentCount = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.MaxConcurrentCount;
            SemaphoreFFmpeg = new SemaphoreSlim(_currentMaxConcurrentCount);

            if (_enableImageCapture)
            {
                Mod = new Harmony("emby.mod");
                PatchResourcePool();
                PatchIsShortcut();
            }

            var resourcePool = (SemaphoreSlim)_resourcePoolField.GetValue(null);
            Plugin.Instance.logger.Info("Current FFmpeg ResourcePool: " + resourcePool?.CurrentCount ?? String.Empty);
        }

        private static bool IsPatched(MethodBase methodInfo)
        {
            var patchedMethods = Harmony.GetAllPatchedMethods();
            if (!patchedMethods.Contains(methodInfo)) return false;
            var patchInfo = Harmony.GetPatchInfo(methodInfo);

            return patchInfo.Prefixes.Any(p => p.owner == Mod.Id) ||
                   patchInfo.Transpilers.Any(p => p.owner == Mod.Id);
        }

        private static void PatchResourcePool()
        {
            switch (_fallbackPatchApproach)
            {
                case PatchApproach.Harmony:
                    try
                    {
                        if (!IsPatched(_staticConstructor))
                        {
                            Mod.Patch(_staticConstructor,
                                prefix: new HarmonyMethod(typeof(Patch).GetMethod("ResourcePoolPrefix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            //Mod.Patch(_staticConstructor,
                            //    transpiler: new HarmonyMethod(typeof(Patch).GetMethod("ResourcePoolTranspiler",
                            //        BindingFlags.Static | BindingFlags.NonPublic)));

                            Plugin.Instance.logger.Debug("Patch FFmpeg ResourcePool Success by Harmony");
                        }
                    }
                    catch (Exception he)
                    {
                        Plugin.Instance.logger.Debug("Patch FFmpeg ResourcePool Failed by Harmony");
                        Plugin.Instance.logger.Debug(he.Message);
                        Plugin.Instance.logger.Debug(he.StackTrace);
                        _fallbackPatchApproach = PatchApproach.Reflection;

                        try
                        {
                            _resourcePoolField.SetValue(null, SemaphoreFFmpeg);
                            Plugin.Instance.logger.Debug("Patch FFmpeg ResourcePool Success by Reflection");
                        }
                        catch (Exception re)
                        {
                            Plugin.Instance.logger.Debug("Patch FFmpeg ResourcePool Failed by Reflection");
                            Plugin.Instance.logger.Debug(re.Message);
                            _fallbackPatchApproach = PatchApproach.None;
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
                        _fallbackPatchApproach = PatchApproach.None;
                    }
                    break;
            }
        }

        private static void PatchIsShortcut()
        {
            if (_fallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (!IsPatched(_isShortcutGetter))
                    {
                        Mod.Patch(_isShortcutGetter,
                            prefix: new HarmonyMethod(typeof(Patch).GetMethod("IsShortcutPrefix",
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
                    _fallbackPatchApproach = PatchApproach.Reflection;
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

                switch (_fallbackPatchApproach)
                {
                    case PatchApproach.Harmony:
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
            switch (_fallbackPatchApproach)
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
                        _fallbackPatchApproach = PatchApproach.None;
                    }
                    break;
            }
        }

        public static void UnpatchInstanceIsShortcut(BaseItem item)
        {
            switch (_fallbackPatchApproach)
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
                        _fallbackPatchApproach = PatchApproach.None;
                    }
                    break;
            }
        }

        public static void UnpatchResourcePool()
        {
            if (_fallbackPatchApproach== PatchApproach.Harmony)
            {
                try
                {
                    if (IsPatched(_staticConstructor))
                    {
                        Mod.Unpatch(_staticConstructor, HarmonyPatchType.All);
                        Plugin.Instance.logger.Debug("Unpatch IsShortcut Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch IsShortcut Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                    _fallbackPatchApproach = PatchApproach.Reflection;
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
            if (_fallbackPatchApproach== PatchApproach.Harmony)
            {
                try
                {
                    if (IsPatched(_isShortcutGetter))
                    {
                        Mod.Unpatch(_isShortcutGetter, HarmonyPatchType.Prefix);
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
            if (__instance == CurrentItem.Value)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}
