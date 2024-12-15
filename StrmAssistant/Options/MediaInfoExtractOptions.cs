using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using MediaBrowser.Model.MediaInfo;
using StrmAssistant.Properties;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace StrmAssistant
{
    public class MediaInfoExtractOptions: EditableOptionsBase
    {
        [DisplayNameL("PluginOptions_EditorTitle_Strm_Extract", typeof(Resources))]
        public override string EditorTitle => Resources.PluginOptions_EditorTitle_Strm_Extract;

        [DisplayNameL("PluginOptions_IncludeExtra_Include_Extra", typeof(Resources))]
        [DescriptionL("PluginOptions_IncludeExtra_Include_media_extras_to_extract__Default_is_False_", typeof(Resources))]
        [Required]
        public bool IncludeExtra { get; set; } = false;

        [DisplayNameL("PluginOptions_EnableImageCapture_Enable_Image_Capture", typeof(Resources))]
        [DescriptionL("PluginOptions_EnableImageCapture_Perform_image_capture_for_videos_without_primary_image__Default_is_False_", typeof(Resources))]
        [Required]
        [EnabledCondition(nameof(IsModSupported), SimpleCondition.IsTrue)]
        public bool EnableImageCapture { get; set; } = false;

        [Browsable(false)]
        [Required]
        public string ImageCaptureExcludeMediaContainers { get; set; } =
            string.Join(",", new[] { MediaContainers.MpegTs, MediaContainers.Ts, MediaContainers.M2Ts });

        [DisplayNameL("ModOptions_ExclusiveExtract_Exclusive_Extract", typeof(Resources))]
        [DescriptionL("ModOptions_ExclusiveExtract_Only_allow_this_plugin_to_extract_media_info__ffprobe__and_capture_image__ffmpeg___Default_is_OFF_", typeof(Resources))]
        [Required]
        [EnabledCondition(nameof(IsModSupported), SimpleCondition.IsTrue)]
        public bool ExclusiveExtract { get; set; } = false;

        public enum ExclusiveControl
        {
            [DescriptionL("ExclusiveControl_IgnoreFileChange_IgnoreFileChange", typeof(Resources))]
            IgnoreFileChange,
            [DescriptionL("ExclusiveControl_CatchAllAllow_CatchAllAllow", typeof(Resources))]
            CatchAllAllow,
            [DescriptionL("ExclusiveControl_CatchAllBlock_CatchAllBlock", typeof(Resources))]
            CatchAllBlock
        }

        [Browsable(false)]
        public IEnumerable<EditorSelectOption> ExclusiveControlList { get; set; }

        [DisplayNameL("MediaInfoExtractOptions_ExclusiveExtractFeatureControl_Exclusive_Extract_Feature_Control", typeof(Resources))]
        [EditMultilSelect]
        [SelectItemsSource(nameof(ExclusiveControlList))]
        [VisibleCondition(nameof(ExclusiveExtract), SimpleCondition.IsTrue)]
        public string ExclusiveControlFeatures { get; set; } = string.Empty;

        [DisplayNameL("MediaInfoExtractOptions_PersistMediaInfo_Persist_MediaInfo", typeof(Resources))]
        [DescriptionL("MediaInfoExtractOptions_PersistMediaInfo_Persist_media_info_in_JSON_file__Default_is_OFF_", typeof(Resources))]
        [Required]
        public bool PersistMediaInfo { get; set; } = false;

        [DisplayNameL("MediaInfoExtractOptions_MediaInfoJsonRootFolder_MediaInfo_Json_Root_Folder", typeof(Resources))]
        [DescriptionL("MediaInfoExtractOptions_MediaInfoJsonRootFolder_Store_or_load_media_info_JSON_files_under_this_root_folder__Default_is_EMPTY_", typeof(Resources))]
        [EditFolderPicker]
        [VisibleCondition(nameof(PersistMediaInfo), SimpleCondition.IsTrue)]
        public string MediaInfoJsonRootFolder { get; set; } = string.Empty;

        [Browsable(false)]
        public IEnumerable<EditorSelectOption> LibraryList { get; set; }

        [DisplayNameL("PluginOptions_LibraryScope_Library_Scope", typeof(Resources))]
        [DescriptionL("PluginOptions_LibraryScope_Library_scope_to_extract__Blank_includes_all_", typeof(Resources))]
        [EditMultilSelect]
        [SelectItemsSource(nameof(LibraryList))]
        public string LibraryScope { get; set; } = string.Empty;

        [Browsable(false)]
        public bool IsModSupported { get; } = RuntimeInformation.ProcessArchitecture == Architecture.X64;
    }
}
