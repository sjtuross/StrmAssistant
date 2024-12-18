using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Properties;
using System;
using System.ComponentModel;
using System.Linq;

namespace StrmAssistant.Options
{
    public class PluginOptions : EditableOptionsBase
    {
        public override string EditorTitle => Resources.PluginOptions_EditorTitle_Strm_Assistant;

        public override string EditorDescription => string.Empty;

        [VisibleCondition(nameof(IsConflictPluginLoaded), SimpleCondition.IsTrue)]
        public StatusItem ConflictPluginLoadedStatus { get; set; } = new StatusItem();

        [DisplayNameL("GeneralOptions_EditorTitle_General_Options", typeof(Resources))]
        public GeneralOptions GeneralOptions { get; set; } = new GeneralOptions();

        [DisplayNameL("PluginOptions_ModOptions_Mod_Features", typeof(Resources))]
        public ModOptions ModOptions { get; set; } = new ModOptions();

        [DisplayNameL("NetworkOptions_EditorTitle_Network", typeof(Resources))]
        public NetworkOptions NetworkOptions { get; set; } = new NetworkOptions();

        [DisplayNameL("AboutOptions_EditorTitle_About", typeof(Resources))]
        public AboutOptions AboutOptions { get; set; } = new AboutOptions();

        [Browsable(false)]
        public bool IsConflictPluginLoaded { get; } = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name)
            .Any(n => n == "StrmExtract" || n == "InfuseSync");

        public void Initialize()
        {
            if (IsConflictPluginLoaded)
            {
                ConflictPluginLoadedStatus.Caption = Resources
                    .PluginOptions_IncompatibleMessage_Please_uninstall_the_conflict_plugin_Strm_Extract;
                ConflictPluginLoadedStatus.StatusText = string.Empty;
                ConflictPluginLoadedStatus.Status = ItemStatus.Warning;
            }
            else
            {
                ConflictPluginLoadedStatus.Caption=string.Empty;
                ConflictPluginLoadedStatus.StatusText = string.Empty;
                ConflictPluginLoadedStatus.Status = ItemStatus.None;
            }
        }
    }
}
