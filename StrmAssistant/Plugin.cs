using Emby.Web.GenericEdit.Common;
using MediaBrowser.Common;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StrmAssistant
{
    public class Plugin: BasePluginSimpleUI<PluginOptions>, IHasThumbImage
    {
        public static Plugin Instance { get; private set; }
        public static LibraryApi LibraryApi { get; private set; }
        public static ChapterApi ChapterApi { get; private set; }
        public static NotificationApi NotificationApi { get; private set; }
        public static PlaySessionMonitor PlaySessionMonitor { get; private set; }

        private readonly Guid _id = new Guid("63c322b7-a371-41a3-b11f-04f8418b37d8");

        public readonly ILogger logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;

        private int _currentMaxConcurrentCount;
        private bool _currentCatchupMode;
        private bool _currentEnableIntroSkip;
        private bool _currentMergeMultiVersion;

        public Plugin(IApplicationHost applicationHost,
            ILogManager logManager,
            IFileSystem fileSystem,
            ILibraryManager libraryManager,
            ISessionManager sessionManager,
            IItemRepository itemRepository,
            INotificationManager notificationManager,
            IUserManager userManager,
            IUserDataManager userDataManager) : base(applicationHost)
        {
            Instance = this;
            logger = logManager.GetLogger(Name);
            logger.Info("Plugin is getting loaded.");

            _currentMaxConcurrentCount = GetOptions().MediaInfoExtractOptions.MaxConcurrentCount;
            QueueManager.Initialize();

            _currentMergeMultiVersion = GetOptions().ModOptions.MergeMultiVersion;
            Patch.Initialize();

            _libraryManager = libraryManager;
            _userManager = userManager;
            _userDataManager = userDataManager;

            LibraryApi = new LibraryApi(libraryManager, fileSystem, userManager);
            ChapterApi = new ChapterApi(libraryManager, itemRepository);
            NotificationApi = new NotificationApi(notificationManager, userManager, sessionManager);

            _currentCatchupMode = GetOptions().GeneralOptions.CatchupMode;
            if (_currentCatchupMode)
            {
                InitializeCatchupMode();
            }

            PlaySessionMonitor = new PlaySessionMonitor(libraryManager, sessionManager, itemRepository, userManager);
            _currentEnableIntroSkip = GetOptions().IntroSkipOptions.EnableIntroSkip;
            if (_currentEnableIntroSkip)
            {
                PlaySessionMonitor.Initialize();
            }
        }

        public void Dispose()
        {
            DisposeCatchupMode();
            PlaySessionMonitor.Dispose();
        }

        private void InitializeCatchupMode()
        {
            DisposeCatchupMode();

            _libraryManager.ItemAdded += OnItemAdded;
            _userDataManager.UserDataSaved += OnUserDataSaved;
            _userManager.UserCreated += OnUserCreated;
            _userManager.UserDeleted += OnUserDeleted;

            if (QueueManager.MediaInfoExtractProcessTask == null || QueueManager.MediaInfoExtractProcessTask.IsCompleted)
            {
                QueueManager.MediaInfoExtractProcessTask =
                    Task.Run(() => QueueManager.MediaInfoExtract_ProcessItemQueueAsync());
            }
        }

        private void DisposeCatchupMode()
        {
            _libraryManager.ItemAdded -= OnItemAdded;
            _userDataManager.UserDataSaved -= OnUserDataSaved;
            _userManager.UserCreated -= OnUserCreated;
            _userManager.UserDeleted -= OnUserDeleted;
        }

        private void OnUserCreated(object sender, GenericEventArgs<User> e)
        {
            LibraryApi.FetchUsers();
        }

        private void OnUserDeleted(object sender, GenericEventArgs<User> e)
        {
            LibraryApi.FetchUsers();
        }

        private void OnItemAdded(object sender, ItemChangeEventArgs e)
        {
            if (e.Item.IsShortcut) //Strm only for real-time extract
            {
                QueueManager.MediaInfoExtractItemQueue.Enqueue(e.Item);
            }

            NotificationApi.CatchupUpdateSendNotification(e.Item);
        }

        private void OnUserDataSaved(object sender, UserDataSaveEventArgs e)
        {
            if (e.UserData.IsFavorite)
            {
                QueueManager.MediaInfoExtractItemQueue.Enqueue(e.Item);
            }
        }
        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        public override string Description => "Extract MediaInfo and Enable IntroSkip";

        public override Guid Id => _id;

        public sealed override string Name => "Strm Assistant";

        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".Properties.thumb.png");
        }

        public PluginOptions GetPluginOptions()
        {
            return this.GetOptions();
        }

        protected override void OnOptionsSaved(PluginOptions options)
        {
            logger.Info("MaxConcurrentCount is set to {0}", options.MediaInfoExtractOptions.MaxConcurrentCount);
            if (_currentMaxConcurrentCount != options.MediaInfoExtractOptions.MaxConcurrentCount)
            {
                _currentMaxConcurrentCount = options.MediaInfoExtractOptions.MaxConcurrentCount;
                QueueManager.UpdateSemaphore(_currentMaxConcurrentCount);
                Patch.UpdateResourcePool(_currentMaxConcurrentCount);
            }

            logger.Info("StrmOnly is set to {0}", options.GeneralOptions.StrmOnly);
            logger.Info("IncludeExtra is set to {0}", options.MediaInfoExtractOptions.IncludeExtra);
            logger.Info("EnableImageCapture is set to {0}", options.MediaInfoExtractOptions.EnableImageCapture);

            logger.Info("MergeMultiVersion is set to {0}", options.ModOptions.MergeMultiVersion);
            if (_currentMergeMultiVersion!= GetOptions().ModOptions.MergeMultiVersion)
            {
                _currentMergeMultiVersion = GetOptions().ModOptions.MergeMultiVersion;

                if (_currentMergeMultiVersion)
                {
                    Patch.PatchIsEligibleForMultiVersion();
                }
                else
                {
                    Patch.UnpatchIsEligibleForMultiVersion();
                }
            }

            logger.Info("CatchupMode is set to {0}", options.GeneralOptions.CatchupMode);
            if (_currentCatchupMode != options.GeneralOptions.CatchupMode)
            {
                _currentCatchupMode = options.GeneralOptions.CatchupMode;

                if (options.GeneralOptions.CatchupMode)
                {
                    InitializeCatchupMode();
                }
                else
                {
                    DisposeCatchupMode();
                }
            }

            logger.Info("EnableIntroSkip is set to {0}", options.IntroSkipOptions.EnableIntroSkip);
            logger.Info("MaxIntroDurationSeconds is set to {0}", options.IntroSkipOptions.MaxIntroDurationSeconds);
            logger.Info("MaxCreditsDurationSeconds is set to {0}", options.IntroSkipOptions.MaxCreditsDurationSeconds);
            
            if (_currentEnableIntroSkip != options.IntroSkipOptions.EnableIntroSkip)
            {
                _currentEnableIntroSkip = options.IntroSkipOptions.EnableIntroSkip;
                if (options.IntroSkipOptions.EnableIntroSkip)
                {
                    PlaySessionMonitor.Initialize();
                }
                else
                {
                    PlaySessionMonitor.Dispose();
                }
            }

            if (!_currentCatchupMode && !_currentEnableIntroSkip)
            {
                if (QueueManager.MediaInfoExtractTokenSource != null) QueueManager.MediaInfoExtractTokenSource.Cancel();
            }

            var libraryScope = string.Join(", ",
                options.MediaInfoExtractOptions.LibraryScope.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v =>
                        options.MediaInfoExtractOptions.LibraryList.FirstOrDefault(option => option.Value == v)?.Name));
            logger.Info("MediaInfoExtract - LibraryScope is set to {0}",
                string.IsNullOrEmpty(libraryScope) ? "ALL" : libraryScope);

            var intoSkipLibraryScope = string.Join(", ",
                options.IntroSkipOptions.LibraryScope.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => options.IntroSkipOptions.LibraryList
                        .FirstOrDefault(option => option.Value == v)
                        ?.Name));
            logger.Info("IntroSkip - LibraryScope is set to {0}",
                string.IsNullOrEmpty(intoSkipLibraryScope) ? "ALL" : intoSkipLibraryScope);
            PlaySessionMonitor.UpdateLibraryPaths();
            
            base.OnOptionsSaved(options);
        }

        protected override PluginOptions OnBeforeShowUI(PluginOptions options)
        {
            var libraries = _libraryManager.GetVirtualFolders();

            var list = new List<EditorSelectOption>();
            var listShows = new List<EditorSelectOption>();

            foreach (var item in libraries)
            {
                var selectOption = new EditorSelectOption
                {
                    Value = item.ItemId,
                    Name = item.Name,
                    IsEnabled = true,
                };

                list.Add(selectOption);

                if (item.CollectionType == "tvshows" || item.CollectionType is null) // null means mixed content library
                {
                    listShows.Add(selectOption);
                }
            }

            options.MediaInfoExtractOptions.LibraryList = list;
            options.IntroSkipOptions.LibraryList = listShows;

            return base.OnBeforeShowUI(options);
        }
    }
}
