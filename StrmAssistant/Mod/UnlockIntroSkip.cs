using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class UnlockIntroSkip
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();
        private static MethodInfo _isIntroDetectionSupported;
        private static MethodInfo _createQueryForEpisodeIntroDetection;

        private static readonly AsyncLocal<bool> IsIntroDetectionSupportedInstancePatched = new AsyncLocal<bool>();

        public static void Initialize()
        {
            try
            {
                var embyProviders = Assembly.Load("Emby.Providers");
                var audioFingerprintManager = embyProviders.GetType("Emby.Providers.Markers.AudioFingerprintManager");
                _isIntroDetectionSupported = audioFingerprintManager.GetMethod("IsIntroDetectionSupported",
                    BindingFlags.Public | BindingFlags.Instance);
                var markerScheduledTask = embyProviders.GetType("Emby.Providers.Markers.MarkerScheduledTask");
                _createQueryForEpisodeIntroDetection = markerScheduledTask.GetMethod(
                    "CreateQueryForEpisodeIntroDetection",
                    BindingFlags.Public | BindingFlags.Static);
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("UnlockIntroSkip - Patch Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.GetPluginOptions().IntroSkipOptions.UnlockIntroSkip)
            {
                Patch();
            }
        }

        public static void Patch()
        {
            EnableImageCapture.PatchIsShortcut();

            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (!IsPatched(_isIntroDetectionSupported, typeof(UnlockIntroSkip)))
                    {
                        HarmonyMod.Patch(_isIntroDetectionSupported,
                            prefix: new HarmonyMethod(typeof(UnlockIntroSkip).GetMethod(
                                "IsIntroDetectionSupportedPrefix", BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(UnlockIntroSkip).GetMethod(
                                "IsIntroDetectionSupportedPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug("Patch IsIntroDetectionSupported Success by Harmony");
                    }

                    if (!IsPatched(_createQueryForEpisodeIntroDetection, typeof(UnlockIntroSkip)))
                    {
                        HarmonyMod.Patch(_createQueryForEpisodeIntroDetection,
                            postfix: new HarmonyMethod(typeof(UnlockIntroSkip).GetMethod(
                                "CreateQueryForEpisodeIntroDetectionPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug("Patch CreateQueryForEpisodeIntroDetection Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch UnlockIntroSkip Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                    PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;
                }
            }
        }

        public static void Unpatch()
        {
            EnableImageCapture.UnpatchIsShortcut();

            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (IsPatched(_isIntroDetectionSupported, typeof(UnlockIntroSkip)))
                    {
                        HarmonyMod.Unpatch(_isIntroDetectionSupported,
                            AccessTools.Method(typeof(UnlockIntroSkip), "IsIntroDetectionSupportedPrefix"));
                        HarmonyMod.Unpatch(_isIntroDetectionSupported,
                            AccessTools.Method(typeof(UnlockIntroSkip), "IsIntroDetectionSupportedPostfix"));
                        Plugin.Instance.logger.Debug("Unpatch IsIntroDetectionSupported Success by Harmony");
                    }

                    if (IsPatched(_createQueryForEpisodeIntroDetection, typeof(UnlockIntroSkip)))
                    {
                        HarmonyMod.Unpatch(_createQueryForEpisodeIntroDetection,
                            AccessTools.Method(typeof(UnlockIntroSkip), "CreateQueryForEpisodeIntroDetectionPostfix"));
                        Plugin.Instance.logger.Debug("Unpatch CreateQueryForEpisodeIntroDetection Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch UnlockIntroSkip Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        [HarmonyPrefix]
        private static bool IsIntroDetectionSupportedPrefix(Episode item, LibraryOptions libraryOptions,
            ref bool __result)
        {
            if (item.IsShortcut)
            {
                EnableImageCapture.PatchIsShortcutInstance(item);
                IsIntroDetectionSupportedInstancePatched.Value = true;
            }

            return true;
        }

        [HarmonyPostfix]
        private static void IsIntroDetectionSupportedPostfix(Episode item, LibraryOptions libraryOptions,
            ref bool __result)
        {
            if (IsIntroDetectionSupportedInstancePatched.Value)
            {
                EnableImageCapture.UnpatchIsShortcutInstance(item);
                IsIntroDetectionSupportedInstancePatched.Value = false;
            }
        }

        [HarmonyPostfix]
        private static void CreateQueryForEpisodeIntroDetectionPostfix(LibraryOptions libraryOptions, ref InternalItemsQuery __result)
        {
            var libraries = Plugin.ChapterApi.GetMarkerEnabledLibraries(true);

            if (libraries.Any())
            {
                __result.PathStartsWithAny = libraries.SelectMany(l => l.Locations)
                    .Select(ls =>
                        ls.EndsWith(Path.DirectorySeparatorChar.ToString()) ? ls : ls + Path.DirectorySeparatorChar)
                    .ToArray();
            }
        }
    }
}
