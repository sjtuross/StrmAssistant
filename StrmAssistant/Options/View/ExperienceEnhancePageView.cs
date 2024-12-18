using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using StrmAssistant.Options.Store;
using StrmAssistant.Options.UIBaseClasses.Views;
using System.Threading.Tasks;

namespace StrmAssistant.Options.View
{
    internal class ExperienceEnhancePageView : PluginPageView
    {
        private readonly ExperienceEnhanceOptionsStore _store;

        public ExperienceEnhancePageView(PluginInfo pluginInfo, ExperienceEnhanceOptionsStore store)
            : base(pluginInfo.Id)
        {
            _store = store;
            ContentData = store.GetOptions();
        }

        public ExperienceEnhanceOptions ExperienceEnhanceOptions => ContentData as ExperienceEnhanceOptions;

        public override Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
        {
            _store.SetOptions(ExperienceEnhanceOptions);
            return base.OnSaveCommand(itemId, commandId, data);
        }
    }
}
