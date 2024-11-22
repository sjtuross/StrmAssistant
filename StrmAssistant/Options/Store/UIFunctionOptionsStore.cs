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

        public UIFunctionOptionsStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
            : base(applicationHost, logger, pluginFullName)
        {
            _logger = logger;

            _currentHidePersonNoImage = UIFunctionOptions.HidePersonNoImage;
            _currentEnforceLibraryOrder = UIFunctionOptions.EnforceLibraryOrder;
            _currentBeautifyMissingMetadata = UIFunctionOptions.BeautifyMissingMetadata;
            
            FileSaved += OnFileSaved;
        }

        public UIFunctionOptions UIFunctionOptions => GetOptions();

        private void OnFileSaved(object sender, UIBaseClasses.Store.FileSavedEventArgs e)
        {
            _logger.Info("HidePersonNoImage is set to {0}", UIFunctionOptions.HidePersonNoImage);
            if (_currentHidePersonNoImage != UIFunctionOptions.HidePersonNoImage)
            {
                _currentHidePersonNoImage = UIFunctionOptions.HidePersonNoImage;

                if (_currentHidePersonNoImage)
                {
                    HidePersonNoImage.Patch();
                }
                else
                {
                    HidePersonNoImage.Unpatch();
                }
            }

            _logger.Info("EnforceLibraryOrder is set to {0}", UIFunctionOptions.EnforceLibraryOrder);
            if (_currentEnforceLibraryOrder != UIFunctionOptions.EnforceLibraryOrder)
            {
                _currentEnforceLibraryOrder = UIFunctionOptions.EnforceLibraryOrder;

                if (_currentEnforceLibraryOrder)
                {
                    EnforceLibraryOrder.Patch();
                }
                else
                {
                    EnforceLibraryOrder.Unpatch();
                }
            }

            _logger.Info("BeautifyMissingMetadata is set to {0}", UIFunctionOptions.BeautifyMissingMetadata);
            if (_currentBeautifyMissingMetadata != UIFunctionOptions.BeautifyMissingMetadata)
            {
                _currentBeautifyMissingMetadata = UIFunctionOptions.BeautifyMissingMetadata;

                if (_currentBeautifyMissingMetadata)
                {
                    BeautifyMissingMetadata.Patch();
                }
                else
                {
                    BeautifyMissingMetadata.Unpatch();
                }
            }
        }

    }
}
