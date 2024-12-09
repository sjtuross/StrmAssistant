using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Validation;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Properties;
using System;
using System.ComponentModel;
using System.Linq;
using static StrmAssistant.CommonUtility;

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
        public MetadataEnhanceOptions MetadataEnhanceOptions { get; set; } = new MetadataEnhanceOptions();

        [DisplayNameL("PluginOptions_IntroSkipOptions_Intro_Credits_Detection", typeof(Resources))]
        [VisibleCondition(nameof(IsConflictPluginLoaded),SimpleCondition.IsFalse)]
        [EnabledCondition(nameof(IsConflictPluginLoaded),SimpleCondition.IsFalse)]
        public IntroSkipOptions IntroSkipOptions { get; set; } = new IntroSkipOptions();

        [DisplayNameL("UIFunctionOptions_EditorTitle_UI_Functions", typeof(Resources))]
        [VisibleCondition(nameof(IsConflictPluginLoaded),SimpleCondition.IsFalse)]
        [EnabledCondition(nameof(IsConflictPluginLoaded),SimpleCondition.IsFalse)]
        public UIFunctionOptions UIFunctionOptions { get; set; } = new UIFunctionOptions();

        [DisplayNameL("PluginOptions_ModOptions_Mod_Features", typeof(Resources))]
        [VisibleCondition(nameof(IsConflictPluginLoaded), SimpleCondition.IsFalse)]
        [EnabledCondition(nameof(IsConflictPluginLoaded), SimpleCondition.IsFalse)]
        public ModOptions ModOptions { get; set; } = new ModOptions();

        [DisplayNameL("AboutOptions_EditorTitle_About", typeof(Resources))]
        public AboutOptions AboutOptions { get; set; } = new AboutOptions();

        [Browsable(false)]
        public bool IsConflictPluginLoaded { get; } =
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "StrmExtract");

        protected override void Validate(ValidationContext context)
        {
            string metadataOptionsErrors = null;

            foreach (var (value, isValid, errorResource) in new (string, Func<string, bool>, string)[]
                     {
                         (MetadataEnhanceOptions.AltMovieDbApiUrl, IsValidHttpUrl,
                             Resources.InvalidAltMovieDbApiUrl),
                         (MetadataEnhanceOptions.AltMovieDbImageUrl, IsValidHttpUrl,
                             Resources.InvalidAltMovieDbImageUrl),
                         (MetadataEnhanceOptions.AltMovieDbApiKey, IsValidMovieDbApiKey,
                             Resources.InvalidAltMovieDbApiKey)
                     })
            {
                if (!string.IsNullOrWhiteSpace(value) && !isValid(value))
                {
                    metadataOptionsErrors = metadataOptionsErrors == null ? errorResource : $"{metadataOptionsErrors}; {errorResource}";
                }
            }

            if (!string.IsNullOrEmpty(metadataOptionsErrors))
            {
                context.AddValidationError(nameof(MetadataEnhanceOptions), metadataOptionsErrors);
            }

            if (!string.IsNullOrWhiteSpace(ModOptions.ProxyServerUrl) && !IsValidProxyUrl(ModOptions.ProxyServerUrl))
            {
                context.AddValidationError(nameof(ModOptions), Resources.InvalidProxyServer);
            }
        }
    }
}
