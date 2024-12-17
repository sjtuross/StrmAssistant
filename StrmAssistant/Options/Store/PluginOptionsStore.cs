using Emby.Media.Common.Extensions;
using Emby.Web.GenericEdit.Elements;
using MediaBrowser.Common;
using MediaBrowser.Model.Logging;
using StrmAssistant.Common;
using StrmAssistant.Mod;
using StrmAssistant.Options.UIBaseClasses.Store;
using StrmAssistant.Properties;
using System;
using System.Linq;
using static StrmAssistant.Common.CommonUtility;
using static StrmAssistant.Options.MediaInfoExtractOptions;

namespace StrmAssistant.Options.Store
{
    public class PluginOptionsStore : SimpleFileStore<PluginOptions>
    {
        private readonly ILogger _logger;

        private int _currentMaxConcurrentCount;
        private bool _currentPersistMediaInfo;
        private bool _currentEnableImageCapture;
        private bool _currentCatchupMode;
        private bool _currentExclusiveExtract;
        private string _currentExclusiveControlFeatures;

        private bool _currentSuppressOnOptionsSaved;
        private bool _currentMergeMultiVersion;
        private bool _currentEnhanceChineseSearch;
        private string _currentSearchScope;

        private bool _currentProxyServerEnabled;
        private string _currentProxyServerUrl;

        public PluginOptionsStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
            : base(applicationHost, logger, pluginFullName)
        {
            _logger = logger;

            _currentMaxConcurrentCount = PluginOptions.GeneralOptions.MaxConcurrentCount;
            _currentPersistMediaInfo = PluginOptions.MediaInfoExtractOptions.PersistMediaInfo;
            _currentEnableImageCapture = PluginOptions.MediaInfoExtractOptions.EnableImageCapture;
            _currentCatchupMode = PluginOptions.GeneralOptions.CatchupMode;
            _currentExclusiveExtract = PluginOptions.MediaInfoExtractOptions.ExclusiveExtract;
            _currentExclusiveControlFeatures = PluginOptions.MediaInfoExtractOptions.ExclusiveControlFeatures;

            _currentMergeMultiVersion = PluginOptions.ModOptions.MergeMultiVersion;
            _currentEnhanceChineseSearch = PluginOptions.ModOptions.EnhanceChineseSearch;
            _currentSearchScope = PluginOptions.ModOptions.SearchScope;

            _currentProxyServerEnabled = PluginOptions.NetworkOptions.EnableProxyServer;
            _currentProxyServerUrl = PluginOptions.NetworkOptions.ProxyServerUrl;

            FileSaved += OnFileSaved;
            FileSaving += OnFileSaving;
        }

        public PluginOptions PluginOptions => GetOptions();

        public void SavePluginOptionsSuppress()
        {
            _currentSuppressOnOptionsSaved = true;
            SetOptions(PluginOptions);
        }
        
        private void OnFileSaving(object sender, FileSavingEventArgs e)
        {
            if (e.Options is PluginOptions options)
            {
                options.MediaInfoExtractOptions.LibraryScope = string.Join(",",
                    options.MediaInfoExtractOptions.LibraryScope
                        ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(v => options.MediaInfoExtractOptions.LibraryList.Any(option => option.Value == v)) ??
                    Enumerable.Empty<string>());

                var controlFeatures = options.MediaInfoExtractOptions.ExclusiveControlFeatures;
                var selectedFeatures = controlFeatures.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(f => !(f == ExclusiveControl.CatchAllAllow.ToString() &&
                                  controlFeatures.Contains(ExclusiveControl.CatchAllBlock.ToString())))
                    .ToList();
                options.MediaInfoExtractOptions.ExclusiveControlFeatures = string.Join(",", selectedFeatures);

                var isSimpleTokenizer = string.Equals(EnhanceChineseSearch.CurrentTokenizerName, "simple",
                    StringComparison.Ordinal);
                options.ModOptions.EnhanceChineseSearchRestore =
                    !options.ModOptions.EnhanceChineseSearch && isSimpleTokenizer;

                options.NetworkOptions.ProxyServerUrl =
                    !string.IsNullOrWhiteSpace(options.NetworkOptions.ProxyServerUrl)
                        ? options.NetworkOptions.ProxyServerUrl.Trim().TrimEnd('/')
                        : options.NetworkOptions.ProxyServerUrl?.Trim();

                if (options.NetworkOptions.EnableProxyServer &&
                    !string.IsNullOrWhiteSpace(options.NetworkOptions.ProxyServerUrl))
                {
                    if (TryParseProxyUrl(options.NetworkOptions.ProxyServerUrl, out var host, out var port) &&
                        CheckProxyReachability(host, port) is (true, var tcpPing))
                    {
                        options.NetworkOptions.ProxyServerStatus.Status = ItemStatus.Succeeded;
                        options.NetworkOptions.ProxyServerStatus.Caption = Resources.ProxyServer_Available;
                        options.NetworkOptions.ProxyServerStatus.StatusText = $"{tcpPing} ms";
                    }
                    else
                    {
                        options.NetworkOptions.ProxyServerStatus.Status = ItemStatus.Unavailable;
                        options.NetworkOptions.ProxyServerStatus.Caption = Resources.ProxyServer_Unavailable;
                        options.NetworkOptions.ProxyServerStatus.StatusText = "N/A";
                    }

                    options.NetworkOptions.ShowProxyServerStatus = true;
                }
                else
                {
                    options.NetworkOptions.ProxyServerStatus.StatusText = string.Empty;
                    options.NetworkOptions.ShowProxyServerStatus = false;
                }
            }
        }

