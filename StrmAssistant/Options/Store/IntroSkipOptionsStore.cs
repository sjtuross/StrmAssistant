using MediaBrowser.Common;
using MediaBrowser.Model.Logging;
using StrmAssistant.Mod;
using StrmAssistant.Options.UIBaseClasses.Store;
using System;
using System.Linq;

namespace StrmAssistant.Options.Store
{
    public class IntroSkipOptionsStore : SimpleFileStore<IntroSkipOptions>
    {
        private readonly ILogger _logger;

        private bool _currentEnableIntroSkip;
        private bool _currentUnlockIntroSkip;

        public IntroSkipOptionsStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
            : base(applicationHost, logger, pluginFullName)
        {
            _logger = logger;
            _currentEnableIntroSkip = IntroSkipOptions.EnableIntroSkip;
            FileSaved += OnFileSaved;
            FileSaving += OnFileSaving;
        }

        public IntroSkipOptions IntroSkipOptions => GetOptions();
        
        private void OnFileSaving(object sender, FileSavingEventArgs e)
        {
            if (e.Options is IntroSkipOptions options)
            {
                options.LibraryScope = string.Join(",",
                    options.LibraryScope
                        ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(v => options.LibraryList.Any(option => option.Value == v)) ??
                    Enumerable.Empty<string>());

                options.UserScope = string.Join(",",
                    options.UserScope
                        ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(v => options.UserList.Any(option => option.Value == v)) ??
                    Enumerable.Empty<string>());

                options.MarkerEnabledLibraryScope =
                    options.MarkerEnabledLibraryScope
                        ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Contains("-1") == true
                        ? "-1"
                        : string.Join(",",
                            options.MarkerEnabledLibraryScope
                                ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                .Where(v => options.MarkerEnabledLibraryList.Any(option =>
                                    option.Value == v)) ?? Enumerable.Empty<string>());
            }
        }

        private void OnFileSaved(object sender, UIBaseClasses.Store.FileSavedEventArgs e)
        {
            if (e.Options is IntroSkipOptions options)
            {
                _logger.Info("EnableIntroSkip is set to {0}", options.EnableIntroSkip);
                _logger.Info("MaxIntroDurationSeconds is set to {0}", options.MaxIntroDurationSeconds);
                _logger.Info("MaxCreditsDurationSeconds is set to {0}", options.MaxCreditsDurationSeconds);
                _logger.Info("MinOpeningPlotDurationSeconds is set to {0}",
                    options.MinOpeningPlotDurationSeconds);

                if (_currentEnableIntroSkip != options.EnableIntroSkip)
                {
                    _currentEnableIntroSkip = options.EnableIntroSkip;
                    if (options.EnableIntroSkip)
                    {
                        Plugin.PlaySessionMonitor.Initialize();
                    }
                    else
                    {
                        Plugin.PlaySessionMonitor.Dispose();
                    }
                }

                var intoSkipLibraryScope = string.Join(", ",
                    options.LibraryScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => options.LibraryList
                            .FirstOrDefault(option => option.Value == v)
                            ?.Name) ?? Enumerable.Empty<string>());
                _logger.Info("IntroSkip - LibraryScope is set to {0}",
                    string.IsNullOrEmpty(intoSkipLibraryScope) ? "ALL" : intoSkipLibraryScope);

                var introSkipUserScope = string.Join(", ",
                    options.UserScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => options.UserList
                            .FirstOrDefault(option => option.Value == v)
                            ?.Name) ?? Enumerable.Empty<string>());
                _logger.Info("IntroSkip - UserScope is set to {0}",
                    string.IsNullOrEmpty(introSkipUserScope) ? "ALL" : introSkipUserScope);

                Plugin.PlaySessionMonitor.UpdateLibraryPathsInScope();
                Plugin.PlaySessionMonitor.UpdateUsersInScope();

                _logger.Info("UnlockIntroSkip is set to {0}", options.UnlockIntroSkip);
                if (_currentUnlockIntroSkip != options.UnlockIntroSkip)
                {
                    _currentUnlockIntroSkip = options.UnlockIntroSkip;
                    if (options.IsModSupported)
                    {
                        if (_currentUnlockIntroSkip)
                        {
                            UnlockIntroSkip.Patch();
                        }
                        else
                        {
                            UnlockIntroSkip.Unpatch();
                        }
                    }
                }

                var markerEnabledLibraryScope = string.Join(", ",
                    options.MarkerEnabledLibraryScope
                        ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(v =>
                            options.MarkerEnabledLibraryList
                                .FirstOrDefault(option => option.Value == v)?.Name) ?? Enumerable.Empty<string>());
                _logger.Info("MarkerEnabledLibraryScope is set to {0}",
                    string.IsNullOrEmpty(markerEnabledLibraryScope)
                        ? options.MarkerEnabledLibraryList.Any(o => o.Value != "-1") ? "ALL" : "EMPTY"
                        : markerEnabledLibraryScope);
                _logger.Info("IntroDetectionFingerprintMinutes is set to {0}",
                    options.IntroDetectionFingerprintMinutes);

                Plugin.ChapterApi.UpdateLibraryIntroDetectionFingerprintLength();
            }
        }
    }
}
