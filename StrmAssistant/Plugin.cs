using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins.UI;
using MediaBrowser.Model.Serialization;
using StrmAssistant.Common;
using StrmAssistant.IntroSkip;
using StrmAssistant.Mod;
using StrmAssistant.Options;
using StrmAssistant.Options.Store;
using StrmAssistant.Options.View;
using StrmAssistant.Web.Helper;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Options.GeneralOptions;
using static StrmAssistant.Options.Utility;

namespace StrmAssistant
{
    public class Plugin : BasePlugin, IHasThumbImage, IHasUIPages
    {
        private List<IPluginUIPageController> _pages;
        public readonly PluginOptionsStore MainOptionsStore;
        public readonly MediaInfoExtractOptionsStore MediaInfoExtractStore;
        public readonly MetadataEnhanceOptionsStore MetadataEnhanceStore;
        public readonly IntroSkipOptionsStore IntroSkipStore;
        public readonly ExperienceEnhanceOptionsStore ExperienceEnhanceStore;

        public static Plugin Instance { get; private set; }
        public static LibraryApi LibraryApi { get; private set; }
        public static ChapterApi ChapterApi { get; private set; }
        public static FingerprintApi FingerprintApi { get; private set; }
        public static NotificationApi NotificationApi { get; private set; }
        public static SubtitleApi SubtitleApi { get; private set; }
        public static PlaySessionMonitor PlaySessionMonitor { get; private set; }
        public static MetadataApi MetadataApi { get; private set; }

        private readonly Guid _id = new Guid("63c322b7-a371-41a3-b11f-04f8418b37d8");

        public readonly ILogger Logger;
        public readonly IApplicationHost ApplicationHost;
        public readonly IApplicationPaths ApplicationPaths;

        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;

        public Plugin(IApplicationHost applicationHost, IApplicationPaths applicationPaths, ILogManager logManager,
            IFileSystem fileSystem, ILibraryManager libraryManager, ISessionManager sessionManager,
            IItemRepository itemRepository, INotificationManager notificationManager,
            IMediaSourceManager mediaSourceManager, IMediaMountManager mediaMountManager,
            IMediaProbeManager mediaProbeManager, ILocalizationManager localizationManager, IUserManager userManager,
            IUserDataManager userDataManager, IFfmpegManager ffmpegManager, IMediaEncoder mediaEncoder,
            IJsonSerializer jsonSerializer, IHttpClient httpClient, IServerApplicationHost serverApplicationHost,
            IServerConfigurationManager configurationManager)
        {
            Instance = this;
            Logger = logManager.GetLogger(Name);
            Logger.Info("Plugin is getting loaded.");
            ApplicationHost = applicationHost;
            ApplicationPaths = applicationPaths;

            _libraryManager = libraryManager;
            _userManager = userManager;
            _userDataManager = userDataManager;

            MainOptionsStore = new PluginOptionsStore(applicationHost, Logger, Name);
            MediaInfoExtractStore =
                new MediaInfoExtractOptionsStore(applicationHost, Logger, Name + "_" + nameof(MediaInfoExtractOptions));
            MetadataEnhanceStore =
                new MetadataEnhanceOptionsStore(applicationHost, Logger, Name + "_" + nameof(MetadataEnhanceOptions));
            IntroSkipStore = new IntroSkipOptionsStore(applicationHost, Logger, Name + "_" + nameof(IntroSkipOptions));
            ExperienceEnhanceStore =
                new ExperienceEnhanceOptionsStore(applicationHost, Logger, Name + "_" + nameof(ExperienceEnhanceOptions));

            LibraryApi = new LibraryApi(libraryManager, fileSystem, mediaSourceManager, mediaMountManager,
                itemRepository, jsonSerializer, userManager);
            ChapterApi = new ChapterApi(libraryManager, itemRepository);
            FingerprintApi = new FingerprintApi(libraryManager, fileSystem, applicationPaths, ffmpegManager,
                mediaEncoder, mediaMountManager, jsonSerializer, serverApplicationHost);
            PlaySessionMonitor = new PlaySessionMonitor(libraryManager, userManager, sessionManager);
            NotificationApi = new NotificationApi(notificationManager, userManager, sessionManager);
            SubtitleApi = new SubtitleApi(libraryManager, fileSystem, mediaProbeManager, localizationManager,
                itemRepository);
            MetadataApi = new MetadataApi(libraryManager, fileSystem, configurationManager, localizationManager,
                jsonSerializer, httpClient);
            ShortcutMenuHelper.Initialize(configurationManager);

            PatchManager.Initialize();
            if (MainOptionsStore.GetOptions().GeneralOptions.CatchupMode)
            {
                UpdateCatchupScope(MainOptionsStore.GetOptions().GeneralOptions.CatchupTaskScope);
                QueueManager.Initialize();
            }
            if (IntroSkipStore.GetOptions().EnableIntroSkip) PlaySessionMonitor.Initialize();

            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemUpdated += OnItemUpdated;
            _libraryManager.ItemRemoved += OnItemRemoved;
            _userManager.UserCreated += OnUserCreated;
            _userManager.UserDeleted += OnUserDeleted;
            _userManager.UserConfigurationUpdated += OnUserConfigurationUpdated;
            _userDataManager.UserDataSaved += OnUserDataSaved;
        }

