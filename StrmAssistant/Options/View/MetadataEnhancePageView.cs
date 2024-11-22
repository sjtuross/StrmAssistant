using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using StrmAssistant.Options.Store;
using StrmAssistant.Options.UIBaseClasses.Views;
using System.Threading.Tasks;

namespace StrmAssistant.Options.View
{
    internal class MetadataEnhancePageView : PluginPageView
    {
        private readonly MetadataEnhanceOptionsStore _store;

        public MetadataEnhancePageView(PluginInfo pluginInfo, MetadataEnhanceOptionsStore store)
            : base(pluginInfo.Id)
        {
            this._store = store;
            ContentData = store.GetOptions();
        }

        public MetadataEnhanceOptions MetadataEnhanceOptions => ContentData as MetadataEnhanceOptions;

        public override Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
        {
            _store.SetOptions(MetadataEnhanceOptions);
            return base.OnSaveCommand(itemId, commandId, data);
        }
    }
}
