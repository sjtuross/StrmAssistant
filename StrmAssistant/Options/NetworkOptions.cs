using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Validation;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Properties;
using System.ComponentModel;
using System.Runtime.InteropServices;
using static StrmAssistant.Common.CommonUtility;

namespace StrmAssistant
{
    public class NetworkOptions : EditableOptionsBase
    {
        [DisplayNameL("NetworkOptions_EditorTitle_Network", typeof(Resources))]
        public override string EditorTitle => Resources.NetworkOptions_EditorTitle_Network;

        [DisplayNameL("NetworkOptions_EnableProxyServer_Enable_Proxy_Server", typeof(Resources))]
        [DescriptionL("NetworkOptions_EnableProxyServer_Enable_Proxy_Server__Default_is_OFF_", typeof(Resources))]
        [EnabledCondition(nameof(IsModSupported), SimpleCondition.IsTrue)]
        public bool EnableProxyServer { get; set; } = false;

        [DisplayNameL("ModOptions_ProxyServer_Proxy_Server", typeof(Resources))]
        [DescriptionL("ModOptions_ProxyServer_Enable_http_proxy_server__Blank_is_OFF_", typeof(Resources))]
        [VisibleCondition(nameof(EnableProxyServer), SimpleCondition.IsTrue)]
        public string ProxyServerUrl { get; set; } = string.Empty;

        [VisibleCondition(nameof(ShowProxyServerStatus), SimpleCondition.IsTrue)]
        public StatusItem ProxyServerStatus { get; set; } = new StatusItem();

        [Browsable(false)]
        public bool ShowProxyServerStatus { get; set; } = false;

        [Browsable(false)]
        public bool IgnoreCertificateValidation { get; set; } = false;

        [Browsable(false)]
        public bool IsModSupported { get; } = RuntimeInformation.ProcessArchitecture == Architecture.X64;

        protected override void Validate(ValidationContext context)
        {
            if (!string.IsNullOrWhiteSpace(ProxyServerUrl) && !IsValidProxyUrl(ProxyServerUrl))
            {
                context.AddValidationError(nameof(NetworkOptions), Resources.InvalidProxyServer);
            }
        }

        public void Initialize()
        {
            ProxyServerStatus.StatusText = string.Empty;
            ShowProxyServerStatus = false;
        }
    }
}
