using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Options.GeneralOptions;
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
        private static int _currentMasterMaxConcurrentCount;
        private static int _currentTier2MaxConcurrentCount;

        public static CancellationTokenSource MediaInfoTokenSource;
        public static CancellationTokenSource IntroSkipTokenSource;
        public static CancellationTokenSource FingerprintTokenSource;
        public static SemaphoreSlim MasterSemaphore;
        public static SemaphoreSlim Tier2Semaphore;
        public static ConcurrentQueue<BaseItem> MediaInfoExtractItemQueue = new ConcurrentQueue<BaseItem>();
        public static ConcurrentQueue<BaseItem> ExternalSubtitleItemQueue = new ConcurrentQueue<BaseItem>();
        public static ConcurrentQueue<Episode> IntroSkipItemQueue = new ConcurrentQueue<Episode>();
        public static ConcurrentQueue<BaseItem> FingerprintItemQueue = new ConcurrentQueue<BaseItem>();
        public static Task MediaInfoProcessTask;
        public static Task FingerprintProcessTask;

        public static bool IsMediaInfoProcessTaskRunning { get; private set; }

        static QueueManager()
        {
            _logger = Plugin.Instance.Logger;
            _currentMasterMaxConcurrentCount = Plugin.Instance.GetPluginOptions().GeneralOptions.MaxConcurrentCount;
            _currentTier2MaxConcurrentCount = Plugin.Instance.GetPluginOptions().GeneralOptions.Tier2MaxConcurrentCount;

            MasterSemaphore = new SemaphoreSlim(_currentMasterMaxConcurrentCount);
            Tier2Semaphore = new SemaphoreSlim(_currentTier2MaxConcurrentCount);
        }

        public static void Initialize()
        {
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

        public static void UpdateMasterSemaphore(int maxConcurrentCount)
        {
            if (_currentMasterMaxConcurrentCount != maxConcurrentCount)
            {
                _currentMasterMaxConcurrentCount = maxConcurrentCount;

                var newMasterSemaphore = new SemaphoreSlim(maxConcurrentCount);
                var oldMasterSemaphore = MasterSemaphore;
                MasterSemaphore = newMasterSemaphore;
                oldMasterSemaphore.Dispose();
            }
        }

        public static void UpdateTier2Semaphore(int maxConcurrentCount)
        {
            if (_currentTier2MaxConcurrentCount != maxConcurrentCount)
            {
                _currentTier2MaxConcurrentCount = maxConcurrentCount;

                var newTier2Semaphore = new SemaphoreSlim(maxConcurrentCount);
                var oldTier2Semaphore = Tier2Semaphore;
                Tier2Semaphore = newTier2Semaphore;
                oldTier2Semaphore.Dispose();
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
                        await Task.Delay(remainingTime, cancellationToken).ConfigureAwait(false);
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
                                    bool? result = null;

                                    try
                                    {
                                        if (cancellationToken.IsCancellationRequested)
                                        {
                                            _logger.Info("MediaInfoExtract - Item Cancelled: " + taskItem.Name + " - " +
                                                         taskItem.Path);
                                            return null;
                                        }

                                        result = await Plugin.LibraryApi
                                            .OrchestrateMediaInfoProcessAsync(taskItem, "MediaInfoExtract Catchup",
                                                cancellationToken).ConfigureAwait(false);

                                        if (result is null)
                                        {
                                            _logger.Info("MediaInfoExtract - Item Skipped: " + taskItem.Name + " - " + taskItem.Path);
                                            return null;
                                        }

                                        if (IsCatchupTaskSelected(CatchupTask.IntroSkip) &&
                                            Plugin.PlaySessionMonitor.IsLibraryInScope(taskItem))
                                        {
                                            IntroSkipItemQueue.Enqueue(taskItem as Episode);
                                        }

                                        _logger.Info("MediaInfoExtract - Item Processed: " + taskItem.Name + " - " +
                                                     taskItem.Path);
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

                                    return result;
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

            var maxConcurrentCount = Plugin.Instance.GetPluginOptions().GeneralOptions.MaxConcurrentCount;
            _logger.Info("Master Max Concurrent Count: " + maxConcurrentCount);
            var cooldownSeconds = maxConcurrentCount == 1
                ? Plugin.Instance.GetPluginOptions().GeneralOptions.CooldownDurationSeconds
                : (int?)null;
            if (cooldownSeconds.HasValue) _logger.Info("Cooldown Duration Seconds: " + cooldownSeconds.Value);
            _logger.Info("Tier2 Max Concurrent Count: " +
                         Plugin.Instance.GetPluginOptions().GeneralOptions.Tier2MaxConcurrentCount);

            var tasks = new List<Task>();

            while (!cancellationToken.IsCancellationRequested)
            {
                if (TaskQueue.TryDequeue(out var taskWrapper))
                {
                    var selectedSemaphore = taskWrapper.Source == "ExternalSubtitle"
                        ? Tier2Semaphore
                        : MasterSemaphore;

                    try
                    {
                        await selectedSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        break;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        selectedSemaphore.Release();
                        break;
                    }

                    var task = Task.Run(async () =>
                    {
                        object result = null;

                        try
                        {
                            result = await taskWrapper.Action().ConfigureAwait(false);
                        }
                        finally
                        {
                            if (result is true && cooldownSeconds.HasValue)
                            {
                                try
                                {
                                    await Task.Delay(cooldownSeconds.Value * 1000, cancellationToken)
                                        .ConfigureAwait(false);
                                }
                                catch
                                {
                                    // ignored
                                }
                            }

                            selectedSemaphore.Release();
                        }
                    }, cancellationToken);
                    tasks.Add(task);
                }
                else
                {
                    break;
                }
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

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
                        await Task.Delay(remainingTime, cancellationToken).ConfigureAwait(false);
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

                        if (episodes.Count > 0)
                        {
                            var maxConcurrentCount = Plugin.Instance.GetPluginOptions().GeneralOptions
                                .MaxConcurrentCount;
                            _logger.Info("Master Max Concurrent Count: " + maxConcurrentCount);
                            var cooldownSeconds = maxConcurrentCount == 1
                                ? Plugin.Instance.GetPluginOptions().GeneralOptions.CooldownDurationSeconds
                                : (int?)null;
                            if (cooldownSeconds.HasValue) _logger.Info("Cooldown Duration Seconds: " + cooldownSeconds.Value);

                            var groupedBySeason = episodes.GroupBy(e => e.Season).ToList();
                            var seasonTasks = new List<Task>();

                            foreach (var season in groupedBySeason)
                            {
                                var taskSeason = season.Key;

                                if (cancellationToken.IsCancellationRequested)
                                {
                                    _logger.Info("Fingerprint - Season Cancelled: " + taskSeason.Name + " - " + taskSeason.Path);
                                    break;
                                }

                                var episodeTasks = new List<Task>();
                                var seasonSkip = false;

                                foreach (var episode in season)
                                {
                                    var taskItem = episode;

                                    try
                                    {
                                        await MasterSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                                    }
                                    catch
                                    {
                                        break;
                                    }

                                    if (cancellationToken.IsCancellationRequested)
                                    {
                                        MasterSemaphore.Release();
                                        break;
                                    }

                                    var task = Task.Run(async () =>
                                    {
                                        bool? result1 = null;
                                        Tuple<string, bool> result2 = null;

                                        try
                                        {
                                            if (cancellationToken.IsCancellationRequested)
                                            {
                                                _logger.Info("Fingerprint - Episode Cancelled: " + taskItem.Name + " - " +
                                                             taskItem.Path);
                                                return;
                                            }

                                            if (Plugin.LibraryApi.IsExtractNeeded(taskItem))
                                            {
                                                result1 = await Plugin.LibraryApi
                                                    .OrchestrateMediaInfoProcessAsync(taskItem, "Fingerprint Catchup",
                                                        cancellationToken).ConfigureAwait(false);

                                                if (result1 is null)
                                                {
                                                    _logger.Info("Fingerprint - Episode Skipped: " + taskItem.Name +
                                                                 " - " + taskItem.Path);
                                                    seasonSkip = true;
                                                    return;
                                                }

                                                if (cooldownSeconds.HasValue)
                                                {
                                                    await Task.Delay(cooldownSeconds.Value * 1000, cancellationToken)
                                                        .ConfigureAwait(false);
                                                }
                                            }

                                            var dateCreated = taskItem.DateCreated;
                                            taskItem.DateCreated = new DateTimeOffset(
                                                new DateTime(dateCreated.Year, dateCreated.Month, dateCreated.Day,
                                                    dateCreated.Hour, dateCreated.Minute, dateCreated.Second),
                                                dateCreated.Offset);

                                            result2 = await Plugin.FingerprintApi
                                                .ExtractIntroFingerprint(taskItem, cancellationToken)
                                                .ConfigureAwait(false);

                                            _logger.Info("Fingerprint - Episode Processed: " + taskItem.Name + " - " +
                                                         taskItem.Path);
                                        }
                                        catch (TaskCanceledException)
                                        {
                                            _logger.Info("Fingerprint - Episode Cancelled: " + taskItem.Name + " - " +
                                                         taskItem.Path);
                                        }
                                        catch (Exception e)
                                        {
                                            _logger.Error("Fingerprint - Episode Failed: " + taskItem.Name + " - " +
                                                          taskItem.Path);
                                            _logger.Error(e.Message);
                                            _logger.Debug(e.StackTrace);
                                        }
                                        finally
                                        {
                                            if ((result1 is true || result2?.Item2 is true) && cooldownSeconds.HasValue)
                                            {
                                                try
                                                {
                                                    await Task.Delay(cooldownSeconds.Value * 1000, cancellationToken)
                                                        .ConfigureAwait(false);
                                                }
                                                catch
                                                {
                                                    // ignored
                                                }
                                            }

                                            MasterSemaphore.Release();
                                        }
                                    }, cancellationToken);
                                    episodeTasks.Add(task);
                                }

                                var seasonTask = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await Task.WhenAll(episodeTasks).ConfigureAwait(false);

                                        if (cancellationToken.IsCancellationRequested)
                                        {
                                            _logger.Info("Fingerprint - Season Cancelled: " + taskSeason.Name + " - " + taskSeason.Path);
                                            return;
                                        }

                                        if (seasonSkip)
                                        {
                                            _logger.Info("Fingerprint - Season Skipped: " + taskSeason.Name + " - " + taskSeason.Path);
                                            return;
                                        }

                                        await Tier2Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                                        await Plugin.FingerprintApi
                                            .UpdateIntroMarkerForSeason(taskSeason, cancellationToken)
                                            .ConfigureAwait(false);
                                    }
                                    catch (TaskCanceledException)
                                    {
                                        _logger.Info("Fingerprint - Season Cancelled: " + taskSeason.Name + " - " + taskSeason.Path);
                                    }
                                    catch (Exception e)
                                    {
                                        _logger.Error("Fingerprint - Season Failed: " + taskSeason.Name + " - " + taskSeason.Path);
                                        _logger.Error(e.Message);
                                        _logger.Debug(e.StackTrace);
                                    }
                                    finally
                                    {
                                        Tier2Semaphore.Release();
                                    }
                                }, cancellationToken);
                                seasonTasks.Add(seasonTask);
                            }
                            await Task.WhenAll(seasonTasks).ConfigureAwait(false);
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
                        await Task.Delay(remainingTime, cancellationToken).ConfigureAwait(false);
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
