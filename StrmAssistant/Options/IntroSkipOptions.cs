using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.List;
using Emby.Web.GenericEdit.Validation;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Common;
using StrmAssistant.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace StrmAssistant.Options
{
    public class IntroSkipOptions : EditableOptionsBase
    {
        [DisplayNameL("PluginOptions_IntroSkipOptions_Intro_Credits_Detection", typeof(Resources))]
        public override string EditorTitle => Resources.PluginOptions_IntroSkipOptions_Intro_Credits_Detection;
        
        [DisplayNameL("IntroSkipOptions_UnlockIntroSkip_Built_in_Intro_Skip_Enhanced", typeof(Resources))]
        [DescriptionL("IntroSkipOptions_UnlockIntroSkip_Unlock_Strm_support_for_built_in_intro_skip_detection", typeof(Resources))]
        [Required]
        public bool UnlockIntroSkip { get; set; } = false;

        [DisplayNameL("IntroSkipOptions_IntroDetectionFingerprintMinutes_Intro_Detection_Fingerprint_Minutes", typeof(Resources))]
        [DescriptionL("IntroSkipOptions_IntroDetectionFingerprintMinutes_It_must_be_between_2_and_20__Default_is_10_", typeof(Resources))]
        [MinValue(2), MaxValue(20)]
        [Required]
        [VisibleCondition(nameof(UnlockIntroSkip), SimpleCondition.IsTrue)]
        public int IntroDetectionFingerprintMinutes { get; set; } = 10;

        [Browsable(false)]
        public List<EditorSelectOption> MarkerEnabledLibraryList { get; set; } = new List<EditorSelectOption>();

        [DisplayNameL("IntroSkipOptions_MarkerEnabledLibraryScope_Library_Scope", typeof(Resources))]
        [DescriptionL("IntroSkipOptions_MarkerEnabledLibraryScope_Intro_detection_enabled_library_scope__Blank_includes_all_", typeof(Resources))]
        [EditMultilSelect]
        [SelectItemsSource(nameof(MarkerEnabledLibraryList))]
        [VisibleCondition(nameof(UnlockIntroSkip), SimpleCondition.IsTrue)]
        [EnabledCondition(nameof(IsModSupported),SimpleCondition.IsTrue)]
        public string MarkerEnabledLibraryScope { get; set; } = string.Empty;

        [DisplayNameL("IntroSkipOptions_BlacklistShows_Optional_Blacklist_Shows", typeof(Resources))]
        [DescriptionL("IntroSkipOptions_BlacklistShows_List_of_Series_Id_or_Season_Id_separated_by_comma_or_semicolon__Default_is_EMPTY", typeof(Resources))]
        [VisibleCondition(nameof(UnlockIntroSkip), SimpleCondition.IsTrue)]
        [EnabledCondition(nameof(IsModSupported),SimpleCondition.IsTrue)]
        public string FingerprintBlacklistShows { get; set; } = string.Empty;

        [VisibleCondition(nameof(UnlockIntroSkip), SimpleCondition.IsTrue)]
        public GenericItemList FingerprintBlacklistShowsResult { get; set; } = new GenericItemList();

        [VisibleCondition(nameof(UnlockIntroSkip), SimpleCondition.IsTrue)]
        public SpacerItem Separator { get; set; } = new SpacerItem(SpacerSize.Medium);

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

        [DisplayNameL("IntroSkipOptions_MinOpeningPlotDurationSeconds", typeof(Resources))]
        [MinValue(30), MaxValue(120)]
        [Required]
        [VisibleCondition(nameof(EnableIntroSkip), SimpleCondition.IsTrue)]
        public int MinOpeningPlotDurationSeconds { get; set; } = 60;

        [Browsable(false)]
        public List<EditorSelectOption> LibraryList { get; set; } = new List<EditorSelectOption>();

        [DisplayNameL("IntroSkipOptions_LibraryScope_Library_Scope", typeof(Resources))]
        [DescriptionL("IntroSkipOptions_LibraryScope_TV_shows_library_scope_to_detect__Blank_includes_all_", typeof(Resources))]
        [EditMultilSelect]
        [SelectItemsSource(nameof(LibraryList))]
        [VisibleCondition(nameof(EnableIntroSkip), SimpleCondition.IsTrue)]
        public string LibraryScope { get; set; } = string.Empty;

        [Browsable(false)]
        public List<EditorSelectOption> UserList { get; set; } = new List<EditorSelectOption>();

        [DisplayNameL("IntroSkipOptions_UserScope_User_Scope", typeof(Resources))]
        [DescriptionL("IntroSkipOptions_UserScope_Users_allowed_to_detect__Blank_includes_all", typeof(Resources))]
        [EditMultilSelect]
        [SelectItemsSource(nameof(UserList))]
        [VisibleCondition(nameof(EnableIntroSkip), SimpleCondition.IsTrue)]
        public string UserScope { get; set; } = string.Empty;

        [DisplayNameL("IntroSkipOptions_ClientScope_Client_Scope", typeof(Resources))]
        [DescriptionL("IntroSkipOptions_ClientScope_Allowed_clients__Default_is_Emby_Infuse_SenPlayer", typeof(Resources))]
        [Required]
        [VisibleCondition(nameof(EnableIntroSkip), SimpleCondition.IsTrue)]
        public string ClientScope { get; set; } = "Emby,Infuse,SenPlayer";

        [Browsable(false)]
        public bool IsModSupported { get; } = RuntimeInformation.ProcessArchitecture == Architecture.X64;

        protected override void Validate(ValidationContext context)
        {
            if (MarkerEnabledLibraryList.All(o => o.Value == "-1") &&
                MarkerEnabledLibraryScope.Contains("-1"))
            {
                context.AddValidationError(Resources.InvalidMarkerEnabledLibraryScope);
            }

            if (!string.IsNullOrWhiteSpace(FingerprintBlacklistShows))
            {
                var ids = FingerprintBlacklistShows.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => id.Trim())
                    .ToArray();

                var allInvalidIds = new List<string>();

                var invalidIds = ids.Where(id => !long.TryParse(id, out _));
                allInvalidIds.AddRange(invalidIds);

                var validIds = ids.Where(id => long.TryParse(id, out _)).Select(long.Parse).ToArray();
                var items = Plugin.LibraryApi.GetItemsByIds(validIds);

                var missingItems = validIds.Where(id => items.All(item => item.InternalId != id));
                allInvalidIds.AddRange(missingItems.Select(id => id.ToString()));

                var invalidItemTypes = items.Where(i => !(i is Season || i is Series))
                    .Select(i => i.InternalId.ToString());
                allInvalidIds.AddRange(invalidItemTypes);

                if (allInvalidIds.Any())
                {
                    context.AddValidationError(string.Format(Resources.InvalidBlacklistShowIds, string.Join(", ", allInvalidIds)));
                }
            }
        }

        public void Initialize(ILibraryManager libraryManager)
        {
            LibraryList.Clear();
            UserList.Clear();
            MarkerEnabledLibraryList.Clear();

            MarkerEnabledLibraryList.Add(new EditorSelectOption
            {
                Value = "-1",
                Name = Resources.Favorites,
                IsEnabled = true
            });

            var libraries = libraryManager.GetVirtualFolders();

            foreach (var item in libraries)
            {
                var selectOption = new EditorSelectOption
                {
                    Value = item.ItemId,
                    Name = item.Name,
                    IsEnabled = true,
                };

                if (item.CollectionType == CollectionType.TvShows.ToString() || item.CollectionType is null) // null means mixed content library
                {
                    LibraryList.Add(selectOption);

                    if (item.LibraryOptions.EnableMarkerDetection)
                    {
                        MarkerEnabledLibraryList.Add(selectOption);
                    }
                }
            }

            var allUsers = LibraryApi.AllUsers;

            foreach (var user in allUsers)
            {
                var selectOption = new EditorSelectOption
                {
                    Value = user.Key.InternalId.ToString(),
                    Name = (user.Value ? "\ud83d\udc51" : "\ud83d\udc64") + user.Key.Name,
                    IsEnabled = true,
                };

                UserList.Add(selectOption);
            }
        }
    }
}
