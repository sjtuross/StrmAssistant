using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Session;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StrmAssistant
{
    public class PlaySessionMonitor
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ISessionManager _sessionManager;
        private readonly ILogger _logger;

        private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(10);
        private readonly ConcurrentDictionary<string, PlaySessionData> _playSessionData = new ConcurrentDictionary<string, PlaySessionData>();
        private readonly ConcurrentDictionary<Episode, Task> _ongoingIntroUpdates = new ConcurrentDictionary<Episode, Task>();
        private readonly ConcurrentDictionary<Episode, Task> _ongoingCreditsUpdates = new ConcurrentDictionary<Episode, Task>();
        private readonly ConcurrentDictionary<Episode, DateTime> _lastIntroUpdateTimes = new ConcurrentDictionary<Episode, DateTime>();
        private readonly ConcurrentDictionary<Episode, DateTime> _lastCreditsUpdateTimes = new ConcurrentDictionary<Episode, DateTime>();
        private readonly object _introLock = new object();
        private readonly object _creditsLock = new object();

        private static Task _introSkipProcessTask;

        public static List<string> LibraryPathsInScope;

        public PlaySessionMonitor(ILibraryManager libraryManager, ISessionManager sessionManager,
            IItemRepository itemRepository, IUserManager userManager)
        {
            _logger = Plugin.Instance.logger;
            _libraryManager = libraryManager;
            _sessionManager = sessionManager;

            UpdateLibraryPaths();
        }

        public void UpdateLibraryPaths()
        {
            var libraryIds = Plugin.Instance.GetPluginOptions().IntroSkipOptions.LibraryScope?.Split(',')
                .Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
            LibraryPathsInScope = _libraryManager.GetVirtualFolders()
                .Where(f => libraryIds != null && libraryIds.Any()
                    ? libraryIds.Contains(f.Id)
                    : f.CollectionType == "tvshows" || f.CollectionType is null)
                .SelectMany(l => l.Locations)
                .ToList();
        }

        public void Initialize()
        {
            Dispose();

            _sessionManager.PlaybackStart += OnPlaybackStart;
            _sessionManager.PlaybackProgress += OnPlaybackProgress;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            _libraryManager.ItemAdded += OnItemAdded;

            if (_introSkipProcessTask == null || _introSkipProcessTask.IsCompleted)
            {
                _introSkipProcessTask = Task.Run(() => QueueManager.IntroSkip_ProcessItemQueueAsync());
            }

            if (QueueManager.MediaInfoExtractProcessTask == null || QueueManager.MediaInfoExtractProcessTask.IsCompleted)
            {
                QueueManager.MediaInfoExtractProcessTask =
                    Task.Run(() => QueueManager.MediaInfoExtract_ProcessItemQueueAsync());
            }
        }

        private void OnItemAdded(object sender, ItemChangeEventArgs e)
        {
            if (IsInScope(e.Item))
            {
                if (!Plugin.LibraryApi.HasMediaStream(e.Item))
                {
                    QueueManager.MediaInfoExtractItemQueue.Enqueue(e.Item);
                }
                else
                {
                    QueueManager.IntroSkipItemQueue.Enqueue(e.Item as Episode);
                }
            }
        }

        private void OnPlaybackStart(object sender, PlaybackProgressEventArgs e)
        {
            if (!e.PlaybackPositionTicks.HasValue || e.Item is null) return;

            var playSessionData = GetPlaySessionData(e);
            if (playSessionData is null) return;

            playSessionData.PlaybackStartTicks = e.PlaybackPositionTicks.Value;
            playSessionData.PreviousPositionTicks = e.PlaybackPositionTicks.Value;
            playSessionData.PreviousEventTime = DateTime.UtcNow;
            if (!Plugin.ChapterApi.HasIntro(e.Item))
            {
                _logger.Info("Playback start time: " +
                             new TimeSpan(playSessionData.PlaybackStartTicks).ToString(@"hh\:mm\:ss\.fff"));
                _logger.Info("IntroSkip - Detection Started");
            }
        }

        private void OnPlaybackProgress(object sender, PlaybackProgressEventArgs e)
        {
            if (e.Item == null || e.EventName != ProgressEvent.TimeUpdate && e.EventName != ProgressEvent.Unpause ||
                !e.PlaybackPositionTicks.HasValue)
                return;

            var playSessionData = GetPlaySessionData(e);
            if (playSessionData is null) return;

            long currentPositionTicks = e.PlaybackPositionTicks.Value;

            if (e.EventName == ProgressEvent.TimeUpdate && !Plugin.ChapterApi.HasIntro(e.Item))
            {
                DateTime currentEventTime = DateTime.UtcNow;

                double elapsedTime = (currentEventTime - playSessionData.PreviousEventTime).TotalSeconds;
                double positionTimeDiff = TimeSpan.FromTicks(currentPositionTicks - playSessionData.PreviousPositionTicks)
                    .TotalSeconds;

                if (Math.Abs(positionTimeDiff) - elapsedTime > 5 &&
                    currentPositionTicks < playSessionData.MaxIntroDurationTicks)
                {
                    _logger.Info(
                        positionTimeDiff > 0
                            ? "Fast-forward {0} seconds by {1}."
                            : "Rewind {0} seconds by {1}.", positionTimeDiff - elapsedTime, e.Session.UserName);

                    if (!playSessionData.FirstJumpPositionTicks.HasValue &&
                        TimeSpan.FromTicks(playSessionData.PlaybackStartTicks).TotalSeconds < 5 &&
                        positionTimeDiff > 0) //fast-forward only
                    {
                        playSessionData.FirstJumpPositionTicks = playSessionData.PreviousPositionTicks;
                        if (playSessionData.PreviousPositionTicks > 60 * TimeSpan.TicksPerSecond)
                        {
                            _logger.Info("First jump start time: " +
                                         new TimeSpan(playSessionData.FirstJumpPositionTicks.Value).ToString(
                                             @"hh\:mm\:ss\.fff"));
                            playSessionData.MaxIntroDurationTicks += playSessionData.PreviousPositionTicks;
                            _logger.Info("MaxIntroDurationSeconds is extended to: {0} ({1})",
                                TimeSpan.FromTicks(playSessionData.MaxIntroDurationTicks).TotalSeconds
                                , TimeSpan.FromTicks(playSessionData.MaxIntroDurationTicks).ToString(@"hh\:mm\:ss\.fff"));
                        }
                    }

                    playSessionData.LastJumpPositionTicks = currentPositionTicks;
                    _logger.Info("Last jump to time: " +
                                 new TimeSpan(playSessionData.LastJumpPositionTicks.Value).ToString(@"hh\:mm\:ss\.fff"));
                }

                if (currentPositionTicks >= playSessionData.MaxIntroDurationTicks)
                {
                    if (playSessionData.LastJumpPositionTicks.HasValue)
                    {
                        UpdateIntroTask(e.Item as Episode, e.Session,
                            playSessionData.FirstJumpPositionTicks ?? new TimeSpan(0, 0, 0).Ticks,
                            playSessionData.LastJumpPositionTicks.Value);
                    }
                }

                playSessionData.PreviousPositionTicks = currentPositionTicks;
                playSessionData.PreviousEventTime = currentEventTime;
            }

            if (e.EventName == ProgressEvent.Unpause &&
                currentPositionTicks < playSessionData.MaxIntroDurationTicks && Plugin.ChapterApi.HasIntro(e.Item))
            {
                UpdateIntroTask(e.Item as Episode, e.Session, new TimeSpan(0, 0, 0).Ticks, currentPositionTicks);
            }

            if (e.EventName == ProgressEvent.Unpause && e.Item.RunTimeTicks.HasValue &&
                currentPositionTicks > e.Item.RunTimeTicks - playSessionData.MaxCreditsDurationTicks &&
                Plugin.ChapterApi.HasCredits(e.Item))
            {
                if (e.Item.RunTimeTicks.Value > currentPositionTicks)
                {
                    UpdateCreditsTask(e.Item as Episode, e.Session, e.Item.RunTimeTicks.Value - currentPositionTicks);
                }
            }
        }

        private void OnPlaybackStopped(object sender, PlaybackStopEventArgs e)
        {
            if (e.Item is null || !e.PlaybackPositionTicks.HasValue || !e.Item.RunTimeTicks.HasValue) return;

            var playSessionData = GetPlaySessionData(e);
            if (playSessionData is null) return;

            if (!Plugin.ChapterApi.HasCredits(e.Item))
            {
                long currentPositionTicks = e.PlaybackPositionTicks.Value;
                if (currentPositionTicks > e.Item.RunTimeTicks - playSessionData.MaxCreditsDurationTicks)
                {
                    if (e.Item.RunTimeTicks.Value > currentPositionTicks)
                    {
                        UpdateCreditsTask(e.Item as Episode, e.Session,
                            e.Item.RunTimeTicks.Value - currentPositionTicks);
                    }
                }
            }
            _playSessionData.TryRemove(e.PlaySessionId, out _);
        }

        private PlaySessionData GetPlaySessionData(PlaybackProgressEventArgs e)
        {
            if (!IsInScope(e.Item)) return null;
            //&& _userManager.GetUserById(e.Session.UserInternalId).Policy.IsAdministrator; //Admin only

            var playSessionId = e.PlaySessionId;
            if (!_playSessionData.ContainsKey(playSessionId))
            {
                _playSessionData[playSessionId] = new PlaySessionData();
            }

            var playSessionData = _playSessionData[playSessionId];
            return playSessionData;
        }

        public bool IsInScope(BaseItem item)
        {
            bool isEnable = item is Episode && (Plugin.Instance.GetPluginOptions().GeneralOptions.StrmOnly ? item.IsShortcut : true);
            if (!isEnable) return false;
            
            bool isInScope = LibraryPathsInScope.Any(l => item.ContainingFolderPath.StartsWith(l));

            return isInScope;
        }

        private void UpdateIntroTask(Episode episode, SessionInfo session, long introStartPositionTicks,
            long introEndPositionTicks)
        {
            var now = DateTime.UtcNow;

            lock (_introLock)
            {
                if (_ongoingIntroUpdates.ContainsKey(episode))
                {
                    return;
                }

                if (_lastIntroUpdateTimes.TryGetValue(episode, out var lastUpdateTime))
                {
                    if (now - lastUpdateTime < _updateInterval)
                    {
                        return;
                    }
                }

                var task = new Task(() =>
                {
                    try
                    {
                        Plugin.ChapterApi.UpdateIntro(episode, session, introStartPositionTicks,
                            introEndPositionTicks);
                    }
                    catch (Exception e)
                    {
                        _logger.Debug(e.Message);
                        _logger.Debug(e.StackTrace);
                    }
                });

                if (_ongoingIntroUpdates.TryAdd(episode, task))
                {
                    task.ContinueWith(t => { _ongoingIntroUpdates.TryRemove(episode, out _); },
                        TaskContinuationOptions.ExecuteSynchronously);
                    _lastIntroUpdateTimes[episode] = now;
                    task.Start();
                }
            }
        }

        private void UpdateCreditsTask(Episode episode, SessionInfo session, long creditsDurationTicks)
        {
            var now = DateTime.UtcNow;

            lock (_creditsLock)
            {
                if (_ongoingCreditsUpdates.ContainsKey(episode))
                {
                    return;
                }

                if (_lastCreditsUpdateTimes.TryGetValue(episode, out var lastUpdateTime))
                {
                    if (now - lastUpdateTime < _updateInterval)
                    {
                        return;
                    }
                }

                var task = new Task(() =>
                {
                    try
                    {
                        Plugin.ChapterApi.UpdateCredits(episode, session, creditsDurationTicks);
                    }
                    catch (Exception e)
                    {
                        _logger.Debug(e.Message);
                        _logger.Debug(e.StackTrace);
                    }
                });

                if (_ongoingCreditsUpdates.TryAdd(episode, task))
                {
                    task.ContinueWith(t => { _ongoingCreditsUpdates.TryRemove(episode, out _); },
                        TaskContinuationOptions.ExecuteSynchronously);
                    _lastCreditsUpdateTimes[episode] = now;
                    task.Start();
                }
            }
        }
        
        public void Dispose()
        {
            _sessionManager.PlaybackStart -= OnPlaybackStart;
            _sessionManager.PlaybackProgress -= OnPlaybackProgress;
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
            _libraryManager.ItemAdded -= OnItemAdded;
            if (QueueManager.IntroSkipTokenSource != null) QueueManager.IntroSkipTokenSource.Cancel();
        }
    }
}