        private void OnFileSaved(object sender, FileSavedEventArgs e)
        {
            if (e.Options is PluginOptions options)
            {
                Plugin.LibraryApi.UpdateLibraryPathsInScope();

                if (_currentCatchupMode != options.GeneralOptions.CatchupMode)
                {
                    _currentCatchupMode = options.GeneralOptions.CatchupMode;

                    if (options.GeneralOptions.CatchupMode)
                    {
                        Plugin.Instance.InitializeCatchupMode();
                    }
                    else
                    {
                        Plugin.Instance.DisposeCatchupMode();
                    }
                }

                if (_currentMaxConcurrentCount != options.GeneralOptions.MaxConcurrentCount)
                {
                    _currentMaxConcurrentCount = options.GeneralOptions.MaxConcurrentCount;

                    QueueManager.UpdateSemaphore(_currentMaxConcurrentCount);

                    if (options.MediaInfoExtractOptions.EnableImageCapture)
                        EnableImageCapture.UpdateResourcePool(_currentMaxConcurrentCount);
                }

                if (_currentPersistMediaInfo != options.MediaInfoExtractOptions.PersistMediaInfo)
                {
                    _currentPersistMediaInfo = options.MediaInfoExtractOptions.PersistMediaInfo;

                    if (_currentPersistMediaInfo)
                    {
                        ChapterChangeTracker.Patch();
                    }
                    else
                    {
                        ChapterChangeTracker.Unpatch();
                    }
                }

                if (_currentEnableImageCapture != options.MediaInfoExtractOptions.EnableImageCapture)
                {
                    _currentEnableImageCapture = options.MediaInfoExtractOptions.EnableImageCapture;

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

                if (_currentExclusiveExtract != options.MediaInfoExtractOptions.ExclusiveExtract)
                {
                    _currentExclusiveExtract = options.MediaInfoExtractOptions.ExclusiveExtract;

                    if (_currentExclusiveExtract)
                    {
                        ExclusiveExtract.Patch();
                    }
                    else
                    {
                        ExclusiveExtract.Unpatch();
                    }
                }

                if (_currentExclusiveControlFeatures !=
                    options.MediaInfoExtractOptions.ExclusiveControlFeatures)
                {
                    _currentExclusiveControlFeatures = options.MediaInfoExtractOptions.ExclusiveControlFeatures;

                    if (_currentExclusiveExtract) ExclusiveExtract.UpdateControlFeatures();
                }

                if (_currentMergeMultiVersion != options.ModOptions.MergeMultiVersion)
                {
                    _currentMergeMultiVersion = options.ModOptions.MergeMultiVersion;

                    if (_currentMergeMultiVersion)
                    {
                        MergeMultiVersion.Patch();
                    }
                    else
                    {
                        MergeMultiVersion.Unpatch();
                    }
                }

                if (_currentEnhanceChineseSearch != options.ModOptions.EnhanceChineseSearch)
                {
                    _currentEnhanceChineseSearch = options.ModOptions.EnhanceChineseSearch;

                    var isSimpleTokenizer = string.Equals(EnhanceChineseSearch.CurrentTokenizerName, "simple",
                        StringComparison.Ordinal);

                    if ((!_currentEnhanceChineseSearch && isSimpleTokenizer) ||
                        (_currentEnhanceChineseSearch && !isSimpleTokenizer))
                    {
                        Plugin.Instance.ApplicationHost.NotifyPendingRestart();
                    }
                }

                if (_currentSearchScope != options.ModOptions.SearchScope)
                {
                    _currentSearchScope = options.ModOptions.SearchScope;

                    if (options.ModOptions.EnhanceChineseSearch) EnhanceChineseSearch.UpdateSearchScope();
                }

                if (_currentProxyServerEnabled != options.NetworkOptions.EnableProxyServer)
                {
                    _currentProxyServerEnabled = options.NetworkOptions.EnableProxyServer;

                    if (_currentProxyServerEnabled)
                    {
                        EnableProxyServer.Patch();
                    }
                    else
                    {
                        EnableProxyServer.Unpatch();
                    }

                    Plugin.Instance.ApplicationHost.NotifyPendingRestart();
                }

                if (_currentProxyServerUrl != options.NetworkOptions.ProxyServerUrl)
                {
                    _currentProxyServerUrl = options.NetworkOptions.ProxyServerUrl;

                    if (_currentProxyServerEnabled &&
                        options.NetworkOptions.ProxyServerStatus.Status == ItemStatus.Succeeded)
                    {
                        Plugin.Instance.ApplicationHost.NotifyPendingRestart();
                    }
                }

                var suppressLogger = _currentSuppressOnOptionsSaved;

                if (!suppressLogger)
                {
                    _logger.Info("PersistMediaInfo is set to {0}",
                        options.MediaInfoExtractOptions.PersistMediaInfo);
                    _logger.Info("MediaInfoJsonRootFolder is set to {0}",
                        !string.IsNullOrEmpty(options.MediaInfoExtractOptions.MediaInfoJsonRootFolder)
                            ? options.MediaInfoExtractOptions.MediaInfoJsonRootFolder
                            : "EMPTY");
                    _logger.Info("IncludeExtra is set to {0}", options.MediaInfoExtractOptions.IncludeExtra);
                    _logger.Info("MaxConcurrentCount is set to {0}", options.GeneralOptions.MaxConcurrentCount);
                    _logger.Info("CatchupMode is set to {0}", options.GeneralOptions.CatchupMode);
                    _logger.Info("EnableImageCapture is set to {0}",
                        options.MediaInfoExtractOptions.EnableImageCapture);
                    _logger.Info("ExclusiveExtract is set to {0}",
                        options.MediaInfoExtractOptions.ExclusiveExtract);

                    var controlFeatures = string.Join(", ",
                        options.MediaInfoExtractOptions.ExclusiveControlFeatures
                            ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s =>
                                Enum.TryParse(s.Trim(), true, out ExclusiveControl type)
                                    ? type.GetDescription()
                                    : null)
                            .Where(d => d != null) ?? Array.Empty<string>());
                    _logger.Info("ExclusiveExtract - ControlFeatures is set to {0}",
                        string.IsNullOrEmpty(controlFeatures) ? "EMPTY" : controlFeatures);

                    var libraryScope = string.Join(", ",
                        options.MediaInfoExtractOptions.LibraryScope
                            ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(v =>
                                options.MediaInfoExtractOptions.LibraryList
                                    .FirstOrDefault(option => option.Value == v)
                                    ?.Name) ?? Enumerable.Empty<string>());
                    _logger.Info("MediaInfoExtract - LibraryScope is set to {0}",
                        string.IsNullOrEmpty(libraryScope) ? "ALL" : libraryScope);

                    _logger.Info("MergeMultiVersion is set to {0}", options.ModOptions.MergeMultiVersion);
                    _logger.Info("EnhanceChineseSearch is set to {0}", options.ModOptions.EnhanceChineseSearch);

                    var searchScope = string.Join(", ",
                        options.ModOptions.SearchScope
                            ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s =>
                                Enum.TryParse(s.Trim(), true, out ModOptions.SearchItemType type)
                                    ? type.GetDescription()
                                    : null)
                            .Where(d => d != null) ?? Enumerable.Empty<string>());
                    _logger.Info("EnhanceChineseSearch - SearchScope is set to {0}",
                        string.IsNullOrEmpty(searchScope) ? "ALL" : searchScope);

                    _logger.Info("EnableProxyServer is set to {0}", options.NetworkOptions.EnableProxyServer);
                    _logger.Info("ProxyServerUrl is set to {0}",
                        !string.IsNullOrEmpty(options.NetworkOptions.ProxyServerUrl)
                            ? options.NetworkOptions.ProxyServerUrl
                            : "EMPTY");
                }

                if (suppressLogger) _currentSuppressOnOptionsSaved = false;
            }
        }
    }
}
