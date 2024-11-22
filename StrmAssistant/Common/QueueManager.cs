using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StrmAssistant.Mod;

namespace StrmAssistant.Common
{
    public static class QueueManager
    {
        private static ILogger _logger;
        private static readonly ConcurrentQueue<Func<Task>> _taskQueue = new ConcurrentQueue<Func<Task>>();
        private static bool _isProcessing = false;
        private static readonly object _lock = new object();
        private static DateTime _masterProcessLastRunTime = DateTime.MinValue;
        private static DateTime _introSkipProcessLastRunTime = DateTime.MinValue;
        private static readonly TimeSpan ThrottleInterval = TimeSpan.FromSeconds(30);
        private static int _currentMaxConcurrentCount;

        public static CancellationTokenSource MasterTokenSource;
        public static CancellationTokenSource IntroSkipTokenSource;
        public static SemaphoreSlim SemaphoreMaster;
        public static ConcurrentQueue<BaseItem> MediaInfoExtractItemQueue = new ConcurrentQueue<BaseItem>();
        public static ConcurrentQueue<BaseItem> ExternalSubtitleItemQueue = new ConcurrentQueue<BaseItem>();
        public static ConcurrentQueue<Episode> IntroSkipItemQueue = new ConcurrentQueue<Episode>();
        public static Task MasterProcessTask;

