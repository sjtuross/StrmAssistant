using Emby.Media.Common.Extensions;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.PropertyDiff;
using MediaBrowser.Common;
using MediaBrowser.Model.Logging;
using StrmAssistant.Common;
using StrmAssistant.Mod;
using StrmAssistant.Options.UIBaseClasses.Store;
using StrmAssistant.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using static StrmAssistant.Common.CommonUtility;
using static StrmAssistant.Options.MediaInfoExtractOptions;

namespace StrmAssistant.Options.Store
{
    public class PluginOptionsStore : SimpleFileStore<PluginOptions>
    {
        private readonly ILogger _logger;

        private bool _currentSuppressOnOptionsSaved;

        public PluginOptionsStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
            : base(applicationHost, logger, pluginFullName)
        {
            _logger = logger;

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

                var changes = PropertyChangeDetector.DetectObjectPropertyChanges(PluginOptions, options);
                var changedProperties = new HashSet<string>(changes.Select(c => c.PropertyName));

                if (changedProperties.Contains(nameof(PluginOptions.GeneralOptions.CatchupMode)))
                {
                    if (options.GeneralOptions.CatchupMode)
                    {
                        Plugin.Instance.InitializeCatchupMode();
                    }
                    else
                    {
                        Plugin.Instance.DisposeCatchupMode();
                    }
                }

                if (changedProperties.Contains(nameof(PluginOptions.GeneralOptions.MaxConcurrentCount)))
                {
                    QueueManager.UpdateSemaphore(options.GeneralOptions.MaxConcurrentCount);

                    if (options.MediaInfoExtractOptions.EnableImageCapture)
                        EnableImageCapture.UpdateResourcePool(options.GeneralOptions.MaxConcurrentCount);
                }

                if (changedProperties.Contains(nameof(PluginOptions.MediaInfoExtractOptions.PersistMediaInfo)))
                {
                    if (options.MediaInfoExtractOptions.PersistMediaInfo)
                    {
                        ChapterChangeTracker.Patch();
                    }
                    else
                    {
                        ChapterChangeTracker.Unpatch();
                    }
                }

                if (changedProperties.Contains(nameof(PluginOptions.MediaInfoExtractOptions.EnableImageCapture)))
                {
                    if (options.MediaInfoExtractOptions.EnableImageCapture)
                    {
                        EnableImageCapture.Patch();
                        if (options.GeneralOptions.MaxConcurrentCount > 1)
                            Plugin.Instance.ApplicationHost.NotifyPendingRestart();
                    }
                    else
                    {
                        EnableImageCapture.Unpatch();
                    }
                }

                if (changedProperties.Contains(nameof(PluginOptions.MediaInfoExtractOptions.ExclusiveExtract)))
                {
                    if (options.MediaInfoExtractOptions.ExclusiveExtract)
                    {
                        ExclusiveExtract.Patch();
                    }
                    else
                    {
                        ExclusiveExtract.Unpatch();
                    }
                }

                if (changedProperties.Contains(nameof(PluginOptions.MediaInfoExtractOptions
                        .ExclusiveControlFeatures)) && options.MediaInfoExtractOptions.ExclusiveExtract)
                {
                    ExclusiveExtract.UpdateControlFeatures();
                }
                
                if (changedProperties.Contains(nameof(PluginOptions.MediaInfoExtractOptions.LibraryScope)))
                {
                    Plugin.LibraryApi.UpdateLibraryPathsInScope();
                }

                if (changedProperties.Contains(nameof(PluginOptions.ModOptions.MergeMultiVersion)))
                {
                    if (options.ModOptions.MergeMultiVersion)
                    {
                        MergeMultiVersion.Patch();
                    }
                    else
                    {
                        MergeMultiVersion.Unpatch();
                    }
                }

                if (changedProperties.Contains(nameof(PluginOptions.ModOptions.EnhanceChineseSearch)) &&
                    ((!options.ModOptions.EnhanceChineseSearch && isSimpleTokenizer) ||
                     (options.ModOptions.EnhanceChineseSearch && !isSimpleTokenizer)))
                {
                    Plugin.Instance.ApplicationHost.NotifyPendingRestart();
                }

                if (changedProperties.Contains(nameof(PluginOptions.ModOptions.SearchScope)) &&
                    options.ModOptions.EnhanceChineseSearch)
                {
                    EnhanceChineseSearch.UpdateSearchScope();
                }

                if (changedProperties.Contains(nameof(PluginOptions.NetworkOptions.EnableProxyServer)))
                {
                    if (options.NetworkOptions.EnableProxyServer)
                    {
                        EnableProxyServer.Patch();
                    }
                    else
                    {
                        EnableProxyServer.Unpatch();
                    }

                    Plugin.Instance.ApplicationHost.NotifyPendingRestart();
                }

                if (changedProperties.Contains(nameof(PluginOptions.NetworkOptions.ProxyServerUrl)) &&
                    options.NetworkOptions.EnableProxyServer &&
                    options.NetworkOptions.ProxyServerStatus.Status == ItemStatus.Succeeded)
                {
                    Plugin.Instance.ApplicationHost.NotifyPendingRestart();
                }
            }
        }

        private void OnFileSaved(object sender, FileSavedEventArgs e)
        {
            if (e.Options is PluginOptions options)
            {
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
