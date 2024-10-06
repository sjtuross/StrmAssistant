using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;

namespace StrmAssistant.Mod
{
    public static class PatchManager
    {
        public static Harmony HarmonyMod;

        public static void Initialize()
        {
            try
            {
                HarmonyMod = new Harmony("emby.mod");
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("Harmony Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
            }

            EnableImageCapture.Initialize();
            MergeMultiVersion.Initialize();
            ChineseMovieDb.Initialize();
            ExclusiveExtract.Initialize();
        }

        public static bool IsPatched(MethodBase methodInfo)
        {
            var patchedMethods = Harmony.GetAllPatchedMethods();
            if (!patchedMethods.Contains(methodInfo)) return false;
            var patchInfo = Harmony.GetPatchInfo(methodInfo);

            return patchInfo.Prefixes.Any(p => p.owner == HarmonyMod.Id) ||
                   patchInfo.Postfixes.Any(p => p.owner == HarmonyMod.Id) ||
                   patchInfo.Transpilers.Any(p => p.owner == HarmonyMod.Id);
        }
    }
}
