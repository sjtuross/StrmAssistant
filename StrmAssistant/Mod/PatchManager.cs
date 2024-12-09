using HarmonyLib;
using System;
using System.Diagnostics;
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
            EnhanceChineseSearch.Initialize();
            MergeMultiVersion.Initialize();
            ExclusiveExtract.Initialize();
            ChineseMovieDb.Initialize();
            EnhanceMovieDbPerson.Initialize();
            AltMovieDbConfig.Initialize();
            EnableProxyServer.Initialize();
            PreferOriginalPoster.Initialize();
            UnlockIntroSkip.Initialize();
            PinyinSortName.Initialize();
            EnhanceNfoMetadata.Initialize();
            HidePersonNoImage.Initialize();
            EnforceLibraryOrder.Initialize();
            BeautifyMissingMetadata.Initialize();
            EnhanceMissingEpisodes.Initialize();
        }

        public static bool IsPatched(MethodBase methodInfo, Type type)
        {
            var patchedMethods = Harmony.GetAllPatchedMethods();
            if (!patchedMethods.Contains(methodInfo)) return false;
            var patchInfo = Harmony.GetPatchInfo(methodInfo);

            return patchInfo.Prefixes.Any(p => p.owner == HarmonyMod.Id && p.PatchMethod.DeclaringType == type) ||
                   patchInfo.Postfixes.Any(p => p.owner == HarmonyMod.Id && p.PatchMethod.DeclaringType == type) ||
                   patchInfo.Transpilers.Any(p => p.owner == HarmonyMod.Id && p.PatchMethod.DeclaringType == type);
        }

        public static bool WasCalledByMethod(Assembly assembly, string callingMethodName)
        {
            var stackFrames = new StackTrace(1, false).GetFrames();
            if (stackFrames != null && stackFrames.Select(f => f.GetMethod()).Any(m =>
                    m?.DeclaringType?.Assembly == assembly && m?.Name == callingMethodName))
                return true;

            return false;
        }
    }
}
