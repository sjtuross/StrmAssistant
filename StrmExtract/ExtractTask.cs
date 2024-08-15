using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StrmExtract
{
    public class ExtractTask: IScheduledTask
    {
        private readonly ILogger _logger;

        public ExtractTask()
        {
            _logger = Plugin.Instance.logger;
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("Scheduled Task Execute");
            _logger.Info("Max Concurrent Count: " + Plugin.Instance.GetPluginOptions().MaxConcurrentCount);

            List<BaseItem> items = Plugin.LibraryUtility.FetchItems();

            double total = items.Count;
            int index = 0;
            int current = 0;
            
            List<Task> tasks = new List<Task>();

            foreach (BaseItem item in items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.Info("Scheduled Task Cancelled");
                    break;
                }

                await QueueManager.semaphore.WaitAsync(cancellationToken);
                var taskIndex = ++index;
                var itemName = item.Name;
                var itemPath = item.Path;
                var task = Task.Run(async () =>
                {
                    try
                    {
                        ItemUpdateType resp = await item.RefreshMetadata(LibraryUtility.MediaInfoRefreshOptions,
                            cancellationToken);
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.Info("Item cancelled: " + itemName + " - " + itemPath);
                    }
                    catch
                    {
                        _logger.Info("Item failed: " + itemName + " - " + itemPath);
                    }
                    finally
                    {
                        Interlocked.Increment(ref current);
                        progress.Report(current / total * 100);
                        _logger.Info(current + "/" + total + " - " + "Task " + taskIndex + ": " + itemPath);
                        QueueManager.semaphore.Release();
                    }
                }, cancellationToken);
                tasks.Add(task);
            }
            await Task.WhenAll(tasks);

            progress.Report(100.0);
            _logger.Info("Scheduled Task Complete");
        }

        public string Category
        {
            get { return "Strm Extract"; }
        }

        public string Key
        {
            get { return "StrmExtractTask"; }
        }

        public string Description
        {
            get { return "Run Strm Media Info Extraction"; }
        }

        public string Name
        {
            get { return "Process Strm targets"; }
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
                {
                    new TaskTriggerInfo
                    {
                        Type = TaskTriggerInfo.TriggerDaily,
                        TimeOfDayTicks = TimeSpan.FromHours(3).Ticks,
                        MaxRuntimeTicks = TimeSpan.FromHours(24).Ticks
                    }
                };
        }
    }
}
