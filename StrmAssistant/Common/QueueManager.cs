using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using StrmAssistant.Mod;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.GeneralOptions;
using static StrmAssistant.Options.Utility;

namespace StrmAssistant
{
    public static class QueueManager
    {
        private static ILogger _logger;
        private static readonly ConcurrentQueue<Func<Task>> _taskQueue = new ConcurrentQueue<Func<Task>>();
        private static readonly object _lock = new object();
        private static DateTime _mediaInfoProcessLastRunTime = DateTime.MinValue;
        private static DateTime _introSkipProcessLastRunTime = DateTime.MinValue;
        private static DateTime _fingerprintProcessLastRunTime = DateTime.MinValue;
        private static readonly TimeSpan ThrottleInterval = TimeSpan.FromSeconds(30);
        private static int _currentMaxConcurrentCount;

        public static CancellationTokenSource MediaInfoTokenSource;
        public static CancellationTokenSource IntroSkipTokenSource;
        public static CancellationTokenSource FingerprintTokenSource;
        public static SemaphoreSlim SemaphoreMaster;
        public static SemaphoreSlim SemaphoreLocal;
        public static ConcurrentQueue<BaseItem> MediaInfoExtractItemQueue = new ConcurrentQueue<BaseItem>();
        public static ConcurrentQueue<BaseItem> ExternalSubtitleItemQueue = new ConcurrentQueue<BaseItem>();
        public static ConcurrentQueue<Episode> IntroSkipItemQueue = new ConcurrentQueue<Episode>();
        public static ConcurrentQueue<BaseItem> FingerprintItemQueue = new ConcurrentQueue<BaseItem>();
        public static Task MediaInfoProcessTask;
        public static Task FingerprintProcessTask;

        public static bool IsMediaInfoProcessTaskRunning { get; private set; }

        public static void Initialize()
        {
            _logger = Plugin.Instance.logger;
            _currentMaxConcurrentCount = Plugin.Instance.GetPluginOptions().GeneralOptions.MaxConcurrentCount;
            SemaphoreMaster = new SemaphoreSlim(_currentMaxConcurrentCount);
            SemaphoreLocal = new SemaphoreSlim(_currentMaxConcurrentCount);

            if (MediaInfoProcessTask is null || MediaInfoProcessTask.IsCompleted)
            {
                MediaInfoProcessTask = Task.Run(MediaInfo_ProcessItemQueueAsync);
            }

            if (FingerprintProcessTask is null || FingerprintProcessTask.IsCompleted)
            {
                FingerprintProcessTask = Task.Run(Fingerprint_ProcessItemQueueAsync);
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

                var newSemaphoreLocal = new SemaphoreSlim(maxConcurrentCount);
                var oldSemaphoreLocal = SemaphoreLocal;
                SemaphoreLocal = newSemaphoreLocal;
                oldSemaphoreLocal.Dispose();
            }
        }

        public static async Task MediaInfo_ProcessItemQueueAsync()
        {
            _logger.Info("MediaInfo - ProcessItemQueueAsync Started");
            MediaInfoTokenSource = new CancellationTokenSource();
            var cancellationToken = MediaInfoTokenSource.Token;
            while (!cancellationToken.IsCancellationRequested)
            {
                var timeSinceLastRun = DateTime.UtcNow - _mediaInfoProcessLastRunTime;
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
                    var enableIntroSkip = Plugin.Instance.GetPluginOptions().IntroSkipOptions.EnableIntroSkip;
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
                                try
                                {
                                    if (cancellationToken.IsCancellationRequested)
                                    {
                                        _logger.Info("MediaInfoExtract - Item Cancelled: " + taskItem.Name + " - " + taskItem.Path);
                                        return;
                                    }

                                    await InternalMediaInfoProcessAsync(taskItem, cancellationToken).ConfigureAwait(false);

                                    if (IsCatchupTaskSelected(CatchupTask.IntroSkip) &&
                                        Plugin.PlaySessionMonitor.IsLibraryInScope(taskItem))
                                    {
                                        IntroSkipItemQueue.Enqueue(taskItem as Episode);
                                    }

                                    _logger.Info("MediaInfoExtract - Item Processed: " + taskItem.Name + " - " + taskItem.Path);
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
                        if (!IsMediaInfoProcessTaskRunning && (dequeueMediaInfoItems.Count > 0 || dequeueSubtitleItems.Count > 0))
                        {
                            IsMediaInfoProcessTaskRunning = true;
                            var task = Task.Run(() => MediaInfo_ProcessTaskQueueAsync(cancellationToken));
                        }
                    }
                }
                _mediaInfoProcessLastRunTime = DateTime.UtcNow;
            }

