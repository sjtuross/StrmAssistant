using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI;
using MediaBrowser.Model.Plugins.UI.Views;
using StrmAssistant.Options.Store;
using StrmAssistant.Options.UIBaseClasses;
using StrmAssistant.Properties;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace StrmAssistant.Options.View
{

    internal class MainPageController : ControllerBase, IHasTabbedUIPages
    {
        private readonly PluginInfo _pluginInfo;
        private readonly ILibraryManager _libraryManager;
        private readonly PluginOptionsStore _mainOptionsStore;
        private readonly List<IPluginUIPageController> _tabPages = new List<IPluginUIPageController>();

        public MainPageController(PluginInfo pluginInfo, ILibraryManager libraryManager,
            PluginOptionsStore mainOptionsStore, MetadataEnhanceOptionsStore metadataEnhanceOptionsStore,
            IntroSkipOptionsStore introSkipOptionsStore, UIFunctionOptionsStore uiFunctionOptionsStore)
            : base(pluginInfo.Id)
        {
            Resources.Culture = new CultureInfo("zh-CN");

            _pluginInfo = pluginInfo;
            _libraryManager = libraryManager;
            _mainOptionsStore = mainOptionsStore;
            PageInfo = new PluginPageInfo
            {
                Name = "StrmAssistant",
                EnableInMainMenu = true,
                DisplayName = Resources.PluginOptions_EditorTitle_Strm_Assistant,
                MenuIcon = "video_settings",
                IsMainConfigPage = false,
            };

            _tabPages.Add(new TabPageController(pluginInfo, nameof(MetadataEnhancePageView),
                Resources.PluginOptions_MetadataEnhanceOptions_Metadata_Enhance,
                e => new MetadataEnhancePageView(pluginInfo, metadataEnhanceOptionsStore)));
            _tabPages.Add(new TabPageController(pluginInfo, nameof(IntroSkipPageView),
                Resources.PluginOptions_IntroSkipOptions_Intro_Credits_Detection,
                e => new IntroSkipPageView(pluginInfo, _libraryManager, introSkipOptionsStore)));
            _tabPages.Add(new TabPageController(pluginInfo, nameof(UIFunctionPageView),
                Resources.UIFunctionOptions_EditorTitle_UI_Functions,
                e => new UIFunctionPageView(pluginInfo, uiFunctionOptionsStore)));
        }

        public override PluginPageInfo PageInfo { get; }

        public override Task<IPluginUIView> CreateDefaultPageView()
        {
            IPluginUIView view = new HomePageView(_pluginInfo, _libraryManager, _mainOptionsStore);
            return Task.FromResult(view);
        }

        public IReadOnlyList<IPluginUIPageController> TabPageControllers => _tabPages.AsReadOnly();
    }
}
