using Emby.Web.GenericEdit.Elements;
using HarmonyLib;
using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using static StrmAssistant.Common.CommonUtility;
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
                Plugin.Instance.Logger.Warn("EnableProxyServer - Patch Init Failed");
                Plugin.Instance.Logger.Debug(e.Message);
                Plugin.Instance.Logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.MainOptionsStore.GetOptions().NetworkOptions.EnableProxyServer)
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
                        Plugin.Instance.Logger.Debug(
                            "Patch CreateHttpClientHandler Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Patch EnableProxyServer Failed by Harmony");
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
                    if (IsPatched(_createHttpClientHandler, typeof(EnableProxyServer)))
                    {
                        HarmonyMod.Unpatch(_createHttpClientHandler,
                            AccessTools.Method(typeof(EnableProxyServer), "CreateHttpClientHandlerPostfix"));
                        Plugin.Instance.Logger.Debug("Unpatch CreateHttpClientHandler Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.Logger.Debug("Unpatch EnableProxyServer Failed by Harmony");
                    Plugin.Instance.Logger.Debug(he.Message);
                    Plugin.Instance.Logger.Debug(he.StackTrace);
                }
            }
        }

        [HarmonyPostfix]
        private static void CreateHttpClientHandlerPostfix(ref HttpClientHandler __result)
        {
            var options = Plugin.Instance.MainOptionsStore.PluginOptions.NetworkOptions;
            var proxyStatus = options.ProxyServerStatus.Status;
            var ignoreCertificateValidation = options.IgnoreCertificateValidation;

            if (Uri.TryCreate(options.ProxyServerUrl, UriKind.Absolute, out var proxyUri) &&
                proxyStatus == ItemStatus.Succeeded && TryParseProxyUrl(options.ProxyServerUrl, out var schema,
                    out var host, out var port, out var username, out var password))
            {
                __result.Proxy = new WebProxy(proxyUri)
                {
                    BypassProxyOnLocal = true,
                    Credentials = !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password)
                        ? new NetworkCredential(username, password)
                        : null
                };

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
