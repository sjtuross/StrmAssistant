using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using StrmAssistant.Options.Store;
using StrmAssistant.Options.UIBaseClasses.Views;
using System.Threading.Tasks;

namespace StrmAssistant.Options.View
{
    internal class IntroSkipPageView : PluginPageView
    {
        private readonly IntroSkipOptionsStore _store;

        public IntroSkipPageView(PluginInfo pluginInfo, ILibraryManager libraryManager,
            IntroSkipOptionsStore store)
            : base(pluginInfo.Id)
        {
            _store = store;
            ContentData = store.GetOptions();
            IntroSkipOptions.Initialize(libraryManager);
        }

        public IntroSkipOptions IntroSkipOptions => ContentData as IntroSkipOptions;

        public override Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
        {
            _store.SetOptions(IntroSkipOptions);
            return base.OnSaveCommand(itemId, commandId, data);
        }
    }
}
