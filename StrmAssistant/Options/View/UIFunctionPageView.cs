using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using StrmAssistant.Options.Store;
using StrmAssistant.Options.UIBaseClasses.Views;
using System.Threading.Tasks;

namespace StrmAssistant.Options.View
{
    internal class UIFunctionPageView : PluginPageView
    {
        private readonly UIFunctionOptionsStore _store;

        public UIFunctionPageView(PluginInfo pluginInfo, UIFunctionOptionsStore store)
            : base(pluginInfo.Id)
        {
            _store = store;
            ContentData = store.GetOptions();
        }

        public UIFunctionOptions UIFunctionOptions => ContentData as UIFunctionOptions;

        public override Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
        {
            _store.SetOptions(UIFunctionOptions);
            return base.OnSaveCommand(itemId, commandId, data);
        }
    }
}
