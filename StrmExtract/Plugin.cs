using MediaBrowser.Common;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;
using System;
using System.IO;

namespace StrmExtract
{
    public class Plugin : BasePluginSimpleUI<PluginOptions>, IHasThumbImage
    {
        public static Plugin Instance { get; private set; }

        private readonly Guid _id = new Guid("6107fc8c-443a-4171-b70e-7590658706d8");

        private readonly ILogger logger;

        public Plugin(IApplicationHost applicationHost, ILogManager logManager) : base(applicationHost)
        {
            Instance = this;
            this.logger = logManager.GetLogger(this.Name);
            this.logger.Info("Plugin is getting loaded.");
        }

        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        public override string Description => "Extracts info from Strm targets";

        public override Guid Id => _id;

        public override sealed string Name => "Strm Extract";

        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".Images.thumb.png");
        }
        public PluginOptions GetPluginOptions()
        {
            return this.GetOptions();
        }
        protected override void OnOptionsSaved(PluginOptions options)
        {
            this.logger.Info("MaxConcurrentCount is set to {0}", options.MaxConcurrentCount);
            this.logger.Info("StrmOnly is set to {0}", options.StrmOnly);
            this.logger.Info("IncludeExtra is set to {0}", options.IncludeExtra);
        }
    }
}
