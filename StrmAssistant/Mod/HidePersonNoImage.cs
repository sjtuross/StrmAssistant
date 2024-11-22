using HarmonyLib;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using System;
using System.Linq;
using System.Reflection;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class HidePersonNoImage
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static MethodInfo _attachPeople;

        public static void Initialize()
        {
            try
            {
                var embyServerImplementationsAssembly = Assembly.Load("Emby.Server.Implementations");
                var dtoService =
                    embyServerImplementationsAssembly.GetType("Emby.Server.Implementations.Dto.DtoService");
                _attachPeople =
                    dtoService.GetMethod("AttachPeople", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            catch (Exception e)
            {
                Plugin.Instance.Logger.Warn("HidePersonNoImage - Patch Init Failed");
                Plugin.Instance.Logger.Debug(e.Message);
                Plugin.Instance.Logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.UIFunctionStore.GetOptions().HidePersonNoImage)
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
                    if (!IsPatched(_attachPeople, typeof(HidePersonNoImage)))
                    {
                        HarmonyMod.Patch(_attachPeople,
                            postfix: new HarmonyMethod(typeof(HidePersonNoImage).GetMethod("AttachPeoplePostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch ToBaseItemPerson Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Patch AttachPeople Failed by Harmony");
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
                    if (IsPatched(_attachPeople, typeof(HidePersonNoImage)))
                    {
                        HarmonyMod.Unpatch(_attachPeople,
                            AccessTools.Method(typeof(HidePersonNoImage), "AttachPeoplePostfix"));
                        Plugin.Instance.Logger.Debug("Unpatch AttachPeople Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Unpatch HidePersonNoImage Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                }
            }
        }

        [HarmonyPostfix]
        private static void AttachPeoplePostfix(BaseItemDto dto, BaseItem item, DtoOptions options)
        {
            if (dto.People == null) return;

            dto.People = dto.People.Where(p => p.HasPrimaryImage).ToArray();
        }
    }
}
