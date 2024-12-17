using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using StrmAssistant.Common;
using StrmAssistant.Mod;
using StrmAssistant.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.ScheduledTask
{
    public class ExtractMediaInfoTask : IScheduledTask
    {
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;

        public ExtractMediaInfoTask(IFileSystem fileSystem)
        {
            _logger = Plugin.Instance.Logger;
            _fileSystem = fileSystem;
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("MediaInfoExtract - Scheduled Task Execute");
            _logger.Info("Max Concurrent Count: " + Plugin.Instance.MainOptionsStore.GetOptions().GeneralOptions.MaxConcurrentCount);
            var persistMediaInfo = Plugin.Instance.MainOptionsStore.GetOptions().MediaInfoExtractOptions.PersistMediaInfo;
            _logger.Info("Persist Media Info: " + persistMediaInfo);
            var enableImageCapture = Plugin.Instance.MainOptionsStore.GetOptions().MediaInfoExtractOptions.EnableImageCapture;
            _logger.Info("Enable Image Capture: " + enableImageCapture);
            var enableIntroSkip = Plugin.Instance.IntroSkipStore.GetOptions().EnableIntroSkip;
            _logger.Info("Intro Skip Enabled: " + enableIntroSkip);
            var exclusiveExtract = Plugin.Instance.MainOptionsStore.GetOptions().MediaInfoExtractOptions.ExclusiveExtract;

            var items = Plugin.LibraryApi.FetchPreExtractTaskItems();

            if (items.Count > 0) IsRunning = true;

            var directoryService = new DirectoryService(_logger, _fileSystem);

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
                            _logger.Info("MediaInfoExtract - Scheduled Task Cancelled");
                            return;
                        }

                        if (exclusiveExtract)
                        {
                            ExclusiveExtract.AllowExtractInstance(taskItem);
                        }

                        var imageCapture = false;

                        if (enableImageCapture && !taskItem.HasImage(ImageType.Primary))
                        {
                            var filePath = taskItem.Path;
                            if (taskItem.IsShortcut)
                            {
                                filePath = await Plugin.LibraryApi.GetStrmMountPath(filePath).ConfigureAwait(false);
                            }

                            var fileExtension = Path.GetExtension(filePath).TrimStart('.');
                            if (!LibraryApi.ExcludeMediaExtensions.Contains(fileExtension))
                            {
                                if (taskItem.IsShortcut)
                                {
                                    EnableImageCapture.AllowImageCaptureInstance(taskItem);
                                }

                                imageCapture = true;
                                var refreshOptions = LibraryApi.ImageCaptureRefreshOptions;
                                await taskItem.RefreshMetadata(refreshOptions, cancellationToken).ConfigureAwait(false);
                            }
                        }

                        var deserializeResult = false;

                        if (!imageCapture)
                        {
                            if (persistMediaInfo)
                            {
                                deserializeResult = await Plugin.LibraryApi
                                    .DeserializeMediaInfo(taskItem, directoryService, cancellationToken)
                                    .ConfigureAwait(false);
                            }

                            if (!deserializeResult)
                            {
                                await Plugin.LibraryApi.ProbeMediaInfo(taskItem, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                        }

                        if (enableIntroSkip && Plugin.PlaySessionMonitor.IsLibraryInScope(taskItem))
                        {
                            if (taskItem is Episode episode && Plugin.ChapterApi.SeasonHasIntroCredits(episode))
                            {
                                QueueManager.IntroSkipItemQueue.Enqueue(episode);
                            }
                        }

                        if (persistMediaInfo)
                        {
                            if (!deserializeResult)
                            {
                                await Plugin.LibraryApi
                                    .SerializeMediaInfo(taskItem, directoryService, true, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                            else if (Plugin.SubtitleApi.HasExternalSubtitleChanged(taskItem))
                            {
                                await Plugin.SubtitleApi.UpdateExternalSubtitles(taskItem, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.Info("MediaInfoExtract - Item cancelled: " + taskItem.Name + " - " + taskItem.Path);
                    }
                    catch (Exception e)
                    {
                        _logger.Error("MediaInfoExtract - Item failed: " + taskItem.Name + " - " + taskItem.Path);
                        _logger.Error(e.Message);
                        _logger.Debug(e.StackTrace);
                    }
                    finally
                    {
                        QueueManager.SemaphoreMaster.Release();

                        var currentCount = Interlocked.Increment(ref current);
                        progress.Report(currentCount / total * 100);
                        _logger.Info("MediaInfoExtract - Progress " + currentCount + "/" + total + " - " +
                                     "Task " + taskIndex + ": " +
                                     taskItem.Path);
                    }
                }, cancellationToken);
                tasks.Add(task);
            }
            await Task.WhenAll(tasks);

            if (items.Count > 0) IsRunning = false;

            progress.Report(100.0);
            _logger.Info("MediaInfoExtract - Scheduled Task Complete");
        }

        public string Category =>
            Resources.ResourceManager.GetString("PluginOptions_EditorTitle_Strm_Assistant",
                Plugin.Instance.DefaultUICulture);

        public string Key => "MediaInfoExtractTask";

        public string Description => Resources.ResourceManager.GetString(
            "ExtractMediaInfoTask_Description_Extracts_media_info_from_videos_and_audios",
            Plugin.Instance.DefaultUICulture);

        public string Name => "Extract MediaInfo";
        //public string Name => Resources.ResourceManager.GetString("ExtractMediaInfoTask_Name_Extract_MediaInfo",
        //    Plugin.Instance.DefaultUICulture);

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return Array.Empty<TaskTriggerInfo>();
        }

        public static bool IsRunning { get; private set; }
    }
}
