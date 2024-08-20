using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StrmExtract
{
    public static class QueueManager
    {
        private static readonly ILogger _logger = Plugin.Instance.logger;
        private static readonly ConcurrentQueue<Func<Task>> _taskQueue = new();
        private static bool _isProcessing = false;
        private static readonly object _lock = new();
        private static DateTime lastRunTime = DateTime.MinValue;
        private static readonly TimeSpan ThrottleInterval = TimeSpan.FromSeconds(30);

        public static CancellationTokenSource _cts;
        public static SemaphoreSlim SemaphoreMaster;

        public static ConcurrentQueue<BaseItem> ItemQueue = new();

        public static void InitializeSemaphore(int maxConcurrentCount)
        {
            SemaphoreMaster = new SemaphoreSlim(maxConcurrentCount);
        }

        public static void UpdateSemaphore(int maxConcurrentCount)
        {
            var newSemaphoreMaster = new SemaphoreSlim(maxConcurrentCount);
            var oldSemaphoreMaster = SemaphoreMaster;
            SemaphoreMaster = newSemaphoreMaster;
            oldSemaphoreMaster.Dispose();
        }

        public static async Task ProcessItemQueueAsync()
        {
            _logger.Info("ProcessItemQueueAsync Started");
            _cts = new CancellationTokenSource();
            CancellationToken cancellationToken = _cts.Token;
            while (!cancellationToken.IsCancellationRequested)
            {
                var timeSinceLastRun = DateTime.UtcNow - lastRunTime;
                var remainingTime = ThrottleInterval - timeSinceLastRun;
                if (remainingTime > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(remainingTime, cancellationToken);
                    }
                    catch
                    {
                        break;
                    }                    
                }

                if (!ItemQueue.IsEmpty)
                {
                    _logger.Info("Clear Item Queue Started");
                    List<BaseItem> dequeueItems = new List<BaseItem>();
                    while (ItemQueue.TryDequeue(out var dequeueItem))
                    {
                        dequeueItems.Add(dequeueItem);
                    }
                    List<BaseItem> items = Plugin.LibraryUtility.FetchItems(dequeueItems);

                    foreach (BaseItem item in items)
                    {
                        var itemName = item.Name;
                        var itemPath = item.Path;
                        var itemHasImage = item.HasImage(ImageType.Primary);
                        var itemMediaStreamCount = item.GetMediaStreams().Count;
                        MetadataRefreshOptions refreshOptions;
                        if (itemMediaStreamCount == 0 && itemHasImage)
                        {
                            refreshOptions = LibraryUtility.MediaInfoRefreshOptions;
                        }
                        else
                        {
                            refreshOptions = LibraryUtility.ImageCaptureRefreshOptions;
                        }
                        _taskQueue.Enqueue(async () =>
                        {
                            try
                            {
                                ItemUpdateType resp = await item.RefreshMetadata(refreshOptions, cancellationToken)
                                    .ConfigureAwait(false);
                                _logger.Info("Item Processed: " + itemName + " - " + itemPath);
                            }
                            catch (TaskCanceledException)
                            {
                                _logger.Info("Item Cancelled: " + itemName + " - " + itemPath);
                            }
                            catch
                            {
                                _logger.Info("Item Failed: " + itemName + " - " + itemPath);
                            }
                        });
                    }
                    lock (_lock)
                    {
                        if (!_isProcessing)
                        {
                            _isProcessing = true;
                            var task = Task.Run(() => ProcessTaskQueueAsync(cancellationToken));
                        }
                    }
                    _logger.Info("Clear Item Queue Stopped");
                }
                lastRunTime = DateTime.UtcNow;
            }
            if (ItemQueue.IsEmpty)
            {
                _logger.Info("ProcessItemQueueAsync Stopped");
            }
            else
            {
                _logger.Info("ProcessItemQueueAsync Cancelled");
            }
        }

        private static async Task ProcessTaskQueueAsync(CancellationToken cancellationToken)
        {
            _logger.Info("ProcessTaskQueueAsync Started");
            _logger.Info("Max Concurrent Count: " + Plugin.Instance.GetPluginOptions().MaxConcurrentCount);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (_taskQueue.TryDequeue(out Func<Task> action))
                {
                    try
                    {
                        await SemaphoreMaster.WaitAsync(cancellationToken);
                    }
                    catch
                    {
                        break;
                    }

                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            await action();
                        }
                        finally
                        {
                            SemaphoreMaster.Release();
                        }
                    }, cancellationToken);
                }
                else
                {
                    break;
                }
            }

            lock (_lock)
            {
                _isProcessing = false;
                if (_taskQueue.IsEmpty)
                {
                    _logger.Info("ProcessTaskQueueAsync Stopped");
                }
                else
                {
                    _logger.Info("ProcessTaskQueueAsync Cancelled");
                }
            }
        }
    }
}
