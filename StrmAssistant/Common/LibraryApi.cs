extern alias SystemMemory;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Querying;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant
{
    public class LibraryApi
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly IUserManager _userManager;
        private readonly IMediaSourceManager _mediaSourceManager;
        private readonly ILogger _logger;

        public static MetadataRefreshOptions MediaInfoRefreshOptions;
        public static MetadataRefreshOptions ImageCaptureRefreshOptions;
        public static MetadataRefreshOptions FullRefreshOptions;

        public static ExtraType[] IncludeExtraTypes =
        {
            ExtraType.AdditionalPart, ExtraType.BehindTheScenes, ExtraType.Clip, ExtraType.DeletedScene,
            ExtraType.Interview, ExtraType.Sample, ExtraType.Scene, ExtraType.ThemeSong, ExtraType.ThemeVideo,
            ExtraType.Trailer
        };

        public static MediaContainers[] ExcludeMediaContainers =
        {
            MediaContainers.MpegTs, MediaContainers.Ts, MediaContainers.M2Ts
        };

        public static Dictionary<User, bool> AllUsers = new Dictionary<User, bool>();

        private readonly bool _fallbackProbeApproach;
        private readonly MethodInfo GetPlayackMediaSources;

        public LibraryApi(ILibraryManager libraryManager,
            IFileSystem fileSystem,
            IMediaSourceManager mediaSourceManager,
            IUserManager userManager)
        {
            _libraryManager = libraryManager;
            _logger = Plugin.Instance.logger;
            _fileSystem = fileSystem;
            _userManager = userManager;
            _mediaSourceManager = mediaSourceManager;

            FetchUsers();

            MediaInfoRefreshOptions = new MetadataRefreshOptions(_fileSystem)
            {
                EnableRemoteContentProbe = true,
                ReplaceAllMetadata = true,
                EnableThumbnailImageExtraction = false,
                EnableSubtitleDownloading = false,
                ImageRefreshMode = MetadataRefreshMode.ValidationOnly,
                MetadataRefreshMode = MetadataRefreshMode.ValidationOnly,
                ReplaceAllImages = false
            };

            ImageCaptureRefreshOptions = new MetadataRefreshOptions(_fileSystem)
            {
                EnableRemoteContentProbe = true,
                ReplaceAllMetadata = true,
                EnableThumbnailImageExtraction = false,
                EnableSubtitleDownloading = false,
                ImageRefreshMode = MetadataRefreshMode.Default,
                MetadataRefreshMode = MetadataRefreshMode.Default,
                ReplaceAllImages = true
            };

            FullRefreshOptions = new MetadataRefreshOptions(_fileSystem)
            {
                EnableRemoteContentProbe = true,
                ReplaceAllMetadata = true,
                EnableThumbnailImageExtraction = false,
                EnableSubtitleDownloading = false,
                ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllImages = true
            };

            if (Plugin.Instance.ApplicationHost.ApplicationVersion > new Version("4.9.0.14"))
            {
                try
                {
                    GetPlayackMediaSources = mediaSourceManager.GetType()
                        .GetMethod("GetPlayackMediaSources",
                            new[]
                            {
                                typeof(BaseItem), typeof(User), typeof(bool), typeof(string), typeof(bool),
                                typeof(bool), typeof(CancellationToken)
                            });
                    _fallbackProbeApproach = true;
                }
                catch (Exception e)
                {
                    _logger.Debug("GetPlayackMediaSources - Init Failed");
                    _logger.Debug(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }
        }

        public void FetchUsers()
        {
            var userQuery = new UserQuery
            {
                IsDisabled = false,
            };
            var allUsers = _userManager.GetUserList(userQuery);

            foreach (var user in allUsers)
            {
                AllUsers[user] = _userManager.GetUserById(user.InternalId).Policy.IsAdministrator;
            }
        }

        public bool HasMediaStream(BaseItem item)
        {
            var mediaStreamCount = item.GetMediaStreams()
                .FindAll(i => i.Type == MediaStreamType.Video || i.Type == MediaStreamType.Audio).Count;

            return mediaStreamCount > 0;
        }

        public List<BaseItem> FetchExtractQueueItems(List<BaseItem> items)
        {
            var libraryIds = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.LibraryScope
                ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToArray();
            var includeFavorites = libraryIds == null || !libraryIds.Any() || libraryIds.Contains("-1");
            _logger.Info("Include Favorites: " + includeFavorites);

            var includeExtra = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.IncludeExtra;
            var catchupMode=Plugin.Instance.GetPluginOptions().GeneralOptions.CatchupMode;
            var enableIntroSkip = Plugin.Instance.GetPluginOptions().IntroSkipOptions.EnableIntroSkip;

            var resultItems = new List<BaseItem>();

            if (catchupMode)
            {
                if (includeFavorites) resultItems = ExpandFavorites(items, true);

                var incomingItems = items.OfType<Movie>().Cast<BaseItem>().Concat(items.OfType<Episode>()).ToList();

                var libraryPathsInScope = _libraryManager.GetVirtualFolders()
                    .Where(f => libraryIds == null || !libraryIds.Any() || libraryIds.Contains(f.Id))
                    .SelectMany(l => l.Locations)
                    .Select(ls => ls.EndsWith(Path.DirectorySeparatorChar.ToString())
                        ? ls
                        : ls + Path.DirectorySeparatorChar)
                    .ToList();

                if (libraryIds == null || !libraryIds.Any())
                {
                    resultItems = resultItems.Concat(incomingItems).ToList();
                }

                if (libraryIds != null && libraryIds.Any(id => id != "-1") && libraryPathsInScope.Any())
                {
                    var filteredItems = incomingItems
                        .Where(i => libraryPathsInScope.Any(p => i.ContainingFolderPath.StartsWith(p)))
                        .ToList();
                    resultItems = resultItems.Concat(filteredItems).ToList();
                }
            }

            if (enableIntroSkip)
            {
                var episodesIntroSkip = Plugin.ChapterApi.SeasonHasIntroCredits(items.OfType<Episode>().ToList());
                resultItems = resultItems.Concat(episodesIntroSkip).ToList();
            }

            resultItems = resultItems.GroupBy(i => i.InternalId).Select(g => g.First()).ToList();

            var unprocessedItems = FilterUnprocessed(resultItems
                .Concat(includeExtra ? resultItems.SelectMany(f => f.GetExtras(IncludeExtraTypes)) : Enumerable.Empty<BaseItem>())
                .ToList());
            var orderedItems = OrderUnprocessed(unprocessedItems);

            return orderedItems;
        }

        public List<BaseItem> FetchExtractTaskItems()
        {
            var libraryIds = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.LibraryScope
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToArray();
            var libraries = _libraryManager.GetVirtualFolders()
                .Where(f => !libraryIds.Any() || libraryIds.Contains(f.Id)).ToList();
            var librariesWithImageCapture = libraries.Where(l =>
                l.LibraryOptions.TypeOptions.Any(t => t.ImageFetchers.Contains("Image Capture"))).ToList();

            _logger.Info("MediaInfoExtract - LibraryScope: " +
                         (libraryIds.Any() ? string.Join(", ", libraries.Select(l => l.Name)) : "ALL"));

            var includeExtra = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.IncludeExtra;
            _logger.Info("Include Extra: " + includeExtra);
            var enableImageCapture = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.EnableImageCapture;

            var favoritesWithExtra = Array.Empty<BaseItem>();
            if (libraryIds.Contains("-1"))
            {
                var favorites = AllUsers.Select(e => e.Key)
                    .SelectMany(user => _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        User = user,
                        IsFavorite = true
                    })).GroupBy(i => i.InternalId).Select(g => g.First()).ToList();

                var expanded = ExpandFavorites(favorites, false);

                favoritesWithExtra = expanded.Concat(includeExtra
                        ? expanded.SelectMany(f => f.GetExtras(IncludeExtraTypes))
                        : Enumerable.Empty<BaseItem>())
                    .ToArray();
            }

            var items = Array.Empty<BaseItem>();
            var extras = Array.Empty<BaseItem>();

            if (!libraryIds.Any() || libraryIds.Any(id => id != "-1"))
            {
                var itemsMediaInfoQuery = new InternalItemsQuery
                {
                    HasPath = true,
                    HasAudioStream = false,
                    MediaTypes = new[] { MediaType.Video, MediaType.Audio }
                };

                if (libraryIds.Any(id => id != "-1") && libraries.Any())
                {
                    itemsMediaInfoQuery.PathStartsWithAny = libraries.SelectMany(l => l.Locations).Select(ls =>
                        ls.EndsWith(Path.DirectorySeparatorChar.ToString())
                            ? ls
                            : ls + Path.DirectorySeparatorChar).ToArray();
                }

                var itemsMediaInfo = _libraryManager.GetItemList(itemsMediaInfoQuery);

                var itemsImageCaptureQuery = new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "Movie", "Episode" },
                    HasPath = true,
                    MediaTypes = new[] { MediaType.Video }
                };

                if (enableImageCapture && librariesWithImageCapture.Any())
                {
                    itemsImageCaptureQuery.PathStartsWithAny =
                        librariesWithImageCapture.SelectMany(l => l.Locations).Select(ls =>
                            ls.EndsWith(Path.DirectorySeparatorChar.ToString())
                                ? ls
                                : ls + Path.DirectorySeparatorChar).ToArray();

                    var itemsImageCapture = _libraryManager.GetItemList(itemsImageCaptureQuery)
                        .Where(i => !i.HasImage(ImageType.Primary)).ToList();
                    items = itemsMediaInfo.Concat(itemsImageCapture).GroupBy(i => i.InternalId).Select(g => g.First())
                        .ToArray();
                }
                else
                {
                    items = itemsMediaInfo;
                }

                if (includeExtra)
                {
                    itemsMediaInfoQuery.ExtraTypes = IncludeExtraTypes;
                    var extrasMediaInfo = _libraryManager.GetItemList(itemsMediaInfoQuery);

                    if (enableImageCapture && librariesWithImageCapture.Any())
                    {
                        itemsImageCaptureQuery.ExtraTypes = IncludeExtraTypes;
                        var extrasImageCapture = _libraryManager.GetItemList(itemsImageCaptureQuery);
                        extras = extrasImageCapture.Concat(extrasMediaInfo).GroupBy(i => i.InternalId)
                            .Select(g => g.First()).ToArray();
                    }
                    else
                    {
                        extras = extrasMediaInfo;
                    }
                }
            }

            var combined = favoritesWithExtra.Concat(items).Concat(extras).GroupBy(i => i.InternalId)
                .Select(g => g.First()).ToList();
            var filtered = FilterUnprocessed(combined);
            var results = OrderUnprocessed(filtered);

            return results;
        }

        public List<BaseItem> OrderUnprocessed(List<BaseItem> items)
        {
            var results = items.OrderBy(i => i.ExtraType == null ? 0 : 1)
                .ThenByDescending(i =>
                    i is Episode e && e.PremiereDate == DateTimeOffset.MinValue ? e.Series.PremiereDate :
                    i.ExtraType != null ? i.DateCreated : i.PremiereDate)
                .ThenByDescending(i => i.IndexNumber)
                .ToList();
            return results;
        }

        private List<BaseItem> FilterUnprocessed(List<BaseItem> items)
        {
            var strmOnly = Plugin.Instance.GetPluginOptions().GeneralOptions.StrmOnly;
            _logger.Info("Strm Only: " + strmOnly);

            var results = new List<BaseItem>();

            foreach (var item in items)
            {
                if ((!strmOnly || item.IsShortcut) && (!HasMediaStream(item) || !item.HasImage(ImageType.Primary) &&
                        !(HasMediaStream(item) && item.MediaContainer.HasValue &&
                          ExcludeMediaContainers.Contains(item.MediaContainer.Value))))
                {
                    results.Add(item);
                }
                else
                {
                    _logger.Debug("MediaInfoExtract - Item dropped: " + item.Name + " - " + item.Path);
                }
            }

            _logger.Info("MediaInfoExtract - Number of items: " + results.Count);

            return results;
        }

        public List<BaseItem> ExpandFavorites(List<BaseItem> items, bool filterNeeded)
        {
            var enableImageCapture = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.EnableImageCapture;

            var movies = items.OfType<Movie>().Cast<BaseItem>().ToList();

            var seriesIds = items.OfType<Series>().Select(s => s.InternalId)
                .Union(items.OfType<Episode>().Select(e => e.SeriesId)).ToArray();

            var episodes = Array.Empty<BaseItem>();
            if (seriesIds.Length > 0)
            {
                var episodesMediaInfoQuery = new InternalItemsQuery
                {
                    HasPath = true,
                    HasAudioStream = false,
                    MediaTypes = new[] { MediaType.Video },
                    Recursive = true,
                    AncestorIds = seriesIds
                };
                var episodesMediaInfo = _libraryManager.GetItemList(episodesMediaInfoQuery);

                if (enableImageCapture)
                {
                    var episodesImageCaptureQuery = new InternalItemsQuery
                    {
                        HasPath = true,
                        MediaTypes = new[] { MediaType.Video },
                        Recursive = true,
                        AncestorIds = seriesIds
                    };
                    var episodesImageCapture = _libraryManager.GetItemList(episodesImageCaptureQuery)
                        .Where(i => !i.HasImage(ImageType.Primary)).ToList();
                    episodes = episodesMediaInfo.Concat(episodesImageCapture).GroupBy(i => i.InternalId)
                        .Select(g => g.First()).ToArray();
                }
                else
                {
                    episodes = episodesMediaInfo;
                }
            }

            var combined = movies.Concat(episodes).ToList();
            return filterNeeded ? FilterByFavorites(combined) : combined;
        }

        private List<BaseItem> FilterByFavorites(List<BaseItem> items)
        {
            var movies = AllUsers.Select(e => e.Key)
                .SelectMany(u => items.OfType<Movie>()
                .Where(i => i.IsFavoriteOrLiked(u)));
            var episodes = AllUsers.Select(e => e.Key)
                .SelectMany(u => items.OfType<Episode>()
                .GroupBy(e => e.SeriesId)
                .Where(g => g.Any(i => i.IsFavoriteOrLiked(u)) || g.First().Series.IsFavoriteOrLiked(u))
                .SelectMany(g => g)
                );
            var results = movies.Cast<BaseItem>().Concat(episodes.Cast<BaseItem>())
                .GroupBy(i => i.InternalId).Select(g => g.First()).ToList();

            return results;
        }

        public List<User> GetUsersByFavorites(BaseItem item)
        {
            var users = AllUsers.Select(e=>e.Key).Where(u =>
                (item is Movie || item is Series) && item.IsFavoriteOrLiked(u) ||
                item is Episode e && (e.IsFavoriteOrLiked(u) ||
                                            (e.Series != null && e.Series.IsFavoriteOrLiked(u)))
            ).ToList();

            return users;
        }
        
        public bool HasFileChanged(BaseItem item)
        {
            if (item.IsFileProtocol)
            {
                var directoryService = new DirectoryService(_logger, _fileSystem);
                var file = directoryService.GetFile(item.Path);
                if (file != null && item.HasDateModifiedChanged(file.LastWriteTimeUtc))
                    return true;
            }

            return false;
        }

        public async Task ProbeMediaInfo(BaseItem item, CancellationToken cancellationToken)
        {
            var probeMediaSources = item.GetMediaSources(true, true, _libraryManager.GetLibraryOptions(item));

            if (!_fallbackProbeApproach)
            {
                await Task.WhenAll(probeMediaSources.Select(async probeMediaSource =>
                {
                    var resultMediaSources = await _mediaSourceManager
                        .GetPlayackMediaSources(item, null, true, probeMediaSource.Id, true, cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var resultMediaSource in resultMediaSources)
                    {
                        resultMediaSource.Container = StreamBuilder.NormalizeMediaSourceFormatIntoSingleContainer(
                            SystemMemory::System.MemoryExtensions.AsSpan(resultMediaSource.Container),
                            SystemMemory::System.MemoryExtensions.AsSpan(resultMediaSource.Path), null, DlnaProfileType.Video);
                    }
                }));
            }
            else
            {
                await Task.WhenAll(probeMediaSources.Select(async probeMediaSource =>
                {
                    var resultMediaSources = await ((Task<List<MediaSourceInfo>>)GetPlayackMediaSources.Invoke(
                            _mediaSourceManager,
                            new object[] { item, null, true, probeMediaSource.Id, true, false, cancellationToken }))
                        .ConfigureAwait(false);

                    foreach (var resultMediaSource in resultMediaSources)
                    {
                        resultMediaSource.Container = StreamBuilder.NormalizeMediaSourceFormatIntoSingleContainer(
                            SystemMemory::System.MemoryExtensions.AsSpan(resultMediaSource.Container),
                            SystemMemory::System.MemoryExtensions.AsSpan(resultMediaSource.Path), null, DlnaProfileType.Video);
                    }
                }));
            }
        }
    }
}
