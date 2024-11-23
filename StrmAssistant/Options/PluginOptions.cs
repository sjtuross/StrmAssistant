using Emby.Web.GenericEdit;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Properties;
using System;
using System.ComponentModel;
using System.Linq;

namespace StrmAssistant
{
    public class PluginOptions: EditableOptionsBase
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
        
        [DisplayNameL("PluginOptions_MetadataEnhanceOptions_Metadata_Enhance", typeof(Resources))]
        [VisibleCondition(nameof(IsConflictPluginLoaded), SimpleCondition.IsFalse)]
        [EnabledCondition(nameof(IsConflictPluginLoaded), SimpleCondition.IsFalse)]
        [Browsable(false)]
        public MetadataEnhanceOptions MetadataEnhanceOptions { get; set; } = new MetadataEnhanceOptions();

        [DisplayNameL("PluginOptions_IntroSkipOptions_Intro_Credits_Detection", typeof(Resources))]
        [VisibleCondition(nameof(IsConflictPluginLoaded),SimpleCondition.IsFalse)]
        [EnabledCondition(nameof(IsConflictPluginLoaded),SimpleCondition.IsFalse)]
        public IntroSkipOptions IntroSkipOptions { get; set; } = new IntroSkipOptions();

        [DisplayNameL("AboutOptions_EditorTitle_About", typeof(Resources))]
        public AboutOptions AboutOptions { get; set; } = new AboutOptions();

        [Browsable(false)]
        public bool IsConflictPluginLoaded { get; } =
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "StrmExtract");
    }
}
