using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant
{
    public class ChapterApi
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;
        private readonly ILogger _logger;

        private readonly object AudioFingerprintManager;
        private readonly MethodInfo CreateTitleFingerprint;

        private const string MarkerSuffix = "#SA";

        public ChapterApi(ILibraryManager libraryManager, IItemRepository itemRepository,IFileSystem fileSystem,
            IApplicationPaths applicationPaths,IFfmpegManager ffmpegManager,IMediaEncoder mediaEncoder,
            IMediaMountManager mediaMountManager,IJsonSerializer jsonSerializer,IServerApplicationHost serverApplicationHost)
        {
            _logger = Plugin.Instance.logger;
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;

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
            }
            catch (Exception e)
            {
                _logger.Debug("AudioFingerprintManager - Init Failed");
                _logger.Debug(e.Message);
                _logger.Debug(e.StackTrace);
            }
        }

        public bool HasIntro(BaseItem item)
        {
            return _itemRepository.GetChapters(item)
                .Any(c => c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd);
        }

        public long? GetIntroEnd(BaseItem item)
        {
            var introEnd = _itemRepository.GetChapters(item)
                .FirstOrDefault(c => c.MarkerType == MarkerType.IntroEnd);
            return introEnd?.StartPositionTicks;
        }

        public bool HasCredits(BaseItem item)
        {
            return _itemRepository.GetChapters(item)
                .Any(c => c.MarkerType is MarkerType.CreditsStart);
        }

        public long? GetCreditsStart(BaseItem item)
        {
            var creditsStart = _itemRepository.GetChapters(item)
                .FirstOrDefault(c => c.MarkerType == MarkerType.CreditsStart);
            return creditsStart?.StartPositionTicks;
        }

        public void UpdateIntro(Episode item, SessionInfo session, long introStartPositionTicks,
            long introEndPositionTicks)
        {
            var resultEpisodes = FetchEpisodes(item, MarkerType.IntroEnd);

            foreach (var episode in resultEpisodes)
            {
                var chapters = _itemRepository.GetChapters(episode);

                chapters.RemoveAll(c => c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd);

                var introStart = new ChapterInfo
                {
                    Name = MarkerType.IntroStart + MarkerSuffix,
                    MarkerType = MarkerType.IntroStart,
                    StartPositionTicks = introStartPositionTicks
                };
                chapters.Add(introStart);
                var introEnd = new ChapterInfo
                {
                    Name = MarkerType.IntroEnd + MarkerSuffix,
                    MarkerType = MarkerType.IntroEnd,
                    StartPositionTicks = introEndPositionTicks
                };
                chapters.Add(introEnd);

                chapters.Sort((c1, c2) => c1.StartPositionTicks.CompareTo(c2.StartPositionTicks));

                _itemRepository.SaveChapters(episode.InternalId, chapters);
            }

            _logger.Info("Intro marker updated by " + session.UserName + " for " +
                         item.FindSeriesName() + " - " + item.FindSeasonName() + " - " + item.Season.Path);
            var introStartTime = new TimeSpan(introStartPositionTicks).ToString(@"hh\:mm\:ss\.fff");
            _logger.Info("Intro start time: " + introStartTime);
            var introEndTime = new TimeSpan(introEndPositionTicks).ToString(@"hh\:mm\:ss\.fff");
            _logger.Info("Intro end time: " + introEndTime);
            Plugin.NotificationApi.IntroUpdateSendNotification(item, session, introStartTime, introEndTime);
        }

        public void UpdateCredits(Episode item, SessionInfo session, long creditsDurationTicks)
        {
            var resultEpisodes = FetchEpisodes(item, MarkerType.CreditsStart);

            foreach (var episode in resultEpisodes)
            {
                if (episode.RunTimeTicks.HasValue)
                {
                    if (episode.RunTimeTicks.Value - creditsDurationTicks > 0)
                    {
                        var chapters = _itemRepository.GetChapters(episode);
                        chapters.RemoveAll(c => c.MarkerType == MarkerType.CreditsStart);

                        var creditsStart = new ChapterInfo
                        {
                            Name = MarkerType.CreditsStart + MarkerSuffix,
                            MarkerType = MarkerType.CreditsStart,
                            StartPositionTicks = episode.RunTimeTicks.Value - creditsDurationTicks
                        };
                        chapters.Add(creditsStart);

                        chapters.Sort((c1, c2) => c1.StartPositionTicks.CompareTo(c2.StartPositionTicks));

                        _itemRepository.SaveChapters(episode.InternalId, chapters);
                    }
                }
            }

            _logger.Info("Credits marker updated by " + session.UserName + " for " +
                         item.FindSeriesName() + " - " + item.FindSeasonName() + " - " + item.Season.Path);
            var creditsDuration = new TimeSpan(creditsDurationTicks).ToString(@"hh\:mm\:ss\.fff");
            _logger.Info("Credits duration: " + new TimeSpan(creditsDurationTicks).ToString(@"hh\:mm\:ss\.fff"));
            Plugin.NotificationApi.CreditsUpdateSendNotification(item, session, creditsDuration);
        }

        private bool IsMarkerAddedByIntroSkip(ChapterInfo chapter)
        {
            return chapter.Name.EndsWith(MarkerSuffix);
        }

        public List<BaseItem> FetchEpisodes(BaseItem item, MarkerType markerType)
        {
            var episodesInSeasonQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Episode) },
                HasPath = true,
                MediaTypes = new[] { MediaType.Video },
                ParentIds = new[] { item.ParentId },
                OrderBy = new (string, SortOrder)[] { (ItemSortBy.IndexNumber, SortOrder.Ascending) }
            };
            var episodesInSeason =
                _libraryManager.GetItemList(episodesInSeasonQuery).ToList();

            var priorEpisodesWithoutMarkers = episodesInSeason.Where(e => e.IndexNumber < item.IndexNumber)
                .Where(e =>
                {
                    if (!Plugin.LibraryApi.HasMediaStream(e))
                    {
                        QueueManager.MediaInfoExtractItemQueue.Enqueue(e);
                        return false;
                    }

                    var chapters = _itemRepository.GetChapters(e);
                    switch (markerType)
                    {
                        case MarkerType.IntroEnd:
                            {
                                var hasIntroStart = chapters.Any(c => c.MarkerType == MarkerType.IntroStart);
                                var hasIntroEnd = chapters.Any(c => c.MarkerType == MarkerType.IntroEnd);
                                return !hasIntroStart || !hasIntroEnd;
                            }
                        case MarkerType.CreditsStart:
                            var hasCredits = chapters.Any(c => c.MarkerType == MarkerType.CreditsStart);
                            return !hasCredits;
                    }

                    return false;
                });

            var followingEpisodes = episodesInSeason.Where(e => e.IndexNumber > item.IndexNumber)
                .Where(e =>
                {
                    if (!Plugin.LibraryApi.HasMediaStream(e))
                    {
                        QueueManager.MediaInfoExtractItemQueue.Enqueue(e);
                        return false;
                    }

                    var chapters = _itemRepository.GetChapters(e);
                    switch (markerType)
                    {
                        case MarkerType.IntroEnd:
                        {
                            var hasIntroStart = chapters.Any(c =>
                                c.MarkerType == MarkerType.IntroStart && !IsMarkerAddedByIntroSkip(c));
                            var hasIntroEnd = chapters.Any(c =>
                                c.MarkerType == MarkerType.IntroEnd && !IsMarkerAddedByIntroSkip(c));
                            return !hasIntroStart || !hasIntroEnd;
                        }
                        case MarkerType.CreditsStart:
                            var hasCredits = chapters.Any(c =>
                                c.MarkerType == MarkerType.CreditsStart && !IsMarkerAddedByIntroSkip(c));
                            return !hasCredits;
                    }

                    return false;
                });

            var result = priorEpisodesWithoutMarkers.Concat(new[] { item }).Concat(followingEpisodes).ToList();
            return result;
        }

        public void RemoveSeasonIntroCreditsMarkers(Episode item, SessionInfo session)
        {
            var episodesInSeasonQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Episode) },
                HasPath = true,
                MediaTypes = new[] { MediaType.Video },
                ParentIds = new[] { item.ParentId },
                MinIndexNumber = item.IndexNumber,
                OrderBy = new (string, SortOrder)[] { (ItemSortBy.IndexNumber, SortOrder.Ascending) }
            };
            var episodesInSeason = _libraryManager.GetItemList(episodesInSeasonQuery);

            foreach (var episode in episodesInSeason)
            {
                RemoveIntroCreditsMarkers(episode);
            }

            _logger.Info("Intro and Credits markers are cleared by " + session.UserName + " since " + item.Name +
                         " in " + item.FindSeriesName() + " - " + item.FindSeasonName() + " - " + item.Season.Path);
        }

        public void RemoveIntroCreditsMarkers(BaseItem item)
        {
            var chapters = _itemRepository.GetChapters(item);
            chapters.RemoveAll(c =>
                c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd ||
                c.MarkerType == MarkerType.CreditsStart);
            _itemRepository.SaveChapters(item.InternalId, chapters);
        }

        public List<BaseItem> FetchClearTaskItems()
        {
            var libraryIds = Plugin.Instance.GetPluginOptions().IntroSkipOptions.LibraryScope
                ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToArray();
            var libraries = _libraryManager.GetVirtualFolders()
                .Where(f => libraryIds != null && libraryIds.Any()
                    ? libraryIds.Contains(f.Id)
                    : f.CollectionType == CollectionType.TvShows.ToString() || f.CollectionType is null).ToList();

            _logger.Info("IntroSkip - LibraryScope: " +
                         (libraryIds != null && libraryIds.Any()
                             ? string.Join(", ", libraries.Select(l => l.Name))
                             : "ALL"));

            var itemsIntroSkipQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Episode) },
                HasPath = true,
                MediaTypes = new[] { MediaType.Video },
                PathStartsWithAny = libraries.SelectMany(l => l.Locations).Select(ls =>
                    ls.EndsWith(Path.DirectorySeparatorChar.ToString())
                        ? ls
                        : ls + Path.DirectorySeparatorChar).ToArray()
            };

            var results = _libraryManager.GetItemList(itemsIntroSkipQuery);

            var items = new List<BaseItem>();
            foreach (var item in results)
            {
                var chapters = _itemRepository.GetChapters(item);
                if (chapters != null && chapters.Any())
                {
                    var hasMarkers = chapters.Any(c =>
                        (c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd ||
                         c.MarkerType == MarkerType.CreditsStart) && IsMarkerAddedByIntroSkip(c));
                    if (hasMarkers)
                    {
                        items.Add(item);
                    }
                }
            }
            _logger.Info("IntroSkip - Number of items: " + items.Count);

            return items;
        }

        public bool SeasonHasIntroCredits(Episode item)
        {
            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Episode) },
                HasPath = true,
                MediaTypes = new[] { MediaType.Video },
                ParentIds = new[]{ item.ParentId }
            };

            var allEpisodesInSeason = _libraryManager.GetItemList(query);

            var result = allEpisodesInSeason.Any(e =>
            {
                var chapters = _itemRepository.GetChapters(e);
                var hasIntroMarkers =
                    chapters.Any(c => c.MarkerType == MarkerType.IntroStart && IsMarkerAddedByIntroSkip(c)) &&
                    chapters.Any(c => c.MarkerType == MarkerType.IntroEnd && IsMarkerAddedByIntroSkip(c));
                var hasCreditsStart =
                    chapters.Any(c => c.MarkerType == MarkerType.CreditsStart && IsMarkerAddedByIntroSkip(c));

                return hasIntroMarkers || hasCreditsStart;
            });

            return result;
        }

        public List<Episode> SeasonHasIntroCredits(List<Episode> episodes)
        {
            var episodesInScope = episodes
                .Where(e => Plugin.PlaySessionMonitor.IsLibraryInScope(e)).ToList();

            var seasonIds = episodesInScope.Select(e => e.ParentId).Distinct().ToArray();

            var episodesQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Episode) },
                HasPath = true,
                MediaTypes = new[] { MediaType.Video },
                ParentIds = seasonIds
            };

            var groupedBySeason = _libraryManager.GetItemList(episodesQuery)
                .OfType<Episode>()
                .Where(ep => !episodesInScope.Select(e => e.InternalId).Contains(ep.InternalId))
                .GroupBy(ep => ep.ParentId);

            var resultEpisodes = new List<Episode>();

            foreach (var season in groupedBySeason)
            {
                var hasMarkers = season.Any(e =>
                {
                    var chapters = _itemRepository.GetChapters(e);

                    if (chapters != null && chapters.Any())
                    {
                        var hasIntroMarkers =
                            chapters.Any(c => c.MarkerType == MarkerType.IntroStart && IsMarkerAddedByIntroSkip(c)) &&
                            chapters.Any(c => c.MarkerType == MarkerType.IntroEnd && IsMarkerAddedByIntroSkip(c));
                        var hasCreditsMarker = chapters.Any(c =>
                            c.MarkerType == MarkerType.CreditsStart && IsMarkerAddedByIntroSkip(c));
                        return hasIntroMarkers || hasCreditsMarker;
                    }

                    return false;
                });

                if (hasMarkers)
                {
                    var episodesCanMarkers = episodesInScope.Where(e => e.ParentId == season.Key).ToList();
                    resultEpisodes.AddRange(episodesCanMarkers);
                }
            }

            return resultEpisodes;
        }

        public void PopulateIntroCredits(List<Episode> incomingEpisodes)
        {
            var episodesLatestDataQuery = new InternalItemsQuery
            {
                ItemIds = incomingEpisodes.Select(e => e.InternalId).ToArray()
            };
            var episodes = _libraryManager.GetItemList(episodesLatestDataQuery);

            var seasonIds = episodes.Select(e => e.ParentId).Distinct().ToArray();

            var episodesQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Episode) },
                HasPath = true,
                MediaTypes = new[] { MediaType.Video },
                ParentIds = seasonIds
            };

            var groupedBySeason = _libraryManager.GetItemList(episodesQuery)
                .Where(ep => !episodes.Select(e => e.InternalId).Contains(ep.InternalId))
                .OfType<Episode>()
                .GroupBy(ep => ep.ParentId);

            foreach (var season in groupedBySeason)
            {
                Episode lastIntroEpisode = null;
                Episode lastCreditsEpisode = null;

                foreach (var episode in season.Reverse())
                {
                    var chapters = _itemRepository.GetChapters(episode);

                    var hasIntroMarkers =
                        chapters.Any(c => c.MarkerType == MarkerType.IntroStart && IsMarkerAddedByIntroSkip(c)) &&
                        chapters.Any(c => c.MarkerType == MarkerType.IntroEnd && IsMarkerAddedByIntroSkip(c));

                    if (hasIntroMarkers && lastIntroEpisode == null)
                    {
                        lastIntroEpisode = episode;
                    }

                    var hasCreditsMarker =
                        chapters.Any(c => c.MarkerType == MarkerType.CreditsStart && IsMarkerAddedByIntroSkip(c));

                    if (hasCreditsMarker && lastCreditsEpisode == null && episode.RunTimeTicks.HasValue)
                    {
                        lastCreditsEpisode = episode;
                    }

                    if (lastIntroEpisode != null && lastCreditsEpisode != null)
                    {
                        break;
                    }
                }

                if (lastIntroEpisode != null)
                {
                    var introChapters = _itemRepository.GetChapters(lastIntroEpisode);
                    var introStart = introChapters.FirstOrDefault(c => c.MarkerType == MarkerType.IntroStart);
                    var introEnd = introChapters.FirstOrDefault(c => c.MarkerType == MarkerType.IntroEnd);

                    if (introStart != null && introEnd != null)
                    {
                        foreach (var episode in episodes.Where(e => e.ParentId == season.Key))
                        {
                            var chapters = _itemRepository.GetChapters(episode);
                            chapters.Add(introStart);
                            chapters.Add(introEnd);
                            chapters.Sort((c1, c2) => c1.StartPositionTicks.CompareTo(c2.StartPositionTicks));
                            _itemRepository.SaveChapters(episode.InternalId, chapters);
                            _logger.Info("Intro marker updated for " + episode.Path);
                        }
                    }
                }

                if (lastCreditsEpisode != null && lastCreditsEpisode.RunTimeTicks.HasValue)
                {
                    var lastEpisodeChapters = _itemRepository.GetChapters(lastCreditsEpisode);
                    var lastEpisodeCreditsStart = lastEpisodeChapters.FirstOrDefault(c => c.MarkerType == MarkerType.CreditsStart);

                    if (lastEpisodeCreditsStart !=null)
                    {
                        var creditsDurationTicks = lastCreditsEpisode.RunTimeTicks.Value - lastEpisodeCreditsStart.StartPositionTicks;
                        if (creditsDurationTicks > 0)
                        {
                            foreach (var episode in episodes.Where(e => e.ParentId == season.Key))
                            {
                                if (episode.RunTimeTicks.HasValue)
                                {
                                    var creditsStartTicks = episode.RunTimeTicks.Value - creditsDurationTicks;
                                    if (creditsStartTicks > 0)
                                    {
                                        var chapters = _itemRepository.GetChapters(episode);
                                        var creditsStart = new ChapterInfo
                                        {
                                            Name = MarkerType.CreditsStart + MarkerSuffix,
                                            MarkerType = MarkerType.CreditsStart,
                                            StartPositionTicks = creditsStartTicks
                                        };
                                        chapters.Add(creditsStart);
                                        chapters.Sort((c1, c2) => c1.StartPositionTicks.CompareTo(c2.StartPositionTicks));
                                        _itemRepository.SaveChapters(episode.InternalId, chapters);
                                        _logger.Info("Credits marker updated for " + episode.Path);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public List<VirtualFolderInfo> GetMarkerEnabledLibraries(bool suppressLogging)
        {
            var libraryIds = Plugin.Instance.GetPluginOptions()
                .IntroSkipOptions.MarkerEnabledLibraryScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();

            var libraries = _libraryManager.GetVirtualFolders()
                .Where(f => libraryIds != null && libraryIds.Any()
                    ? libraryIds.Contains(f.Id)
                    : f.LibraryOptions.EnableMarkerDetection &&
                      (f.CollectionType == CollectionType.TvShows.ToString() || f.CollectionType is null))
                .ToList();

            if (!suppressLogging)
            {
                _logger.Info("MarkerEnabledLibraryScope: " + (libraryIds != null && libraryIds.Any()
                    ? string.Join(", ", libraries.Select(l => l.Name))
                    : "ALL"));
            }

            return libraries;
        }

        public List<Episode> FetchIntroFingerprintTaskItems()
        {
            var libraries = UpdateLibraryIntroDetectionFingerprintLength();

            var introDetectionFingerprintMinutes = Plugin.Instance.GetPluginOptions().IntroSkipOptions.IntroDetectionFingerprintMinutes;
            
            var itemsFingerprintQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Episode) },
                Recursive = true,
                GroupByPresentationUniqueKey = false,
                WithoutChapterMarkers = new[] { MarkerType.IntroStart },
                MinRunTimeTicks = TimeSpan.FromMinutes(introDetectionFingerprintMinutes).Ticks,
                HasIntroDetectionFailure = false,
                HasAudioStream = true,
                PathStartsWithAny = libraries.SelectMany(l => l.Locations)
                    .Select(ls =>
                        ls.EndsWith(Path.DirectorySeparatorChar.ToString()) ? ls : ls + Path.DirectorySeparatorChar)
                    .ToArray()
            };

            var items = _libraryManager.GetItemList(itemsFingerprintQuery).OfType<Episode>().ToList();
            
            return items;
        }

        public List<VirtualFolderInfo> UpdateLibraryIntroDetectionFingerprintLength()
        {
            var libraries = GetMarkerEnabledLibraries(false);

            var introDetectionFingerprintMinutes = Plugin.Instance.GetPluginOptions().IntroSkipOptions.IntroDetectionFingerprintMinutes;

            foreach (var library in libraries)
            {
                library.LibraryOptions.IntroDetectionFingerprintLength = introDetectionFingerprintMinutes;
            }

            return libraries;
        }

        public async Task ExtractIntroFingerprint(Episode item, IDirectoryService directoryService,
            CancellationToken cancellationToken)
        {
            var libraryOptions = _libraryManager.GetLibraryOptions(item);

            await ((Task<Tuple<string, bool>>)CreateTitleFingerprint.Invoke(AudioFingerprintManager,
                new object[] { item, libraryOptions, directoryService, cancellationToken })).ConfigureAwait(false);
        }
    }
}
