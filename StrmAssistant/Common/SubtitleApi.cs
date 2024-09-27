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

        public async Task UpdateExternalSubtitles(BaseItem item, MetadataRefreshOptions options,
            CancellationToken cancellationToken)
        {
            var directoryService = new DirectoryService(_logger, _fileSystem);
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
                                    item, subtitleStream, options, libraryOptions, cancellationToken
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
    }
}
