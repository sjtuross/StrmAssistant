using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
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
        private static ConcurrentQueue<Func<Task>> _taskQueue = new ConcurrentQueue<Func<Task>>();
        private static bool _isProcessing = false;
        private static readonly object _lock = new object();
        private static DateTime lastRunTime = DateTime.MinValue;
        private static readonly TimeSpan ThrottleInterval = TimeSpan.FromSeconds(30);

        public static CancellationTokenSource _cts;
        public static SemaphoreSlim semaphore = new SemaphoreSlim(Plugin.Instance.GetPluginOptions().MaxConcurrentCount);
        public static ConcurrentQueue<BaseItem> itemQueue = new ConcurrentQueue<BaseItem>();

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

                if (!itemQueue.IsEmpty)
                {
                    _logger.Info("Clear Item Queue Started");
                    List<BaseItem> dequeueItems = new List<BaseItem>();
                    BaseItem dequeueItem;
                    while (itemQueue.TryDequeue(out dequeueItem))
                    {
                        dequeueItems.Add(dequeueItem);
                    }
                    List<BaseItem> items = Plugin.LibraryUtility.FetchItems(dequeueItems);

                    foreach (BaseItem item in items)
                    {
                        var itemName = item.Name;
                        var itemPath = item.Path;
                        _taskQueue.Enqueue(async () =>
                        {
                            try
                            {
                                ItemUpdateType resp = await item.RefreshMetadata(LibraryUtility.MediaInfoRefreshOptions,
                                    cancellationToken);
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

                        lock (_lock)
                        {
                            if (!_isProcessing)
                            {
                                _isProcessing = true;
                                var task = Task.Run(() => ProcessTaskQueueAsync(cancellationToken));
                            }
                        }
                    }
                    _logger.Info("Clear Item Queue Stopped");
                }
                lastRunTime = DateTime.UtcNow;
            }
            if (itemQueue.IsEmpty)
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
                        await semaphore.WaitAsync(cancellationToken);
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
                            semaphore.Release();
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
