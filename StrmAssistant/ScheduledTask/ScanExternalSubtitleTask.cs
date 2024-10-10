using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant
{
    public class ScanExternalSubtitleTask : IScheduledTask
    {
        private readonly ILogger _logger;

        public ScanExternalSubtitleTask()
        {
            _logger = Plugin.Instance.logger;
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("ExternalSubtitle - Scan Task Execute");
            await Task.Yield();
            progress.Report(0);

            var items = Plugin.SubtitleApi.FetchScanTaskItems();
            _logger.Info("ExternalSubtitle - Number of items: " + items.Count);

            double total = items.Count;
            var current = 0;
            var tasks = new List<Task>();

            foreach (var item in items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.Info("ExternalSubtitle - Scan Task Cancelled");
                    break;
                }

                await QueueManager.SemaphoreMaster.WaitAsync(cancellationToken);

                var taskItem = item;
                var task = Task.Run(async () =>
                {
                    try
                    {
                        if (Plugin.SubtitleApi.HasExternalSubtitleChanged(taskItem))
                        {
                            await Plugin.SubtitleApi.UpdateExternalSubtitles(taskItem, cancellationToken).ConfigureAwait(false);

                            _logger.Info("ExternalSubtitle - Item Processed: " + taskItem.Name + " - " + taskItem.Path);
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.Info("ExternalSubtitle - Item cancelled: " + taskItem.Name + " - " + taskItem.Path);
                    }
                    catch (Exception e)
                    {
                        _logger.Info("ExternalSubtitle - Item failed: " + taskItem.Name + " - " + taskItem.Path);
                        _logger.Debug(e.Message);
                        _logger.Debug(e.StackTrace);
                    }
                    finally
                    {
                        Interlocked.Increment(ref current);
                        progress.Report(current / total * 100);
                        QueueManager.SemaphoreMaster.Release();
                    }
                }, cancellationToken);
                tasks.Add(task);
                Task.Delay(10).Wait();
            }
            await Task.WhenAll(tasks);

            progress.Report(100.0);
            _logger.Info("ExternalSubtitle - Scan Task Complete");
        }

        public string Category => Plugin.Instance.Name;

        public string Key => "ScanExternalSubtitleTask";

        public string Description => "Scans external subtitles for videos";

        public string Name => "Scan External Subtitles";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return Array.Empty<TaskTriggerInfo>();
        }
    }
}
