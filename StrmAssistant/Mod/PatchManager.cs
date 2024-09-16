using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using MediaBrowser.Common;

namespace StrmAssistant
{
    public static class PatchManager
    {
        public enum PatchApproach
        {
            None = 0,
            Reflection = 1,
            Harmony = 2,
        }

        public static PatchApproach FallbackPatchApproach { get; set; } = PatchApproach.Harmony;

        public static Harmony Mod;
        public static IApplicationHost ApplicationHost;

        public static void Initialize(IApplicationHost applicationHost)
        {
            ApplicationHost = applicationHost;

            try
            {
                Mod = new Harmony("emby.mod");
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("Harmony Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
                FallbackPatchApproach = PatchApproach.Reflection;
            }

            if (FallbackPatchApproach != PatchApproach.None)
            {
                EnableImageCapture.Initialize();
                MergeMultiVersion.Initialize();
                ChineseMovieDb.Initialize();
            }
        }

        public static bool IsPatched(MethodBase methodInfo)
        {
            var patchedMethods = Harmony.GetAllPatchedMethods();
            if (!patchedMethods.Contains(methodInfo)) return false;
            var patchInfo = Harmony.GetPatchInfo(methodInfo);

            return patchInfo.Prefixes.Any(p => p.owner == Mod.Id) ||
                   patchInfo.Postfixes.Any(p => p.owner == Mod.Id) ||
                   patchInfo.Transpilers.Any(p => p.owner == Mod.Id);
        }
    }
}
