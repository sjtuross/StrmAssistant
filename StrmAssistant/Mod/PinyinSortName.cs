using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static StrmAssistant.Common.LanguageUtility;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class PinyinSortName
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static MethodInfo _createSortName;
        private static MethodInfo _getPrefixes;
        private static MethodInfo _getArtistPrefixes;

        public static void Initialize()
        {
            try
            {
                _createSortName = typeof(BaseItem).GetMethod("CreateSortName",
                    BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new[] { typeof(ReadOnlySpan<char>) }, null);
                var embyApi = Assembly.Load("Emby.Api");
                var tagService = embyApi.GetType("Emby.Api.UserLibrary.TagService");
                _getPrefixes =
                    tagService.GetMethod("Get", new[] { embyApi.GetType("Emby.Api.UserLibrary.GetPrefixes") });
                _getArtistPrefixes =
                    tagService.GetMethod("Get", new[] { embyApi.GetType("Emby.Api.UserLibrary.GetArtistPrefixes") });
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
                    if (!IsPatched(_createSortName, typeof(PinyinSortName)))
                    {
                        HarmonyMod.Patch(_createSortName,
                            postfix: new HarmonyMethod(typeof(PinyinSortName).GetMethod("CreateSortNamePostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug("Patch CreateSortName Success by Harmony");
                    }
                    if (!IsPatched(_getPrefixes, typeof(PinyinSortName)))
                    {
                        HarmonyMod.Patch(_getPrefixes,
                            postfix: new HarmonyMethod(typeof(PinyinSortName).GetMethod("GetPrefixesPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug("Patch GetPrefixes Success by Harmony");
                    }
                    if (!IsPatched(_getArtistPrefixes, typeof(PinyinSortName)))
                    {
                        HarmonyMod.Patch(_getArtistPrefixes,
                            postfix: new HarmonyMethod(typeof(PinyinSortName).GetMethod("GetPrefixesPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug("Patch GetArtistPrefixes Success by Harmony");
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
                    if (IsPatched(_createSortName, typeof(PinyinSortName)))
                    {
                        HarmonyMod.Unpatch(_createSortName,
                            AccessTools.Method(typeof(PinyinSortName), "CreateSortNamePostfix"));
                        Plugin.Instance.Logger.Debug("Unpatch CreateSortName Success by Harmony");
                    }
                    if (IsPatched(_getPrefixes, typeof(PinyinSortName)))
                    {
                        HarmonyMod.Unpatch(_getPrefixes,
                            AccessTools.Method(typeof(PinyinSortName), "GetPrefixesPostfix"));
                        Plugin.Instance.Logger.Debug("Unpatch GetPrefixes Success by Harmony");
                    }
                    if (IsPatched(_getArtistPrefixes, typeof(PinyinSortName)))
                    {
                        HarmonyMod.Unpatch(_getArtistPrefixes,
                            AccessTools.Method(typeof(PinyinSortName), "GetPrefixesPostfix"));
                        Plugin.Instance.Logger.Debug("Unpatch GetArtistPrefixes Success by Harmony");
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

        [HarmonyPostfix]
        private static void CreateSortNamePostfix(BaseItem __instance, ref ReadOnlySpan<char> __result)
        {
            if (__instance.SupportsUserData && __instance.EnableAlphaNumericSorting && !(__instance is IHasSeries) &&
                (__instance is Video || __instance is Audio || __instance is IItemByName ||
                 __instance is Folder && !__instance.IsTopParent) && !__instance.IsFieldLocked(MetadataFields.SortName))
            {
                var result = new string(__result);

                if (IsJapanese(result) || !IsChinese(result)) return;

                var nameToProcess = __instance is BoxSet ? RemoveDefaultCollectionName(result) : result;

                __result = ConvertToPinyinInitials(nameToProcess).AsSpan();
            }
        }

        [HarmonyPostfix]
        private static void GetPrefixesPostfix(object request, ref object __result)
        {
            if (__result is NameValuePair[] pairs)
            {
                var validChars = new HashSet<char>("#ABCDEFGHIJKLMNOPQRSTUVWXYZ");

                var filteredPairs = pairs.Where(p => p.Name?.Length == 1 && validChars.Contains(p.Name[0])).ToArray();

                if (filteredPairs.Length != pairs.Length && filteredPairs.Any(p => p.Name[0] != '#'))
                {
                    __result = filteredPairs;
                }
            }
        }
    }
}
