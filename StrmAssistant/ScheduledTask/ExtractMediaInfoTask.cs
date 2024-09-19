using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using StrmAssistant.Mod;
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
            var enableImageCapture = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.EnableImageCapture;
            _logger.Info("Enable Image Capture: " + enableImageCapture);
            var exclusiveExtract = Plugin.Instance.GetPluginOptions().ModOptions.ExclusiveExtract;

            var items = Plugin.LibraryApi.FetchExtractTaskItems();

            if (items.Count > 0) ExclusiveExtract.PatchFFProbeTimeout();

            double total = items.Count;
            var index = 0;
            var current = 0;
            
            var tasks = new List<Task>();

            foreach (var item in items)
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
                    var isShortcutPatched = false;
                    var isExtractAllowed = false;
                    try
                    {
                        if (exclusiveExtract)
                        {
                            ExclusiveExtract.AllowExtractInstance(taskItem);
                            isExtractAllowed = true;
                        }

                        if (enableImageCapture && !taskItem.HasImage(ImageType.Primary))
                        {
                            if (taskItem.IsShortcut)
                            {
                                EnableImageCapture.PatchInstanceIsShortcut(taskItem);
                                isShortcutPatched = true;
                            }
                            var refreshOptions = LibraryApi.ImageCaptureRefreshOptions;
                            await taskItem.RefreshMetadata(refreshOptions, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            await Plugin.LibraryApi.ProbeMediaInfo(taskItem, cancellationToken).ConfigureAwait(false);
                        }
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

                        if (isShortcutPatched) EnableImageCapture.UnpatchInstanceIsShortcut(taskItem);
                        if (isExtractAllowed) ExclusiveExtract.DisallowExtractInstance(taskItem);

                        QueueManager.SemaphoreMaster.Release();
                    }
                }, cancellationToken);
                tasks.Add(task);
            }
            await Task.WhenAll(tasks);

            if (items.Count > 0) ExclusiveExtract.UnpatchFFProbeTimeout();

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
