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

namespace StrmAssistant
{
    public class LibraryApi
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
        public static User[] AllUsers;

        public LibraryApi(ILibraryManager libraryManager,
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
            AllUsers = _userManager.GetUserList(userQuery);
        }

        public bool HasMediaStream(BaseItem item)
        {
            var mediaStreamCount = item.GetMediaStreams()
                .FindAll(i => i.Type is MediaStreamType.Video or MediaStreamType.Audio).Count;

            return mediaStreamCount > 0;
        }

        public List<BaseItem> FetchExtractQueueItems(List<BaseItem> items)
        {
            bool includeExtra = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.IncludeExtra;
            _logger.Info("Include Extra: " + includeExtra);
            bool enableImageCapture = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.EnableImageCapture;
            bool enableIntroSkip = Plugin.Instance.GetPluginOptions().IntroSkipOptions.EnableIntroSkip;

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
                    episodes = episodesMediaInfo.Concat(episodesImageCapture).DistinctBy(i => i.InternalId).ToArray();
                }
                else
                {
                    episodes = episodesMediaInfo;
                }
            }

            var favorites = FilterByFavorites(movies.Concat(episodes).ToList());

            List<BaseItem> combined;
            if (enableIntroSkip)
            {
                var episodesIntroSkip = Plugin.ChapterApi.SeasonHasIntroCredits(items.OfType<Episode>().ToList());
                combined = favorites.Concat(episodesIntroSkip).DistinctBy(i => i.InternalId).ToList();
            }
            else
            {
                combined = favorites;
            }

            var filtered = FilterUnprocessed(combined
                .Concat(includeExtra ? favorites.SelectMany(f => f.GetExtras(extraType)) : Enumerable.Empty<BaseItem>())
                .ToList());
            var results = OrderUnprocessed(filtered);

            return results;
        }

        public List<BaseItem> FetchExtractTaskItems()
        {
            var libraryIds = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.LibraryScope?.Split(',');
            var libraries = _libraryManager.GetVirtualFolders()
                .Where(f => libraryIds != null && libraryIds.Contains(f.Id)).ToList();
            var librariesWithImageCapture = libraries.Where(l =>
                l.LibraryOptions.TypeOptions.Any(t => t.ImageFetchers.Contains("Image Capture"))).ToList();
            _logger.Info("MediaInfoExtract - LibraryScope: " +
                         (libraries.Any() ? string.Join(", ", libraries.Select(l => l.Name)) : "ALL"));

            bool includeExtra = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.IncludeExtra;
            _logger.Info("Include Extra: " + includeExtra);
            bool enableImageCapture = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.EnableImageCapture;

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
                items = itemsMediaInfo.Concat(itemsImageCapture).DistinctBy(i => i.InternalId).ToArray();
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
                    extras = extrasImageCapture.Concat(extrasMediaInfo).DistinctBy(i => i.InternalId).ToArray();
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

        private List<BaseItem> FilterUnprocessed(List<BaseItem> items)
        {
            var strmOnly = Plugin.Instance.GetPluginOptions().StrmOnly;
            _logger.Info("Strm Only: " + strmOnly);

            List<BaseItem> results = new List<BaseItem>();

            foreach (var item in items)
            {
                if (strmOnly ? item.IsShortcut : true && !HasMediaStream(item))
                {
                    results.Add(item);
                }
                else if (strmOnly ? item.IsShortcut : true && !item.HasImage(ImageType.Primary))
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

        private List<BaseItem> FilterByFavorites(List<BaseItem> items)
        {
            var movies = AllUsers
                .SelectMany(u => items.OfType<Movie>()
                .Where(i => i.IsFavoriteOrLiked(u)));
            var episodes = AllUsers
                .SelectMany(u => items.OfType<Episode>()
                .GroupBy(e => e.SeriesId)
                .Where(g => g.Any(i => i.IsFavoriteOrLiked(u)) || g.First().Series.IsFavoriteOrLiked(u))
                .SelectMany(g => g)
                );
            var results = movies.Cast<BaseItem>().Concat(episodes.Cast<BaseItem>())
                .DistinctBy(i => i.InternalId).ToList();

            return results;
        }
    }
}
