using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class ChapterChangeTracker
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static MethodInfo _saveChapters;
        private static MethodInfo _deleteChapters;

        private static readonly AsyncLocal<long> BypassItem = new AsyncLocal<long>();

        public static void Initialize()
        {
            try
            {
                var embyServerImplementationsAssembly = Assembly.Load("Emby.Server.Implementations");
                var sqliteItemRepository =
                    embyServerImplementationsAssembly.GetType("Emby.Server.Implementations.Data.SqliteItemRepository");
                _saveChapters = sqliteItemRepository.GetMethod("SaveChapters",
                    BindingFlags.Instance | BindingFlags.Public, null,
                    new[] { typeof(long), typeof(bool), typeof(List<ChapterInfo>) }, null);
                _deleteChapters =
                    sqliteItemRepository.GetMethod("DeleteChapters", BindingFlags.Instance | BindingFlags.Public);
            }
            catch (Exception e)
            {
                Plugin.Instance.Logger.Warn("ChapterChangeTracker - Patch Init Failed");
                Plugin.Instance.Logger.Debug(e.Message);
                Plugin.Instance.Logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.MediaInfoExtractStore.GetOptions().PersistMediaInfo)
            {
                Patch();
            }
        }

        public static void Patch()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony &&
                Plugin.Instance.MediaInfoExtractStore.GetOptions().IsModSupported)
            {
                try
                {
                    if (!IsPatched(_saveChapters, typeof(ChapterChangeTracker)))
                    {
                        HarmonyMod.Patch(_saveChapters,
                            postfix: new HarmonyMethod(typeof(ChapterChangeTracker).GetMethod("SaveChaptersPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch SaveChapters Success by Harmony");
                    }

                    if (!IsPatched(_deleteChapters, typeof(ChapterChangeTracker)))
                    {
                        HarmonyMod.Patch(_deleteChapters,
                            postfix: new HarmonyMethod(typeof(ChapterChangeTracker).GetMethod("DeleteChaptersPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.Logger.Debug(
                            "Patch DeleteChapters Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Patch ChapterChangeTracker Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                    PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;
                }
            }
        }

        public static void Unpatch()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony &&
                Plugin.Instance.MediaInfoExtractStore.GetOptions().IsModSupported)
            {
                try
                {
                    if (IsPatched(_saveChapters, typeof(ChapterChangeTracker)))
                    {
                        HarmonyMod.Unpatch(_saveChapters,
                            AccessTools.Method(typeof(ChapterChangeTracker), "SaveChaptersPostfix"));
                        Plugin.Instance.Logger.Debug("Unpatch SaveChapters Success by Harmony");
                    }

                    if (IsPatched(_deleteChapters, typeof(ChapterChangeTracker)))
                    {
                        HarmonyMod.Unpatch(_deleteChapters,
                            AccessTools.Method(typeof(ChapterChangeTracker), "DeleteChaptersPostfix"));
                        Plugin.Instance.Logger.Debug("Unpatch DeleteChapters Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Unpatch ChapterChangeTracker Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                }
            }
        }

        public static void BypassInstance(BaseItem item)
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony &&
                Plugin.Instance.MediaInfoExtractStore.GetOptions().IsModSupported)
            {
                BypassItem.Value = item.InternalId;
            }
        }

        [HarmonyPostfix]
        private static void SaveChaptersPostfix(long itemId, bool clearExtractionFailureResult,
            List<ChapterInfo> chapters)
        {
            if (chapters.Count == 0) return;

            if (BypassItem.Value != 0 && BypassItem.Value == itemId) return;

            Task.Run(() => Plugin.LibraryApi.SerializeMediaInfo(itemId, true, "Save Chapters", CancellationToken.None));
        }

        [HarmonyPostfix]
        private static void DeleteChaptersPostfix(long itemId, MarkerType[] markerTypes)
        {
            if (BypassItem.Value != 0 && BypassItem.Value == itemId) return;

            Task.Run(() => Plugin.LibraryApi.SerializeMediaInfo(itemId, true, "Delete Chapters", CancellationToken.None));
        }
    }
}
