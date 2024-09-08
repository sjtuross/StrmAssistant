using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant
{
    public class ClearChapterMarkersTask : IScheduledTask
    {
        private readonly ILogger _logger;

        public ClearChapterMarkersTask()
        {
            _logger = Plugin.Instance.logger;
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("IntroSkip - Clear Task Execute");
            await Task.Yield();
            progress.Report(0);

            List<BaseItem> items = new List<BaseItem>();

            await Task.Run(() =>
            {
                items = Plugin.ChapterApi.FetchClearTaskItems();
            }, cancellationToken);

            progress.Report(50.0);

            double total = items.Count;
            int current = 0;

            await Task.Run(() =>
            {
                foreach (BaseItem item in items)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.Info("IntroSkip - Clear Task Cancelled");
                        break;
                    }

                    double percentDone = (current / total) * 100;
                    double adjustedProgress = 50 + (percentDone / 50);
                    progress.Report(adjustedProgress);

                    Plugin.ChapterApi.RemoveIntroCreditsMarkers(item);

                    current++;
                    _logger.Info("IntroSkip - Clear Task " + current + "/" + total + " - " + item.Path);

                    // Optional: Add a small delay to allow for cancellation and responsiveness
                    Task.Delay(10).Wait(); // 10ms delay
                }
            }, cancellationToken);

            progress.Report(100.0);
            _logger.Info("IntroSkip - Clear Task Complete");
        }

        public string Category => Plugin.Instance.Name;

        public string Key => "IntroSkipClearTask";

        public string Description => "Clears intro and credits markers of episodes";

        public string Name => "Clear Episode Intros";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return Array.Empty<TaskTriggerInfo>();
        }
    }
}
