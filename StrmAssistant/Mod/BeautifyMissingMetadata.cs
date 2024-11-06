using HarmonyLib;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Dto;
using System;
using System.Linq;
using System.Reflection;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class BeautifyMissingMetadata
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();
        
        private static MethodInfo _getBaseItemDtos;
        private static MethodInfo _getBaseItemDto;

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

            var item = items.FirstOrDefault();

            if (!(item is Episode episode) || episode.GetPreferredMetadataLanguage() != "zh-CN") return;

            foreach (var (currentItem, index) in items.Select((currentItem, index) => (currentItem, index)))
            {
                if (string.Equals(currentItem.Name, currentItem.FileNameWithoutExtension, StringComparison.Ordinal))
                {
                    var matchItem = __result[index];
                    matchItem.Name = $"第 {currentItem.IndexNumber} 集";
                }
            }
        }

        [HarmonyPostfix]
        private static void GetBaseItemDtoPostfix(BaseItem item, DtoOptions options, User user, ref BaseItemDto __result)
        {
            if (item is Episode && item.GetPreferredMetadataLanguage() == "zh-CN" &&
                string.Equals(item.Name, item.FileNameWithoutExtension, StringComparison.Ordinal))
            {
                __result.Name = $"第 {item.IndexNumber} 集";
            }
        }
    }
}
