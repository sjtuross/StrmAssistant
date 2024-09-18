using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using StrmAssistant.Mod;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant
{
    public static class QueueManager
    {
        private static ILogger _logger;
        private static readonly ConcurrentQueue<Func<Task>> _taskQueue = new ConcurrentQueue<Func<Task>>();
        private static bool _isProcessing = false;
        private static readonly object _lock = new object();
        private static DateTime _mediaInfoExtractLastRunTime = DateTime.MinValue;
        private static DateTime _introSkipLastRunTime = DateTime.MinValue;
        private static readonly TimeSpan ThrottleInterval = TimeSpan.FromSeconds(30);
        private static int _currentMaxConcurrentCount;

        public static CancellationTokenSource MediaInfoExtractTokenSource;
        public static CancellationTokenSource IntroSkipTokenSource;
        public static SemaphoreSlim SemaphoreMaster;
        public static ConcurrentQueue<BaseItem> MediaInfoExtractItemQueue = new ConcurrentQueue<BaseItem>();
        public static ConcurrentQueue<Episode> IntroSkipItemQueue = new ConcurrentQueue<Episode>();
        public static Task MediaInfoExtractProcessTask;

        public static void Initialize()
        {
            _logger = Plugin.Instance.logger;
            _currentMaxConcurrentCount = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.MaxConcurrentCount;
            SemaphoreMaster = new SemaphoreSlim(_currentMaxConcurrentCount);
        }

        public static void UpdateSemaphore(int maxConcurrentCount)
        {
            if (_currentMaxConcurrentCount != maxConcurrentCount)
            {
                _currentMaxConcurrentCount = maxConcurrentCount;
                var newSemaphoreMaster = new SemaphoreSlim(maxConcurrentCount);
                var oldSemaphoreMaster = SemaphoreMaster;
                SemaphoreMaster = newSemaphoreMaster;
                oldSemaphoreMaster.Dispose();
            }
        }

        public static async Task MediaInfoExtract_ProcessItemQueueAsync()
        {
            _logger.Info("MediaInfoExtract - ProcessItemQueueAsync Started");
            MediaInfoExtractTokenSource = new CancellationTokenSource();
            var cancellationToken = MediaInfoExtractTokenSource.Token;
            while (!cancellationToken.IsCancellationRequested)
            {
                var timeSinceLastRun = DateTime.UtcNow - _mediaInfoExtractLastRunTime;
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

                if (!MediaInfoExtractItemQueue.IsEmpty)
                {
                    _logger.Info("MediaInfoExtract - Clear Item Queue Started");
                    var enableImageCapture = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.EnableImageCapture;
                    _logger.Info("Image Capture Enabled: " + enableImageCapture);
                    var exclusiveExtract = Plugin.Instance.GetPluginOptions().ModOptions.ExclusiveExtract;
                    var enableIntroSkip = Plugin.Instance.GetPluginOptions().IntroSkipOptions.EnableIntroSkip;
                    _logger.Info("Intro Skip Enabled: " + enableIntroSkip);

                    var dequeueItems = new List<BaseItem>();
                    while (MediaInfoExtractItemQueue.TryDequeue(out var dequeueItem))
                    {
                        dequeueItems.Add(dequeueItem);
                    }

                    var dedupQueueItems = dequeueItems.GroupBy(i => i.InternalId).Select(g => g.First()).ToList();
                    var items = Plugin.LibraryApi.FetchExtractQueueItems(dedupQueueItems);

                    foreach (var item in items)
                    {
                        var taskItem = item;
                        _taskQueue.Enqueue(async () =>
                        {
                            var isShortcutPatched = false;
                            var isExtractAllowed = false;

                            try
                            {
                                if (exclusiveExtract)
                                {
                                    ExclusiveExtract.AllowExtractInstance(taskItem);
                                    isExtractAllowed = true;
                                }

                                if (enableImageCapture && !taskItem.HasImage(ImageType.Primary))
                                {
                                    if (taskItem.IsShortcut)
                                    {
                                        EnableImageCapture.PatchInstanceIsShortcut(taskItem);
                                        isShortcutPatched = true;
                                    }
                                    var refreshOptions = LibraryApi.ImageCaptureRefreshOptions;
                                    await item.RefreshMetadata(refreshOptions, cancellationToken).ConfigureAwait(false);
                                }
                                else
                                {
                                    await Plugin.LibraryApi.ProbeMediaInfo(taskItem, cancellationToken)
                                        .ConfigureAwait(false);
                                }

                                _logger.Info("MediaInfoExtract - Item Processed: " + taskItem.Name + " - " + taskItem.Path);

                                if (enableIntroSkip && taskItem is Episode episode)
                                {
                                    IntroSkipItemQueue.Enqueue(episode);
                                }
                            }
                            catch (TaskCanceledException)
                            {
                                _logger.Info("MediaInfoExtract - Item Cancelled: " + taskItem.Name + " - " + taskItem.Path);
                            }
                            catch
                            {
                                _logger.Info("MediaInfoExtract - Item Failed: " + taskItem.Name + " - " + taskItem.Path);
                            }
                            finally
                            {
                                if (isShortcutPatched) EnableImageCapture.UnpatchInstanceIsShortcut(taskItem);
                                if (isExtractAllowed) ExclusiveExtract.DisallowExtractInstance(taskItem);
                            }
                        });
                    }
                    lock (_lock)
                    {
                        if (!_isProcessing)
                        {
                            _isProcessing = true;
                            var task = Task.Run(() => MediaInfoExtract_ProcessTaskQueueAsync(cancellationToken));
                        }
                    }
                    _logger.Info("MediaInfoExtract - Clear Item Queue Stopped");
                }
                _mediaInfoExtractLastRunTime = DateTime.UtcNow;
            }
            if (MediaInfoExtractItemQueue.IsEmpty)
            {
                _logger.Info("MediaInfoExtract - ProcessItemQueueAsync Stopped");
            }
            else
            {
                _logger.Info("MediaInfoExtract - ProcessItemQueueAsync Cancelled");
            }
        }

        private static async Task MediaInfoExtract_ProcessTaskQueueAsync(CancellationToken cancellationToken)
        {
            _logger.Info("MediaInfoExtract - ProcessTaskQueueAsync Started");
            _logger.Info("Max Concurrent Count: " + Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.MaxConcurrentCount);

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
                    _logger.Info("MediaInfoExtract - ProcessTaskQueueAsync Stopped");
                }
                else
                {
                    _logger.Info("MediaInfoExtract - ProcessTaskQueueAsync Cancelled");
                }
            }
        }

        public static async Task IntroSkip_ProcessItemQueueAsync()
        {
            _logger.Info("IntroSkip - ProcessItemQueueAsync Started");
            IntroSkipTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = IntroSkipTokenSource.Token;
            while (!cancellationToken.IsCancellationRequested)
            {
                var timeSinceLastRun = DateTime.UtcNow - _introSkipLastRunTime;
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

                if (!IntroSkipItemQueue.IsEmpty)
                {
                    Plugin.Instance.logger.Info("IntroSkip - Clear Item Queue Started");

                    List<Episode> dequeueItems = new List<Episode>();
                    while (IntroSkipItemQueue.TryDequeue(out var dequeueItem))
                    {
                        dequeueItems.Add(dequeueItem);
                    }
                    Plugin.ChapterApi.PopulateIntroCredits(dequeueItems);

                    _logger.Info("IntroSkip - Clear Item Queue Stopped");
                }
                _introSkipLastRunTime = DateTime.UtcNow;
            }
            if (IntroSkipItemQueue.IsEmpty)
            {
                _logger.Info("IntroSkip - ProcessItemQueueAsync Stopped");
            }
            else
            {
                _logger.Info("IntroSkip - ProcessItemQueueAsync Cancelled");
            }
        }
    }
}
