using Emby.Web.GenericEdit.Elements;
using HarmonyLib;
using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using static StrmAssistant.CommonUtility;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class EnableProxyServer
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static MethodInfo _createHttpClientHandler;

        public static void Initialize()
        {
            try
            {
                var embyServerImplementationsAssembly = Assembly.Load("Emby.Server.Implementations");
                var applicationHost =
                    embyServerImplementationsAssembly.GetType("Emby.Server.Implementations.ApplicationHost");
                _createHttpClientHandler=applicationHost.GetMethod("CreateHttpClientHandler", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("EnableProxyServer - Patch Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.GetPluginOptions().NetworkOptions.EnableProxyServer)
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
                    if (!IsPatched(_createHttpClientHandler, typeof(EnableProxyServer)))
                    {
                        HarmonyMod.Patch(_createHttpClientHandler,
                            postfix: new HarmonyMethod(typeof(EnableProxyServer).GetMethod("CreateHttpClientHandlerPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug(
                            "Patch CreateHttpClientHandler Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch EnableProxyServer Failed by Harmony");
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
                    if (IsPatched(_createHttpClientHandler, typeof(EnableProxyServer)))
                    {
                        HarmonyMod.Unpatch(_createHttpClientHandler,
                            AccessTools.Method(typeof(EnableProxyServer), "CreateHttpClientHandlerPostfix"));
                        Plugin.Instance.logger.Debug("Unpatch CreateHttpClientHandler Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch EnableProxyServer Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        [HarmonyPostfix]
        private static void CreateHttpClientHandlerPostfix(ref HttpClientHandler __result)
        {
            var proxyServer = Plugin.Instance.GetPluginOptions().NetworkOptions.ProxyServerUrl;
            var proxyStatus = Plugin.Instance.GetPluginOptions().NetworkOptions.ProxyServerStatus.Status;
            var ignoreCertificateValidation =
                Plugin.Instance.GetPluginOptions().NetworkOptions.IgnoreCertificateValidation;

            if (IsValidProxyUrl(proxyServer) && proxyStatus == ItemStatus.Succeeded)
            {
                __result.Proxy = new WebProxy(proxyServer);
                __result.UseProxy = true;

                if (ignoreCertificateValidation)
                {
                    __result.ServerCertificateCustomValidationCallback =
                        (httpRequestMessage, cert, chain, sslErrors) => true;
                }
            }
        }
    }
}
