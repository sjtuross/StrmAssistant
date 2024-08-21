using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StrmExtract
{
    public class LibraryUtility
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly IUserManager _userManager;
        private readonly ILogger _logger;

        public static MetadataRefreshOptions MediaInfoRefreshOptions;
        public static MetadataRefreshOptions ImageCaptureRefreshOptions;
        public static MetadataRefreshOptions FullRefreshOptions;
        public static ExtraType[] extraType = new ExtraType[] { ExtraType.AdditionalPart,
                                                                ExtraType.BehindTheScenes,
                                                                ExtraType.Clip,
                                                                ExtraType.DeletedScene,
                                                                ExtraType.Interview,
                                                                ExtraType.Sample,
                                                                ExtraType.Scene,
                                                                ExtraType.ThemeSong,
                                                                ExtraType.ThemeVideo,
                                                                ExtraType.Trailer };
        public static User[] allUsers;

        public LibraryUtility(ILibraryManager libraryManager,
            IFileSystem fileSystem,
            IUserManager userManager)
        {
            _libraryManager = libraryManager;
            _logger = Plugin.Instance.logger;
            _fileSystem = fileSystem;
            _userManager = userManager;

            FetchUsers();

            MediaInfoRefreshOptions = new MetadataRefreshOptions(_fileSystem)
            {
                EnableRemoteContentProbe = true,
                ReplaceAllMetadata = true,
                EnableThumbnailImageExtraction = false,
                ImageRefreshMode = MetadataRefreshMode.ValidationOnly,
                MetadataRefreshMode = MetadataRefreshMode.ValidationOnly,
                ReplaceAllImages = false
            };

            ImageCaptureRefreshOptions = new MetadataRefreshOptions(_fileSystem)
            {
                EnableRemoteContentProbe = true,
                ReplaceAllMetadata = true,
                EnableThumbnailImageExtraction = true,
                ImageRefreshMode = MetadataRefreshMode.Default,
                MetadataRefreshMode = MetadataRefreshMode.Default,
                ReplaceAllImages = true
            };

            FullRefreshOptions = new MetadataRefreshOptions(_fileSystem)
            {
                EnableRemoteContentProbe = true,
                ReplaceAllMetadata = true,
                EnableThumbnailImageExtraction = true,
                ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllImages = true
            };
        }

        public void FetchUsers()
        {
            UserQuery userQuery = new UserQuery
            {
                IsDisabled = false
            };
            allUsers = _userManager.GetUserList(userQuery);
        }

        public List<BaseItem> FetchItems(List<BaseItem> items)
        {
            bool includeExtra = Plugin.Instance.GetPluginOptions().IncludeExtra;
            _logger.Info("Include Extra: " + includeExtra);
            bool enableImageCapture = Plugin.Instance.GetPluginOptions().EnableImageCapture;

            var movies = items.OfType<Movie>().Cast<BaseItem>();

            var ancestorIds = items.OfType<Series>().Select(s => s.InternalId)
                .Union(items.OfType<Episode>().Select(e => e.SeriesId)).ToArray();

            var episodes = Array.Empty<BaseItem>();
            if (ancestorIds.Length > 0)
            {
                var episodesMediaInfoQuery = new InternalItemsQuery
                {
                    HasPath = true,
                    HasAudioStream = false,
                    MediaTypes = new[] { MediaType.Video },
                    Recursive = true,
                    AncestorIds = ancestorIds
                };
                var episodesMediaInfo = _libraryManager.GetItemList(episodesMediaInfoQuery);

                if (enableImageCapture)
                {
                    var episodesImageCaptureQuery = new InternalItemsQuery
                    {
                        HasPath = true,
                        MediaTypes = new[] { MediaType.Video },
                        Recursive = true,
                        AncestorIds = ancestorIds
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

            var favorites = FilterByFavorites(movies.Concat(episodes)).ToList();
            var filtered = FilterUnprocessed(favorites
                .Concat(includeExtra ? favorites.SelectMany(f => f.GetExtras(extraType)) : Enumerable.Empty<BaseItem>())
                .ToList());
            var results = OrderUnprocessed(filtered);

            return results;
        }

        public List<BaseItem> FetchItems()
        {
            var libraryIds = Plugin.Instance.GetPluginOptions().LibraryScope?.Split(',');
            var libraries = _libraryManager.GetVirtualFolders()
                .Where(f => libraryIds != null && libraryIds.Contains(f.Id)).ToList();
            var librariesWithImageCapture = libraries.Where(l =>
                l.LibraryOptions.TypeOptions.Any(t => t.ImageFetchers.Contains("Image Capture"))).ToList();
            _logger.Info("LibraryScope: " +
                         (libraries.Any() ? string.Join(", ", libraries.Select(l => l.Name)) : "ALL"));
            
            bool includeExtra = Plugin.Instance.GetPluginOptions().IncludeExtra;
            _logger.Info("Include Extra: " + includeExtra);
            bool enableImageCapture = Plugin.Instance.GetPluginOptions().EnableImageCapture;

            var itemsMediaInfoQuery = new InternalItemsQuery
            {
                HasPath = true,
                HasAudioStream = false,
                MediaTypes = new [] { MediaType.Video, MediaType.Audio },
                PathStartsWithAny = libraries.SelectMany(l => l.Locations).ToArray()
            };
            var itemsImageCaptureQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Movie", "Episode" },
                HasPath = true,
                MediaTypes = new[] { MediaType.Video },
                PathStartsWithAny = librariesWithImageCapture.SelectMany(l => l.Locations).ToArray()
            };

            BaseItem[] items = Array.Empty<BaseItem>();
            var itemsMediaInfo = _libraryManager.GetItemList(itemsMediaInfoQuery);

            if (enableImageCapture)
            {
                var itemsImageCapture = _libraryManager.GetItemList(itemsImageCaptureQuery)
                    .Where(i => !i.HasImage(ImageType.Primary)).ToList();
                items = itemsMediaInfo.Concat(itemsImageCapture).GroupBy(i => i.InternalId).Select(g => g.First())
                    .ToArray();
            }
            else
            {
                items = itemsMediaInfo;
            }

            BaseItem[] extras = Array.Empty<BaseItem>();
            if (includeExtra)
            {
                itemsMediaInfoQuery.ExtraTypes = extraType;
                var extrasMediaInfo = _libraryManager.GetItemList(itemsMediaInfoQuery);

                if (enableImageCapture)
                {
                    itemsImageCaptureQuery.ExtraTypes = extraType;
                    var extrasImageCapture = _libraryManager.GetItemList(itemsImageCaptureQuery);
                    extras = extrasImageCapture.Concat(extrasMediaInfo).GroupBy(i => i.InternalId)
                        .Select(g => g.First()).ToArray();
                }
                else
                {
                    extras = extrasMediaInfo;
                }
            }

            var filtered = FilterUnprocessed(items.Concat(extras).ToList());
            var results = OrderUnprocessed(filtered);

            return results;
        }

        private List<BaseItem> OrderUnprocessed(List<BaseItem> items)
        {
            var results = items.OrderBy(i => i.ExtraType == null ? 0 : 1)
                .ThenByDescending(i =>
                    i is Episode e && e.PremiereDate == DateTimeOffset.MinValue ? e.Series.PremiereDate :
                    i.ExtraType != null ? i.DateCreated : i.PremiereDate)
                .ThenByDescending(i => i.IndexNumber)
                .ToList();
            return results;
        }

        private List<BaseItem> FilterUnprocessed(List<BaseItem> results)
        {
            var strmOnly = Plugin.Instance.GetPluginOptions().StrmOnly;
            _logger.Info("Strm Only: " + strmOnly);

            _logger.Info("Number of items before: " + results.Count);
            List<BaseItem> items = new List<BaseItem>();

            foreach (var item in results)
            {
                var mediaStreamCount = item.GetMediaStreams()
                    .FindAll(i => i.Type == MediaStreamType.Video || i.Type == MediaStreamType.Audio).Count;

                if (strmOnly ? item.IsShortcut : true && mediaStreamCount == 0)
                {
                    items.Add(item);
                }
                else if (strmOnly ? item.IsShortcut : true && !item.HasImage(ImageType.Primary))
                {
                    items.Add(item);
                }
                else
                {
                    _logger.Debug("Item dropped: " + item.Name + " - " + item.Path);
                }
            }

            _logger.Info("Number of items dropped: " + (results.Count - items.Count));
            _logger.Info("Number of items after: " + items.Count);
            return items;
        }

        private IEnumerable<BaseItem> FilterByFavorites(IEnumerable<BaseItem> items)
        {
            var movies = allUsers
                .SelectMany(u => items?.OfType<Movie>()
                .Where(i => i.IsFavoriteOrLiked(u)));
            var episodes = allUsers
                .SelectMany(u => items?.OfType<Episode>()
                .GroupBy(e => e.SeriesId)
                .Where(g => g.Any(i => i.IsFavoriteOrLiked(u)) || g.First().Series.IsFavoriteOrLiked(u))
                .SelectMany(g => g)
                );
            var results = movies.Cast<BaseItem>().Concat(episodes.Cast<BaseItem>())
                .GroupBy(i => i.InternalId).Select(g => g.First());
            return results;
        }
    }
}
