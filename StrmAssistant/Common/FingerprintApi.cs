using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using StrmAssistant.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Options.Utility;

namespace StrmAssistant.Common
{
    public class FingerprintApi
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger _logger;

        private readonly object AudioFingerprintManager;
        private readonly MethodInfo CreateTitleFingerprint;
        private readonly MethodInfo GetAllFingerprintFilesForSeason;
        private readonly MethodInfo UpdateSequencesForSeason;

        public static List<string> LibraryPathsInScope;

        public FingerprintApi(ILibraryManager libraryManager, IFileSystem fileSystem,
            IApplicationPaths applicationPaths, IFfmpegManager ffmpegManager, IMediaEncoder mediaEncoder,
            IMediaMountManager mediaMountManager, IJsonSerializer jsonSerializer,
            IServerApplicationHost serverApplicationHost)
        {
            _logger = Plugin.Instance.Logger;
            _libraryManager = libraryManager;
            _fileSystem = fileSystem;

            UpdateLibraryPathsInScope();

            try
            {
                var embyProviders = Assembly.Load("Emby.Providers");
                var audioFingerprintManager = embyProviders.GetType("Emby.Providers.Markers.AudioFingerprintManager");
                var audioFingerprintManagerConstructor = audioFingerprintManager.GetConstructor(
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[]
                    {
                        typeof(IFileSystem), typeof(ILogger), typeof(IApplicationPaths), typeof(IFfmpegManager),
                        typeof(IMediaEncoder), typeof(IMediaMountManager), typeof(IJsonSerializer),
                        typeof(IServerApplicationHost)
                    }, null);
                AudioFingerprintManager = audioFingerprintManagerConstructor?.Invoke(new object[]
                {
                    fileSystem, _logger, applicationPaths, ffmpegManager, mediaEncoder, mediaMountManager,
                    jsonSerializer, serverApplicationHost
                });
                CreateTitleFingerprint = audioFingerprintManager.GetMethod("CreateTitleFingerprint",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[]
                    {
                        typeof(Episode), typeof(LibraryOptions), typeof(IDirectoryService),
                        typeof(CancellationToken)
                    }, null);
                GetAllFingerprintFilesForSeason = audioFingerprintManager.GetMethod("GetAllFingerprintFilesForSeason",
                    BindingFlags.Public | BindingFlags.Instance);
                UpdateSequencesForSeason = audioFingerprintManager.GetMethod("UpdateSequencesForSeason",
                    BindingFlags.Public | BindingFlags.Instance);
            }
            catch (Exception e)
            {
                _logger.Debug("AudioFingerprintManager - Init Failed");
                _logger.Debug(e.Message);
                _logger.Debug(e.StackTrace);
            }
        }

        public bool IsLibraryInScope(BaseItem item)
        {
            if (string.IsNullOrEmpty(item.ContainingFolderPath)) return false;

            var isLibraryInScope = LibraryPathsInScope.Any(l => item.ContainingFolderPath.StartsWith(l));

            return isLibraryInScope;
        }

        public void UpdateLibraryPathsInScope()
        {
            var libraryIds = Plugin.Instance.GetPluginOptions()
                .IntroSkipOptions.MarkerEnabledLibraryScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();

            LibraryPathsInScope = _libraryManager.GetVirtualFolders()
                .Where(f => libraryIds != null && libraryIds.Any(id => id != "-1")
                    ? libraryIds.Contains(f.Id)
                    : f.LibraryOptions.EnableMarkerDetection &&
                      (f.CollectionType == CollectionType.TvShows.ToString() || f.CollectionType is null))
                .SelectMany(l => l.Locations)
                .Select(ls => ls.EndsWith(Path.DirectorySeparatorChar.ToString())
                    ? ls
                    : ls + Path.DirectorySeparatorChar)
                .ToList();
        }

        public long[] GetAllFavoriteSeasons()
        {
            var favorites = LibraryApi.AllUsers.Select(e => e.Key)
                .SelectMany(u => _libraryManager.GetItemList(new InternalItemsQuery
                {
                    User = u,
                    IsFavorite = true,
                    IncludeItemTypes = new[] { nameof(Series), nameof(Episode) },
                    PathStartsWithAny = LibraryPathsInScope.ToArray()
                }))
                .GroupBy(i => i.InternalId)
                .Select(g => g.First())
                .ToList();

            var expanded = Plugin.LibraryApi.ExpandFavorites(favorites, false, null).OfType<Episode>();

            var result = expanded.GroupBy(e => e.ParentId).Select(g => g.Key).ToArray();

            return result;
        }

        public List<Episode> FetchFingerprintQueueItems(List<BaseItem> items)
        {
            var libraryIds = Plugin.Instance.GetPluginOptions().IntroSkipOptions.LibraryScope?
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToArray();

            var includeFavorites = libraryIds?.Contains("-1") == true;

            var resultItems = new List<Episode>();
            var incomingItems = items.OfType<Episode>().ToList();

            if (IsCatchupTaskSelected(GeneralOptions.CatchupTask.Fingerprint) && LibraryPathsInScope.Any())
            {
                if (includeFavorites)
                {
                    resultItems = Plugin.LibraryApi.ExpandFavorites(items, true, null).OfType<Episode>().ToList();
                }

                if (libraryIds is null || !libraryIds.Any() || libraryIds.Any(id => id != "-1"))
                {
                    var filteredItems = incomingItems
                        .Where(i => LibraryPathsInScope.Any(p => i.ContainingFolderPath.StartsWith(p)))
                        .ToList();
                    resultItems = resultItems.Concat(filteredItems).ToList();
                }
            }

            resultItems = resultItems.GroupBy(i => i.InternalId).Select(g => g.First()).ToList();

            return resultItems;
        }

