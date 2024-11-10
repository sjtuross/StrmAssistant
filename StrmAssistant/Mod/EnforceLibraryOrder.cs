using HarmonyLib;
using MediaBrowser.Controller.Entities;
using System;
using System.Linq;
using System.Reflection;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class EnforceLibraryOrder
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static MethodInfo _getUserViews;

        public static void Initialize()
        {
            try
            {
                var embyServerImplementationsAssembly = Assembly.Load("Emby.Server.Implementations");
                var userViewManager = embyServerImplementationsAssembly.GetType("Emby.Server.Implementations.Library.UserViewManager");
                _getUserViews = userViewManager.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "GetUserViews" &&
                                         (m.GetParameters().Length == 3 || m.GetParameters().Length == 4));
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("EnforceLibraryOrder - Patch Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.GetPluginOptions().UIFunctionOptions.EnforceLibraryOrder)
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
                    if (!IsPatched(_getUserViews, typeof(EnforceLibraryOrder)))
                    {
                        HarmonyMod.Patch(_getUserViews,
                            prefix: new HarmonyMethod(typeof(EnforceLibraryOrder).GetMethod("GetUserViewsPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch GetUserViews Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch GetUserViews Failed by Harmony");
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
                    if (IsPatched(_getUserViews, typeof(EnforceLibraryOrder)))
                    {
                        HarmonyMod.Unpatch(_getUserViews,
                            AccessTools.Method(typeof(EnforceLibraryOrder), "GetUserViewsPrefix"));
                        Plugin.Instance.logger.Debug("Unpatch GetUserViews Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch GetUserViews Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        [HarmonyPrefix]
        private static bool GetUserViewsPrefix(User user)
        {
            user.Configuration.OrderedViews = LibraryApi.AdminOrderedViews;

            return true;
        }
    }
}
