using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using Emby.Web.GenericEdit.Validation;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using static StrmAssistant.Common.CommonUtility;

namespace StrmAssistant.Options
{
    public enum RefreshPersonMode
    {
        Default,
        FullRefresh
    }

    public class MetadataEnhanceOptions : EditableOptionsBase
    {
        [DisplayNameL("PluginOptions_MetadataEnhanceOptions_Metadata_Enhance", typeof(Resources))]
        public override string EditorTitle => Resources.PluginOptions_MetadataEnhanceOptions_Metadata_Enhance;

        [DisplayNameL("ModOptions_ChineseMovieDb_Chinese_MovieDb", typeof(Resources))]
        [DescriptionL("ModOptions_ChineseMovieDb_Optimize_MovieDb_for_Chinese_metadata__Default_is_OFF_", typeof(Resources))]
        [EnabledCondition(nameof(IsMovieDbPluginLoaded), SimpleCondition.IsTrue)]
        [Required]
        public bool ChineseMovieDb { get; set; } = false;

        [Browsable(false)]
        public List<EditorSelectOption> LanguageList { get; set; } = new List<EditorSelectOption>
        {
            new EditorSelectOption
            {
                Value = "zh-cn",
                Name = "zh-CN",
                IsEnabled = true
            },
            new EditorSelectOption
            {
                Value = "zh-sg",
                Name = "zh-SG",
                IsEnabled = true
            },
            new EditorSelectOption
            {
                Value = "zh-hk",
                Name = "zh-HK",
                IsEnabled = true
            },
            new EditorSelectOption
            {
                Value = "zh-tw",
                Name = "zh-TW",
                IsEnabled = true
            },
            new EditorSelectOption
            {
                Value = "ja-jp",
                Name = "ja-JP",
                IsEnabled = true
            }
        };

        [DisplayNameL("ModOptions_FallbackLanguages_Fallback_Languages", typeof(Resources))]
        [DescriptionL("ModOptions_FallbackLanguages_Fallback_languages__Default_is_zh_SG_", typeof(Resources))]
        [EditMultilSelect]
        [SelectItemsSource(nameof(LanguageList))]
        [VisibleCondition(nameof(ChineseMovieDb), SimpleCondition.IsTrue)]
        public string FallbackLanguages { get; set; }

        [DisplayNameL("MetadataEnhanceOptions_MovieDbEpisodeGroup_Support_MovieDb_Episode_Group", typeof(Resources))]
        [DescriptionL("MetadataEnhanceOptions_MovieDbEpisodeGroup_Support_MovieDb_episode_group_scrapping_for_TV_shows__Default_is_OFF_", typeof(Resources))]
        [EnabledCondition(nameof(IsMovieDbPluginLoaded), SimpleCondition.IsTrue)]
        [Required]
        public bool MovieDbEpisodeGroup { get; set; }

        [DisplayNameL("MetadataEnhanceOptions_EnhanceMovieDbPerson_Enhance_MovieDb_Person", typeof(Resources))]
        [DescriptionL("MetadataEnhanceOptions_EnhanceMovieDbPerson_Import_season_cast_and_update_series_people__Default_is_OFF_", typeof(Resources))]
        [EnabledCondition(nameof(IsMovieDbPluginLoaded), SimpleCondition.IsTrue)]
        [Required]
        public bool EnhanceMovieDbPerson { get; set; } = false;
        
        [Browsable(false)]
        [Required]
        public RefreshPersonMode RefreshPersonMode { get; set; } = RefreshPersonMode.Default;

        [DisplayNameL("ModOptions_OriginalPoster_Original_Poster", typeof(Resources))]
        [DescriptionL("ModOptions_OriginalPoster_Show_original_poster_based_on_original_language__Default_is_OFF_", typeof(Resources))]
        [Required]
        [EnabledCondition(nameof(IsModSupported), SimpleCondition.IsTrue)]
        public bool PreferOriginalPoster { get; set; } = false;

