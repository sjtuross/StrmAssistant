using Emby.Web.GenericEdit;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Properties;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace StrmAssistant
{
    public class UIFunctionOptions: EditableOptionsBase
    {
        [DisplayNameL("UIFunctionOptions_EditorTitle_UI_Functions", typeof(Resources))]
        public override string EditorTitle => Resources.UIFunctionOptions_EditorTitle_UI_Functions;

        [DisplayNameL("ModOptions_HidePersonNoImage_Hide_Person_without_Image", typeof(Resources))]
        [DescriptionL("ModOptions_HidePersonNoImage_Hide_person_without_image__Default_is_OFF_", typeof(Resources))]
        [Required]
        [EnabledCondition(nameof(IsModSupported), SimpleCondition.IsTrue)]
        public bool HidePersonNoImage { get; set; } = false;

        [DisplayNameL("UIFunctionOptions_BeautifyMissingMetadata_Beautify_Missing_Metadata", typeof(Resources))]
        [DescriptionL("UIFunctionOptions_BeautifyMissingMetadata_Beautify_missing_metadata_for_episode_display__Default_is_OFF_", typeof(Resources))]
        [Required]
        [EnabledCondition(nameof(IsModSupported), SimpleCondition.IsTrue)]
        public bool BeautifyMissingMetadata { get; set; } = false;
        
        [DisplayNameL("UIFunctionOptions_EnhanceMissingEpisodes_Missing_Episodes_Enhanced", typeof(Resources))]
        [DescriptionL("UIFunctionOptions_EnhanceMissingEpisodes_Add_MovieDb_support_for_the_missing_episodes_function__Default_is_OFF_", typeof(Resources))]
        [Required]
        [EnabledCondition(nameof(IsModSupported), SimpleCondition.IsTrue)]
        public bool EnhanceMissingEpisodes { get; set; } = false;

        [DisplayNameL("UIFunctionOptions_EnforceLibraryOrder_Enforce_Library_Order", typeof(Resources))]
        [DescriptionL("UIFunctionOptions_EnforceLibraryOrder_Enforce_library_order_per_the_first_admin__Default_if_OFF_", typeof(Resources))]
        [Required]
        [EnabledCondition(nameof(IsModSupported), SimpleCondition.IsTrue)]
        public bool EnforceLibraryOrder { get; set; } = false;

        [Browsable(false)]
        public bool IsModSupported { get; } = RuntimeInformation.ProcessArchitecture == Architecture.X64;
    }
}
