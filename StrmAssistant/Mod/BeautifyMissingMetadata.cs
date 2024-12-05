using Emby.Naming.Common;
using HarmonyLib;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Dto;
using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class BeautifyMissingMetadata
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();
        
        private static MethodInfo _getBaseItemDtos;
        private static MethodInfo _getBaseItemDto;

        private static MethodInfo _getMainExpression;
        private static readonly string SeasonNumberAndEpisodeNumberExpression =
            "(?<![a-z]|[0-9])(?<seasonnumber>[0-9]+)(?:[ ._x-]*e|x|[ ._-]*ep[._ -]*|[ ._-]*episode[._ -]+)";

        public static void Initialize()
        {
            try
            {
                var embyServerImplementationsAssembly = Assembly.Load("Emby.Server.Implementations");
                var dtoService =
                    embyServerImplementationsAssembly.GetType("Emby.Server.Implementations.Dto.DtoService");
                _getBaseItemDtos = dtoService.GetMethod("GetBaseItemDtos", BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { typeof(BaseItem[]), typeof(int), typeof(DtoOptions), typeof(User) }, null);
                _getBaseItemDto = dtoService.GetMethod("GetBaseItemDto", BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { typeof(BaseItem), typeof(DtoOptions), typeof(User) }, null);

                _getMainExpression =
                    typeof(NamingOptions).GetMethod("GetMainExpression", BindingFlags.NonPublic | BindingFlags.Static);
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("BeautifyMissingMetadata - Patch Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.GetPluginOptions().UIFunctionOptions.BeautifyMissingMetadata)
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
                    if (!IsPatched(_getBaseItemDtos, typeof(BeautifyMissingMetadata)))
                    {
                        HarmonyMod.Patch(_getBaseItemDtos,
                            postfix: new HarmonyMethod(typeof(BeautifyMissingMetadata).GetMethod("GetBaseItemDtosPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch GetBaseItemDtos Success by Harmony");
                    }
                    if (!IsPatched(_getBaseItemDto, typeof(BeautifyMissingMetadata)))
                    {
                        HarmonyMod.Patch(_getBaseItemDto,
                            postfix: new HarmonyMethod(typeof(BeautifyMissingMetadata).GetMethod("GetBaseItemDtoPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch GetBaseItemDto Success by Harmony");
                    }
                    if (!IsPatched(_getMainExpression, typeof(BeautifyMissingMetadata)))
                    {
                        HarmonyMod.Patch(_getMainExpression,
                            postfix: new HarmonyMethod(typeof(BeautifyMissingMetadata).GetMethod("GetMainExpressionPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug("Patch GetMainExpression Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch BeautifyMissingMetadata Failed by Harmony");
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
                    if (IsPatched(_getBaseItemDtos, typeof(BeautifyMissingMetadata)))
                    {
                        HarmonyMod.Unpatch(_getBaseItemDtos,
                            AccessTools.Method(typeof(BeautifyMissingMetadata), "GetBaseItemDtosPostfix"));
                        Plugin.Instance.logger.Debug("Unpatch GetBaseItemDtos Success by Harmony");
                    }
                    if (IsPatched(_getBaseItemDto, typeof(BeautifyMissingMetadata)))
                    {
                        HarmonyMod.Unpatch(_getBaseItemDto,
                            AccessTools.Method(typeof(BeautifyMissingMetadata), "GetBaseItemDtoPostfix"));
                        Plugin.Instance.logger.Debug("Unpatch GetBaseItemDto Success by Harmony");
                    }
                    if (IsPatched(_getMainExpression, typeof(BeautifyMissingMetadata)))
                    {
                        HarmonyMod.Unpatch(_getMainExpression,
                            AccessTools.Method(typeof(BeautifyMissingMetadata), "GetMainExpressionPostfix"));
                        Plugin.Instance.logger.Debug("Unpatch GetMainExpression Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch BeautifyMissingMetadata Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        [HarmonyPostfix]
        private static void GetBaseItemDtosPostfix(BaseItem[] items, int itemCount, DtoOptions options, User user,
            ref BaseItemDto[] __result)
        {
            if (itemCount == 0) return;

            var checkItem = items.FirstOrDefault();

            if (!(checkItem is Episode episode) || !episode.GetPreferredMetadataLanguage()
                    .Equals("zh-CN", StringComparison.OrdinalIgnoreCase)) return;

            var episodes = !string.IsNullOrEmpty(checkItem.FileNameWithoutExtension)
                ? items
                : Plugin.LibraryApi.GetItemsByIds(items.Select(i => i.InternalId).ToArray());

            foreach (var (currentItem, index) in episodes.Select((currentItem, index) => (currentItem, index)))
            {
                if (currentItem.IndexNumber.HasValue && string.Equals(currentItem.Name,
                        currentItem.FileNameWithoutExtension, StringComparison.Ordinal))
                {
                    var matchItem = __result[index];
                    matchItem.Name = $"第 {currentItem.IndexNumber} 集";
                }
            }
        }

        [HarmonyPostfix]
        private static void GetBaseItemDtoPostfix(BaseItem item, DtoOptions options, User user,
            ref BaseItemDto __result)
        {
            if (item is Episode && item.IndexNumber.HasValue &&
                item.GetPreferredMetadataLanguage().Equals("zh-CN", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Name, item.FileNameWithoutExtension, StringComparison.Ordinal))
            {
                __result.Name = $"第 {item.IndexNumber} 集";
            }
        }

        [HarmonyPostfix]
        private static void GetMainExpressionPostfix(ref string __result, bool allowEpisodeNumberOnly,
            bool allowMultiEpisodeNumberOnlyExpression, bool allowX)
        {
            if (allowEpisodeNumberOnly && !allowMultiEpisodeNumberOnlyExpression && allowX)
            {
                __result = Regex.Replace(__result, Regex.Escape(SeasonNumberAndEpisodeNumberExpression) + @"\|?", "");
            }
        }
    }
}