        public List<Episode> FetchIntroFingerprintTaskItems()
        {
            UpdateLibraryIntroDetectionFingerprintLength();

            var introDetectionFingerprintMinutes =
                Plugin.Instance.GetPluginOptions().IntroSkipOptions.IntroDetectionFingerprintMinutes;
            var itemsFingerprintQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Episode) },
                Recursive = true,
                GroupByPresentationUniqueKey = false,
                WithoutChapterMarkers = new[] { MarkerType.IntroStart },
                MinRunTimeTicks = TimeSpan.FromMinutes(introDetectionFingerprintMinutes).Ticks,
                HasAudioStream = true
            };

            var markerEnabledLibraryScope =
                Plugin.Instance.GetPluginOptions().IntroSkipOptions.MarkerEnabledLibraryScope;
            if (!string.IsNullOrEmpty(markerEnabledLibraryScope) && markerEnabledLibraryScope.Contains("-1"))
            {
                itemsFingerprintQuery.ParentIds = GetAllFavoriteSeasons().DefaultIfEmpty(-1).ToArray();
            }
            else
            {
                itemsFingerprintQuery.PathStartsWithAny = LibraryPathsInScope.ToArray();
            }

            var items = _libraryManager.GetItemList(itemsFingerprintQuery).OfType<Episode>().ToList();

            return items;
        }

        public void UpdateLibraryIntroDetectionFingerprintLength()
        {
            var libraryIds = Plugin.Instance.GetPluginOptions().IntroSkipOptions.MarkerEnabledLibraryScope
                ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToArray();
            var libraries = _libraryManager.GetVirtualFolders()
                .Where(f => libraryIds != null && libraryIds.Any(id => id != "-1")
                    ? libraryIds.Contains(f.Id)
                    : f.LibraryOptions.EnableMarkerDetection &&
                      (f.CollectionType == CollectionType.TvShows.ToString() || f.CollectionType is null))
                .ToList();
            _logger.Info("MarkerEnabledLibraryScope: " +
                         (libraries.Any() ? string.Join(", ", libraries.Select(l => l.Name)) : "EMPTY"));
            
            var introDetectionFingerprintMinutes = Plugin.Instance.GetPluginOptions().IntroSkipOptions.IntroDetectionFingerprintMinutes;

            foreach (var library in libraries)
            {
                library.LibraryOptions.IntroDetectionFingerprintLength = introDetectionFingerprintMinutes;
            }
        }

        public async Task<Tuple<string, bool>> ExtractIntroFingerprint(Episode item, IDirectoryService directoryService,
            CancellationToken cancellationToken)
        {
            var libraryOptions = _libraryManager.GetLibraryOptions(item);
            var result = await ((Task<Tuple<string, bool>>)CreateTitleFingerprint.Invoke(AudioFingerprintManager,
                new object[] { item, libraryOptions, directoryService, cancellationToken })).ConfigureAwait(false);

            return result;
        }

        public async Task<Tuple<string, bool>> ExtractIntroFingerprint(Episode item, CancellationToken cancellationToken)
        {
            var directoryService = new DirectoryService(_logger, _fileSystem);

            return await ExtractIntroFingerprint(item, directoryService, cancellationToken).ConfigureAwait(false);
        }

        public async Task UpdateIntroMarkerForSeason(Season season, CancellationToken cancellationToken)
        {
            var introDetectionFingerprintMinutes =
                Plugin.Instance.GetPluginOptions().IntroSkipOptions.IntroDetectionFingerprintMinutes;

            var libraryOptions = _libraryManager.GetLibraryOptions(season);
            var directoryService = new DirectoryService(_logger, _fileSystem);

            var episodeQuery = new InternalItemsQuery
            {
                GroupByPresentationUniqueKey = false,
                EnableTotalRecordCount = false,
                MinRunTimeTicks = TimeSpan.FromMinutes(introDetectionFingerprintMinutes).Ticks,
                HasAudioStream = true
            };
            var allEpisodes = season.GetEpisodes(episodeQuery).Items.OfType<Episode>().ToArray();

            episodeQuery.WithoutChapterMarkers = new[] { MarkerType.IntroStart };
            var episodesWithoutMarkers = season.GetEpisodes(episodeQuery).Items.OfType<Episode>().ToArray();

            var task = (Task)GetAllFingerprintFilesForSeason.Invoke(AudioFingerprintManager,
                new object[] { season, allEpisodes, libraryOptions, directoryService, cancellationToken });

            await task.ConfigureAwait(false);

            var seasonFingerprintInfo = task.GetType().GetProperty("Result")?.GetValue(task);

            foreach (var episode in episodesWithoutMarkers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                UpdateSequencesForSeason.Invoke(AudioFingerprintManager,
                    new[] { season, seasonFingerprintInfo, episode, libraryOptions, directoryService });
            }
        }
    }
}