        public static void Initialize()
        {
            _logger = Plugin.Instance.Logger;
            _currentMaxConcurrentCount = Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.MaxConcurrentCount;
            SemaphoreMaster = new SemaphoreSlim(_currentMaxConcurrentCount);

            if (MasterProcessTask == null || MasterProcessTask.IsCompleted)
            {
                MasterProcessTask = Task.Run(() => Master_ProcessItemQueueAsync());
            }
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

        public static async Task Master_ProcessItemQueueAsync()
        {
            _logger.Info("Master - ProcessItemQueueAsync Started");
            MasterTokenSource = new CancellationTokenSource();
            var cancellationToken = MasterTokenSource.Token;
            while (!cancellationToken.IsCancellationRequested)
            {
                var timeSinceLastRun = DateTime.UtcNow - _masterProcessLastRunTime;
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

                if (!MediaInfoExtractItemQueue.IsEmpty || !ExternalSubtitleItemQueue.IsEmpty)
                {
                    var enableImageCapture = Plugin.Instance.MainOptionsStore.GetOptions().MediaInfoExtractOptions.EnableImageCapture;
                    _logger.Info("Image Capture Enabled: " + enableImageCapture);
                    var exclusiveExtract = Plugin.Instance.MainOptionsStore.GetOptions().MediaInfoExtractOptions.ExclusiveExtract;
                    var enableIntroSkip = Plugin.Instance.IntroSkipStore.GetOptions().EnableIntroSkip;
                    _logger.Info("Intro Skip Enabled: " + enableIntroSkip);

                    var dequeueMediaInfoItems = new List<BaseItem>();
                    while (MediaInfoExtractItemQueue.TryDequeue(out var dequeueItem))
                    {
                        dequeueMediaInfoItems.Add(dequeueItem);
                    }

                    if (dequeueMediaInfoItems.Count > 0)
                    {
                        _logger.Info("MediaInfoExtract - Clear Item Queue Started");

                        var dedupMediaInfoItems = dequeueMediaInfoItems.GroupBy(i => i.InternalId).Select(g => g.First()).ToList();
                        var mediaInfoItems = Plugin.LibraryApi.FetchExtractQueueItems(dedupMediaInfoItems);

                        foreach (var item in mediaInfoItems)
                        {
                            var taskItem = item;
                            _taskQueue.Enqueue(async () =>
                            {
                                var isExtractAllowed = false;

                                try
                                {
                                    if (cancellationToken.IsCancellationRequested)
                                    {
                                        _logger.Info("MediaInfoExtract - Item Cancelled: " + taskItem.Name + " - " + taskItem.Path);
                                        return;
                                    }

                                    if (exclusiveExtract)
                                    {
                                        ExclusiveExtract.AllowExtractInstance(taskItem);
                                        isExtractAllowed = true;
                                    }

                                    if (enableImageCapture && !taskItem.HasImage(ImageType.Primary))
                                    {
                                        if (taskItem.IsShortcut)
                                        {
                                            EnableImageCapture.AllowImageCaptureInstance(taskItem);
                                        }
                                        var refreshOptions = LibraryApi.ImageCaptureRefreshOptions;
                                        await taskItem.RefreshMetadata(refreshOptions, cancellationToken).ConfigureAwait(false);
                                    }
                                    else
                                    {
                                        await Plugin.LibraryApi.ProbeMediaInfo(taskItem, cancellationToken)
                                            .ConfigureAwait(false);
                                    }

                                    _logger.Info("MediaInfoExtract - Item Processed: " + taskItem.Name + " - " + taskItem.Path);

                                    if (enableIntroSkip && Plugin.PlaySessionMonitor.IsLibraryInScope(taskItem))
                                    {
                                        IntroSkipItemQueue.Enqueue(taskItem as Episode);
                                    }
                                }
                                catch (TaskCanceledException)
                                {
                                    _logger.Info("MediaInfoExtract - Item Cancelled: " + taskItem.Name + " - " + taskItem.Path);
                                }
                                catch (Exception e)
                                {
                                    _logger.Error("MediaInfoExtract - Item Failed: " + taskItem.Name + " - " + taskItem.Path);
                                    _logger.Error(e.Message);
                                    _logger.Debug(e.StackTrace);
                                }
                                finally
                                {
                                    if (isExtractAllowed) ExclusiveExtract.DisallowExtractInstance(taskItem);
                                }
                            });
                        }
                        _logger.Info("MediaInfoExtract - Clear Item Queue Stopped");
                    }

                    var dequeueSubtitleItems = new List<BaseItem>();
                    while (ExternalSubtitleItemQueue.TryDequeue(out var dequeueItem))
                    {
                        dequeueSubtitleItems.Add(dequeueItem);
                    }

                    if (dequeueSubtitleItems.Count > 0)
                    {
                        _logger.Info("ExternalSubtitle - Clear Item Queue Started");

                        var dedupSubtitleItems =
                            dequeueSubtitleItems.GroupBy(i => i.InternalId).Select(g => g.First()).ToList();

                        foreach (var item in dedupSubtitleItems)
                        {
                            var taskItem = item;
                            _taskQueue.Enqueue(async () =>
                            {
                                try
                                {
                                    if (cancellationToken.IsCancellationRequested)
                                    {
                                        _logger.Info("ExternalSubtitle - Item Cancelled: " + taskItem.Name + " - " + taskItem.Path);
                                        return;
                                    }

                                    await Plugin.SubtitleApi.UpdateExternalSubtitles(taskItem, cancellationToken)
                                        .ConfigureAwait(false);

                                    _logger.Info("ExternalSubtitle - Item Processed: " + taskItem.Name + " - " + taskItem.Path);
                                }
                                catch (TaskCanceledException)
                                {
                                    _logger.Info("ExternalSubtitle - Item Cancelled: " + taskItem.Name + " - " + taskItem.Path);
                                }
                                catch (Exception e)
                                {
                                    _logger.Error("ExternalSubtitle - Item Failed: " + taskItem.Name + " - " + taskItem.Path);
                                    _logger.Error(e.Message);
                                    _logger.Debug(e.StackTrace);
                                }
                            });
                        }
                        _logger.Info("ExternalSubtitle - Clear Item Queue Stopped");
                    }

                    lock (_lock)
                    {
                        if (!_isProcessing && (dequeueMediaInfoItems.Count > 0 || dequeueSubtitleItems.Count > 0))
                        {
                            _isProcessing = true;
                            var task = Task.Run(() => Master_ProcessTaskQueueAsync(cancellationToken));
                        }
                    }
                }
                _masterProcessLastRunTime = DateTime.UtcNow;
            }

            if (MediaInfoExtractItemQueue.IsEmpty && ExternalSubtitleItemQueue.IsEmpty)
            {
                _logger.Info("Master - ProcessItemQueueAsync Stopped");
            }
            else
            {
                _logger.Info("Master - ProcessItemQueueAsync Cancelled");
            }
        }

        private static async Task Master_ProcessTaskQueueAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Master - ProcessTaskQueueAsync Started");
            _logger.Info("Max Concurrent Count: " + Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.MaxConcurrentCount);

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
                    _logger.Info("Master - ProcessTaskQueueAsync Stopped");
                }
                else
                {
                    _logger.Info("Master - ProcessTaskQueueAsync Cancelled");
                }
            }
        }

        public static async Task IntroSkip_ProcessItemQueueAsync()
        {
            _logger.Info("IntroSkip - ProcessItemQueueAsync Started");
            IntroSkipTokenSource = new CancellationTokenSource();
            var cancellationToken = IntroSkipTokenSource.Token;
            while (!cancellationToken.IsCancellationRequested)
            {
                var timeSinceLastRun = DateTime.UtcNow - _introSkipProcessLastRunTime;
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
                    var dequeueItems = new List<Episode>();
                    while (IntroSkipItemQueue.TryDequeue(out var dequeueItem))
                    {
                        dequeueItems.Add(dequeueItem);
                    }

                    if (dequeueItems.Count > 0)
                    {
                        _logger.Info("IntroSkip - Clear Item Queue Started");

                        Plugin.ChapterApi.PopulateIntroCredits(dequeueItems);

                        _logger.Info("IntroSkip - Clear Item Queue Stopped");
                    }
                }
                _introSkipProcessLastRunTime = DateTime.UtcNow;
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

        public static void Dispose()
        {
            MasterTokenSource?.Cancel();
        }
    }
}
