using HarmonyLib;
using System;
using System.Reflection;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class NoBoxsetsAutoCreation
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static MethodInfo _ensureLibraryFolder;

        public static void Initialize()
        {
            try
            {
                var embyServerImplementationsAssembly = Assembly.Load("Emby.Server.Implementations");
                var collectionManager =
                    embyServerImplementationsAssembly.GetType(
                        "Emby.Server.Implementations.Collections.CollectionManager");
                _ensureLibraryFolder = collectionManager.GetMethod("EnsureLibraryFolder",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            }
            catch (Exception e)
            {
                Plugin.Instance.Logger.Warn("NoBoxsetsAutoCreation - Patch Init Failed");
                Plugin.Instance.Logger.Debug(e.Message);
                Plugin.Instance.Logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.ExperienceEnhanceStore.GetOptions().UIFunctionOptions.NoBoxsetsAutoCreation)
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
                    if (!IsPatched(_ensureLibraryFolder, typeof(NoBoxsetsAutoCreation)))
                    {
                        HarmonyMod.Patch(_ensureLibraryFolder,
                            prefix: new HarmonyMethod(typeof(NoBoxsetsAutoCreation).GetMethod("EnsureLibraryFolderPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch EnsureLibraryFolder Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Patch EnsureLibraryFolder Failed by Harmony");
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
                    if (IsPatched(_ensureLibraryFolder, typeof(NoBoxsetsAutoCreation)))
                    {
                        HarmonyMod.Unpatch(_ensureLibraryFolder,
                            AccessTools.Method(typeof(NoBoxsetsAutoCreation), "EnsureLibraryFolderPrefix"));
                        Plugin.Instance.Logger.Debug("Unpatch EnsureLibraryFolder Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Unpatch EnsureLibraryFolder Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                }
            }
        }

        [HarmonyPrefix]
        private static bool EnsureLibraryFolderPrefix()
        {
            return false;
        }
    }
}
