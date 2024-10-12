using Emby.Web.GenericEdit.Common;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.List;
using MediaBrowser.Common;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using StrmAssistant.Mod;
using StrmAssistant.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace StrmAssistant
{
    public class Plugin: BasePluginSimpleUI<PluginOptions>, IHasThumbImage
    {
        public static Plugin Instance { get; private set; }
        public static LibraryApi LibraryApi { get; private set; }
        public static ChapterApi ChapterApi { get; private set; }
        public static NotificationApi NotificationApi { get; private set; }
        public static SubtitleApi SubtitleApi { get; private set; }
        public static PlaySessionMonitor PlaySessionMonitor { get; private set; }
        public static MetadataApi MetadataApi { get; private set; }

        private readonly Guid _id = new Guid("63c322b7-a371-41a3-b11f-04f8418b37d8");

        public readonly ILogger logger;
        public readonly IApplicationHost ApplicationHost;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;

        private int _currentMaxConcurrentCount;
        private bool _currentEnableImageCapture;
        private bool _currentCatchupMode;
        private bool _currentEnableIntroSkip;
        private bool _currentUnlockIntroSkip;
        private bool _currentMergeMultiVersion;
        private bool _currentChineseMovieDb;
        private bool _currentExclusiveExtract;
        private bool _currentPreferOriginalPoster;

        public Plugin(IApplicationHost applicationHost,
            ILogManager logManager,
            IFileSystem fileSystem,
            ILibraryManager libraryManager,
            ISessionManager sessionManager,
            IItemRepository itemRepository,
            INotificationManager notificationManager,
            IMediaSourceManager mediaSourceManager,
            IMediaProbeManager mediaProbeManager,
            ILocalizationManager localizationManager,
            IUserManager userManager,
            IUserDataManager userDataManager,
            IServerConfigurationManager configurationManager) : base(applicationHost)
        {
            Instance = this;
            logger = logManager.GetLogger(Name);
            logger.Info("Plugin is getting loaded.");
            ApplicationHost = applicationHost;

            _libraryManager = libraryManager;
            _userManager = userManager;
            _userDataManager = userDataManager;

            _currentMaxConcurrentCount = GetOptions().MediaInfoExtractOptions.MaxConcurrentCount;
            _currentEnableImageCapture = GetOptions().MediaInfoExtractOptions.EnableImageCapture;
            _currentCatchupMode = GetOptions().GeneralOptions.CatchupMode;
            _currentEnableIntroSkip = GetOptions().IntroSkipOptions.EnableIntroSkip;
            _currentUnlockIntroSkip = GetOptions().IntroSkipOptions.UnlockIntroSkip;
            _currentMergeMultiVersion = GetOptions().ModOptions.MergeMultiVersion;
            _currentChineseMovieDb = GetOptions().ModOptions.ChineseMovieDb;
            _currentExclusiveExtract = GetOptions().ModOptions.ExclusiveExtract;
            _currentPreferOriginalPoster = GetOptions().ModOptions.PreferOriginalPoster;

            LibraryApi = new LibraryApi(libraryManager, fileSystem, mediaSourceManager, userManager);
            ChapterApi = new ChapterApi(libraryManager, itemRepository);
            PlaySessionMonitor = new PlaySessionMonitor(libraryManager, userManager, sessionManager);
            NotificationApi = new NotificationApi(notificationManager, userManager, sessionManager);
            SubtitleApi = new SubtitleApi(libraryManager, fileSystem, mediaProbeManager, localizationManager,
                itemRepository);
            MetadataApi = new MetadataApi(libraryManager, fileSystem, configurationManager, localizationManager);

            PatchManager.Initialize();
            if (_currentCatchupMode) InitializeCatchupMode();
            if (_currentEnableIntroSkip) PlaySessionMonitor.Initialize();
            QueueManager.Initialize();
            _libraryManager.ItemAdded += OnItemAdded;
        }

        private void InitializeCatchupMode()
        {
            DisposeCatchupMode();

            _userDataManager.UserDataSaved += OnUserDataSaved;
            _userManager.UserCreated += OnUserCreated;
            _userManager.UserDeleted += OnUserDeleted;
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
            if (_currentCatchupMode && (_currentExclusiveExtract || e.Item.IsShortcut))
            {
                QueueManager.MediaInfoExtractItemQueue.Enqueue(e.Item);
            }

            if (_currentEnableIntroSkip && PlaySessionMonitor.IsLibraryInScope(e.Item))
            {
                if (!LibraryApi.HasMediaStream(e.Item))
                {
                    QueueManager.MediaInfoExtractItemQueue.Enqueue(e.Item);
                }
                else
                {
                    QueueManager.IntroSkipItemQueue.Enqueue(e.Item as Episode);
                }
            }

            NotificationApi.FavoritesUpdateSendNotification(e.Item);
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
            return GetOptions();
        }

        protected override void OnOptionsSaved(PluginOptions options)
        {
            logger.Info("StrmOnly is set to {0}", options.GeneralOptions.StrmOnly);
            logger.Info("IncludeExtra is set to {0}", options.MediaInfoExtractOptions.IncludeExtra);

            logger.Info("MaxConcurrentCount is set to {0}", options.MediaInfoExtractOptions.MaxConcurrentCount);
            if (_currentMaxConcurrentCount != options.MediaInfoExtractOptions.MaxConcurrentCount)
            {
                _currentMaxConcurrentCount = options.MediaInfoExtractOptions.MaxConcurrentCount;

                QueueManager.UpdateSemaphore(_currentMaxConcurrentCount);

                if (options.MediaInfoExtractOptions.EnableImageCapture)
                    EnableImageCapture.UpdateResourcePool(_currentMaxConcurrentCount);
            }

            logger.Info("EnableImageCapture is set to {0}", options.MediaInfoExtractOptions.EnableImageCapture);
            if (_currentEnableImageCapture != options.MediaInfoExtractOptions.EnableImageCapture)
            {
                _currentEnableImageCapture = options.MediaInfoExtractOptions.EnableImageCapture;

                if (_currentEnableImageCapture)
                {
                    EnableImageCapture.Patch();
                }
                else
                {
                    EnableImageCapture.Unpatch();
                }
            }

            logger.Info("MergeMultiVersion is set to {0}", options.ModOptions.MergeMultiVersion);
            if (_currentMergeMultiVersion!= GetOptions().ModOptions.MergeMultiVersion)
            {
                _currentMergeMultiVersion = GetOptions().ModOptions.MergeMultiVersion;

                if (_currentMergeMultiVersion)
                {
                    MergeMultiVersion.Patch();
                }
                else
                {
                    MergeMultiVersion.Unpatch();
                }
            }

            logger.Info("ExclusiveExtract is set to {0}", options.ModOptions.ExclusiveExtract);
            if (_currentExclusiveExtract != GetOptions().ModOptions.ExclusiveExtract)
            {
                _currentExclusiveExtract = GetOptions().ModOptions.ExclusiveExtract;

                if (_currentExclusiveExtract)
                {
                    ExclusiveExtract.Patch();
                }
                else
                {
                    ExclusiveExtract.Unpatch();
                }
            }

            logger.Info("ChineseMovieDb is set to {0}", options.ModOptions.ChineseMovieDb);
            if (_currentChineseMovieDb != GetOptions().ModOptions.ChineseMovieDb)
            {
                _currentChineseMovieDb = GetOptions().ModOptions.ChineseMovieDb;

                if (_currentChineseMovieDb)
                {
                    ChineseMovieDb.Patch();
                }
                else
                {
                    ChineseMovieDb.Unpatch();
                }
            }

            logger.Info("PreferOriginalPoster is set to {0}", options.ModOptions.PreferOriginalPoster);
            if (_currentPreferOriginalPoster != GetOptions().ModOptions.PreferOriginalPoster)
            {
                _currentPreferOriginalPoster = GetOptions().ModOptions.PreferOriginalPoster;

                if (_currentPreferOriginalPoster)
                {
                    PreferOriginalPoster.Patch();
                }
                else
                {
                    PreferOriginalPoster.Unpatch();
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

            logger.Info("UnlockIntroSkip is set to {0}", options.IntroSkipOptions.UnlockIntroSkip);
            if (_currentUnlockIntroSkip != options.IntroSkipOptions.UnlockIntroSkip)
            {
                _currentUnlockIntroSkip = options.IntroSkipOptions.UnlockIntroSkip;
                if (options.IntroSkipOptions.UnlockIntroSkip)
                {
                    UnlockIntroSkip.Patch();
                }
                else
                {
                    UnlockIntroSkip.Unpatch();
                }
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
            PlaySessionMonitor.UpdateLibraryPathsInScope();
            
            var introSkipUserScope= string.Join(", ",
                options.IntroSkipOptions.UserScope.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => options.IntroSkipOptions.UserList
                        .FirstOrDefault(option => option.Value == v)
                        ?.Name));
            logger.Info("IntroSkip - UserScope is set to {0}",
                string.IsNullOrEmpty(introSkipUserScope) ? "ALL" : introSkipUserScope);
            PlaySessionMonitor.UpdateUsersInScope();

            base.OnOptionsSaved(options);
        }

        protected override PluginOptions OnBeforeShowUI(PluginOptions options)
        {
            var libraries = _libraryManager.GetVirtualFolders();

            var list = new List<EditorSelectOption>();
            var listShows = new List<EditorSelectOption>();

            list.Add(new EditorSelectOption
            {
                Value = "-1",
                Name = Resources.Favorites,
                IsEnabled = true
            });

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

            var languageList = new List<EditorSelectOption>
            {
                new EditorSelectOption
                {
                    Value = "zh-cn",
                    Name = "zh-CN",
                    IsEnabled = true
                },
                new EditorSelectOption
                {
                    Value = "zh-sg",
                    Name = "zh-SG",
                    IsEnabled = true
                },
                new EditorSelectOption
                {
                    Value = "zh-hk",
                    Name = "zh-HK",
                    IsEnabled = true
                },
                new EditorSelectOption
                {
                    Value = "zh-tw",
                    Name = "zh-TW",
                    IsEnabled = true
                },
                new EditorSelectOption
                {
                    Value = "ja-jp",
                    Name = "ja-JP",
                    IsEnabled = true
                }
            };
            options.ModOptions.LanguageList = languageList;

            options.AboutOptions.VersionInfoList.Clear();
            options.AboutOptions.VersionInfoList.Add(
                new GenericListItem
                {
                    PrimaryText = GetVersionHash(),
                    Icon = IconNames.info,
                    IconMode = ItemListIconMode.SmallRegular
                });

            options.AboutOptions.VersionInfoList.Add(
                new GenericListItem
                {
                    PrimaryText = Resources.Repo_Link,
                    Icon = IconNames.code,
                    IconMode = ItemListIconMode.SmallRegular,
                    HyperLink = "https://github.com/sjtuross/StrmAssistant",
                });

            options.AboutOptions.VersionInfoList.Add(
                new GenericListItem
                {
                    PrimaryText = Resources.Wiki_Link,
                    Icon = IconNames.menu_book,
                    IconMode = ItemListIconMode.SmallRegular,
                    HyperLink = "https://github.com/sjtuross/StrmAssistant/wiki",
                });

            var allUsers = LibraryApi.AllUsers;
            var userList = new List<EditorSelectOption>();
            foreach (var user in allUsers)
            {
                var selectOption = new EditorSelectOption
                {
                    Value = user.Key.InternalId.ToString(),
                    Name = (user.Value ? "\ud83d\udc51" : "\ud83d\udc64") + user.Key.Name,
                    IsEnabled = true,
                };

                userList.Add(selectOption);
            }

            options.IntroSkipOptions.UserList = userList;

            return base.OnBeforeShowUI(options);
        }

        protected override void OnCreatePageInfo(PluginPageInfo pageInfo)
        {
            pageInfo.Name = Resources.PluginOptions_EditorTitle_Strm_Assistant;
            pageInfo.EnableInMainMenu = true;

            base.OnCreatePageInfo(pageInfo);
        }

        private static string GetVersionHash()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            var fullVersion = assembly.GetName().Version?.ToString();

            if (informationalVersion != null)
            {
                var parts = informationalVersion.Split('+');
                var shortCommitHash = parts.Length > 1 ? parts[1].Substring(0, 7) : "n/a";
                return $"{fullVersion}+{shortCommitHash}";
            }

            return fullVersion;
        }
    }
}
