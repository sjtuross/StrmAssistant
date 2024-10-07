using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using StrmAssistant.Mod;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.LanguageUtility;

namespace StrmAssistant
{
    public class MetadataApi
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IServerConfigurationManager _configurationManager;

        public static MetadataRefreshOptions PersonRefreshOptions;

        public MetadataApi(ILibraryManager libraryManager, IFileSystem fileSystem,
            IServerConfigurationManager configurationManager)
        {
            _logger = Plugin.Instance.logger;
            _libraryManager = libraryManager;
            _configurationManager = configurationManager;

            PersonRefreshOptions = new MetadataRefreshOptions(fileSystem)
            {
                EnableRemoteContentProbe = false,
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllMetadata = true,
                ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllImages = true,
                IsAutomated = false,
                EnableThumbnailImageExtraction = false,
                EnableSubtitleDownloading = false,
                OverwriteLocalMetadataProviderIds = true,
                ForceSave = false
            };
        }

        public string GetPreferredMetadataLanguage(BaseItem item)
        {
            var libraryOptions = _libraryManager.GetLibraryOptions(item);

            var language = item.PreferredMetadataLanguage;
            if (string.IsNullOrEmpty(language))
            {
                language = item.GetParents().Select(i => i.PreferredMetadataLanguage).FirstOrDefault(i => !string.IsNullOrEmpty(i));
            }
            if (string.IsNullOrEmpty(language))
            {
                language = libraryOptions.PreferredMetadataLanguage;
            }
            if (string.IsNullOrEmpty(language))
            {
                language = _configurationManager.Configuration.PreferredMetadataLanguage;
            }

            return language;
        }

        public async Task<MetadataResult<Person>> GetPersonMetadataFromMovieDb(Person item,
            CancellationToken cancellationToken)
        {
            var libraryOptions = _libraryManager.GetLibraryOptions(item);
            
            IHasLookupInfo<PersonLookupInfo> lookupItem = item;
            var lookupInfo = lookupItem.GetLookupInfo(libraryOptions);
            lookupInfo.MetadataLanguage = GetPreferredMetadataLanguage(item);

            if (GetMovieDbPersonProvider() is IRemoteMetadataProvider<Person, PersonLookupInfo> provider)
            {
                return await GetMetadataFromProvider<Person, PersonLookupInfo>(provider, lookupInfo,
                    cancellationToken).ConfigureAwait(false);
            }

            return await Task.FromResult(new MetadataResult<Person>()).ConfigureAwait(false);
        }

        private IMetadataProvider GetMovieDbPersonProvider()
        {
            var metadataProviders = Plugin.Instance.ApplicationHost.GetExports<IMetadataProvider>().ToArray();
            var movieDbPersonProvider = metadataProviders
                .FirstOrDefault(provider => provider.GetType().Name == "MovieDbPersonProvider");

            return movieDbPersonProvider;
        }

        private Task<MetadataResult<TItemType>> GetMetadataFromProvider<TItemType, TIdType>(
            IRemoteMetadataProvider<TItemType, TIdType> provider,
            TIdType id, CancellationToken cancellationToken)
            where TItemType : BaseItem, IHasLookupInfo<TIdType>, new()
            where TIdType : ItemLookupInfo, new()
        {
            if (!(provider is IRemoteMetadataProviderWithOptions<TItemType, TIdType> providerWithOptions))
                return provider.GetMetadata(id, cancellationToken);
            var options = new RemoteMetadataFetchOptions<TIdType>
            {
                SearchInfo = id,
                DirectoryService = PersonRefreshOptions.DirectoryService
            };
            return providerWithOptions.GetMetadata(options, cancellationToken);
        }

        public Tuple<string, bool> UpdateAsExpected(Person item, string input)
        {
            var isJapaneseFallback = Plugin.Instance.GetPluginOptions().ModOptions.ChineseMovieDb && ChineseMovieDb
                .GetFallbackLanguages()
                .Contains("ja-jp", StringComparer.OrdinalIgnoreCase);

            if (item is null || string.Equals(Plugin.MetadataApi.GetPreferredMetadataLanguage(item), "zh-cn",
                    StringComparison.OrdinalIgnoreCase))
            {
                var convertedInput = input;
                if (IsChinese(input))
                {
                    convertedInput = ConvertTraditionalToSimplified(input);
                }

                if (IsChinese(input) || (isJapaneseFallback && IsJapanese(input)))
                {
                    return new Tuple<string, bool>(convertedInput, true);
                }

                return new Tuple<string, bool>(input, false);
            }

            return new Tuple<string, bool>(input, true);
        }

        public Tuple<string, bool> UpdateAsExpected(string input)
        {
            return UpdateAsExpected(null, input);
        }

        public string CleanPersonName(string input)
        {
            return Regex.Replace(input, @"\s+", "");
        }
    }
}
