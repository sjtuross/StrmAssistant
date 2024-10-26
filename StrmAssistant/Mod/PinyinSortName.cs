using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
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
                Plugin.Instance.logger.Warn("PinyinSortName - Patch Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.GetPluginOptions().ModOptions.PinyinSortName)
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
                        Plugin.Instance.logger.Debug("Patch AfterMetadataRefresh Success by Harmony");
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
                    if (IsPatched(_afterMetadataRefresh, typeof(PinyinSortName)))
                    {
                        HarmonyMod.Unpatch(_afterMetadataRefresh,
                            AccessTools.Method(typeof(PinyinSortName), "AfterMetadataRefreshPrefix"));
                        Plugin.Instance.logger.Debug("Unpatch AfterMetadataRefresh Success by Harmony");
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
        private static bool AfterMetadataRefreshPrefix(BaseItem __instance)
        {
            if (!__instance.IsFieldLocked(MetadataFields.SortName) &&
                IsChinese(__instance.Name) && !(IsJapanese(__instance.Name) || IsKorean(__instance.Name)))
            {
                if (__instance is Movie || __instance is Series)
                {
                    __instance.SetSortNameDirect(ConvertToPinyinInitials(__instance.Name));
                }

                if (__instance is BoxSet)
                {
                    __instance.SetSortNameDirect(ConvertToPinyinInitials(RemoveDefaultCollectionName(__instance.Name)));
                }

                __instance.UpdateToRepository(ItemUpdateType.MetadataEdit);

                return false;
            }

            return true;
        }
    }
}
