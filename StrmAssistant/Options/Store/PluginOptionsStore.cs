using Emby.Media.Common.Extensions;
using MediaBrowser.Common;
using MediaBrowser.Model.Logging;
using StrmAssistant.Common;
using StrmAssistant.Mod;
using StrmAssistant.Options.UIBaseClasses.Store;
using System;
using System.Linq;

namespace StrmAssistant.Options.Store
{
    public class PluginOptionsStore : SimpleFileStore<PluginOptions>
    {
        private readonly ILogger _logger;

        private int _currentMaxConcurrentCount;
        private bool _currentEnableImageCapture;
        private bool _currentCatchupMode;
        private bool _currentExclusiveExtract;

        private bool _currentSuppressOnOptionsSaved;
        private bool _currentMergeMultiVersion;
        private bool _currentEnhanceChineseSearch;
        private string _currentSearchScope;

        public PluginOptionsStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
            : base(applicationHost, logger, pluginFullName)
        {
            _logger = logger;

            _currentMaxConcurrentCount = PluginOptions.GeneralOptions.MaxConcurrentCount;
            _currentEnableImageCapture = PluginOptions.MediaInfoExtractOptions.EnableImageCapture;
            _currentCatchupMode = PluginOptions.GeneralOptions.CatchupMode;
            _currentExclusiveExtract = PluginOptions.MediaInfoExtractOptions.ExclusiveExtract;

            _currentMergeMultiVersion = PluginOptions.ModOptions.MergeMultiVersion;
            _currentEnhanceChineseSearch = PluginOptions.ModOptions.EnhanceChineseSearch;
            _currentSearchScope = PluginOptions.ModOptions.SearchScope;

            FileSaved += OnFileSaved;
        }

        public PluginOptions PluginOptions => GetOptions();

        public void SavePluginOptionsSuppress()
        {
            _currentSuppressOnOptionsSaved = true;
            SetOptions(PluginOptions);
        }

        private void OnFileSaved(object sender, FileSavedEventArgs e)
        {
            if (_currentCatchupMode != PluginOptions.GeneralOptions.CatchupMode)
            {
                _currentCatchupMode = PluginOptions.GeneralOptions.CatchupMode;

                if (PluginOptions.GeneralOptions.CatchupMode)
                {
                    Plugin.Instance.InitializeCatchupMode();
                }
                else
                {
                    Plugin.Instance.DisposeCatchupMode();
                }
            }

            var libraryScope = string.Join(", ",
                PluginOptions.MediaInfoExtractOptions.LibraryScope
                    ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v =>
                        PluginOptions.MediaInfoExtractOptions.LibraryList.FirstOrDefault(option => option.Value == v)
                            ?.Name) ?? Enumerable.Empty<string>());

            if (_currentMaxConcurrentCount != PluginOptions.GeneralOptions.MaxConcurrentCount)
            {
                _currentMaxConcurrentCount = PluginOptions.GeneralOptions.MaxConcurrentCount;

                QueueManager.UpdateSemaphore(_currentMaxConcurrentCount);

                if (PluginOptions.MediaInfoExtractOptions.EnableImageCapture)
                    EnableImageCapture.UpdateResourcePool(_currentMaxConcurrentCount);
            }

            if (_currentEnableImageCapture != PluginOptions.MediaInfoExtractOptions.EnableImageCapture)
            {
                _currentEnableImageCapture = PluginOptions.MediaInfoExtractOptions.EnableImageCapture;

                if (_currentEnableImageCapture)
                {
                    EnableImageCapture.Patch();
                    if (_currentMaxConcurrentCount > 1) Plugin.Instance.ApplicationHost.NotifyPendingRestart();
                }
                else
                {
                    EnableImageCapture.Unpatch();
                }
            }

            if (_currentExclusiveExtract != PluginOptions.MediaInfoExtractOptions.ExclusiveExtract)
            {
                _currentExclusiveExtract = PluginOptions.MediaInfoExtractOptions.ExclusiveExtract;

                if (_currentExclusiveExtract)
                {
                    ExclusiveExtract.Patch();
                }
                else
                {
                    ExclusiveExtract.Unpatch();
                }
            }

            if (_currentMergeMultiVersion != PluginOptions.ModOptions.MergeMultiVersion)
            {
                _currentMergeMultiVersion = PluginOptions.ModOptions.MergeMultiVersion;

                if (_currentMergeMultiVersion)
                {
                    MergeMultiVersion.Patch();
                }
                else
                {
                    MergeMultiVersion.Unpatch();
                }
            }

            if (_currentEnhanceChineseSearch != PluginOptions.ModOptions.EnhanceChineseSearch)
            {
                _currentEnhanceChineseSearch = PluginOptions.ModOptions.EnhanceChineseSearch;

                var isSimpleTokenizer = string.Equals(EnhanceChineseSearch.CurrentTokenizerName, "simple",
                    StringComparison.Ordinal);

                PluginOptions.ModOptions.EnhanceChineseSearchRestore = !_currentEnhanceChineseSearch && isSimpleTokenizer;
                SavePluginOptionsSuppress();

                if ((!_currentEnhanceChineseSearch && isSimpleTokenizer) ||
                    (_currentEnhanceChineseSearch && !isSimpleTokenizer))
                {
                    Plugin.Instance.ApplicationHost.NotifyPendingRestart();
                }
            }

            var searchScope = string.Join(", ",
                PluginOptions.ModOptions.SearchScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s =>
                        Enum.TryParse(s.Trim(), true, out ModOptions.SearchItemType type)
                            ? type.GetDescription()
                            : null)
                    .Where(d => d != null) ?? Enumerable.Empty<string>());

            if (_currentSearchScope != PluginOptions.ModOptions.SearchScope)
            {
                _currentSearchScope = PluginOptions.ModOptions.SearchScope;

                if (PluginOptions.ModOptions.EnhanceChineseSearch) EnhanceChineseSearch.UpdateSearchScope();
            }

            var suppressLogger = _currentSuppressOnOptionsSaved;

            if (!suppressLogger)
            {
                _logger.Info("StrmOnly is set to {0}", PluginOptions.GeneralOptions.StrmOnly);
                _logger.Info("IncludeExtra is set to {0}", PluginOptions.MediaInfoExtractOptions.IncludeExtra);
                _logger.Info("MaxConcurrentCount is set to {0}", PluginOptions.GeneralOptions.MaxConcurrentCount);
                _logger.Info("CatchupMode is set to {0}", PluginOptions.GeneralOptions.CatchupMode);
                _logger.Info("EnableImageCapture is set to {0}", PluginOptions.MediaInfoExtractOptions.EnableImageCapture);
                _logger.Info("ExclusiveExtract is set to {0}", PluginOptions.MediaInfoExtractOptions.ExclusiveExtract);
                _logger.Info("MediaInfoExtract - LibraryScope is set to {0}",
                    string.IsNullOrEmpty(libraryScope) ? "ALL" : libraryScope);
                _logger.Info("MergeMultiVersion is set to {0}", PluginOptions.ModOptions.MergeMultiVersion);
                _logger.Info("EnhanceChineseSearch is set to {0}", PluginOptions.ModOptions.EnhanceChineseSearch);
                _logger.Info("EnhanceChineseSearch - SearchScope is set to {0}",
                    string.IsNullOrEmpty(searchScope) ? "ALL" : searchScope);
            }

            if (suppressLogger) _currentSuppressOnOptionsSaved = false;
        }
    }
}
