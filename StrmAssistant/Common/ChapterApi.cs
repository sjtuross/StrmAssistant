using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StrmAssistant
{
    public class ChapterApi
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;
        private readonly ILogger _logger;

        public ChapterApi(ILibraryManager libraryManager, IItemRepository itemRepository)
        {
            _logger = Plugin.Instance.logger;
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
        }

        public bool HasIntro(BaseItem item)
        {
            return _itemRepository.GetChapters(item)
                .Any(c => c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd);
        }
        public bool HasCredits(BaseItem item)
        {
            return _itemRepository.GetChapters(item)
                .Any(c => c.MarkerType is MarkerType.CreditsStart);
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
                    Name = "IntroStart#SA",
                    MarkerType = MarkerType.IntroStart,
                    StartPositionTicks = introStartPositionTicks
                };
                chapters.Add(introStart);
                var introEnd = new ChapterInfo
                {
                    Name = "IntroEnd#SA",
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
                        var chapters = _itemRepository.GetChapters(item);
                        chapters.RemoveAll(chapter => chapter.MarkerType == MarkerType.CreditsStart);

                        var creditsStart = new ChapterInfo
                        {
                            Name = "CreditsStart#SA",
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

        public List<BaseItem> FetchEpisodes(BaseItem item, MarkerType markerType)
        {
            var episodesInSeasonQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Episode" },
                HasPath = true,
                MediaTypes = new[] { MediaType.Video },
                ParentIds = new[] { item.ParentId }
            };
            var episodesInSeason =
                _libraryManager.GetItemList(episodesInSeasonQuery).OrderBy(e => e.IndexNumber).ToList();

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
                                var hasIntroStart = chapters.Any(chapter => chapter.MarkerType == MarkerType.IntroStart);
                                var hasIntroEnd = chapters.Any(chapter => chapter.MarkerType == MarkerType.IntroEnd);
                                return !hasIntroStart || !hasIntroEnd;
                            }
                        case MarkerType.CreditsStart:
                            var hasCredits = chapters.Any(chapter => chapter.MarkerType == MarkerType.CreditsStart);
                            return !hasCredits;
                    }

                    return false;
                });

            var followingEpisodes = episodesInSeason.Where(e => e.IndexNumber >= item.IndexNumber)
                .Where(e =>
                {
                    if (!Plugin.LibraryApi.HasMediaStream(e))
                    {
                        QueueManager.MediaInfoExtractItemQueue.Enqueue(e);
                        return false;
                    }

                    return true;
                });

            var result = priorEpisodesWithoutMarkers.Concat(followingEpisodes).ToList();
            return result;
        }

        public void RemoveSeasonIntroCreditsMarkers(Episode item, SessionInfo session)
        {
            var episodesInSeasonQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Episode" },
                HasPath = true,
                MediaTypes = new[] { MediaType.Video },
                ParentIds = new[] { item.ParentId }
            };
            var episodesInSeason = _libraryManager.GetItemList(episodesInSeasonQuery)
                .OrderBy(e => e.IndexNumber)
                .Where(e => e.IndexNumber >= item.IndexNumber);

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
                    : f.CollectionType == "tvshows" || f.CollectionType is null).ToList();

            _logger.Info("IntroSkip - LibraryScope: " +
                         (libraryIds != null && libraryIds.Any()
                             ? string.Join(", ", libraries.Select(l => l.Name))
                             : "ALL"));

            var itemsIntroSkipQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Episode" },
                HasPath = true,
                MediaTypes = new[] { MediaType.Video },
                PathStartsWithAny = libraries.SelectMany(l => l.Locations).Select(ls =>
                    ls.EndsWith(Path.DirectorySeparatorChar.ToString())
                        ? ls
                        : ls + Path.DirectorySeparatorChar).ToArray()
            };

            BaseItem[] results = _libraryManager.GetItemList(itemsIntroSkipQuery);

            List<BaseItem> items = new List<BaseItem>();
            foreach (BaseItem item in results)
            {
                var chapters = _itemRepository.GetChapters(item);
                if (chapters != null && chapters.Any())
                {
                    var hasMarkers = chapters.Any(c =>
                        (c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd ||
                         c.MarkerType == MarkerType.CreditsStart) &&
                        c.Name.EndsWith("#SA")); //only the items added by Strm Assistant
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
                IncludeItemTypes = new[] { "Episode" },
                HasPath = true,
                MediaTypes = new[] { MediaType.Video },
                ParentIds = new[]{ item.ParentId }
            };

            var allEpisodesInSeason = _libraryManager.GetItemList(query);

            bool meetsCriteria = allEpisodesInSeason
                .Any(e =>
                {
                    var chapters = _itemRepository.GetChapters(e);
                    bool hasIntroMarkers = chapters.Any(c => c.MarkerType == MarkerType.IntroStart) &&
                                           chapters.Any(c => c.MarkerType == MarkerType.IntroEnd);
                    bool hasCreditsStart = chapters.Any(c => c.MarkerType == MarkerType.CreditsStart);

                    return hasIntroMarkers || hasCreditsStart;
                });

            return meetsCriteria;
        }

        public List<Episode> SeasonHasIntroCredits(List<Episode> episodes)
        {
            var episodesInScope = episodes
                .Where(e => PlaySessionMonitor.LibraryPathsInScope.Any(p => e.ContainingFolderPath.StartsWith(p)))
                .ToList();

            var seasonIds = episodesInScope.Select(e => e.ParentId).Distinct().ToArray();

            var episodesQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Episode" },
                HasPath = true,
                MediaTypes = new[] { MediaType.Video },
                ParentIds = seasonIds,
            };

            var groupedBySeason = _libraryManager.GetItemList(episodesQuery)
                .OfType<Episode>()
                .Where(ep => !episodesInScope.Select(e => e.InternalId).Contains(ep.InternalId))
                .GroupBy(ep => ep.ParentId);

            var resultEpisodes = new List<Episode>();

            foreach (var seasonGroup in groupedBySeason)
            {
                var hasMarkers = seasonGroup.Any(e =>
                {
                    var chapters = _itemRepository.GetChapters(e);
                    if (chapters != null && chapters.Any())
                    {
                        var hasIntroMarkers = chapters.Any(c => c.MarkerType == MarkerType.IntroStart) &&
                                              chapters.Any(c => c.MarkerType == MarkerType.IntroEnd);
                        var hasCreditsMarker = chapters.Any(c => c.MarkerType == MarkerType.CreditsStart);
                        return hasIntroMarkers || hasCreditsMarker;
                    }
                    return false;
                });

                if (hasMarkers)
                {
                    var episodesCanMarkers = episodesInScope.Where(e => e.ParentId == seasonGroup.Key).ToList();
                    resultEpisodes.AddRange(episodesCanMarkers);
                }
            }

            return resultEpisodes;
        }

        public void PopulateIntroCredits(List<Episode> episodes)
        {
            var seasonIds = episodes.Select(e => e.ParentId).Distinct().ToArray();

            var episodesQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Episode" },
                HasPath = true,
                MediaTypes = new[] { MediaType.Video },
                ParentIds = seasonIds
            };

            var groupedBySeason = _libraryManager.GetItemList(episodesQuery)
                .Where(ep => !episodes.Select(e => e.InternalId).Contains(ep.InternalId))
                .OfType<Episode>()
                .GroupBy(ep => ep.ParentId);

            foreach (var seasonGroup in groupedBySeason)
            {
                Episode lastIntroEpisode = null;
                Episode lastCreditsEpisode = null;

                foreach (var episode in seasonGroup.Reverse())
                {
                    var chapters = _itemRepository.GetChapters(episode);

                    var hasIntroMarkers = chapters.Any(c => c.MarkerType == MarkerType.IntroStart) &&
                                          chapters.Any(c => c.MarkerType == MarkerType.IntroEnd);

                    if (hasIntroMarkers && lastIntroEpisode == null)
                    {
                        lastIntroEpisode = episode;
                    }

                    var hasCreditsMarker = chapters.Any(c => c.MarkerType == MarkerType.CreditsStart);

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
                        foreach (var episode in episodes.Where(e => e.ParentId == seasonGroup.Key))
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
                            foreach (var episode in episodes.Where(e => e.ParentId == seasonGroup.Key))
                            {
                                if (episode.RunTimeTicks.HasValue)
                                {
                                    var creditsStartTicks = episode.RunTimeTicks.Value - creditsDurationTicks;
                                    if (creditsStartTicks > 0)
                                    {
                                        var chapters = _itemRepository.GetChapters(episode);
                                        var creditsStart = new ChapterInfo
                                        {
                                            Name = "CreditsStart#SA",
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
    }
}
