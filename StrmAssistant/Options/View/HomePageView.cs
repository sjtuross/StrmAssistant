using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using StrmAssistant.Options.Store;
using StrmAssistant.Options.UIBaseClasses.Views;
using System.Threading.Tasks;

namespace StrmAssistant.Options.View
{
    internal class HomePageView : PluginPageView
    {
        private readonly PluginOptionsStore store;

        public HomePageView(PluginInfo pluginInfo, ILibraryManager libraryManager, PluginOptionsStore store)
            : base(pluginInfo.Id)
        {
            this.store = store;
            ContentData = store.GetOptions();

            PluginOptions.MediaInfoExtractOptions.Initialize(libraryManager);
            PluginOptions.ModOptions.Initialize();
            PluginOptions.NetworkOptions.Initialize();
            PluginOptions.AboutOptions.Initialize();
        }

        public PluginOptions PluginOptions => ContentData as PluginOptions;

        public override Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
        {
            store.SetOptions(PluginOptions);
            return base.OnSaveCommand(itemId, commandId, data);
        }
    }
}
