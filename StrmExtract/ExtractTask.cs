using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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

            MetadataRefreshOptions options = new MetadataRefreshOptions(_fileSystem);
            options.EnableRemoteContentProbe = true;
            options.ReplaceAllMetadata = true;
            options.EnableThumbnailImageExtraction = false;
            options.ImageRefreshMode = MetadataRefreshMode.ValidationOnly;
            options.MetadataRefreshMode = MetadataRefreshMode.ValidationOnly;
            options.ReplaceAllImages = false;

            double total = items.Count;
            int current = 0;
            int maxConcurrentCount = Plugin.Instance.GetPluginOptions().MaxConcurrentCount;
            _logger.Info("StrmExtract - Max Concurrent Count: " + maxConcurrentCount);
            SemaphoreSlim semaphore = new SemaphoreSlim(maxConcurrentCount);
            List<Task> tasks = new List<Task>();

            foreach (BaseItem item in items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.Info("StrmExtract - Task Cancelled");
                    break;
                }

                await semaphore.WaitAsync(cancellationToken);
                var task = Task.Run(async () =>
                {
                    try
                    {
                        ItemUpdateType resp = await item.RefreshMetadata(options, cancellationToken);
                        current++;
                        double percent_done = (current / total) * 100;
                        progress.Report(percent_done);
                        _logger.Info("StrmExtract - " + current + "/" + total + " - " + item.Path);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);
                tasks.Add(task);
            }
            await Task.WhenAll(tasks);

            progress.Report(100.0);
            _logger.Info("StrmExtract - Task Complete");
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
