using Emby.Web.GenericEdit;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Properties;

namespace StrmAssistant
{
    public class ModOptions: EditableOptionsBase
    {
        [DisplayNameL("PluginOptions_ModOptions_Mod_Features", typeof(Resources))]
        public override string EditorTitle => Resources.PluginOptions_ModOptions_Mod_Features;

        [DisplayNameL("GeneralOptions_MergeMultiVersion_Merge_Multiple_Versions", typeof(Resources))]
        [DescriptionL("GeneralOptions_MergeMultiVersion_Auto_merge_multiple_versions_if_in_the_same_folder_", typeof(Resources))]
        [Required]
        public bool MergeMultiVersion { get; set; } = false;
    }
}
