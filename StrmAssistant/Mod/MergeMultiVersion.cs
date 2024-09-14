using HarmonyLib;
using System;
using System.IO;
using System.Reflection;
using static StrmAssistant.PatchManager;

namespace StrmAssistant
{
    public static class MergeMultiVersion
    {
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
                Plugin.Instance.logger.Warn("MergeMultiVersion - Patch Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
                FallbackPatchApproach = PatchApproach.None;
            }

            if (FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.GetPluginOptions().ModOptions.MergeMultiVersion)
            {
                Patch();
            }
        }

        public static void Patch()
        {
            if (FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (!IsPatched(_isEligibleForMultiVersion))
                    {
                        Mod.Patch(_isEligibleForMultiVersion,
                            prefix: new HarmonyMethod(typeof(MergeMultiVersion).GetMethod("IsEligibleForMultiVersionPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch IsEligibleForMultiVersion Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch IsEligibleForMultiVersion Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                    FallbackPatchApproach = PatchApproach.Reflection;
                }
            }
        }

        public static void Unpatch()
        {
            if (FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (IsPatched(_isEligibleForMultiVersion))
                    {
                        Mod.Unpatch(_isEligibleForMultiVersion, HarmonyPatchType.Prefix);
                        Plugin.Instance.logger.Debug("Unpatch IsEligibleForMultiVersion Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch IsEligibleForMultiVersion Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        [HarmonyPrefix]
        private static bool IsEligibleForMultiVersionPrefix(ref string testFilename)
        {
            testFilename = Path.GetFileName(Path.GetDirectoryName(testFilename));

            return true;
        }
    }
}
