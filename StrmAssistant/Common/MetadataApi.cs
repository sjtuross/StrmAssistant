using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System;
using System.Linq;
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
        private readonly ILocalizationManager _localizationManager;
        private readonly IFileSystem _fileSystem;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IHttpClient _httpClient;

        public static MetadataRefreshOptions PersonRefreshOptions;
        
        private const int RequestIntervalMs = 100;
        private static long _lastRequestTicks;
        private static readonly TimeSpan CacheTime = TimeSpan.FromHours(6.0);
        private static readonly LruCache LruCache = new LruCache(20);

        public MetadataApi(ILibraryManager libraryManager, IFileSystem fileSystem,
            IServerConfigurationManager configurationManager, ILocalizationManager localizationManager,
            IJsonSerializer jsonSerializer, IHttpClient httpClient)
        {
            _logger = Plugin.Instance.logger;
            _libraryManager = libraryManager;
            _configurationManager = configurationManager;
            _localizationManager = localizationManager;
            _fileSystem = fileSystem;
            _jsonSerializer = jsonSerializer;
            _httpClient = httpClient;

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

        public string GetServerPreferredMetadataLanguage()
        {
            return _configurationManager.Configuration.PreferredMetadataLanguage;
        }

        public async Task<MetadataResult<Person>> GetPersonMetadataFromMovieDb(Person item,
            string preferredMetadataLanguage, CancellationToken cancellationToken)
        {
            var libraryOptions = _libraryManager.GetLibraryOptions(item);

            IHasLookupInfo<PersonLookupInfo> lookupItem = item;
            var lookupInfo = lookupItem.GetLookupInfo(libraryOptions);
            lookupInfo.MetadataLanguage = preferredMetadataLanguage;

            if (GetMovieDbPersonProvider() is IRemoteMetadataProvider<Person, PersonLookupInfo> provider)
            {
                return await GetMetadataFromProvider<Person, PersonLookupInfo>(provider, lookupInfo, cancellationToken)
                    .ConfigureAwait(false);
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

        public string ProcessPersonInfo(string input, bool clean)
        {
            if (IsChinese(input)) input = ConvertTraditionalToSimplified(input);

            if (clean) input = CleanPersonName(input);

            return input;
        }

        public string GetCollectionOriginalLanguage(BoxSet collection)
        {
            var children = _libraryManager.GetItemList(new InternalItemsQuery
            {
                CollectionIds = new[] { collection.InternalId }
            });

            var concatenatedTitles = string.Join("|", children.Select(c => c.OriginalTitle));

            return GetLanguageByTitle(concatenatedTitles);
        }

        public string ConvertToServerLanguage(string language)
        {
            if (string.Equals(language, "pt", StringComparison.OrdinalIgnoreCase))
                return "pt-br";
            if (string.Equals(language, "por", StringComparison.OrdinalIgnoreCase))
                return "pt";
            if (string.Equals(language, "zhtw", StringComparison.OrdinalIgnoreCase))
                return "zh-tw";
            if (string.Equals(language, "zho", StringComparison.OrdinalIgnoreCase))
                return "zh-hk";
            var languageInfo =
                _localizationManager.FindLanguageInfo(language.AsSpan());
            return languageInfo != null ? languageInfo.TwoLetterISOLanguageName : language;
        }

        public async Task<T> GetMovieDbResponse<T>(string url, string cacheKey, string cachePath,
            CancellationToken cancellationToken) where T : class
        {
            T result;

            if (!string.IsNullOrEmpty(cacheKey) && !string.IsNullOrEmpty(cachePath))
            {
                if (LruCache.TryGetFromCache(cacheKey, out result))
                {
                    return result;
                }

                var cacheFile = _fileSystem.GetFileSystemInfo(cachePath);

                if (cacheFile.Exists && DateTimeOffset.UtcNow - _fileSystem.GetLastWriteTimeUtc(cacheFile) <= CacheTime)
                {
                    result = _jsonSerializer.DeserializeFromFile<T>(cachePath);
                    LruCache.AddOrUpdateCache(cacheKey, result);
                    return result;
                }
            }

            var num = Math.Min((RequestIntervalMs * 10000 - (DateTimeOffset.UtcNow.Ticks - _lastRequestTicks)) / 10000L,
                RequestIntervalMs);
            if (num > 0L)
            {
                _logger.Debug("Throttling Tmdb by {0} ms", num);
                await Task.Delay(Convert.ToInt32(num)).ConfigureAwait(false);
            }

            _lastRequestTicks = DateTimeOffset.UtcNow.Ticks;

            var options = new HttpRequestOptions
            {
                Url = url,
                CancellationToken = cancellationToken,
                AcceptHeader = "application/json",
                BufferContent = true,
                UserAgent = Plugin.Instance.UserAgent
            };

            try
            {
                using var response = await _httpClient.SendAsync(options, "GET").ConfigureAwait(false);
                await using var contentStream = response.Content;
                result = _jsonSerializer.DeserializeFromStream<T>(contentStream);

                if (result is null) return null;

                if (!string.IsNullOrEmpty(cacheKey) && !string.IsNullOrEmpty(cachePath))
                {
                    _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(cachePath));
                    _jsonSerializer.SerializeToFile(result, cachePath);
                    LruCache.AddOrUpdateCache(cacheKey, result);
                }

                return result;
            }
            catch (Exception e)
            {
                _logger.Debug("Failed to get MovieDb response - " + e.Message);
                return null;
            }
        }

        public async Task<T> GetMovieDbResponse<T>(string url, CancellationToken cancellationToken) where T : class
        {
            return await GetMovieDbResponse<T>(url, null, null, cancellationToken);
        }
    }
}
