using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Controller.Providers;
using System.Collections;
using MediaBrowser.Model.Entities;

namespace StrmExtract
{
    public class ExtractTask : IScheduledTask
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILibraryMonitor _libraryMonitor;
        private readonly IMediaProbeManager _mediaProbeManager;

        public ExtractTask(ILibraryManager libraryManager, 
            ILogger logger, 
            IFileSystem fileSystem,
            ILibraryMonitor libraryMonitor,
            IMediaProbeManager prob)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _fileSystem = fileSystem;
            _libraryMonitor = libraryMonitor;
            _mediaProbeManager = prob;
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("StrmExtract - Task Execute");

            InternalItemsQuery query = new InternalItemsQuery();

            query.HasPath = true;
            query.HasContainer = false;
            query.ExcludeItemTypes = new string[] { "Folder", "CollectionFolder", "UserView", "Series", "Season", "Trailer", "Playlist", "PhotoAlbum" };

            BaseItem[] results = _libraryManager.GetItemList(query);
            _logger.Info("StrmExtract - Number of items before: " + results.Length);
            List<BaseItem> items = new List<BaseItem>();
            foreach(BaseItem item in  results)
            {
                if (!string.IsNullOrEmpty(item.Path) &&
                    item.Path.EndsWith(".strm", StringComparison.InvariantCultureIgnoreCase) &&
                    item.GetMediaStreams().FindAll(i => i.Type == MediaStreamType.Video || i.Type == MediaStreamType.Audio).Count == 0)
                {
                    items.Add(item);
                }
                else
                {
                    _logger.Info("StrmExtract - Item dropped: " + item.Name + " - " + item.Path + " - " + item.GetType() + " - " + item.GetMediaStreams().Count);
                }
            }

            _logger.Info("StrmExtract - Number of items after: " + items.Count);

            double total = items.Count;
            int current = 0;
            foreach(BaseItem item in items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.Info("StrmExtract - Task Cancelled");
                    break;
                }
                double percent_done = (current / total) * 100;
                progress.Report(percent_done);

                MetadataRefreshOptions options = new MetadataRefreshOptions(_fileSystem);
                options.EnableRemoteContentProbe = true;
                options.ReplaceAllMetadata = true;
                options.EnableThumbnailImageExtraction = false;
                options.ImageRefreshMode = MetadataRefreshMode.ValidationOnly;
                options.MetadataRefreshMode = MetadataRefreshMode.ValidationOnly;
                options.ReplaceAllImages = false;

                ItemUpdateType resp = await item.RefreshMetadata(options, cancellationToken);

                //Thread.Sleep(5000);
                current++;
                _logger.Info("StrmExtract - " + current + "/" + total + " - " + item.Path);
            }

            progress.Report(100.0);
            _logger.Info("StrmExtract - Task Complete");

            /*
            LibraryOptions lib_options = new LibraryOptions();
            List<MediaSourceInfo> sources = item.GetMediaSources(true, true, lib_options);

            _logger.Info("StrmExtract - GetMediaSources : " + sources.Count);

            MediaInfoRequest request = new MediaInfoRequest();

            MediaSourceInfo mediaSource = sources[0];
            request.MediaSource = mediaSource;

            _logger.Info("StrmExtract - GetMediaInfo");
            MediaInfo info = await _mediaProbeManager.GetMediaInfo(request, cancellationToken);

            _logger.Info("StrmExtract - Extracting Strm info " + info);

            _logger.Info("StrmExtract - Extracting Strm info : url - " + info.DirectStreamUrl);
            _logger.Info("StrmExtract - Extracting Strm info : runtime - " + info.RunTimeTicks);
            */
        }

        public string Category
        {
            get { return "Strm Extract"; }
        }

        public string Key
        {
            get { return "StrmExtractTask"; }
        }

        public string Description
        {
            get { return "Run Strm Media Info Extraction"; }
        }

        public string Name
        {
            get { return "Process Strm targets"; }
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
                {
                    new TaskTriggerInfo
                    {
                        Type = TaskTriggerInfo.TriggerDaily,
                        TimeOfDayTicks = TimeSpan.FromHours(3).Ticks,
                        MaxRuntimeTicks = TimeSpan.FromHours(24).Ticks
                    }
                };
        }
    }
}
