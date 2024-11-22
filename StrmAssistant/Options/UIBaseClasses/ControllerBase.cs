namespace StrmAssistant.Options.UIBaseClasses
{
    using System.Threading;
    using System.Threading.Tasks;

    using MediaBrowser.Model.Plugins;
    using MediaBrowser.Model.Plugins.UI;
    using MediaBrowser.Model.Plugins.UI.Views;

    public abstract class ControllerBase : IPluginUIPageController
    {
        /// <summary>Initializes a new instance of the <see cref="ControllerBase"/> class.</summary>
        /// <param name="pluginId">The plugin identifier.</param>
        protected ControllerBase(string pluginId)
        {
            this.PluginId = pluginId;
        }

        public abstract PluginPageInfo PageInfo { get; }

        public string PluginId { get; }

        public virtual Task Initialize(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        public abstract Task<IPluginUIView> CreateDefaultPageView();
    }
}
