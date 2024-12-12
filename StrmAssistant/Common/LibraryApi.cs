using Emby.Media.Common.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;
using StrmAssistant.Mod;
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
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly IUserManager _userManager;
        private readonly IMediaSourceManager _mediaSourceManager;
        private readonly IMediaMountManager _mediaMountManager;
        private readonly IItemRepository _itemRepository;
        private readonly IJsonSerializer _jsonSerializer;

        private const string MediaInfoFileExtension = "-mediainfo.json";
        
        public static MetadataRefreshOptions MediaInfoRefreshOptions;
        public static MetadataRefreshOptions ImageCaptureRefreshOptions;
        public static MetadataRefreshOptions FullRefreshOptions;

        public static ExtraType[] IncludeExtraTypes =
        {
            ExtraType.AdditionalPart, ExtraType.BehindTheScenes, ExtraType.Clip, ExtraType.DeletedScene,
            ExtraType.Interview, ExtraType.Sample, ExtraType.Scene, ExtraType.ThemeSong, ExtraType.ThemeVideo,
            ExtraType.Trailer
        };

        public static MediaContainers[] ExcludeMediaContainers
        {
            get
            {
                return Plugin.Instance.GetPluginOptions()
                    .MediaInfoExtractOptions.ImageCaptureExcludeMediaContainers
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c =>
                        Enum.TryParse<MediaContainers>(c.Trim(), true, out var container)
                            ? container
                            : (MediaContainers?)null)
                    .Where(container => container.HasValue)
                    .Select(container => container.Value)
                    .ToArray();
            }
        }

        public static string[] ExcludeMediaExtensions
        {
            get
            {
                return Plugin.Instance.GetPluginOptions()
                    .MediaInfoExtractOptions.ImageCaptureExcludeMediaContainers
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .SelectMany(c =>
                    {
                        if (Enum.TryParse<MediaContainers>(c.Trim(), true, out var container))
                        {
                            var aliases = container.GetAliases();
                            return aliases?.Where(a => !string.IsNullOrWhiteSpace(a)) ??
                                   Array.Empty<string>();
                        }

                        return Array.Empty<string>();
                    })
                    .ToArray();
            }
        }

        public static Dictionary<User, bool> AllUsers = new Dictionary<User, bool>();
        public static string[] AdminOrderedViews = Array.Empty<string>();

        private readonly bool _fallbackProbeApproach;
        private readonly MethodInfo GetPlayackMediaSources;

        internal class MediaSourceWithChapters
        {
            public MediaSourceInfo MediaSourceInfo { get; set; }
            public List<ChapterInfo> Chapters { get; set; } = new List<ChapterInfo>();
        }

        public LibraryApi(ILibraryManager libraryManager,
            IFileSystem fileSystem,
            IMediaSourceManager mediaSourceManager,
            IMediaMountManager mediaMountManager,
            IItemRepository itemRepository,
            IJsonSerializer jsonSerializer,
            IUserManager userManager)
        {
            _libraryManager = libraryManager;
            _logger = Plugin.Instance.logger;
            _fileSystem = fileSystem;
            _userManager = userManager;
            _mediaSourceManager = mediaSourceManager;
            _mediaMountManager = mediaMountManager;
            _itemRepository = itemRepository;
            _jsonSerializer = jsonSerializer;

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

            if (Plugin.Instance.ApplicationHost.ApplicationVersion >= new Version("4.9.0.25"))
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

            FetchAdminOrderedViews();
        }

        public void FetchAdminOrderedViews()
        {
            var firstAdmin = AllUsers.Where(kvp => kvp.Value).Select(u => u.Key).OrderBy(u => u.DateCreated)
                .FirstOrDefault();
            AdminOrderedViews = firstAdmin?.Configuration.OrderedViews ?? AdminOrderedViews;
        }

        public bool HasMediaInfo(BaseItem item)
        {
            if (!item.RunTimeTicks.HasValue) return false;

            var mediaStreamCount = item.GetMediaStreams()
                .FindAll(i => i.Type == MediaStreamType.Video || i.Type == MediaStreamType.Audio).Count;

            return mediaStreamCount > 0;
        }

        public bool ImageCaptureEnabled(BaseItem item)
        {
            var libraryOptions = _libraryManager.GetLibraryOptions(item);
            var typeOptions = libraryOptions.GetTypeOptions(item.GetType().Name);

            return typeOptions.ImageFetchers.Contains("Image Capture");
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
                if (includeFavorites) resultItems = ExpandFavorites(items, true, true);

                var incomingItems = items.OfType<Video>().Cast<BaseItem>().ToList();

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

        public List<BaseItem> FetchPreExtractTaskItems()
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

                var expanded = ExpandFavorites(favorites, false, true);

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

        public List<BaseItem> FetchPostExtractTaskItems(bool includeAudio)
        {
            var libraryIds = Plugin.Instance.GetPluginOptions()
                .MediaInfoExtractOptions.LibraryScope.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();
            var libraries = _libraryManager.GetVirtualFolders()
                .Where(f => !libraryIds.Any() || libraryIds.Contains(f.Id))
                .ToList();

            _logger.Info("MediaInfoExtract - LibraryScope: " +
                         (libraryIds.Any() ? string.Join(", ", libraries.Select(l => l.Name)) : "ALL"));

            var includeExtra = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.IncludeExtra;
            _logger.Info("Include Extra: " + includeExtra);

            var favoritesWithExtra = new List<BaseItem>();
            if (libraryIds.Contains("-1"))
            {
                var favorites = AllUsers.Select(e => e.Key)
                    .SelectMany(user => _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        User = user, IsFavorite = true
                    }))
                    .GroupBy(i => i.InternalId)
                    .Select(g => g.First())
                    .ToList();

                var expanded = ExpandFavorites(favorites, false, false);

                favoritesWithExtra = expanded.Concat(includeExtra
                        ? expanded.SelectMany(f => f.GetExtras(IncludeExtraTypes))
                        : Enumerable.Empty<BaseItem>())
                    .Where(HasMediaInfo)
                    .ToList();
            }

            var itemsWithExtras = new List<BaseItem>();
            if (!libraryIds.Any() || libraryIds.Any(id => id != "-1"))
            {
                var itemsQuery = new InternalItemsQuery
                {
                    HasPath = true, HasAudioStream = true,
                    MediaTypes = includeAudio ? new[] { MediaType.Video, MediaType.Audio } : new[] { MediaType.Video }
                };

                if (libraryIds.Any(id => id != "-1") && libraries.Any())
                {
                    itemsQuery.PathStartsWithAny = libraries.SelectMany(l => l.Locations)
                        .Select(ls =>
                            ls.EndsWith(Path.DirectorySeparatorChar.ToString()) ? ls : ls + Path.DirectorySeparatorChar)
                        .ToArray();
                }

                itemsWithExtras = _libraryManager.GetItemList(itemsQuery).ToList();

                if (includeExtra)
                {
                    itemsQuery.ExtraTypes = IncludeExtraTypes;
                    itemsWithExtras = _libraryManager.GetItemList(itemsQuery).Concat(itemsWithExtras).ToList();
                }
            }

            var combined = favoritesWithExtra.Concat(itemsWithExtras)
                .GroupBy(i => i.InternalId)
                .Select(g => g.First())
                .ToList();
            var results = OrderUnprocessed(combined);

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
            var results = new List<BaseItem>();

            foreach (var item in items)
            {
                if (!HasMediaInfo(item) || !item.HasImage(ImageType.Primary) &&
                    !(HasMediaInfo(item) && item.MediaContainer.HasValue &&
                      ExcludeMediaContainers.Contains(item.MediaContainer.Value)))
                {
                    results.Add(item);
                }
                else
                {
                    _logger.Debug("MediaInfoExtract - Item dropped: " + item.Name + " - " + item.Path); // video without audio
                }
            }

            _logger.Info("MediaInfoExtract - Number of items: " + results.Count);

            return results;
        }

        public List<BaseItem> ExpandFavorites(List<BaseItem> items, bool filterNeeded, bool preExtract)
        {
            var enableImageCapture = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.EnableImageCapture;

            var itemsMultiVersions = items.SelectMany(v =>
                    _libraryManager.GetItemList(new InternalItemsQuery
                        {
                            PresentationUniqueKey = v.PresentationUniqueKey
                        }))
                .ToList();

            var videos = itemsMultiVersions.OfType<Video>().Cast<BaseItem>().ToList();

            var seriesIds = itemsMultiVersions.OfType<Series>().Select(s => s.InternalId)
                .Union(itemsMultiVersions.OfType<Episode>().Select(e => e.SeriesId)).ToArray();

            var episodes = Array.Empty<BaseItem>();
            if (seriesIds.Length > 0)
            {
                var episodesMediaInfoQuery = new InternalItemsQuery
                {
                    HasPath = true,
                    HasAudioStream = !preExtract,
                    MediaTypes = new[] { MediaType.Video },
                    Recursive = true,
                    AncestorIds = seriesIds
                };
                var episodesMediaInfo = _libraryManager.GetItemList(episodesMediaInfoQuery);

                if (enableImageCapture && preExtract)
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

            var combined = videos.Concat(episodes).GroupBy(i => i.InternalId).Select(g => g.First()).ToList();

            return filterNeeded ? FilterByFavorites(combined) : combined;
        }

        private List<BaseItem> FilterByFavorites(List<BaseItem> items)
        {
            var videos = AllUsers.Select(e => e.Key)
                .SelectMany(u => items.OfType<Video>().Where(i => i.IsFavoriteOrLiked(u)));

            var episodes = AllUsers.Select(e => e.Key)
                .SelectMany(u => items.OfType<Episode>()
                    .GroupBy(e => e.SeriesId)
                    .Where(g => g.Any(i => i.IsFavoriteOrLiked(u)) || g.First().Series.IsFavoriteOrLiked(u))
                    .SelectMany(g => g));

            var results = videos.Cast<BaseItem>()
                .Concat(episodes)
                .GroupBy(i => i.InternalId)
                .Select(g => g.First())
                .ToList();

            return results;
        }

        public List<User> GetUsersByFavorites(BaseItem item)
        {
            var itemsMultiVersion = _libraryManager.GetItemList(new InternalItemsQuery
            {
                PresentationUniqueKey = item.PresentationUniqueKey
            });

            var users = AllUsers.Select(e => e.Key)
                .Where(u => itemsMultiVersion.Any(i =>
                    (i is Movie || i is Series) && i.IsFavoriteOrLiked(u) || i is Episode e &&
                    (e.IsFavoriteOrLiked(u) || (e.Series != null && e.Series.IsFavoriteOrLiked(u)))))
                .ToList();

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

        private string GetMediaInfoJsonPath(BaseItem item)
        {
            var jsonRootFolder = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.MediaInfoJsonRootFolder;

            var relativePath = item.ContainingFolderPath;
            if (!string.IsNullOrEmpty(jsonRootFolder) && Path.IsPathRooted(item.ContainingFolderPath))
            {
                relativePath = Path.GetRelativePath(Path.GetPathRoot(item.ContainingFolderPath)!,
                    item.ContainingFolderPath);
            }

            var mediaInfoJsonPath = !string.IsNullOrEmpty(jsonRootFolder)
                ? Path.Combine(jsonRootFolder, relativePath, item.FileNameWithoutExtension + MediaInfoFileExtension)
                : Path.Combine(item.ContainingFolderPath!, item.FileNameWithoutExtension + MediaInfoFileExtension);

            return mediaInfoJsonPath;
        }

        public async Task SerializeMediaInfo(BaseItem item, IDirectoryService directoryService, bool overwrite,
            CancellationToken cancellationToken)
        {
            var workItem = item;
            if (!item.RunTimeTicks.HasValue)
            {
                workItem = _libraryManager.GetItemById(item.InternalId);
            }

            var mediaInfoJsonPath = GetMediaInfoJsonPath(workItem);
            var file = directoryService.GetFile(mediaInfoJsonPath);

            if (overwrite || file?.Exists != true || HasFileChanged(workItem))
            {
                if (HasMediaInfo(workItem))
                {
                    try
                    {
                        await Task.Run(() =>
                            {
                                var options = _libraryManager.GetLibraryOptions(workItem);
                                var mediaSources = workItem.GetMediaSources(false, false, options);
                                var chapters = BaseItem.ItemRepository.GetChapters(workItem);
                                var mediaSourcesWithChapters = mediaSources.Select(mediaSource =>
                                        new MediaSourceWithChapters
                                            { MediaSourceInfo = mediaSource, Chapters = chapters })
                                    .ToList();

                                var parentDirectory = Path.GetDirectoryName(mediaInfoJsonPath);
                                if (!string.IsNullOrEmpty(parentDirectory))
                                {
                                    Directory.CreateDirectory(parentDirectory);
                                }

                                _jsonSerializer.SerializeToFile(mediaSourcesWithChapters, mediaInfoJsonPath);
                            }, cancellationToken)
                            .ConfigureAwait(false);
                        _logger.Info("MediaInfoPersist - Serialization Success: " + mediaInfoJsonPath);
                    }
                    catch (Exception e)
                    {
                        _logger.Error("MediaInfoPersist - Serialization Failed: " + mediaInfoJsonPath);
                        _logger.Error(e.Message);
                        _logger.Debug(e.StackTrace);
                    }
                }
                else
                {
                    _logger.Info("MediaInfoPersist - Serialization Skipped: " + mediaInfoJsonPath);
                }
            }
        }

        public async Task SerializeMediaInfo(BaseItem item, bool overwrite, CancellationToken cancellationToken)
        {
            var directoryService = new DirectoryService(_logger, _fileSystem);

            await SerializeMediaInfo(item, directoryService, overwrite, cancellationToken).ConfigureAwait(false);
        }

        public async Task SerializeMediaInfo(long itemId, bool overwrite, CancellationToken cancellationToken)
        {
            var item = _libraryManager.GetItemById(itemId);

            if (!HasMediaInfo(item)) return;

            var directoryService = new DirectoryService(_logger, _fileSystem);

            await SerializeMediaInfo(item, directoryService, overwrite, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> DeserializeMediaInfo(BaseItem item, IDirectoryService directoryService,
            CancellationToken cancellationToken)
        {
            var mediaInfoJsonPath = GetMediaInfoJsonPath(item);
            var file = directoryService.GetFile(mediaInfoJsonPath);

            if (file?.Exists == true && !HasMediaInfo(item))
            {
                try
                {
                    var mediaSourceWithChapters =
                        (await _jsonSerializer
                            .DeserializeFromFileAsync<List<MediaSourceWithChapters>>(mediaInfoJsonPath)
                            .ConfigureAwait(false)).ToArray()[0];

                    if (mediaSourceWithChapters.MediaSourceInfo.RunTimeTicks.HasValue && !HasFileChanged(item))
                    {
                        _itemRepository.SaveMediaStreams(item.InternalId,
                            mediaSourceWithChapters.MediaSourceInfo.MediaStreams, cancellationToken);

                        var workItem = _libraryManager.GetItemById(item.InternalId);

                        workItem.Size = mediaSourceWithChapters.MediaSourceInfo.Size.GetValueOrDefault();
                        workItem.RunTimeTicks = mediaSourceWithChapters.MediaSourceInfo.RunTimeTicks;
                        workItem.Container = mediaSourceWithChapters.MediaSourceInfo.Container;
                        workItem.TotalBitrate = mediaSourceWithChapters.MediaSourceInfo.Bitrate.GetValueOrDefault();

                        _libraryManager.UpdateItems(new List<BaseItem> { workItem }, null,
                            ItemUpdateType.MetadataImport, false, false, null, CancellationToken.None);

                        if (workItem is Video)
                        {
                            ChapterChangeTracker.BypassDeserializeInstance(workItem);
                            _itemRepository.SaveChapters(workItem.InternalId, true, mediaSourceWithChapters.Chapters);
                        }

                        _logger.Info("MediaInfoPersist - Deserialization Success: " + mediaInfoJsonPath);

                        return true;
                    }

                    _logger.Info("MediaInfoPersist - Deserialization Skipped: " + mediaInfoJsonPath);
                }
                catch (Exception e)
                {
                    _logger.Error("MediaInfoPersist - Deserialization Failed: " + mediaInfoJsonPath);
                    _logger.Error(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }

            return false;
        }

        public async Task<bool> DeserializeMediaInfo(BaseItem item, CancellationToken cancellationToken)
        {
            var directoryService = new DirectoryService(_logger, _fileSystem);

            return await DeserializeMediaInfo(item, directoryService, cancellationToken).ConfigureAwait(false);
        }

        public async Task DeleteMediaInfoJson(BaseItem item, CancellationToken cancellationToken)
        {
            var directoryService = new DirectoryService(_logger, _fileSystem);
            var mediaInfoJsonPath = GetMediaInfoJsonPath(item);
            var file = directoryService.GetFile(mediaInfoJsonPath);

            if (file?.Exists == true)
            {
                try
                {
                    await Task.Run(() => _fileSystem.DeleteFile(mediaInfoJsonPath), cancellationToken)
                        .ConfigureAwait(false);
                    _logger.Info("MediaInfoPersist - Delete Success: " + mediaInfoJsonPath);
                }
                catch (Exception e)
                {
                    _logger.Error("MediaInfoPersist - Delete Failed: " + mediaInfoJsonPath);
                    _logger.Error(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }
        }

        public async Task ProbeMediaInfo(BaseItem item, CancellationToken cancellationToken)
        {
            var options = _libraryManager.GetLibraryOptions(item);
            var probeMediaSources = item.GetMediaSources(false, false, options);

            if (!_fallbackProbeApproach)
            {
                await Task.WhenAll(probeMediaSources.Select(async pms =>
                {
                    var resultMediaSources = await _mediaSourceManager
                        .GetPlayackMediaSources(item, null, true, pms.Id, false, cancellationToken)
                        .ConfigureAwait(false);

                    var rms = resultMediaSources.FirstOrDefault(m => m.Id == pms.Id);

                    if (rms != null)
                    {
                        rms.Container =
                            StreamBuilder.NormalizeMediaSourceFormatIntoSingleContainer(rms.Container.AsSpan(),
                                rms.Path.AsSpan(), null, DlnaProfileType.Video);
                    }
                }));
            }
            else
            {
                await Task.WhenAll(probeMediaSources.Select(async pms =>
                {
                    //Method Signature:
                    //Task<List<MediaSourceInfo>> GetPlayackMediaSources(BaseItem item, User user, bool allowMediaProbe,
                    //    string probeMediaSourceId, bool enablePathSubstitution, bool fillChapters,
                    //    CancellationToken cancellationToken);
                    var resultMediaSources = await ((Task<List<MediaSourceInfo>>)GetPlayackMediaSources.Invoke(
                            _mediaSourceManager,
                            new object[] { item, null, true, pms.Id, false, true, cancellationToken }))
                        .ConfigureAwait(false);

                    var rms = resultMediaSources.FirstOrDefault(m => m.Id == pms.Id);

                    if (rms != null)
                    {
                        rms.Container = StreamBuilder.NormalizeMediaSourceFormatIntoSingleContainer(
                            rms.Container.AsSpan(),
                            rms.Path.AsSpan(), null, DlnaProfileType.Video);
                    }
                }));
            }
        }

        public async Task<string> GetStrmMountPath(string strmPath)
        {
            var path = strmPath.AsMemory();

            using var mediaMount = await _mediaMountManager.Mount(path, null, CancellationToken.None);
            
            return mediaMount?.MountedPath;
        }

        public BaseItem[] GetItemsByIds(long[] itemIds)
        {
            var items = _libraryManager.GetItemList(new InternalItemsQuery { ItemIds = itemIds });

            var dict = items.ToDictionary(i => i.InternalId, i => i);

            return itemIds.Select(id => dict[id]).ToArray();
        }

        public void UpdateSeriesPeople(Series series)
        {
            if (!series.ProviderIds.ContainsKey("Tmdb")) return;

            var seriesPeople = _libraryManager.GetItemPeople(series);

            var seasonQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { nameof(Season) },
                ParentWithPresentationUniqueKeyFromItemId = series.InternalId,
                MinIndexNumber = 1,
                OrderBy = new (string, SortOrder)[] { (ItemSortBy.IndexNumber, SortOrder.Ascending) }
            };

            var seasons = _libraryManager.GetItemList(seasonQuery);
            var peopleLists = seasons
                .Select(s => _libraryManager.GetItemPeople(s))
                .ToList();

            peopleLists.Add(seriesPeople);

            var maxPeopleCount = peopleLists.Max(seasonPeople => seasonPeople.Count);

            var combinedPeople = new List<PersonInfo>();
            var uniqueNames = new HashSet<string>();

            for (var i = 0; i < maxPeopleCount; i++)
            {
                foreach (var seasonPeople in peopleLists)
                {
                    var person = i < seasonPeople.Count ? seasonPeople[i] : null;
                    if (person != null && uniqueNames.Add(person.Name))
                    {
                        combinedPeople.Add(person);
                    }
                }
            }

            _libraryManager.UpdatePeople(series, combinedPeople);
        }
    }
}
