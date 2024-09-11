using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace StrmAssistant
{
    public class ModOptions: EditableOptionsBase
    {
        public ModOptions()
        {
            var languageList = new List<EditorSelectOption>
            {
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
                }
            };
            LanguageList = languageList;
        }

        [DisplayNameL("PluginOptions_ModOptions_Mod_Features", typeof(Resources))]
        public override string EditorTitle => Resources.PluginOptions_ModOptions_Mod_Features;

        [DisplayNameL("GeneralOptions_MergeMultiVersion_Merge_Multiple_Versions", typeof(Resources))]
        [DescriptionL("GeneralOptions_MergeMultiVersion_Auto_merge_multiple_versions_if_in_the_same_folder_", typeof(Resources))]
        [Required]
        public bool MergeMultiVersion { get; set; } = false;

        [DisplayNameL("ModOptions_ChineseMovieDb_Chinese_MovieDb", typeof(Resources))]
        [DescriptionL("ModOptions_ChineseMovieDb_Optimize_MovieDb_for_Chinese_metadata__Default_is_OFF_", typeof(Resources))]
        [EnabledCondition(nameof(IsMovieDbPluginLoaded), SimpleCondition.IsTrue)]
        [Required]
        public bool ChineseMovieDb { get; set; } = false;

        [Browsable(false)]
        public IEnumerable<EditorSelectOption> LanguageList { get; set; }

        [DisplayNameL("ModOptions_FallbackLanguages_Fallback_Languages", typeof(Resources))]
        [DescriptionL("ModOptions_FallbackLanguages_Fallback_languages__Default_is_zh_SG_", typeof(Resources))]
        [EditMultilSelect]
        [SelectItemsSource(nameof(LanguageList))]
        [VisibleCondition(nameof(ChineseMovieDb), SimpleCondition.IsTrue)]
        public string FallbackLanguages { get; set; }

        [Browsable(false)]
        public bool IsMovieDbPluginLoaded { get; } =
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "MovieDb");
    }
}