            if (MediaInfoExtractItemQueue.IsEmpty && ExternalSubtitleItemQueue.IsEmpty)
            {
                _logger.Info("MediaInfo - ProcessItemQueueAsync Stopped");
            }
            else
            {
                _logger.Info("MediaInfo - ProcessItemQueueAsync Cancelled");
            }
        }

        private static async Task MediaInfo_ProcessTaskQueueAsync(CancellationToken cancellationToken)
        {
            _logger.Info("MediaInfo - ProcessTaskQueueAsync Started");
            _logger.Info("Max Concurrent Count: " + Plugin.Instance.GetPluginOptions().GeneralOptions.MaxConcurrentCount);

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
                IsMediaInfoProcessTaskRunning = false;
                if (_taskQueue.IsEmpty)
                {
                    _logger.Info("MediaInfo - ProcessTaskQueueAsync Stopped");
                }
                else
                {
                    _logger.Info("MediaInfo - ProcessTaskQueueAsync Cancelled");
                }
            }
        }

        private static async Task InternalMediaInfoProcessAsync(BaseItem taskItem, CancellationToken cancellationToken)
        {
            var persistMediaInfo = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.PersistMediaInfo;
            _logger.Info("Persist Media Info: " + persistMediaInfo);
            var enableImageCapture = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.EnableImageCapture;
            _logger.Info("Image Capture Enabled: " + enableImageCapture);
            var exclusiveExtract = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.ExclusiveExtract;

            if (exclusiveExtract) ExclusiveExtract.AllowExtractInstance(taskItem);

            if (persistMediaInfo) ChapterChangeTracker.BypassInstance(taskItem);

            var imageCapture = false;

            if (enableImageCapture && !taskItem.HasImage(ImageType.Primary))
            {
                var filePath = taskItem.Path;
                if (taskItem.IsShortcut)
                {
                    filePath = await Plugin.LibraryApi.GetStrmMountPath(filePath)
                        .ConfigureAwait(false);
                }

                var fileExtension = Path.GetExtension(filePath).TrimStart('.');
                if (!LibraryApi.ExcludeMediaExtensions.Contains(fileExtension))
                {
                    if (taskItem.IsShortcut)
                    {
                        EnableImageCapture.AllowImageCaptureInstance(taskItem);
                    }

                    imageCapture = true;
                    var refreshOptions = LibraryApi.ImageCaptureRefreshOptions;
                    await taskItem.RefreshMetadata(refreshOptions, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            var deserializeResult = false;

            if (!imageCapture)
            {
                if (persistMediaInfo)
                {
                    deserializeResult = await Plugin.LibraryApi
                        .DeserializeMediaInfo(taskItem, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (!deserializeResult)
                {
                    await Plugin.LibraryApi.ProbeMediaInfo(taskItem, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if (persistMediaInfo)
            {
                if (!deserializeResult)
                {
                    await Plugin.LibraryApi
                        .SerializeMediaInfo(taskItem, true, "Extract MediaInfo Catchup",
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (Plugin.SubtitleApi.HasExternalSubtitleChanged(taskItem))
                {
                    await Plugin.SubtitleApi.UpdateExternalSubtitles(taskItem, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        public static async Task Fingerprint_ProcessItemQueueAsync()
        {
            _logger.Info("Fingerprint - ProcessItemQueueAsync Started");
            FingerprintTokenSource = new CancellationTokenSource();
            var cancellationToken = FingerprintTokenSource.Token;
            while (!cancellationToken.IsCancellationRequested)
            {
                var timeSinceLastRun = DateTime.UtcNow - _fingerprintProcessLastRunTime;
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

                if (!FingerprintItemQueue.IsEmpty)
                {
                    var dequeueItems = new List<BaseItem>();
                    while (FingerprintItemQueue.TryDequeue(out var dequeueItem))
                    {
                        dequeueItems.Add(dequeueItem);
                    }

                    if (dequeueItems.Count > 0)
                    {
                        _logger.Info("Fingerprint - Clear Item Queue Started");
                        
                        var episodes = Plugin.FingerprintApi.FetchFingerprintQueueItems(dequeueItems);

                        if (IsCatchupTaskSelected(CatchupTask.MediaInfo, CatchupTask.IntroSkip))
                        {
                            var episodeIds = episodes.Select(e => e.InternalId).ToHashSet();
                            var mediaInfoItems = dequeueItems.Where(i => !episodeIds.Contains(i.InternalId));
                            foreach (var item in mediaInfoItems)
                            {
                                MediaInfoExtractItemQueue.Enqueue(item);
                            }
                        }

                        _logger.Info("Fingerprint - Number of items: " + episodes.Count);

                        var groupedBySeason = episodes.GroupBy(e => e.Season).ToList();

                        var tasks = new List<Task>();

                        foreach (var season in groupedBySeason)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                break;
                            }

                            foreach (var episode in season)
                            {
                                var taskItem = episode;

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
                                        if (cancellationToken.IsCancellationRequested)
                                        {
                                            _logger.Info("Fingerprint - Item Cancelled: " + taskItem.Name + " - " +
                                                         taskItem.Path);
                                            return;
                                        }

                                        if (Plugin.LibraryApi.IsExtractNeeded(taskItem))
                                        {
                                            await InternalMediaInfoProcessAsync(taskItem, cancellationToken)
                                                .ConfigureAwait(false);
                                        }

                                        await Plugin.FingerprintApi.ExtractIntroFingerprint(taskItem, cancellationToken)
                                            .ConfigureAwait(false);

                                        _logger.Info("Fingerprint - Item Processed: " + taskItem.Name + " - " +
                                                     taskItem.Path);
                                    }
                                    catch (TaskCanceledException)
                                    {
                                        _logger.Info("Fingerprint - Item Cancelled: " + taskItem.Name + " - " +
                                                     taskItem.Path);
                                    }
                                    catch (Exception e)
                                    {
                                        _logger.Error("Fingerprint - Item Failed: " + taskItem.Name + " - " +
                                                      taskItem.Path);
                                        _logger.Error(e.Message);
                                        _logger.Debug(e.StackTrace);
                                    }
                                    finally
                                    {
                                        SemaphoreMaster.Release();
                                    }
                                }, cancellationToken);
                                tasks.Add(task);
                            }
                            await Task.WhenAll(tasks);
                            tasks.Clear();

                            try
                            {
                                await SemaphoreLocal.WaitAsync(cancellationToken);
                            }
                            catch
                            {
                                break;
                            }

                            var taskSeason = season.Key;
                            var seasonTask = Task.Run(async () =>
                            {
                                try
                                {
                                    if (cancellationToken.IsCancellationRequested)
                                    {
                                        return;
                                    }

                                    await Plugin.FingerprintApi
                                        .UpdateIntroMarkerForSeason(taskSeason, cancellationToken)
                                        .ConfigureAwait(false);
                                }
                                catch (TaskCanceledException)
                                {
                                    _logger.Info("Fingerprint - Item Cancelled: " + taskSeason.Name + " - " +
                                                 taskSeason.Path);
                                }
                                catch (Exception e)
                                {
                                    _logger.Error("Fingerprint - Item Failed: " + taskSeason.Name + " - " +
                                                  taskSeason.Path);
                                    _logger.Error(e.Message);
                                    _logger.Debug(e.StackTrace);
                                }
                                finally
                                {
                                    SemaphoreLocal.Release();
                                }
                            }, cancellationToken);
                        }

                        _logger.Info("Fingerprint - Clear Item Queue Stopped");
                    }
                }
                _fingerprintProcessLastRunTime = DateTime.UtcNow;
            }

            if (FingerprintItemQueue.IsEmpty)
            {
                _logger.Info("Fingerprint - ProcessItemQueueAsync Stopped");
            }
            else
            {
                _logger.Info("Fingerprint - ProcessItemQueueAsync Cancelled");
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
            MediaInfoTokenSource?.Cancel();
        }
    }
}
