using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant
{
    public class ClearChapterMarkersTask: IScheduledTask
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

            var items = new List<BaseItem>();

            await Task.Run(() =>
            {
                items = Plugin.ChapterApi.FetchClearTaskItems();
            }, cancellationToken);

            progress.Report(50.0);

            double total = items.Count;
            var current = 0;

            await Task.Run(() =>
            {
                foreach (var item in items)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.Info("IntroSkip - Clear Task Cancelled");
                        break;
                    }

                    var percentDone = (current / total) * 100;
                    var adjustedProgress = 50 + (percentDone / 50);
                    progress.Report(adjustedProgress);

                    Plugin.ChapterApi.RemoveIntroCreditsMarkers(item);

                    current++;
                    _logger.Info("IntroSkip - Clear Task " + current + "/" + total + " - " + item.Path);

                    Task.Delay(10).Wait();
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
