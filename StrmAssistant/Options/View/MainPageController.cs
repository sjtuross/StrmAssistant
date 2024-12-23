using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI;
using MediaBrowser.Model.Plugins.UI.Views;
using StrmAssistant.Options.Store;
using StrmAssistant.Options.UIBaseClasses;
using StrmAssistant.Properties;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StrmAssistant.Options.View
{
    internal class MainPageController : ControllerBase, IHasTabbedUIPages
    {
        private readonly PluginInfo _pluginInfo;
        private readonly PluginOptionsStore _mainOptionsStore;
        private readonly List<IPluginUIPageController> _tabPages = new List<IPluginUIPageController>();

        public MainPageController(PluginInfo pluginInfo, ILibraryManager libraryManager,
            PluginOptionsStore mainOptionsStore, MediaInfoExtractOptionsStore mediaInfoExtractOptionsStore,
            MetadataEnhanceOptionsStore metadataEnhanceOptionsStore,
            IntroSkipOptionsStore introSkipOptionsStore, ExperienceEnhanceOptionsStore experienceEnhanceOptionsStore)
            : base(pluginInfo.Id)
        {
            _pluginInfo = pluginInfo;
            _mainOptionsStore = mainOptionsStore;

            PageInfo = new PluginPageInfo
            {
                Name = "Settings",
                EnableInMainMenu = true,
                DisplayName = Resources.ResourceManager.GetString("PluginOptions_EditorTitle_Strm_Assistant",
                    Plugin.Instance.DefaultUICulture),
                MenuIcon = "video_settings",
                IsMainConfigPage = false,
            };

            _tabPages.Add(new TabPageController(pluginInfo, nameof(MediaInfoExtractPageView),
                Resources.ResourceManager.GetString("PluginOptions_EditorTitle_Strm_Extract",
                    Plugin.Instance.DefaultUICulture),
                e => new MediaInfoExtractPageView(pluginInfo, libraryManager, mediaInfoExtractOptionsStore)));
            _tabPages.Add(new TabPageController(pluginInfo, nameof(MetadataEnhancePageView),
                Resources.ResourceManager.GetString("PluginOptions_MetadataEnhanceOptions_Metadata_Enhance",
                    Plugin.Instance.DefaultUICulture),
                e => new MetadataEnhancePageView(pluginInfo, metadataEnhanceOptionsStore)));
            _tabPages.Add(new TabPageController(pluginInfo, nameof(IntroSkipPageView),
                Resources.ResourceManager.GetString("PluginOptions_IntroSkipOptions_Intro_Credits_Detection",
                    Plugin.Instance.DefaultUICulture),
                e => new IntroSkipPageView(pluginInfo, libraryManager, introSkipOptionsStore)));
            _tabPages.Add(new TabPageController(pluginInfo, nameof(ExperienceEnhancePageView),
                Resources.ResourceManager.GetString("ExperienceEnhanceOptions_EditorTitle_Experience_Enhance",
                    Plugin.Instance.DefaultUICulture),
                e => new ExperienceEnhancePageView(pluginInfo, experienceEnhanceOptionsStore)));
        }

        public override PluginPageInfo PageInfo { get; }

        public override Task<IPluginUIView> CreateDefaultPageView()
        {
            IPluginUIView view = new HomePageView(_pluginInfo, _mainOptionsStore);
            return Task.FromResult(view);
        }

        public IReadOnlyList<IPluginUIPageController> TabPageControllers => _tabPages.AsReadOnly();
    }
}
