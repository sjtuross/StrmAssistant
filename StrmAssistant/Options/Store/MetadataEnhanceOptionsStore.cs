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
        private bool _currentMovieDbEpisodeGroup;
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
            _currentMovieDbEpisodeGroup = MetadataEnhanceOptions.MovieDbEpisodeGroup;
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

        private void OnFileSaving(object sender, FileSavingEventArgs e)
        {
            if (e.Options is MetadataEnhanceOptions options)
            {
                options.AltMovieDbApiUrl =
                    !string.IsNullOrWhiteSpace(options.AltMovieDbApiUrl)
                        ? options.AltMovieDbApiUrl.Trim().TrimEnd('/')
                        : options.AltMovieDbApiUrl?.Trim();

                options.AltMovieDbImageUrl =
                    !string.IsNullOrWhiteSpace(options.AltMovieDbImageUrl)
                        ? options.AltMovieDbImageUrl.Trim().TrimEnd('/')
                        : options.AltMovieDbImageUrl?.Trim();
            }
        }

        private void OnFileSaved(object sender, FileSavedEventArgs e)
        {
            if (e.Options is MetadataEnhanceOptions options)
            {
                _logger.Info("ChineseMovieDb is set to {0}", options.ChineseMovieDb);
                if (_currentChineseMovieDb != options.ChineseMovieDb)
                {
                    _currentChineseMovieDb = options.ChineseMovieDb;

                    if (_currentChineseMovieDb)
                    {
                        ChineseMovieDb.Patch();
                    }
                    else
                    {
                        ChineseMovieDb.Unpatch();
                    }
                }

                _logger.Info("MovieDbEpisodeGroup is set to {0}", options.MovieDbEpisodeGroup);
                if (_currentMovieDbEpisodeGroup != options.MovieDbEpisodeGroup)
                {
                    _currentMovieDbEpisodeGroup = options.MovieDbEpisodeGroup;

                    if (_currentMovieDbEpisodeGroup)
                    {
                        MovieDbEpisodeGroup.Patch();
                    }
                    else
                    {
                        MovieDbEpisodeGroup.Unpatch();
                    }
                }

                _logger.Info("EnhanceMovieDbPerson is set to {0}", options.EnhanceMovieDbPerson);
                if (_currentEnhanceMovieDbPerson != options.EnhanceMovieDbPerson)
                {
                    _currentEnhanceMovieDbPerson = options.EnhanceMovieDbPerson;

                    if (_currentEnhanceMovieDbPerson)
                    {
                        EnhanceMovieDbPerson.Patch();
                    }
                    else
                    {
                        EnhanceMovieDbPerson.Unpatch();
                    }
                }

                _logger.Info("AltMovieDbConfig is set to {0}", options.AltMovieDbConfig);
                _logger.Info("AltMovieDbApiUrl is set to {0}",
                    !string.IsNullOrEmpty(options.AltMovieDbApiUrl)
                        ? options.AltMovieDbApiUrl
                        : "NONE");
                _logger.Info("AltMovieDbImageUrl is set to {0}",
                    !string.IsNullOrEmpty(options.AltMovieDbImageUrl)
                        ? options.AltMovieDbImageUrl
                        : "NONE");
                _logger.Info("AltMovieDbApiKey is set to {0}",
                    !string.IsNullOrEmpty(options.AltMovieDbApiKey)
                        ? options.AltMovieDbApiKey
                        : "NONE");

                if (_currentAltMovieDbConfig != options.AltMovieDbConfig)
                {
                    _currentAltMovieDbConfig = options.AltMovieDbConfig;

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

                if (_currentAltMovieDbImageUrlEnabled !=
                    !string.IsNullOrEmpty(options.AltMovieDbImageUrl))
                {
                    _currentAltMovieDbImageUrlEnabled =
                        !string.IsNullOrEmpty(options.AltMovieDbImageUrl);

                    if (_currentAltMovieDbImageUrlEnabled)
                    {
                        AltMovieDbConfig.PatchImageUrl();
                    }
                    else
                    {
                        AltMovieDbConfig.UnpatchImageUrl();
                    }
                }

                _logger.Info("PreferOriginalPoster is set to {0}", options.PreferOriginalPoster);
                if (_currentPreferOriginalPoster != options.PreferOriginalPoster)
                {
                    _currentPreferOriginalPoster = options.PreferOriginalPoster;

                    if (_currentPreferOriginalPoster)
                    {
                        PreferOriginalPoster.Patch();
                    }
                    else
                    {
                        PreferOriginalPoster.Unpatch();
                    }
                }

                _logger.Info("PinyinSortName is set to {0}", options.PinyinSortName);
                if (_currentPinyinSortName != options.PinyinSortName)
                {
                    _currentPinyinSortName = options.PinyinSortName;

                    if (_currentPinyinSortName)
                    {
                        PinyinSortName.Patch();
                    }
                    else
                    {
                        PinyinSortName.Unpatch();
                    }
                }

                _logger.Info("EnhanceNfoMetadata is set to {0}", options.EnhanceNfoMetadata);
                if (_currentEnhanceNfoMetadata != options.EnhanceNfoMetadata)
                {
                    _currentEnhanceNfoMetadata = options.EnhanceNfoMetadata;

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
}
