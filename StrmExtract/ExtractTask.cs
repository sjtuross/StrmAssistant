using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
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

                await QueueManager.SemaphoreMaster.WaitAsync(cancellationToken);
                
                var taskIndex = ++index;
                var itemName = item.Name;
                var itemPath = item.Path;
                var itemHasImage = item.HasImage(ImageType.Primary);
                var itemMediaStreamCount = item.GetMediaStreams().Count;
                MetadataRefreshOptions refreshOptions;
                if (itemMediaStreamCount == 0 && itemHasImage)
                {
                    refreshOptions = LibraryUtility.MediaInfoRefreshOptions;
                }
                else
                {
                    refreshOptions = LibraryUtility.ImageCaptureRefreshOptions;
                }
                
                var task = Task.Run(async () =>
                {
                    try
                    {
                        ItemUpdateType resp = await item.RefreshMetadata(refreshOptions, cancellationToken).ConfigureAwait(false);
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
                        QueueManager.SemaphoreMaster.Release();
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
            get { return "MediaInfoExtractTask"; }
        }

        public string Description
        {
            get { return "Extract media info from videos"; }
        }

        public string Name
        {
            get { return "Extract media info"; }
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
