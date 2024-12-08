using HarmonyLib;
using System;
using System.IO;
using System.Reflection;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class MergeMultiVersion
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();
        private static MethodInfo _isEligibleForMultiVersion;

        public static void Initialize()
        {
            try
            {
                var namingAssembly = Assembly.Load("Emby.Naming");
                var videoListResolverType = namingAssembly.GetType("Emby.Naming.Video.VideoListResolver");
                _isEligibleForMultiVersion = videoListResolverType.GetMethod("IsEligibleForMultiVersion",
                    BindingFlags.Static | BindingFlags.NonPublic);
            }
            catch (Exception e)
            {
                Plugin.Instance.Logger.Warn("MergeMultiVersion - Patch Init Failed");
                Plugin.Instance.Logger.Debug(e.Message);
                Plugin.Instance.Logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.MainOptionsStore.GetOptions().ModOptions.MergeMultiVersion)
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
                    if (!IsPatched(_isEligibleForMultiVersion, typeof(MergeMultiVersion)))
                    {
                        HarmonyMod.Patch(_isEligibleForMultiVersion,
                            prefix: new HarmonyMethod(typeof(MergeMultiVersion).GetMethod("IsEligibleForMultiVersionPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch IsEligibleForMultiVersion Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Patch IsEligibleForMultiVersion Failed by Harmony");
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
                    if (IsPatched(_isEligibleForMultiVersion, typeof(MergeMultiVersion)))
                    {
                        HarmonyMod.Unpatch(_isEligibleForMultiVersion,
                            AccessTools.Method(typeof(MergeMultiVersion), "IsEligibleForMultiVersionPrefix"));
                        Plugin.Instance.Logger.Debug("Unpatch IsEligibleForMultiVersion Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Unpatch IsEligibleForMultiVersion Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                }
            }
        }

        [HarmonyPrefix]
        private static bool IsEligibleForMultiVersionPrefix(string folderName, string testFilename, ref bool __result)
        {
            __result = string.Equals(folderName, Path.GetFileName(Path.GetDirectoryName(testFilename)),
                StringComparison.OrdinalIgnoreCase);

            return false;
        }
    }
}
