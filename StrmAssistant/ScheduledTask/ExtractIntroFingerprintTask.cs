using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using StrmAssistant.Common;
using StrmAssistant.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.ScheduledTask
{
    public class ExtractIntroFingerprintTask : IScheduledTask
    {
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ITaskManager _taskManager;

        public ExtractIntroFingerprintTask(IFileSystem fileSystem, ITaskManager taskManager)
        {
            _logger = Plugin.Instance.Logger;
            _fileSystem = fileSystem;
            _taskManager = taskManager;
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("IntroFingerprintExtract - Scheduled Task Execute");
            
            var unlockIntroSkip = Plugin.Instance.IntroSkipStore.GetOptions().UnlockIntroSkip;
            if (!unlockIntroSkip)
            {
                progress.Report(100.0);
                _logger.Warn("UnlockIntroSkip is not enabled.");
                _logger.Warn("IntroFingerprintExtract - Scheduled Task Aborted");
                return;
            }

            var maxConcurrentCount = Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.MaxConcurrentCount;
            _logger.Info("Master Max Concurrent Count: " + maxConcurrentCount);
            var cooldownSeconds = maxConcurrentCount == 1
                ? Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.CooldownDurationSeconds
                : (int?)null;
            if (cooldownSeconds.HasValue) _logger.Info("Cooldown Duration Seconds: " + cooldownSeconds.Value);

            _logger.Info("Intro Detection Fingerprint Length (Minutes): " + Plugin.Instance.IntroSkipStore.GetOptions().IntroDetectionFingerprintMinutes);

            var preExtractEpisodes = Plugin.FingerprintApi.FetchIntroPreExtractTaskItems();
            var postExtractEpisodes = Plugin.FingerprintApi.FetchIntroFingerprintTaskItems();
            var episodes= preExtractEpisodes.Concat(postExtractEpisodes).ToList();
            var groupedBySeason = episodes.GroupBy(e => e.Season).ToList();

            _logger.Info("IntroFingerprintExtract - Number of seasons: " + groupedBySeason.Count);
            _logger.Info("IntroFingerprintExtract - Number of episodes: " + episodes.Count);

            var directoryService = new DirectoryService(_logger, _fileSystem);

            double total = episodes.Count;
            var index = 0;
            var current = 0;
            var seasonSkipCount = 0;

            var episodeTasks = new List<Task>();

            foreach (var season in groupedBySeason)
            {
                var taskSeason = season.Key;

                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.Info("IntroFingerprintExtract - Scheduled Task Cancelled");
                    return;
                }

                var seasonSkip = false;

                foreach (var episode in season)
                {
                    var taskEpisode = episode;

                    try
                    {
                        await QueueManager.MasterSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        return;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        QueueManager.MasterSemaphore.Release();
                        _logger.Info("IntroFingerprintExtract - Scheduled Task Cancelled");
                        return;
                    }

                    var taskIndex = ++index;
                    var task = Task.Run(async () =>
                    {
                        bool? result1 = null;
                        Tuple<string, bool> result2 = null;

                        try
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                _logger.Info("IntroFingerprintExtract - Scheduled Task Cancelled");
                                return;
                            }

                            if (Plugin.LibraryApi.IsExtractNeeded(taskEpisode))
                            {
                                result1 = await Plugin.LibraryApi
                                    .OrchestrateMediaInfoProcessAsync(taskEpisode, "IntroFingerprintExtract Task",
                                        cancellationToken).ConfigureAwait(false);

                                if (result1 is null)
                                {
                                    _logger.Info("IntroFingerprintExtract - Episode Skipped: " + taskEpisode.Name +
                                                " - " + taskEpisode.Path);
                                    seasonSkip = true;
                                    return;
                                }
                            }

                            result2 = await Plugin.FingerprintApi
                                .ExtractIntroFingerprint(taskEpisode, directoryService, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (TaskCanceledException)
                        {
                            _logger.Info("IntroFingerprintExtract - Episode cancelled: " + taskEpisode.Name + " - " +
                                         taskEpisode.Path);
                        }
                        catch (Exception e)
                        {
                            _logger.Error("IntroFingerprintExtract - Episode failed: " + taskEpisode.Name + " - " +
                                          taskEpisode.Path);
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

                            QueueManager.MasterSemaphore.Release();

                            var currentCount = Interlocked.Increment(ref current);
                            progress.Report(currentCount / total * 100);
                            _logger.Info("IntroFingerprintExtract - Progress " + currentCount + "/" + total + " - " +
                                         "Task " + taskIndex + ": " + taskEpisode.Path);
                        }
                    }, cancellationToken);
                    episodeTasks.Add(task);

                    if (seasonSkip)
                    {
                        Interlocked.Increment(ref seasonSkipCount);
                        _logger.Info("Fingerprint - Season Skipped: " + taskSeason.Name + " - " + taskSeason.Path);
                        break;
                    }
                }
            }

            await Task.WhenAll(episodeTasks).ConfigureAwait(false);

            progress.Report(100.0);

            var markerTask = _taskManager.ScheduledTasks.FirstOrDefault(t =>
                t.Name.Equals("Detect Episode Intros", StringComparison.OrdinalIgnoreCase));
            if (markerTask != null && groupedBySeason.Count > seasonSkipCount &&
                !cancellationToken.IsCancellationRequested)
            {
                _ = _taskManager.Execute(markerTask, new TaskOptions());
                _logger.Info("IntroFingerprintExtract - Triggered Detect Episode Intros to process fingerprints");
            }

            _logger.Info("IntroFingerprintExtract - Scheduled Task Complete");
        }

        public string Category => Resources.ResourceManager.GetString("PluginOptions_EditorTitle_Strm_Assistant",
            Plugin.Instance.DefaultUICulture);

        public string Key => "IntroFingerprintExtractTask";

        public string Description => Resources.ResourceManager.GetString(
            "ExtractIntroFingerprintTask_Description_Extracts_intro_fingerprint_from_episodes",
            Plugin.Instance.DefaultUICulture);

        public string Name => "Extract Intro Fingerprint";
        //public string Name =>
        //    Resources.ResourceManager.GetString("ExtractIntroFingerprintTask_Name_Extract_Intro_Fingerprint",
        //        Plugin.Instance.DefaultUICulture);

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return Array.Empty<TaskTriggerInfo>();
        }
    }
}