        [DisplayNameL("ModOptions_PinyinSortName_Pinyin_Sort_Title", typeof(Resources))]
        [DescriptionL("ModOptions_PinyinSortName_Auto_generate_pinyin_initials_as_sort_title", typeof(Resources))]
        [EnabledCondition(nameof(IsModSupported), SimpleCondition.IsTrue)]
        public bool PinyinSortName { get; set; } = false;

        [DisplayNameL("MetadataEnhanceOptions_EnhanceNfoMetadata_Nfo_Metadata_Import_Enhanced", typeof(Resources))]
        [DescriptionL("MetadataEnhanceOptions_EnhanceNfoMetadata_Add_support_to_import_actor_image_url__Default_is_OFF_", typeof(Resources))]
        [EnabledCondition(nameof(IsNfoMetadataPluginLoaded), SimpleCondition.IsTrue)]
        [Required]
        public bool EnhanceNfoMetadata { get; set; } = false;
        
        [DisplayNameL("MetadataEnhanceOptions_EnableAltMovieDbUrl", typeof(Resources))]
        [EnabledCondition(nameof(IsMovieDbPluginLoaded), SimpleCondition.IsTrue)]
        [Required]
        public bool AltMovieDbConfig { get; set; } = false;

        [DisplayNameL("MetadataEnhanceOptions_AltMovieDbApiUrl_Alternative_MovieDb_Api_Url", typeof(Resources))]
        [DescriptionL("MetadataEnhanceOptions_AltMovieDbApiUrl_Default_alternative_is_https___api_tmdb_org", typeof(Resources))]
        [VisibleCondition(nameof(AltMovieDbConfig), SimpleCondition.IsTrue)]
        public string AltMovieDbApiUrl { get; set; } = "https://api.tmdb.org";

        [DisplayNameL("MetadataEnhanceOptions_AltMovieDbImageUrl_Alternative_MovieDb_Image_Url", typeof(Resources))]
        [DescriptionL("MetadataEnhanceOptions_AltMovieDbImageUrl_No_default_alternative__Provide_by_yourself_", typeof(Resources))]
        [VisibleCondition(nameof(AltMovieDbConfig), SimpleCondition.IsTrue)]
        public string AltMovieDbImageUrl { get; set; } = string.Empty;

        [DisplayNameL("MetadataEnhanceOptions_AltMovieDbApiKey_Alternative_MovieDb_Api_Key", typeof(Resources))]
        [DescriptionL("MetadataEnhanceOptions_AltMovieDbApiKey_Provide_your_own_MovieDb_Api_Key__Blank_uses_system_default_", typeof(Resources))]
        [VisibleCondition(nameof(AltMovieDbConfig), SimpleCondition.IsTrue)]
        public string AltMovieDbApiKey { get; set; } = string.Empty;

        [Browsable(false)]
        public bool IsMovieDbPluginLoaded { get; } =
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "MovieDb") &&
            RuntimeInformation.ProcessArchitecture == Architecture.X64;

        [Browsable(false)]
        public bool IsNfoMetadataPluginLoaded { get; } =
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "NfoMetadata") &&
            RuntimeInformation.ProcessArchitecture == Architecture.X64;

        [Browsable(false)]
        public bool IsModSupported { get; } = RuntimeInformation.ProcessArchitecture == Architecture.X64;

        protected override void Validate(ValidationContext context)
        {
            string metadataOptionsErrors = null;

            foreach (var (value, isValid, errorResource) in new (string, Func<string, bool>, string)[]
                     {
                         (AltMovieDbApiUrl, IsValidHttpUrl,
                             Resources.InvalidAltMovieDbApiUrl),
                         (AltMovieDbImageUrl, IsValidHttpUrl,
                             Resources.InvalidAltMovieDbImageUrl),
                         (AltMovieDbApiKey, IsValidMovieDbApiKey,
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
        }
    }
}
