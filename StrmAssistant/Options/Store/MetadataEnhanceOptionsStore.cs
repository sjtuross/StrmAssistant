using MediaBrowser.Common;
using MediaBrowser.Model.Logging;
using StrmAssistant.Mod;
using StrmAssistant.Options.UIBaseClasses.Store;

namespace StrmAssistant.Options.Store
{

    public class MetadataEnhanceOptionsStore : SimpleFileStore<MetadataEnhanceOptions>
    {
        private readonly ILogger _logger;

        private bool _currentChineseMovieDb;
        private bool _currentEnhanceMovieDbPerson;
        private bool _currentAltMovieDbConfig;
        private bool _currentAltMovieDbImageUrlEnabled;
        private bool _currentPreferOriginalPoster;
        private bool _currentPinyinSortName;
        private bool _currentEnhanceNfoMetadata;

        public MetadataEnhanceOptionsStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
            : base(applicationHost, logger, pluginFullName)
        {
            _logger= logger;

            _currentChineseMovieDb = MetadataEnhanceOptions.ChineseMovieDb;
            _currentEnhanceMovieDbPerson = MetadataEnhanceOptions.EnhanceMovieDbPerson;
            _currentAltMovieDbConfig = MetadataEnhanceOptions.AltMovieDbConfig;
            _currentAltMovieDbImageUrlEnabled = !string.IsNullOrEmpty(MetadataEnhanceOptions.AltMovieDbImageUrl);
            _currentPreferOriginalPoster = MetadataEnhanceOptions.PreferOriginalPoster;
            _currentPinyinSortName = MetadataEnhanceOptions.PinyinSortName;
            _currentEnhanceNfoMetadata = MetadataEnhanceOptions.EnhanceNfoMetadata;

            FileSaved += OnFileSaved;
            FileSaving += OnFileSaving;
        }

        public MetadataEnhanceOptions MetadataEnhanceOptions => GetOptions();

        private void OnFileSaving(object sender, UIBaseClasses.Store.FileSavingEventArgs e)
        {
            MetadataEnhanceOptions.AltMovieDbApiUrl =
                !string.IsNullOrWhiteSpace(MetadataEnhanceOptions.AltMovieDbApiUrl)
                    ? MetadataEnhanceOptions.AltMovieDbApiUrl.Trim().TrimEnd('/')
                    : MetadataEnhanceOptions.AltMovieDbApiUrl?.Trim();

            MetadataEnhanceOptions.AltMovieDbImageUrl =
                !string.IsNullOrWhiteSpace(MetadataEnhanceOptions.AltMovieDbImageUrl)
                    ? MetadataEnhanceOptions.AltMovieDbImageUrl.Trim().TrimEnd('/')
                    : MetadataEnhanceOptions.AltMovieDbImageUrl?.Trim();
        }

        private void OnFileSaved(object sender, UIBaseClasses.Store.FileSavedEventArgs e)
        {
            _logger.Info("ChineseMovieDb is set to {0}", MetadataEnhanceOptions.ChineseMovieDb);
            if (_currentChineseMovieDb != MetadataEnhanceOptions.ChineseMovieDb)
            {
                _currentChineseMovieDb = MetadataEnhanceOptions.ChineseMovieDb;

                if (_currentChineseMovieDb)
                {
                    ChineseMovieDb.Patch();
                }
                else
                {
                    ChineseMovieDb.Unpatch();
                }
            }

            _logger.Info("EnhanceMovieDbPerson is set to {0}", MetadataEnhanceOptions.EnhanceMovieDbPerson);
            if (_currentEnhanceMovieDbPerson != MetadataEnhanceOptions.EnhanceMovieDbPerson)
            {
                _currentEnhanceMovieDbPerson = MetadataEnhanceOptions.EnhanceMovieDbPerson;

                if (_currentEnhanceMovieDbPerson)
                {
                    EnhanceMovieDbPerson.Patch();
                }
                else
                {
                    EnhanceMovieDbPerson.Unpatch();
                }
            }

            _logger.Info("AltMovieDbConfig is set to {0}", MetadataEnhanceOptions.AltMovieDbConfig);
            _logger.Info("AltMovieDbApiUrl is set to {0}",
                !string.IsNullOrEmpty(MetadataEnhanceOptions.AltMovieDbApiUrl)
                    ? MetadataEnhanceOptions.AltMovieDbApiUrl
                    : "NONE");
            _logger.Info("AltMovieDbImageUrl is set to {0}",
                !string.IsNullOrEmpty(MetadataEnhanceOptions.AltMovieDbImageUrl)
                    ? MetadataEnhanceOptions.AltMovieDbImageUrl
                    : "NONE");
            _logger.Info("AltMovieDbApiKey is set to {0}",
                !string.IsNullOrEmpty(MetadataEnhanceOptions.AltMovieDbApiKey)
                    ? MetadataEnhanceOptions.AltMovieDbApiKey
                    : "NONE");

            if (_currentAltMovieDbConfig != MetadataEnhanceOptions.AltMovieDbConfig)
            {
                _currentAltMovieDbConfig = MetadataEnhanceOptions.AltMovieDbConfig;

                if (_currentAltMovieDbConfig)
                {
                    AltMovieDbConfig.PatchApiUrl();
                    if (_currentAltMovieDbImageUrlEnabled) AltMovieDbConfig.PatchImageUrl();
                }
                else
                {
                    AltMovieDbConfig.UnpatchApiUrl();
                    if (_currentAltMovieDbImageUrlEnabled) AltMovieDbConfig.UnpatchImageUrl();
                }
            }

            if (_currentAltMovieDbImageUrlEnabled != !string.IsNullOrEmpty(MetadataEnhanceOptions.AltMovieDbImageUrl))
            {
                _currentAltMovieDbImageUrlEnabled = !string.IsNullOrEmpty(MetadataEnhanceOptions.AltMovieDbImageUrl);

                if (_currentAltMovieDbImageUrlEnabled)
                {
                    AltMovieDbConfig.PatchImageUrl();
                }
                else
                {
                    AltMovieDbConfig.UnpatchImageUrl();
                }
            }

            _logger.Info("PreferOriginalPoster is set to {0}", MetadataEnhanceOptions.PreferOriginalPoster);
            if (_currentPreferOriginalPoster != MetadataEnhanceOptions.PreferOriginalPoster)
            {
                _currentPreferOriginalPoster = MetadataEnhanceOptions.PreferOriginalPoster;

                if (_currentPreferOriginalPoster)
                {
                    PreferOriginalPoster.Patch();
                }
                else
                {
                    PreferOriginalPoster.Unpatch();
                }
            }

            _logger.Info("PinyinSortName is set to {0}", MetadataEnhanceOptions.PinyinSortName);
            if (_currentPinyinSortName != MetadataEnhanceOptions.PinyinSortName)
            {
                _currentPinyinSortName = MetadataEnhanceOptions.PinyinSortName;

                if (_currentPinyinSortName)
                {
                    PinyinSortName.Patch();
                }
                else
                {
                    PinyinSortName.Unpatch();
                }
            }

            _logger.Info("EnhanceNfoMetadata is set to {0}", MetadataEnhanceOptions.EnhanceNfoMetadata);
            if (_currentEnhanceNfoMetadata != MetadataEnhanceOptions.EnhanceNfoMetadata)
            {
                _currentEnhanceNfoMetadata = MetadataEnhanceOptions.EnhanceNfoMetadata;

                if (_currentEnhanceNfoMetadata)
                {
                    EnhanceNfoMetadata.Patch();
                }
                else
                {
                    EnhanceNfoMetadata.Unpatch();
                }
            }
        }
    }
}
