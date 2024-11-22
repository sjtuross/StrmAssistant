using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using System;
using System.Reflection;
using static StrmAssistant.Common.LanguageUtility;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class PinyinSortName
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static MethodInfo _afterMetadataRefresh;

        public static void Initialize()
        {
            try
            {
                _afterMetadataRefresh =
                    typeof(BaseItem).GetMethod("AfterMetadataRefresh", BindingFlags.Instance | BindingFlags.Public);
            }
            catch (Exception e)
            {
                Plugin.Instance.Logger.Warn("PinyinSortName - Patch Init Failed");
                Plugin.Instance.Logger.Debug(e.Message);
                Plugin.Instance.Logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.MetadataEnhanceStore.GetOptions().PinyinSortName)
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
                    if (!IsPatched(_afterMetadataRefresh, typeof(PinyinSortName)))
                    {
                        HarmonyMod.Patch(_afterMetadataRefresh,
                            prefix: new HarmonyMethod(typeof(PinyinSortName).GetMethod("AfterMetadataRefreshPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug("Patch AfterMetadataRefresh Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Patch PinyinSortName Failed by Harmony");
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
                    if (IsPatched(_afterMetadataRefresh, typeof(PinyinSortName)))
                    {
                        HarmonyMod.Unpatch(_afterMetadataRefresh,
                            AccessTools.Method(typeof(PinyinSortName), "AfterMetadataRefreshPrefix"));
                        Plugin.Instance.Logger.Debug("Unpatch AfterMetadataRefresh Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Unpatch PinyinSortName Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                }
            }
        }

        [HarmonyPrefix]
        private static bool AfterMetadataRefreshPrefix(BaseItem __instance)
        {
            if (!__instance.IsFieldLocked(MetadataFields.SortName) && IsChinese(__instance.Name) &&
                !IsJapanese(__instance.Name))
            {
                if (__instance is Movie || __instance is Series)
                {
                    __instance.SetSortNameDirect(ConvertToPinyinInitials(__instance.Name));
                    __instance.UpdateToRepository(ItemUpdateType.MetadataEdit);
                }

                if (__instance is BoxSet)
                {
                    __instance.SetSortNameDirect(ConvertToPinyinInitials(RemoveDefaultCollectionName(__instance.Name)));
                    __instance.UpdateToRepository(ItemUpdateType.MetadataEdit);
                }

                return false;
            }

            return true;
        }
    }
}
