using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using StrmAssistant.Options.Store;
using StrmAssistant.Options.UIBaseClasses.Views;
using System.Threading.Tasks;

namespace StrmAssistant.Options.View
{
    internal class MediaInfoExtractPageView : PluginPageView
    {
        private readonly MediaInfoExtractOptionsStore _store;

        public MediaInfoExtractPageView(PluginInfo pluginInfo, ILibraryManager libraryManager,
            MediaInfoExtractOptionsStore store)
            : base(pluginInfo.Id)
        {
            _store = store;
            ContentData = store.GetOptions();

            MediaInfoExtractOptions.Initialize(libraryManager);
        }

        public MediaInfoExtractOptions MediaInfoExtractOptions => ContentData as MediaInfoExtractOptions;

        public override Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
        {
            _store.SetOptions(MediaInfoExtractOptions);
            return base.OnSaveCommand(itemId, commandId, data);
        }
    }
}
