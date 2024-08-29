using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Properties;
using System.Collections.Generic;
using System.ComponentModel;

namespace StrmAssistant
{
    public class IntroSkipOptions: EditableOptionsBase
    {
        [DisplayNameL("PluginOptions_IntroSkipOptions_Intro_Credits_Detection", typeof(Resources))]
        public override string EditorTitle => Resources.PluginOptions_IntroSkipOptions_Intro_Credits_Detection;

        [DisplayNameL("PluginOptions_EnableIntroSkip_Enable_Intro_Skip__Experimental_", typeof(Resources))]
        [DescriptionL("PluginOptions_EnableIntroSkip_Enable_intro_skip_and_credits_skip_for_episodes__Default_is_False_", typeof(Resources))]
        public bool EnableIntroSkip { get; set; } = false;

        [DisplayNameL("IntroSkipOptions_MaxIntroDurationSeconds", typeof(Resources))]
        [MinValue(10), MaxValue(600)]
        [Required]
        [VisibleCondition(nameof(EnableIntroSkip), SimpleCondition.IsTrue)]
        public int MaxIntroDurationSeconds { get; set; } = 150;

        [DisplayNameL("IntroSkipOptions_MaxCreditsDurationSeconds", typeof(Resources))]
        [MinValue(10), MaxValue(600)]
        [Required]
        [VisibleCondition(nameof(EnableIntroSkip), SimpleCondition.IsTrue)]
        public int MaxCreditsDurationSeconds { get; set; } = 360;

        [Browsable(false)]
        public IEnumerable<EditorSelectOption> LibraryList { get; set; }

        [DisplayNameL("IntroSkipOptions_LibraryScope_Library_Scope", typeof(Resources))]
        [DescriptionL("IntroSkipOptions_LibraryScope_TV_shows_library_scope_to_detect__Blank_includes_all_", typeof(Resources))]
        [EditMultilSelect]
        [SelectItemsSource(nameof(LibraryList))]
        [VisibleCondition(nameof(EnableIntroSkip), SimpleCondition.IsTrue)]
        public string LibraryScope { get; set; }
    }
}
