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
            IntroSkipOptions.LibraryScope = string.Join(",",
                IntroSkipOptions.LibraryScope
                    ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(v => IntroSkipOptions.LibraryList.Any(option => option.Value == v)) ??
                Enumerable.Empty<string>());

            IntroSkipOptions.UserScope = string.Join(",",
                IntroSkipOptions.UserScope
                    ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(v => IntroSkipOptions.UserList.Any(option => option.Value == v)) ??
                Enumerable.Empty<string>());

            IntroSkipOptions.MarkerEnabledLibraryScope = string.Join(",",
                IntroSkipOptions.MarkerEnabledLibraryScope
                    ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(v => IntroSkipOptions.MarkerEnabledLibraryList.Any(option => option.Value == v)) ??
                Enumerable.Empty<string>());
        }

        private void OnFileSaved(object sender, UIBaseClasses.Store.FileSavedEventArgs e)
        {
            _logger.Info("EnableIntroSkip is set to {0}", IntroSkipOptions.EnableIntroSkip);
            _logger.Info("MaxIntroDurationSeconds is set to {0}", IntroSkipOptions.MaxIntroDurationSeconds);
            _logger.Info("MaxCreditsDurationSeconds is set to {0}", IntroSkipOptions.MaxCreditsDurationSeconds);
            _logger.Info("MinOpeningPlotDurationSeconds is set to {0}", IntroSkipOptions.MinOpeningPlotDurationSeconds);

            if (_currentEnableIntroSkip != IntroSkipOptions.EnableIntroSkip)
            {
                _currentEnableIntroSkip = IntroSkipOptions.EnableIntroSkip;
                if (IntroSkipOptions.EnableIntroSkip)
                {
                    Plugin.PlaySessionMonitor.Initialize();
                }
                else
                {
                    Plugin.PlaySessionMonitor.Dispose();
                }
            }

            var intoSkipLibraryScope = string.Join(", ",
                IntroSkipOptions.LibraryScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => IntroSkipOptions.LibraryList
                        .FirstOrDefault(option => option.Value == v)
                        ?.Name) ?? Enumerable.Empty<string>());
            _logger.Info("IntroSkip - LibraryScope is set to {0}",
                string.IsNullOrEmpty(intoSkipLibraryScope) ? "ALL" : intoSkipLibraryScope);

            var introSkipUserScope = string.Join(", ",
                IntroSkipOptions.UserScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => IntroSkipOptions.UserList
                        .FirstOrDefault(option => option.Value == v)
                        ?.Name) ?? Enumerable.Empty<string>());
            _logger.Info("IntroSkip - UserScope is set to {0}",
                string.IsNullOrEmpty(introSkipUserScope) ? "ALL" : introSkipUserScope);

            Plugin.PlaySessionMonitor.UpdateLibraryPathsInScope();
            Plugin.PlaySessionMonitor.UpdateUsersInScope();

            _logger.Info("UnlockIntroSkip is set to {0}", IntroSkipOptions.UnlockIntroSkip);
            if (_currentUnlockIntroSkip != IntroSkipOptions.UnlockIntroSkip)
            {
                _currentUnlockIntroSkip = IntroSkipOptions.UnlockIntroSkip;
                if (IntroSkipOptions.IsModSupported)
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
                IntroSkipOptions.MarkerEnabledLibraryScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => IntroSkipOptions.MarkerEnabledLibraryList
                        .FirstOrDefault(option => option.Value == v)
                        ?.Name) ?? Enumerable.Empty<string>());
            _logger.Info("Fingerprint - MarkerEnabledLibraryScope is set to {0}",
                string.IsNullOrEmpty(markerEnabledLibraryScope) ? "ALL" : markerEnabledLibraryScope);
            _logger.Info("IntroDetectionFingerprintMinutes is set to {0}",
                IntroSkipOptions.IntroDetectionFingerprintMinutes);

            Plugin.ChapterApi.UpdateLibraryIntroDetectionFingerprintLength();
        }
    }
}
