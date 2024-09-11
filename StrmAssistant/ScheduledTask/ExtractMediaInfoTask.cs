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

namespace StrmAssistant
{
    public class ExtractMediaInfoTask: IScheduledTask
    {
        private readonly ILogger _logger;

        public ExtractMediaInfoTask()
        {
            _logger = Plugin.Instance.logger;
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("MediaInfoExtract - Scheduled Task Execute");
            _logger.Info("Max Concurrent Count: " + Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.MaxConcurrentCount);
            bool enableImageCapture = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.EnableImageCapture;
            _logger.Info("Enable Image Capture: " + enableImageCapture);

            List<BaseItem> items = Plugin.LibraryApi.FetchExtractTaskItems();

            double total = items.Count;
            int index = 0;
            int current = 0;
            
            List<Task> tasks = new List<Task>();

            foreach (BaseItem item in items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.Info("MediaInfoExtract - Scheduled Task Cancelled");
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
                            refreshOptions = LibraryApi.ImageCaptureRefreshOptions;
                        }
                        else
                        {
                            refreshOptions = LibraryApi.MediaInfoRefreshOptions;
                        }

                        if (enableImageCapture && !taskItem.HasImage(ImageType.Primary) && taskItem.IsShortcut)
                        {
                            EnableImageCapture.PatchInstanceIsShortcut(taskItem);
                            isPatched=true;
                        }

                        ItemUpdateType resp = await taskItem.RefreshMetadata(refreshOptions, cancellationToken).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.Info("MediaInfoExtract - Item cancelled: " + taskItem.Name + " - " + taskItem.Path);
                    }
                    catch
                    {
                        _logger.Info("MediaInfoExtract - Item failed: " + taskItem.Name + " - " + taskItem.Path);
                    }
                    finally
                    {
                        Interlocked.Increment(ref current);
                        progress.Report(current / total * 100);
                        _logger.Info("MediaInfoExtract - Scheduled Task " + current + "/" + total + " - " + "Task " + taskIndex + ": " +
                                     taskItem.Path);

                        if (isPatched)
                        {
                            EnableImageCapture.UnpatchInstanceIsShortcut(taskItem);
                        }

                        QueueManager.SemaphoreMaster.Release();
                    }
                }, cancellationToken);
                tasks.Add(task);
            }
            await Task.WhenAll(tasks);

            progress.Report(100.0);
            _logger.Info("MediaInfoExtract - Scheduled Task Complete");
        }

        public string Category => Plugin.Instance.Name;

        public string Key => "MediaInfoExtractTask";

        public string Description => "Extracts media info from videos";

        public string Name => "Extract MediaInfo";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return Array.Empty<TaskTriggerInfo>();
        }
    }
}
