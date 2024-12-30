using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using StrmAssistant.Common;
using StrmAssistant.Properties;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant
{
    public class PersistMediaInfoTask: IScheduledTask
    {
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;

        public PersistMediaInfoTask(IFileSystem fileSystem)
        {
            _logger = Plugin.Instance.Logger;
            _fileSystem = fileSystem;
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("MediaInfoPersist - Scheduled Task Execute");
            _logger.Info("Tier2 Max Concurrent Count: " +
                         Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.Tier2MaxConcurrentCount);

            await Task.Yield();
            progress.Report(0);

            var items = Plugin.LibraryApi.FetchPostExtractTaskItems(true);
            _logger.Info("MediaInfoPersist - Number of items: " + items.Count);

            var directoryService = new DirectoryService(_logger, _fileSystem);

            double total = items.Count;
            var index = 0;
            var current = 0;
            
            var tasks = new List<Task>();

            foreach (var item in items)
            {
                try
                {
                    await QueueManager.Tier2Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    return;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    QueueManager.Tier2Semaphore.Release();
                    _logger.Info("MediaInfoPersist - Scheduled Task Cancelled");
                    return;
                }

                var taskIndex = ++index;
                var taskItem = item;
                var task = Task.Run(async () =>
                {
                    try
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            _logger.Info("MediaInfoPersist - Scheduled Task Cancelled");
                            return;
                        }

                        await Plugin.LibraryApi.SerializeMediaInfo(taskItem, directoryService, false,
                                "Persist MediaInfo Task", cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.Info("MediaInfoPersist - Item cancelled: " + taskItem.Name + " - " + taskItem.Path);
                    }
                    catch (Exception e)
                    {
                        _logger.Error("MediaInfoPersist - Item failed: " + taskItem.Name + " - " + taskItem.Path);
                        _logger.Error(e.Message);
                        _logger.Debug(e.StackTrace);
                    }
                    finally
                    {
                        QueueManager.Tier2Semaphore.Release();

                        var currentCount = Interlocked.Increment(ref current);
                        progress.Report(currentCount / total * 100);
                        _logger.Info("MediaInfoPersist - Progress " + currentCount + "/" + total + " - " +
                                     "Task " + taskIndex + ": " + taskItem.Path);
                    }
                }, cancellationToken);
                tasks.Add(task);
                Task.Delay(10).Wait();
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);

            progress.Report(100.0);
            _logger.Info("MediaInfoPersist - Scheduled Task Complete");
        }

        public string Category => Resources.ResourceManager.GetString("PluginOptions_EditorTitle_Strm_Assistant",
            Plugin.Instance.DefaultUICulture);

        public string Key => "MediaInfoPersistTask";

        public string Description => Resources.ResourceManager.GetString(
            "PersistMediaInfoTask_Description_Persists_media_info_to_json_file", Plugin.Instance.DefaultUICulture);

        public string Name => "Persist MediaInfo";
        //public string Name => Resources.ResourceManager.GetString("PersistMediaInfoTask_Name_Persist_MediaInfo",
        //    Plugin.Instance.DefaultUICulture);

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return Array.Empty<TaskTriggerInfo>();
        }
    }
}
