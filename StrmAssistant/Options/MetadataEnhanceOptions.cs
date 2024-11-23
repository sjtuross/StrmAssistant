using Emby.Web.GenericEdit;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Properties;
using System.ComponentModel;

namespace StrmAssistant
{
    public enum RefreshPersonMode
    {
        Default,
        FullRefresh
    }

    public class MetadataEnhanceOptions: EditableOptionsBase
    {
        [DisplayNameL("PluginOptions_MetadataEnhanceOptions_Metadata_Enhance", typeof(Resources))]
        public override string EditorTitle => Resources.PluginOptions_MetadataEnhanceOptions_Metadata_Enhance;
        
        [Browsable(false)]
        [Required]
        public RefreshPersonMode RefreshPersonMode { get; set; } = RefreshPersonMode.Default;
    }
}
