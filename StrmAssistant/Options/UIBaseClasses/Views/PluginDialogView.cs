namespace StrmAssistant.Options.UIBaseClasses.Views
{
    using System;
    using System.Threading.Tasks;

    using MediaBrowser.Model.Plugins.UI.Views;

    internal abstract class PluginDialogView : PluginViewBase, IPluginDialogView
    {
        protected PluginDialogView(string pluginId)
        : base(pluginId)
        {
            this.PluginId = pluginId;
            this.AllowCancel = true;
            this.AllowOk = true;
        }

        public bool AllowCancel { get; set; }

        public bool AllowOk { get; set; }

        public virtual bool ShowDialogFullScreen { get; } = false;

        public virtual Task OnCancelCommand()
        {
            return Task.CompletedTask;
        }

        public virtual Task OnOkCommand(string providerId, string commandId, string data)
        {
            throw new NotImplementedException();
        }
    }
}
