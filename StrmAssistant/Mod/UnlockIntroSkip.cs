using HarmonyLib;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Configuration;
using System;
using System.Reflection;
using System.Threading;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class UnlockIntroSkip
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();
        private static MethodInfo _isIntroDetectionSupported;

        private static readonly AsyncLocal<bool> IsShortcutPatched = new AsyncLocal<bool>();

        public static void Initialize()
        {
            try
            {
                var embyProviders = Assembly.Load("Emby.Providers");
                var audioFingerprintManager = embyProviders.GetType("Emby.Providers.Markers.AudioFingerprintManager");
                _isIntroDetectionSupported = audioFingerprintManager.GetMethod("IsIntroDetectionSupported",
                    BindingFlags.Public | BindingFlags.Instance);
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
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (!IsPatched(_isIntroDetectionSupported, typeof(UnlockIntroSkip)))
                    {
                        HarmonyMod.Patch(_isIntroDetectionSupported,
                            prefix: new HarmonyMethod(typeof(UnlockIntroSkip).GetMethod(
                                "IsIntroDetectionSupportedPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(UnlockIntroSkip).GetMethod(
                                "IsIntroDetectionSupportedPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch IsIntroDetectionSupported Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch IsIntroDetectionSupported Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
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
                    if (IsPatched(_isIntroDetectionSupported, typeof(UnlockIntroSkip)))
                    {
                        HarmonyMod.Unpatch(_isIntroDetectionSupported,
                            AccessTools.Method(typeof(UnlockIntroSkip), "IsIntroDetectionSupportedPrefix"));
                        HarmonyMod.Unpatch(_isIntroDetectionSupported,
                            AccessTools.Method(typeof(UnlockIntroSkip), "IsIntroDetectionSupportedPostfix"));
                        Plugin.Instance.logger.Debug("Unpatch IsIntroDetectionSupported Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch IsIntroDetectionSupported Failed by Harmony");
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
                EnableImageCapture.PatchInstanceIsShortcut(item);
                IsShortcutPatched.Value = true;
            }

            return true;
        }

        [HarmonyPostfix]
        private static void IsIntroDetectionSupportedPostfix(Episode item, LibraryOptions libraryOptions,
            ref bool __result)
        {
            if (IsShortcutPatched.Value)
            {
                EnableImageCapture.UnpatchInstanceIsShortcut(item);
            }
        }
    }
}
