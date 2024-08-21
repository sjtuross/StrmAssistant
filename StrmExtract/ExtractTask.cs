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
            bool enableImageCapture = Plugin.Instance.GetPluginOptions().EnableImageCapture;
            _logger.Info("Enable Image Capture: " + enableImageCapture);

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
                var taskItem = item;
                var task = Task.Run(async () =>
                {
                    bool isPatched = false;
                    try
                    {
                        MetadataRefreshOptions refreshOptions;
                        if (enableImageCapture && !taskItem.HasImage(ImageType.Primary))
                        {
                            refreshOptions = LibraryUtility.ImageCaptureRefreshOptions;
                        }
                        else
                        {
                            refreshOptions = LibraryUtility.MediaInfoRefreshOptions;
                        }

                        if (enableImageCapture && !taskItem.HasImage(ImageType.Primary) && taskItem.IsShortcut)
                        {
                            Patch.PatchInstanceIsShortcut(taskItem);
                            isPatched=true;
                        }

                        ItemUpdateType resp = await taskItem.RefreshMetadata(refreshOptions, cancellationToken).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.Info("Item cancelled: " + taskItem.Name + " - " + taskItem.Path);
                    }
                    catch
                    {
                        _logger.Info("Item failed: " + taskItem.Name + " - " + taskItem.Path);
                    }
                    finally
                    {
                        Interlocked.Increment(ref current);
                        progress.Report(current / total * 100);
                        _logger.Info(current + "/" + total + " - " + "Task " + taskIndex + ": " + taskItem.Path);

                        if (isPatched)
                        {
                            Patch.UnpatchInstanceIsShortcut(taskItem);
                        }

                        QueueManager.SemaphoreMaster.Release();
                    }
                }, cancellationToken);
                tasks.Add(task);
            }
            await Task.WhenAll(tasks);

            progress.Report(100.0);
            _logger.Info("Scheduled Task Complete");
        }

        public string Category => "Strm Extract";

        public string Key => "MediaInfoExtractTask";

        public string Description => "Extract media info from videos";

        public string Name => "Extract media info";

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
