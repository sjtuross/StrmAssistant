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
            _logger.Info("Max Concurrent Count: " + Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.MaxConcurrentCount);
            _logger.Info("Intro Detection Fingerprint Length (Minutes): " + Plugin.Instance.IntroSkipStore.GetOptions().IntroDetectionFingerprintMinutes);

            var items = Plugin.ChapterApi.FetchIntroFingerprintTaskItems();
            _logger.Info("IntroFingerprintExtract - Number of items: " + items.Count);

            var directoryService = new DirectoryService(_logger, _fileSystem);

            double total = items.Count;
            var index = 0;
            var current = 0;

            var tasks = new List<Task>();

            foreach (var item in items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.Info("IntroFingerprintExtract - Scheduled Task Cancelled");
                    break;
                }

                try
                {
                    await QueueManager.SemaphoreMaster.WaitAsync(cancellationToken);
                }
                catch
                {
                    break;
                }

                var taskIndex = ++index;
                var taskItem = item;
                var task = Task.Run(async () =>
                {
                    try
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            _logger.Info("IntroFingerprintExtract - Scheduled Task Cancelled");
                            return;
                        }

                        await Plugin.ChapterApi.ExtractIntroFingerprint(item, directoryService, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.Info("IntroFingerprintExtract - Item cancelled: " + taskItem.Name + " - " + taskItem.Path);
                    }
                    catch (Exception e)
                    {
                        _logger.Error("IntroFingerprintExtract - Item failed: " + taskItem.Name + " - " + taskItem.Path);
                        _logger.Error(e.Message);
                        _logger.Debug(e.StackTrace);
                    }
                    finally
                    {
                        QueueManager.SemaphoreMaster.Release();

                        var currentCount = Interlocked.Increment(ref current);
                        progress.Report(currentCount / total * 100);
                        _logger.Info("IntroFingerprintExtract - Progress " + currentCount + "/" + total + " - " +
                                     "Task " + taskIndex + ": " +
                                     taskItem.Path);
                    }
                }, cancellationToken);
                tasks.Add(task);
            }
            await Task.WhenAll(tasks);

            progress.Report(100.0);

            _logger.Info("IntroFingerprintExtract - Trigger Detect Episode Intros to import fingerprints");

            var markerTask = _taskManager.ScheduledTasks.FirstOrDefault(t =>
                t.Name.Equals("Detect Episode Intros", StringComparison.OrdinalIgnoreCase));
            if (markerTask != null)
            {
                _ = _taskManager.Execute(markerTask, new TaskOptions());
            }

            _logger.Info("IntroFingerprintExtract - Task Complete");
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