        private void OnUserCreated(object sender, GenericEventArgs<User> e)
        {
            LibraryApi.FetchUsers();
        }

        private void OnUserDeleted(object sender, GenericEventArgs<User> e)
        {
            LibraryApi.FetchUsers();
        }

        private void OnUserConfigurationUpdated(object sender, GenericEventArgs<User> e)
        {
            if (e.Argument.Policy.IsAdministrator) LibraryApi.FetchAdminOrderedViews();
        }

        private void OnItemAdded(object sender, ItemChangeEventArgs e)
        {
            if ((e.Item is Video || e.Item is Audio) && MainOptionsStore.PluginOptions.GeneralOptions.CatchupMode)
            {
                if (IntroSkipStore.IntroSkipOptions.UnlockIntroSkip && IsCatchupTaskSelected(CatchupTask.Fingerprint) &&
                    FingerprintApi.IsLibraryInScope(e.Item) && e.Item is Episode)
                {
                    QueueManager.FingerprintItemQueue.Enqueue(e.Item);
                }
                else
                {
                    if (IsCatchupTaskSelected(CatchupTask.MediaInfo) &&
                        (MediaInfoExtractStore.MediaInfoExtractOptions.ExclusiveExtract || e.Item.IsShortcut))
                    {
                        QueueManager.MediaInfoExtractItemQueue.Enqueue(e.Item);
                    }

                    if (IsCatchupTaskSelected(CatchupTask.IntroSkip) &&
                        PlaySessionMonitor.IsLibraryInScope(e.Item))
                    {
                        if (!LibraryApi.HasMediaInfo(e.Item))
                        {
                            QueueManager.MediaInfoExtractItemQueue.Enqueue(e.Item);
                        }
                        else if (e.Item is Episode episode && ChapterApi.SeasonHasIntroCredits(episode))
                        {
                            QueueManager.IntroSkipItemQueue.Enqueue(episode);
                        }
                    }
                }
            }

            if (e.Item is Movie || e.Item is Series || e.Item is Episode)
            {
                NotificationApi.FavoritesUpdateSendNotification(e.Item);
            }
        }

        private void OnItemUpdated(object sender, ItemChangeEventArgs e)
        {
            if (MetadataEnhanceStore.GetOptions().EnhanceMovieDbPerson &&
                (e.UpdateReason & (ItemUpdateType.MetadataDownload | ItemUpdateType.MetadataImport)) != 0)
            {
                if (e.Item is Season season && season.IndexNumber > 0)
                {
                    LibraryApi.UpdateSeriesPeople(season.Parent as Series);
                }
                else if (e.Item is Series series)
                {
                    LibraryApi.UpdateSeriesPeople(series);
                }
            }
        }

        private void OnItemRemoved(object sender, ItemChangeEventArgs e)
        {
            if (MediaInfoExtractStore.GetOptions().PersistMediaInfo && (e.Item is Video || e.Item is Audio))
            {
                Task.Run(() => LibraryApi.DeleteMediaInfoJson(e.Item, CancellationToken.None));
            }
        }

        private void OnUserDataSaved(object sender, UserDataSaveEventArgs e)
        {
            if (e.UserData.IsFavorite)
            {
                if (MainOptionsStore.PluginOptions.GeneralOptions.CatchupMode &&
                    IntroSkipStore.IntroSkipOptions.UnlockIntroSkip && IsCatchupTaskSelected(CatchupTask.Fingerprint) &&
                    FingerprintApi.IsLibraryInScope(e.Item) && (e.Item is Series || e.Item is Episode))
                {
                    QueueManager.FingerprintItemQueue.Enqueue(e.Item);
                }
                else if (MainOptionsStore.PluginOptions.GeneralOptions.CatchupMode &&
                         IsCatchupTaskSelected(CatchupTask.MediaInfo))
                {
                    QueueManager.MediaInfoExtractItemQueue.Enqueue(e.Item);
                }
            }
        }

        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        public override string Description => "Extract MediaInfo and Enable IntroSkip";

        public override Guid Id => _id;

        public sealed override string Name => "Strm Assistant";

        public string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString();

        public string UserAgent => $"{Name}/{CurrentVersion}";

        public CultureInfo DefaultUICulture =>
            new CultureInfo(MainOptionsStore.GetOptions().AboutOptions.DefaultUICulture);

        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".Properties.thumb.png");
        }

        public IReadOnlyCollection<IPluginUIPageController> UIPageControllers
        {
            get
            {
                if (_pages == null)
                {
                    _pages = new List<IPluginUIPageController>
                    {
                        new MainPageController(GetPluginInfo(), _libraryManager, MainOptionsStore,
                            MediaInfoExtractStore, MetadataEnhanceStore, IntroSkipStore, ExperienceEnhanceStore)
                    };
                }

                return _pages.AsReadOnly();
            }
        }
    }
}
