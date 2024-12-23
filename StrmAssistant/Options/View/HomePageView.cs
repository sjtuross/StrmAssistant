using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using StrmAssistant.Options.Store;
using StrmAssistant.Options.UIBaseClasses.Views;
using System.Threading.Tasks;

namespace StrmAssistant.Options.View
{
    internal class HomePageView : PluginPageView
    {
        private readonly PluginOptionsStore _store;

        public HomePageView(PluginInfo pluginInfo, PluginOptionsStore store)
            : base(pluginInfo.Id)
        {
            _store = store;
            ContentData = store.GetOptions();

            PluginOptions.Initialize();
            PluginOptions.ModOptions.Initialize();
            PluginOptions.NetworkOptions.Initialize();
            PluginOptions.AboutOptions.Initialize();
        }

        public PluginOptions PluginOptions => ContentData as PluginOptions;

        public override Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
        {
            if (ContentData is PluginOptions options)
            {
                options.NetworkOptions.ValidateOrThrow();
            }

            _store.SetOptions(PluginOptions);
            return base.OnSaveCommand(itemId, commandId, data);
        }
    }
}
