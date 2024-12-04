using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using System;
using System.Reflection;
using static StrmAssistant.LanguageUtility;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class PinyinSortName
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static MethodInfo _sortNameGetter;

        public static void Initialize()
        {
            try
            {
                var sortNameProperty =
                    typeof(BaseItem).GetProperty("SortName", BindingFlags.Instance | BindingFlags.Public);
                _sortNameGetter = sortNameProperty?.GetGetMethod();
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("PinyinSortName - Patch Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions.PinyinSortName)
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
                    if (!IsPatched(_sortNameGetter, typeof(PinyinSortName)))
                    {
                        HarmonyMod.Patch(_sortNameGetter,
                            prefix: new HarmonyMethod(typeof(PinyinSortName).GetMethod("SortNameGetterPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug("Patch SortNameGetter Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch PinyinSortName Failed by Harmony");
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
                    if (IsPatched(_sortNameGetter, typeof(PinyinSortName)))
                    {
                        HarmonyMod.Unpatch(_sortNameGetter,
                            AccessTools.Method(typeof(PinyinSortName), "SortNameGetterPrefix"));
                        Plugin.Instance.logger.Debug("Unpatch SortNameGetter Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch PinyinSortName Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        [HarmonyPrefix]
        private static bool SortNameGetterPrefix(BaseItem __instance, ref string __result)
        {
            if (!(__instance is Movie || __instance is Series || __instance is BoxSet)) return true;

            if (__instance.IsFieldLocked(MetadataFields.SortName) || !IsChinese(__instance.Name) ||
                IsJapanese(__instance.Name)) return true;

            var nameToProcess = __instance is BoxSet ? RemoveDefaultCollectionName(__instance.Name) : __instance.Name;

            __result = ConvertToPinyinInitials(nameToProcess);

            return false;
        }
    }
}
