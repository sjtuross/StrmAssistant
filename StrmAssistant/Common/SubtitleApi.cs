using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant
{
    public class SubtitleApi
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly IItemRepository _itemRepository;

        private readonly object SubtitleResolver;
        private readonly MethodInfo GetExternalSubtitleFiles;
        private readonly MethodInfo GetExternalSubtitleStreams;
        private readonly object FFProbeSubtitleInfo;
        private readonly MethodInfo UpdateExternalSubtitleStream;

        private static readonly HashSet<string> ProbeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".sub", ".smi", ".sami", ".mpl" };

        public SubtitleApi(ILibraryManager libraryManager,
            IFileSystem fileSystem,
            IMediaProbeManager mediaProbeManager,
            ILocalizationManager localizationManager,
            IItemRepository itemRepository)
        {
            _logger = Plugin.Instance.logger;
            _libraryManager = libraryManager;
            _fileSystem = fileSystem;
            _itemRepository= itemRepository;

            try
            {
                var embyProviders = Assembly.Load("Emby.Providers");
                var subtitleResolverType = embyProviders.GetType("Emby.Providers.MediaInfo.SubtitleResolver");
                var subtitleResolverConstructor = subtitleResolverType.GetConstructor(new[]
                {
                    typeof(ILocalizationManager), typeof(IFileSystem), typeof(ILibraryManager)
                });
                SubtitleResolver = subtitleResolverConstructor?.Invoke(new object[]
                    { localizationManager, fileSystem, libraryManager });
                GetExternalSubtitleFiles = subtitleResolverType.GetMethod("GetExternalSubtitleFiles");
                GetExternalSubtitleStreams = subtitleResolverType.GetMethod("GetExternalSubtitleStreams");

                var ffProbeSubtitleInfoType = embyProviders.GetType("Emby.Providers.MediaInfo.FFProbeSubtitleInfo");
                var ffProbeSubtitleInfoConstructor = ffProbeSubtitleInfoType.GetConstructor(new[]
                {
                    typeof(IMediaProbeManager)
                });
                FFProbeSubtitleInfo = ffProbeSubtitleInfoConstructor?.Invoke(new object[] { mediaProbeManager });
                UpdateExternalSubtitleStream = ffProbeSubtitleInfoType.GetMethod("UpdateExternalSubtitleStream");
            }
            catch (Exception e)
            {
                _logger.Warn("ExternalSubtitle - Init Failed");
                _logger.Debug(e.Message);
                _logger.Debug(e.StackTrace);
            }
        }

        public bool HasExternalSubtitleChanged(BaseItem item)
        {
            var directoryService = new DirectoryService(_logger, _fileSystem);
            var currentExternalSubtitleFiles = _libraryManager.GetExternalSubtitleFiles(item.InternalId);
            var namingOptions = _libraryManager.GetNamingOptions();

            if (GetExternalSubtitleFiles.Invoke(SubtitleResolver,
                        new object[] { item, directoryService, namingOptions, false }) is List<string>
                    newExternalSubtitleFiles &&
                !currentExternalSubtitleFiles.SequenceEqual(newExternalSubtitleFiles, StringComparer.Ordinal))
            {
                return true;
            }
            return false;
        }

        public async Task UpdateExternalSubtitles(BaseItem item, CancellationToken cancellationToken)
        {
            var directoryService = new DirectoryService(_logger, _fileSystem);
            var refreshOptions = LibraryApi.MediaInfoRefreshOptions;
            var namingOptions = _libraryManager.GetNamingOptions();
            var libraryOptions = _libraryManager.GetLibraryOptions(item);
            var currentStreams = item.GetMediaStreams()
                .FindAll(i =>
                    !(i.IsExternal && i.Type == MediaStreamType.Subtitle && i.Protocol == MediaProtocol.File));
            var startIndex = currentStreams.Count == 0 ? 0 : currentStreams.Max(i => i.Index) + 1;

            if (GetExternalSubtitleStreams.Invoke(SubtitleResolver,
                    new object[] { item, startIndex, directoryService, namingOptions, false }) is List<MediaStream> externalSubtitleStreams)
            {
                foreach (var subtitleStream in externalSubtitleStreams)
                {
                    var extension = Path.GetExtension(subtitleStream.Path);
                    if (!string.IsNullOrEmpty(extension) && ProbeExtensions.Contains(extension))
                    {
                        if (UpdateExternalSubtitleStream.Invoke(FFProbeSubtitleInfo,
                                new object[]
                                {
                                    item, subtitleStream, refreshOptions, libraryOptions, cancellationToken
                                }) is Task<bool> subtitleTask && !await subtitleTask.ConfigureAwait(false))
                        {
                            _logger.Warn("No result when probing external subtitle file: {0}",
                                subtitleStream.Path);
                        }
                    }

                    _logger.Info("ExternalSubtitle - Subtitle Processed: " + subtitleStream.Path);
                }

                currentStreams.AddRange(externalSubtitleStreams);
                _itemRepository.SaveMediaStreams(item.InternalId, currentStreams, cancellationToken);
            }
        }

        public List<BaseItem> FetchScanTaskItems()
        {
            var libraryIds = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.LibraryScope
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToArray();
            var libraries = _libraryManager.GetVirtualFolders()
                .Where(f => !libraryIds.Any() || libraryIds.Contains(f.Id)).ToList();
            _logger.Info("MediaInfoExtract - LibraryScope: " +
                         (libraryIds.Any() ? string.Join(", ", libraries.Select(l => l.Name)) : "ALL"));
            var includeExtra = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.IncludeExtra;
            _logger.Info("Include Extra: " + includeExtra);

            var favoritesWithExtra = new List<BaseItem>();
            if (libraryIds.Contains("-1"))
            {
                var favorites = LibraryApi.AllUsers.Select(e => e.Key)
                    .SelectMany(user => _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        User = user,
                        IsFavorite = true
                    })).GroupBy(i => i.InternalId).Select(g => g.First()).ToList();

                var expanded = Plugin.LibraryApi.ExpandFavorites(favorites, false);

                favoritesWithExtra = expanded
                    .Concat(includeExtra
                        ? expanded.SelectMany(f => f.GetExtras(LibraryApi.IncludeExtraTypes))
                        : Enumerable.Empty<BaseItem>())
                    .Where(Plugin.LibraryApi.HasMediaStream)
                    .ToList();
            }

            var itemsWithExtras = new List<BaseItem>();
            if (!libraryIds.Any() || libraryIds.Any(id => id != "-1"))
            {
                var itemsQuery = new InternalItemsQuery
                {
                    HasPath = true,
                    HasAudioStream = true,
                    MediaTypes = new[] { MediaType.Video }
                };

                if (libraryIds.Any(id => id != "-1") && libraries.Any())
                {
                    itemsQuery.PathStartsWithAny = libraries.SelectMany(l => l.Locations).Select(ls =>
                        ls.EndsWith(Path.DirectorySeparatorChar.ToString())
                            ? ls
                            : ls + Path.DirectorySeparatorChar).ToArray();
                }

                itemsWithExtras = _libraryManager.GetItemList(itemsQuery).ToList();

                if (includeExtra)
                {
                    itemsQuery.ExtraTypes = LibraryApi.IncludeExtraTypes;
                    itemsWithExtras = _libraryManager.GetItemList(itemsQuery).Concat(itemsWithExtras).ToList();
                }
            }

            var combined = favoritesWithExtra.Concat(itemsWithExtras)
                .GroupBy(i => i.InternalId)
                .Select(g => g.First())
                .ToList();

            var results = Plugin.LibraryApi.OrderUnprocessed(combined);

            return results;
        }

        private List<BaseItem> FilterUnprocessed(List<BaseItem> items)
        {
            return items.Where(HasExternalSubtitleChanged).ToList();
        }
    }
}
