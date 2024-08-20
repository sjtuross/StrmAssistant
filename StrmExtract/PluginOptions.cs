using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using Emby.Web.GenericEdit.Validation;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmExtract.Properties;
using System.Collections.Generic;
using System.ComponentModel;

namespace StrmExtract
{
    public class PluginOptions: EditableOptionsBase
    {
        [DisplayNameL("PluginOptions_EditorTitle_Strm_Extract", typeof(Resources))]
        public override string EditorTitle => Resources.PluginOptions_EditorTitle_Strm_Extract;

        //public override string EditorDescription => "MediaInfo Extract Description";

        [DisplayNameL("PluginOptions_MaxConcurrentCount_Max_Concurrent_Count", typeof(Resources))]
        [DescriptionL("PluginOptions_MaxConcurrentCount_Max_Concurrent_Count_must_be_between_1_to_10__Default_is_1_", typeof(Resources))]
        [MediaBrowser.Model.Attributes.Required]
        public int MaxConcurrentCount { get; set; } = 1;

        protected override void Validate(ValidationContext context)
        {
            if (MaxConcurrentCount<1 || MaxConcurrentCount>10)
            {
                context.AddValidationError(nameof(MaxConcurrentCount), "Max Concurrent Count must be between 1 to 10.");
            }
        }

        [DisplayNameL("PluginOptions_StrmOnly_Strm_Only", typeof(Resources))]
        [DescriptionL("PluginOptions_StrmOnly_Extract_media_info_of_Strm_only__Default_is_True_", typeof(Resources))]
        [MediaBrowser.Model.Attributes.Required]
        public bool StrmOnly { get; set; } = true;

        [DisplayNameL("PluginOptions_IncludeExtra_Include_Extra", typeof(Resources))]
        [DescriptionL("PluginOptions_IncludeExtra_Include_media_extras_to_extract__Default_is_False_", typeof(Resources))]
        [MediaBrowser.Model.Attributes.Required]
        public bool IncludeExtra { get; set; } = false;

        [DisplayNameL("PluginOptions_EnableImageCapture_Enable_Image_Capture", typeof(Resources))]
        [DescriptionL("PluginOptions_EnableImageCapture_Perform_image_capture_for_videos_without_primary_image__Default_is_False_", typeof(Resources))]
        [MediaBrowser.Model.Attributes.Required]
        public bool EnableImageCapture { get; set; } = false;

        [DisplayNameL("PluginOptions_CatchupMode_Catch_up_Mode__Experimental_", typeof(Resources))]
        [DescriptionL("PluginOptions_CatchupMode_Catch_up_users_favorites__exclusive_to_Strm___Default_is_False_", typeof(Resources))]
        [MediaBrowser.Model.Attributes.Required]
        public bool CatchupMode { get; set; } = false;

        [Browsable(false)]
        public IEnumerable<EditorSelectOption> LibraryList { get; set; }

        [DisplayNameL("PluginOptions_LibraryScope_Library_Scope", typeof(Resources))]
        [DescriptionL("PluginOptions_LibraryScope_Library_scope_to_extract__Blank_includes_all_", typeof(Resources))]
        [EditMultilSelect]
        [SelectItemsSource(nameof(LibraryList))]
        public string LibraryScope { get; set; }
    }
}
