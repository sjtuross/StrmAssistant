using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Logging;
using StrmAssistant.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Options.Utility;

namespace StrmAssistant.Common
{
    public static class QueueManager
    {
        private static ILogger _logger;

        internal class TaskWrapper<T>
        {
            public string Source { get; set; }
            public Func<Task<T>> Action { get; set; }
        }

        private static readonly ConcurrentQueue<TaskWrapper<object>> TaskQueue = new ConcurrentQueue<TaskWrapper<object>>();
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
            _logger = Plugin.Instance.Logger;
            _currentMaxConcurrentCount = Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.MaxConcurrentCount;
            SemaphoreMaster = new SemaphoreSlim(_currentMaxConcurrentCount);
            SemaphoreLocal = new SemaphoreSlim(_currentMaxConcurrentCount);

            TaskQueue.Clear();
            MediaInfoExtractItemQueue.Clear();
            ExternalSubtitleItemQueue.Clear();
            FingerprintItemQueue.Clear();

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
                    var dequeueMediaInfoItems = new List<BaseItem>();
                    while (MediaInfoExtractItemQueue.TryDequeue(out var dequeueItem))
                    {
                        dequeueMediaInfoItems.Add(dequeueItem);
                    }

                    var mediaInfoItems = new List<BaseItem>();
                    if (dequeueMediaInfoItems.Count > 0)
                    {
                        _logger.Info("MediaInfoExtract - Clear Item Queue Started");

                        var dedupMediaInfoItems = dequeueMediaInfoItems.GroupBy(i => i.InternalId).Select(g => g.First()).ToList();
                        mediaInfoItems = Plugin.LibraryApi.FetchExtractQueueItems(dedupMediaInfoItems);

                        foreach (var item in mediaInfoItems)
                        {
                            var taskItem = item;
                            TaskQueue.Enqueue(new TaskWrapper<object>
                            {
                                Source = "MediaInfoExtract",
                                Action = async () =>
                                {
                                    try
                                    {
                                        if (cancellationToken.IsCancellationRequested)
                                        {
                                            _logger.Info("MediaInfoExtract - Item Cancelled: " + taskItem.Name + " - " +
                                                         taskItem.Path);
                                            return false;
                                        }

                                        var success = await Plugin.LibraryApi
                                            .OrchestrateMediaInfoProcessAsync(taskItem, "MediaInfoExtract Catchup",
                                                cancellationToken).ConfigureAwait(false);

                                        if (!success)
                                        {
                                            _logger.Info("MediaInfoExtract - Item Skipped: " + taskItem.Name + " - " + taskItem.Path);
                                            return false;
                                        }

                                        if (IsCatchupTaskSelected(GeneralOptions.CatchupTask.IntroSkip) &&
                                            Plugin.PlaySessionMonitor.IsLibraryInScope(taskItem))
                                        {
                                            IntroSkipItemQueue.Enqueue(taskItem as Episode);
                                        }

                                        _logger.Info("MediaInfoExtract - Item Processed: " + taskItem.Name + " - " +
                                                     taskItem.Path);

                                        return true;
                                    }
                                    catch (TaskCanceledException)
                                    {
                                        _logger.Info("MediaInfoExtract - Item Cancelled: " + taskItem.Name + " - " + taskItem.Path);
                                    }
                                    catch (Exception e)
                                    {
                                        _logger.Error("MediaInfoExtract - Item Failed: " + taskItem.Name + " - " +
                                                      taskItem.Path);
                                        _logger.Error(e.Message);
                                        _logger.Debug(e.StackTrace);
                                    }

                                    return false;
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

                    var subtitleItems = new List<BaseItem>();
                    if (dequeueSubtitleItems.Count > 0)
                    {
                        _logger.Info("ExternalSubtitle - Clear Item Queue Started");

                        subtitleItems =
                            dequeueSubtitleItems.GroupBy(i => i.InternalId).Select(g => g.First()).ToList();

                        foreach (var item in subtitleItems)
                        {
                            var taskItem = item;
                            TaskQueue.Enqueue(new TaskWrapper<object>
                            {
                                Source = "ExternalSubtitle",
                                Action = async () =>
                                {
                                    try
                                    {
                                        if (cancellationToken.IsCancellationRequested)
                                        {
                                            _logger.Info("ExternalSubtitle - Item Cancelled: " + taskItem.Name + " - " + taskItem.Path);
                                            return null;
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

                                    return null;
                                }
                            });
                        }
                        _logger.Info("ExternalSubtitle - Clear Item Queue Stopped");
                    }

                    lock (_lock)
                    {
                        if (!IsMediaInfoProcessTaskRunning && (mediaInfoItems.Count > 0 || subtitleItems.Count > 0))
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

            var maxConcurrentCount = Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.MaxConcurrentCount;
            _logger.Info("Max Concurrent Count: " + maxConcurrentCount);
            var cooldownSeconds = maxConcurrentCount == 1
                ? Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.CooldownDurationSeconds
                : (int?)null;
            if (cooldownSeconds.HasValue) _logger.Info("Cooldown Duration Seconds: " + cooldownSeconds.Value);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (TaskQueue.TryDequeue(out var taskWrapper))
                {
                    var selectedSemaphore = taskWrapper.Source == "ExternalSubtitle"
                        ? SemaphoreLocal
                        : SemaphoreMaster;

                    try
                    {
                        await selectedSemaphore.WaitAsync(cancellationToken);
                    }
                    catch
                    {
                        break;
                    }

                    var task = Task.Run(async () =>
                    {
                        object result = null;

                        try
                        {
                            result = await taskWrapper.Action();
                        }
                        finally
                        {
                            if (result is bool success && success && cooldownSeconds.HasValue)
                            {
                                try
                                {
                                    await Task.Delay(cooldownSeconds.Value * 1000, cancellationToken);
                                }
                                catch
                                {
                                    // ignored
                                }
                            }

                            selectedSemaphore.Release();
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
                if (TaskQueue.IsEmpty)
                {
                    _logger.Info("MediaInfo - ProcessTaskQueueAsync Stopped");
                }
                else
                {
                    _logger.Info("MediaInfo - ProcessTaskQueueAsync Cancelled");
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

                        if (IsCatchupTaskSelected(GeneralOptions.CatchupTask.MediaInfo, GeneralOptions.CatchupTask.IntroSkip))
                        {
                            var episodeIds = episodes.Select(e => e.InternalId).ToHashSet();
                            var mediaInfoItems = dequeueItems.Where(i => !episodeIds.Contains(i.InternalId));
                            foreach (var item in mediaInfoItems)
                            {
                                MediaInfoExtractItemQueue.Enqueue(item);
                            }
                        }

                        _logger.Info("Fingerprint - Number of items: " + episodes.Count);

                        if (episodes.Count > 0)
                        {
                            var maxConcurrentCount = Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions
                                .MaxConcurrentCount;
                            _logger.Info("Max Concurrent Count: " + maxConcurrentCount);
                            var cooldownSeconds = maxConcurrentCount == 1
                                ? Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.CooldownDurationSeconds
                                : (int?)null;
                            if (cooldownSeconds.HasValue) _logger.Info("Cooldown Duration Seconds: " + cooldownSeconds.Value);

                            var groupedBySeason = episodes.GroupBy(e => e.Season).ToList();

                            foreach (var season in groupedBySeason)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    break;
                                }

                                var episodeTasks = new List<Task>();
                                var seasonSkip = false;

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
                                        var success = false;

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
                                                success = await Plugin.LibraryApi
                                                    .OrchestrateMediaInfoProcessAsync(taskItem, "Fingerprint Catchup",
                                                        cancellationToken).ConfigureAwait(false);

                                                if (!success)
                                                {
                                                    _logger.Info("Fingerprint - Item Skipped: " + taskItem.Name +
                                                                 " - " + taskItem.Path);
                                                    seasonSkip = true;
                                                    return;
                                                }

                                                if (cooldownSeconds.HasValue)
                                                {
                                                    await Task.Delay(cooldownSeconds.Value * 1000, cancellationToken);
                                                }
                                            }

                                            await Plugin.FingerprintApi
                                                .ExtractIntroFingerprint(taskItem, cancellationToken)
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
                                            if (success && cooldownSeconds.HasValue)
                                            {
                                                try
                                                {
                                                    await Task.Delay(cooldownSeconds.Value * 1000, cancellationToken);
                                                }
                                                catch
                                                {
                                                    // ignored
                                                }
                                            }

                                            SemaphoreMaster.Release();
                                        }
                                    }, cancellationToken);
                                    episodeTasks.Add(task);
                                }

                                var taskSeason = season.Key;
                                var seasonTask = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await Task.WhenAll(episodeTasks);

                                        if (cancellationToken.IsCancellationRequested)
                                        {
                                            return;
                                        }

                                        if (seasonSkip)
                                        {
                                            _logger.Info("Fingerprint - Season Skipped: " + taskSeason.Name + " - " +
                                                         taskSeason.Path);
                                            return;
                                        }

                                        await SemaphoreLocal.WaitAsync(cancellationToken);

                                        await Plugin.FingerprintApi
                                            .UpdateIntroMarkerForSeason(taskSeason, cancellationToken)
                                            .ConfigureAwait(false);
                                    }
                                    catch (TaskCanceledException)
                                    {
                                        _logger.Info("Fingerprint - Season Cancelled: " + taskSeason.Name + " - " +
                                                     taskSeason.Path);
                                    }
                                    catch (Exception e)
                                    {
                                        _logger.Error("Fingerprint - Season Failed: " + taskSeason.Name + " - " +
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
            FingerprintTokenSource?.Cancel();
        }
    }
}
