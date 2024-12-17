using MediaBrowser.Common;
using MediaBrowser.Model.Logging;
using StrmAssistant.Mod;
using StrmAssistant.Options.UIBaseClasses.Store;

namespace StrmAssistant.Options.Store
{
    public class UIFunctionOptionsStore : SimpleFileStore<UIFunctionOptions>
    {
        private readonly ILogger _logger;

        private bool _currentHidePersonNoImage;
        private bool _currentEnforceLibraryOrder;
        private bool _currentBeautifyMissingMetadata;
        private bool _currentEnhanceMissingEpisodes;

        public UIFunctionOptionsStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
            : base(applicationHost, logger, pluginFullName)
        {
            _logger = logger;

            _currentHidePersonNoImage = UIFunctionOptions.HidePersonNoImage;
            _currentEnforceLibraryOrder = UIFunctionOptions.EnforceLibraryOrder;
            _currentBeautifyMissingMetadata = UIFunctionOptions.BeautifyMissingMetadata;
            _currentEnhanceMissingEpisodes = UIFunctionOptions.EnhanceMissingEpisodes;

            FileSaved += OnFileSaved;
        }

        public UIFunctionOptions UIFunctionOptions => GetOptions();

        private void OnFileSaved(object sender, FileSavedEventArgs e)
        {
            if (e.Options is UIFunctionOptions options)
            {
                _logger.Info("HidePersonNoImage is set to {0}", options.HidePersonNoImage);
                if (_currentHidePersonNoImage != options.HidePersonNoImage)
                {
                    _currentHidePersonNoImage = options.HidePersonNoImage;

                    if (_currentHidePersonNoImage)
                    {
                        HidePersonNoImage.Patch();
                    }
                    else
                    {
                        HidePersonNoImage.Unpatch();
                    }
                }

                _logger.Info("EnforceLibraryOrder is set to {0}", options.EnforceLibraryOrder);
                if (_currentEnforceLibraryOrder != options.EnforceLibraryOrder)
                {
                    _currentEnforceLibraryOrder = options.EnforceLibraryOrder;

                    if (_currentEnforceLibraryOrder)
                    {
                        EnforceLibraryOrder.Patch();
                    }
                    else
                    {
                        EnforceLibraryOrder.Unpatch();
                    }
                }

                _logger.Info("BeautifyMissingMetadata is set to {0}", options.BeautifyMissingMetadata);
                if (_currentBeautifyMissingMetadata != options.BeautifyMissingMetadata)
                {
                    _currentBeautifyMissingMetadata = options.BeautifyMissingMetadata;

                    if (_currentBeautifyMissingMetadata)
                    {
                        BeautifyMissingMetadata.Patch();
                    }
                    else
                    {
                        BeautifyMissingMetadata.Unpatch();
                    }
                }

                _logger.Info("EnhanceMissingEpisodes is set to {0}", options.EnhanceMissingEpisodes);
                if (_currentEnhanceMissingEpisodes != options.EnhanceMissingEpisodes)
                {
                    _currentEnhanceMissingEpisodes = options.EnhanceMissingEpisodes;

                    if (_currentEnhanceMissingEpisodes)
                    {
                        EnhanceMissingEpisodes.Patch();
                    }
                    else
                    {
                        EnhanceMissingEpisodes.Unpatch();
                    }
                }
            }
        }
    }
}
