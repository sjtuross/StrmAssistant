using HarmonyLib;
using MediaBrowser.Controller.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;

namespace StrmExtract
{
    public static class Patch
    {
        private static Assembly _mediaEncodingAssembly;
        private static Type _imageExtractorBaseType;
        private static ConstructorInfo _staticConstructor;
        private static FieldInfo _resourcePoolField;
        private static MethodInfo _isShortcutGetter;
        private static PropertyInfo _isShortcut;
        private static readonly ConditionalWeakTable<BaseItem, object> _targetBaseItems = new();
        private static int _currentMaxConcurrentCount;

        public static SemaphoreSlim SemaphoreFFmpeg;
        public static Harmony Mod;

        public static void Initialize()
        {
            try
            {
                _mediaEncodingAssembly = Assembly.Load("Emby.Server.MediaEncoding");
                _imageExtractorBaseType =
                    _mediaEncodingAssembly.GetType("Emby.Server.MediaEncoding.ImageExtraction.ImageExtractorBase");
                _staticConstructor = _imageExtractorBaseType.GetConstructor(BindingFlags.Static | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                _resourcePoolField =
                    _imageExtractorBaseType.GetField("resourcePool", BindingFlags.NonPublic | BindingFlags.Static);
                _isShortcutGetter = typeof(BaseItem).GetProperty("IsShortcut", BindingFlags.Instance | BindingFlags.Public)
                    .GetGetMethod();
                _isShortcut = typeof(BaseItem).GetProperty("IsShortcut", BindingFlags.Instance | BindingFlags.Public);

                Mod = new Harmony("emby.mod");

                _currentMaxConcurrentCount = Plugin.Instance.GetPluginOptions().MaxConcurrentCount;
                SemaphoreFFmpeg = new SemaphoreSlim(_currentMaxConcurrentCount);
                UpdateResourcePool(_currentMaxConcurrentCount);
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("Patch Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
            }
        }

        public static void UpdateResourcePool(int maxConcurrentCount)
        {
            SemaphoreSlim resourcePool;
            try
            {
                var patchedMethods = Harmony.GetAllPatchedMethods();

                //un-patch and re-patch don't work for readonly static field
                if (patchedMethods.Contains(_staticConstructor))
                {
                    var patchInfo = Harmony.GetPatchInfo(_staticConstructor);
                    bool isPatched = patchInfo.Prefixes.Any(p => p.owner == Mod.Id);
                    //bool isPatched = patchInfo.Transpilers.Any(p => p.owner == Mod.Id);
                    if (isPatched)
                    {
                        Mod.Unpatch(_staticConstructor, HarmonyPatchType.All, Mod.Id);
                        //resourcePool = (SemaphoreSlim)_resourcePoolField.GetValue(null);
                        //Plugin.Instance.logger.Info("Current Resource Pool: " + resourcePool.CurrentCount);
                    }
                }

                Mod.Patch(_staticConstructor,
                    prefix: new HarmonyMethod(typeof(Patch).GetMethod("ResourcePoolPrefix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
                //Mod.Patch(_staticConstructor,
                //    transpiler: new HarmonyMethod(typeof(Patch).GetMethod("ResourcePoolTranspiler",
                //        BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception he)
            {
                Plugin.Instance.logger.Debug("Patch ResourcePool Failed by Harmony");
                Plugin.Instance.logger.Debug(he.Message);
                Plugin.Instance.logger.Debug(he.StackTrace);
                try
                {
                    if (_currentMaxConcurrentCount != maxConcurrentCount)
                    {
                        _currentMaxConcurrentCount = maxConcurrentCount;
                        var newSemaphoreFFmpeg = new SemaphoreSlim(maxConcurrentCount);
                        var oldSemaphoreFFmpeg = SemaphoreFFmpeg;
                        SemaphoreFFmpeg = newSemaphoreFFmpeg;
                        _resourcePoolField.SetValue(null, SemaphoreFFmpeg);
                        oldSemaphoreFFmpeg.Dispose();
                    }
                    _resourcePoolField.SetValue(null, SemaphoreFFmpeg);
                }
                catch (Exception re)
                {
                    Plugin.Instance.logger.Debug("Patch ResourcePool Failed by Reflection");
                    Plugin.Instance.logger.Debug(re.Message);
                }
            }
            finally
            {
                resourcePool = (SemaphoreSlim)_resourcePoolField.GetValue(null);
                Plugin.Instance.logger.Info("Current Resource Pool: " + resourcePool?.CurrentCount ?? String.Empty);
            }
        }

        public static void SetIsShortcutFalse(BaseItem item)
        {
            try
            {
                _targetBaseItems.Add(item, null);
                Mod.Patch(_isShortcutGetter,
                    prefix: new HarmonyMethod(typeof(Patch).GetMethod("IsShortcutPrefix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception he)
            {
                Plugin.Instance.logger.Debug("Patch IsShortCut Failed by Harmony");
                Plugin.Instance.logger.Debug(he.Message);
                Plugin.Instance.logger.Debug(he.StackTrace);
                try
                {
                    _isShortcut.SetValue(item, true);
                }
                catch (Exception re)
                {
                    Plugin.Instance.logger.Debug("Patch IsShortCut Failed by Reflection");
                    Plugin.Instance.logger.Debug(re.Message);
                }
            }
        }

        private static bool ResourcePoolPrefix()
        {
            _resourcePoolField.SetValue(null, SemaphoreFFmpeg);
            return false;
        }

        private static IEnumerable<CodeInstruction> ResourcePoolTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_I4_1)
                {
                    codes[i] = new CodeInstruction(OpCodes.Ldc_I4_S,
                        (sbyte)Plugin.Instance.GetPluginOptions().MaxConcurrentCount);
                    if (i + 1 < codes.Count && codes[i + 1].opcode == OpCodes.Ldc_I4_1)
                    {
                        codes[i + 1] = new CodeInstruction(OpCodes.Ldc_I4_S,
                            (sbyte)Plugin.Instance.GetPluginOptions().MaxConcurrentCount);
                    }
                    break;
                }
            }
            return codes.AsEnumerable();
        }

        private static bool IsShortcutPrefix(BaseItem __instance, ref bool __result)
        {
            if (_targetBaseItems.TryGetValue(__instance, out _))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
