using Emby.Web.GenericEdit;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using System;
using System.ComponentModel;
using System.Linq;
using StrmAssistant.Properties;

namespace StrmAssistant.Options
{
    public class PluginOptions : EditableOptionsBase
    {
        public override string EditorTitle => Resources.PluginOptions_EditorTitle_Strm_Assistant;

        public override string EditorDescription =>
            IsConflictPluginLoaded
                ? Resources.PluginOptions_IncompatibleMessage_Please_uninstall_the_conflict_plugin_Strm_Extract
                : string.Empty;

        [DisplayNameL("GeneralOptions_EditorTitle_General_Options", typeof(Resources))]
        [VisibleCondition(nameof(IsConflictPluginLoaded), SimpleCondition.IsFalse)]
        [EnabledCondition(nameof(IsConflictPluginLoaded), SimpleCondition.IsFalse)]
        public GeneralOptions GeneralOptions { get; set; } = new GeneralOptions();

        [DisplayNameL("PluginOptions_EditorTitle_Strm_Extract", typeof(Resources))]
        [VisibleCondition(nameof(IsConflictPluginLoaded), SimpleCondition.IsFalse)]
        [EnabledCondition(nameof(IsConflictPluginLoaded), SimpleCondition.IsFalse)]
        public MediaInfoExtractOptions MediaInfoExtractOptions { get; set; } = new MediaInfoExtractOptions();

        [DisplayNameL("PluginOptions_ModOptions_Mod_Features", typeof(Resources))]
        [VisibleCondition(nameof(IsConflictPluginLoaded), SimpleCondition.IsFalse)]
        [EnabledCondition(nameof(IsConflictPluginLoaded), SimpleCondition.IsFalse)]
        public ModOptions ModOptions { get; set; } = new ModOptions();

        [DisplayNameL("AboutOptions_EditorTitle_About", typeof(Resources))]
        public AboutOptions AboutOptions { get; set; } = new AboutOptions();

        [Browsable(false)]
        public bool IsConflictPluginLoaded { get; } =
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "StrmExtract");
    }
}
