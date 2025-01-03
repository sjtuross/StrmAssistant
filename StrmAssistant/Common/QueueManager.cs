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
        private static readonly ILogger Logger;
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
        public static ConcurrentQueue<Episode> IntroSkipItemQueue = new ConcurrentQueue<Episode>();
        public static ConcurrentQueue<BaseItem> FingerprintItemQueue = new ConcurrentQueue<BaseItem>();
        public static Task MediaInfoProcessTask;
        public static Task FingerprintProcessTask;

        public static bool IsMediaInfoProcessTaskRunning { get; private set; }

        static QueueManager()
        {
            Logger = Plugin.Instance.Logger;
            _currentMasterMaxConcurrentCount = Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.MaxConcurrentCount;
            _currentTier2MaxConcurrentCount = Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.Tier2MaxConcurrentCount;

            MasterSemaphore = new SemaphoreSlim(_currentMasterMaxConcurrentCount);
            Tier2Semaphore = new SemaphoreSlim(_currentTier2MaxConcurrentCount);
        }

        public static void Initialize()
        {
            if (MediaInfoProcessTask is null || MediaInfoProcessTask.IsCompleted)
            {
                MediaInfoExtractItemQueue.Clear();
                MediaInfoProcessTask = MediaInfo_ProcessItemQueueAsync();
            }

            if (FingerprintProcessTask is null || FingerprintProcessTask.IsCompleted)
            {
                FingerprintItemQueue.Clear();
                FingerprintProcessTask = Fingerprint_ProcessItemQueueAsync();
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
            Logger.Info("MediaInfo - ProcessItemQueueAsync Started");
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

                if (!MediaInfoExtractItemQueue.IsEmpty)
                {
                    if (Plugin.LibraryApi.IsLibraryScanRunning())
                    {
                        Logger.Info("MediaInfoExtract - ProcessItemQueueAsync Deferred (Library Scan Running)");
                        Logger.Info("MediaInfoExtract - ProcessItemQueueAsync Queue: " + MediaInfoExtractItemQueue.Count);
                        _mediaInfoProcessLastRunTime = DateTime.UtcNow;
                        continue;
                    }

                    var dequeueMediaInfoItems = new List<BaseItem>();
                    while (MediaInfoExtractItemQueue.TryDequeue(out var dequeueItem))
                    {
                        dequeueMediaInfoItems.Add(dequeueItem);
                    }

                    Logger.Info("MediaInfoExtract - Clear Item Queue Started");

                    var dedupMediaInfoItems =
                        dequeueMediaInfoItems.GroupBy(i => i.InternalId).Select(g => g.First()).ToList();
                    var mediaInfoItems = Plugin.LibraryApi.FetchExtractQueueItems(dedupMediaInfoItems);

                    if (mediaInfoItems.Count > 0)
                    {
                        var maxConcurrentCount = Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions
                            .MaxConcurrentCount;
                        Logger.Info("Master Max Concurrent Count: " + maxConcurrentCount);
                        var cooldownSeconds = maxConcurrentCount == 1
                            ? Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.CooldownDurationSeconds
                            : (int?)null;
                        if (cooldownSeconds.HasValue)
                            Logger.Info("Cooldown Duration Seconds: " + cooldownSeconds.Value);
                        Logger.Info("Tier2 Max Concurrent Count: " +
                                    Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions
                                        .Tier2MaxConcurrentCount);

                        var tasks = new List<Task>();
                        IsMediaInfoProcessTaskRunning = true;

                        foreach (var item in mediaInfoItems)
                        {
                            var taskItem = item;

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
                                Logger.Info("MediaInfoExtract - Item Cancelled: " + taskItem.Name + " - " +
                                            taskItem.Path);
                                break;
                            }

                            var task = Task.Run(async () =>
                            {
                                bool? result = null;

                                try
                                {
                                    if (cancellationToken.IsCancellationRequested)
                                    {
                                        Logger.Info("MediaInfoExtract - Item Cancelled: " + taskItem.Name + " - " +
                                                    taskItem.Path);
                                        return;
                                    }

                                    result = await Plugin.LibraryApi
                                        .OrchestrateMediaInfoProcessAsync(taskItem, "MediaInfoExtract Catchup",
                                            cancellationToken).ConfigureAwait(false);

                                    if (result is null)
                                    {
                                        Logger.Info("MediaInfoExtract - Item Skipped: " + taskItem.Name + " - " +
                                                    taskItem.Path);
                                        return;
                                    }

                                    if (IsCatchupTaskSelected(GeneralOptions.CatchupTask.IntroSkip) &&
                                        Plugin.PlaySessionMonitor.IsLibraryInScope(taskItem))
                                    {
                                        IntroSkipItemQueue.Enqueue(taskItem as Episode);
                                    }

                                    Logger.Info("MediaInfoExtract - Item Processed: " + taskItem.Name + " - " +
                                                taskItem.Path);
                                }
                                catch (TaskCanceledException)
                                {
                                    Logger.Info("MediaInfoExtract - Item Cancelled: " + taskItem.Name + " - " +
                                                taskItem.Path);
                                }
                                catch (Exception e)
                                {
                                    Logger.Error("MediaInfoExtract - Item Failed: " + taskItem.Name + " - " +
                                                 taskItem.Path);
                                    Logger.Error(e.Message);
                                    Logger.Debug(e.StackTrace);
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

                                    MasterSemaphore.Release();
                                }
                            }, cancellationToken);
                            tasks.Add(task);
                        }

                        await Task.WhenAll(tasks).ConfigureAwait(false);

                        IsMediaInfoProcessTaskRunning = false;
                    }

                    Logger.Info("MediaInfoExtract - Clear Item Queue Stopped");
                }

                _mediaInfoProcessLastRunTime = DateTime.UtcNow;
            }

            if (MediaInfoExtractItemQueue.IsEmpty)
            {
                Logger.Info("MediaInfo - ProcessItemQueueAsync Stopped");
            }
            else
            {
                Logger.Info("MediaInfo - ProcessItemQueueAsync Cancelled");
            }
        }

        public static async Task Fingerprint_ProcessItemQueueAsync()
        {
            Logger.Info("Fingerprint - ProcessItemQueueAsync Started");
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
                    if (Plugin.LibraryApi.IsLibraryScanRunning())
                    {
                        Logger.Info("Fingerprint - ProcessItemQueueAsync Deferred (Library Scan Running)");
                        Logger.Info("Fingerprint - ProcessItemQueueAsync Queue: " + FingerprintItemQueue.Count);
                        _fingerprintProcessLastRunTime = DateTime.UtcNow;
                        continue;
                    }

                    var dequeueItems = new List<BaseItem>();
                    while (FingerprintItemQueue.TryDequeue(out var dequeueItem))
                    {
                        dequeueItems.Add(dequeueItem);
                    }

                    Logger.Info("Fingerprint - Clear Item Queue Started");

                    var episodes = Plugin.FingerprintApi.FetchFingerprintQueueItems(dequeueItems);

                    if (IsCatchupTaskSelected(GeneralOptions.CatchupTask.MediaInfo,
                            GeneralOptions.CatchupTask.IntroSkip))
                    {
                        var episodeIds = episodes.Select(e => e.InternalId).ToHashSet();
                        var mediaInfoItems = dequeueItems.Where(i => !episodeIds.Contains(i.InternalId));
                        foreach (var item in mediaInfoItems)
                        {
                            MediaInfoExtractItemQueue.Enqueue(item);
                        }
                    }

                    Logger.Info("Fingerprint - Number of items: " + episodes.Count);

                    if (episodes.Count > 0)
                    {
                        var maxConcurrentCount = Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions
                            .MaxConcurrentCount;
                        Logger.Info("Master Max Concurrent Count: " + maxConcurrentCount);
                        var cooldownSeconds = maxConcurrentCount == 1
                            ? Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.CooldownDurationSeconds
                            : (int?)null;
                        if (cooldownSeconds.HasValue)
                            Logger.Info("Cooldown Duration Seconds: " + cooldownSeconds.Value);

                        var groupedBySeason = episodes.GroupBy(e => e.Season).ToList();
                        var seasonTasks = new List<Task>();
                        
                        IsMediaInfoProcessTaskRunning = true;

                        foreach (var season in groupedBySeason)
                        {
                            var taskSeason = season.Key;

                            if (cancellationToken.IsCancellationRequested)
                            {
                                Logger.Info("Fingerprint - Season Cancelled: " + taskSeason.Name + " - " +
                                            taskSeason.Path);
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
                                            Logger.Info("Fingerprint - Episode Cancelled: " + taskItem.Name + " - " +
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
                                                Logger.Info("Fingerprint - Episode Skipped: " + taskItem.Name +
                                                            " - " + taskItem.Path);
                                                seasonSkip = true;
                                                return;
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

                                        Logger.Info("Fingerprint - Episode Processed: " + taskItem.Name + " - " +
                                                    taskItem.Path);
                                    }
                                    catch (TaskCanceledException)
                                    {
                                        Logger.Info("Fingerprint - Episode Cancelled: " + taskItem.Name + " - " +
                                                    taskItem.Path);
                                    }
                                    catch (Exception e)
                                    {
                                        Logger.Error("Fingerprint - Episode Failed: " + taskItem.Name + " - " +
                                                     taskItem.Path);
                                        Logger.Error(e.Message);
                                        Logger.Debug(e.StackTrace);
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
                                        Logger.Info("Fingerprint - Season Cancelled: " + taskSeason.Name + " - " +
                                                    taskSeason.Path);
                                        return;
                                    }

                                    if (seasonSkip)
                                    {
                                        Logger.Info("Fingerprint - Season Skipped: " + taskSeason.Name + " - " +
                                                    taskSeason.Path);
                                        return;
                                    }

                                    await Tier2Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                                    await Plugin.FingerprintApi
                                        .UpdateIntroMarkerForSeason(taskSeason, cancellationToken)
                                        .ConfigureAwait(false);
                                }
                                catch (TaskCanceledException)
                                {
                                    Logger.Info("Fingerprint - Season Cancelled: " + taskSeason.Name + " - " +
                                                taskSeason.Path);
                                }
                                catch (Exception e)
                                {
                                    Logger.Error("Fingerprint - Season Failed: " + taskSeason.Name + " - " +
                                                 taskSeason.Path);
                                    Logger.Error(e.Message);
                                    Logger.Debug(e.StackTrace);
                                }
                                finally
                                {
                                    Tier2Semaphore.Release();
                                }
                            }, cancellationToken);
                            seasonTasks.Add(seasonTask);
                        }

                        await Task.WhenAll(seasonTasks).ConfigureAwait(false);
                        IsMediaInfoProcessTaskRunning = false;
                    }

                    Logger.Info("Fingerprint - Clear Item Queue Stopped");
                }

                _fingerprintProcessLastRunTime = DateTime.UtcNow;
            }

            if (FingerprintItemQueue.IsEmpty)
            {
                Logger.Info("Fingerprint - ProcessItemQueueAsync Stopped");
            }
            else
            {
                Logger.Info("Fingerprint - ProcessItemQueueAsync Cancelled");
            }
        }

        public static async Task IntroSkip_ProcessItemQueueAsync()
        {
            Logger.Info("IntroSkip - ProcessItemQueueAsync Started");
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
                        Logger.Info("IntroSkip - Clear Item Queue Started");

                        Plugin.ChapterApi.PopulateIntroCredits(dequeueItems);

                        Logger.Info("IntroSkip - Clear Item Queue Stopped");
                    }
                }
                _introSkipProcessLastRunTime = DateTime.UtcNow;
            }

            if (IntroSkipItemQueue.IsEmpty)
            {
                Logger.Info("IntroSkip - ProcessItemQueueAsync Stopped");
            }
            else
            {
                Logger.Info("IntroSkip - ProcessItemQueueAsync Cancelled");
            }
        }

        public static void Dispose()
        {
            MediaInfoTokenSource?.Cancel();
            FingerprintTokenSource?.Cancel();
        }
    }
}
