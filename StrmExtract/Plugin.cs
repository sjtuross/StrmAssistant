using Emby.Web.GenericEdit.Common;
using MediaBrowser.Common;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StrmExtract
{
    public class Plugin: BasePluginSimpleUI<PluginOptions>, IHasThumbImage
    {
        public static Plugin Instance { get; private set; }
        public static LibraryUtility LibraryUtility { get; private set; }

        private readonly Guid _id = new Guid("6107fc8c-443a-4171-b70e-7590658706d8");

        public readonly ILogger logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;

        private bool _currentCatchupMode;
        private static Task processTask;

        public Plugin(IApplicationHost applicationHost,
            ILogManager logManager,
            IFileSystem fileSystem,
            ILibraryManager libraryManager,
            IUserManager userManager,
            IUserDataManager userDataManager) : base(applicationHost)
        {
            Instance = this;
            logger = logManager.GetLogger(Name);
            logger.Info("Plugin is getting loaded.");

            _libraryManager = libraryManager;
            _userManager = userManager;
            _userDataManager = userDataManager;

            _currentCatchupMode = GetOptions().CatchupMode;
            if (_currentCatchupMode)
            {
                _libraryManager.ItemAdded += OnItemAdded;
                _userDataManager.UserDataSaved += OnUserDataSaved;
                _userManager.UserCreated += OnUserCreated;
                _userManager.UserDeleted += OnUserDeleted;
                processTask = Task.Run(() => QueueManager.ProcessItemQueueAsync());
            }

            LibraryUtility = new LibraryUtility(libraryManager, fileSystem, userManager);
        }

        public void Dispose()
        {
            _libraryManager.ItemAdded -= OnItemAdded;
            _userDataManager.UserDataSaved -= OnUserDataSaved;
            _userManager.UserCreated -= OnUserCreated;
            _userManager.UserDeleted -= OnUserDeleted;
            QueueManager._cts.Cancel();
        }

        private void OnUserCreated(object sender, GenericEventArgs<User> e)
        {
            LibraryUtility.FetchUsers();
        }

        private void OnUserDeleted(object sender, GenericEventArgs<User> e)
        {
            LibraryUtility.FetchUsers();
        }

        private void OnItemAdded(object sender, ItemChangeEventArgs e)
        {
            if (e.Item.IsShortcut) //Strm exclusive for real-time extract
            {
                QueueManager.itemQueue.Enqueue(e.Item);
            }
        }

        private void OnUserDataSaved(object sender, UserDataSaveEventArgs e)
        {
            if (e.UserData.IsFavorite == true)
            {
                QueueManager.itemQueue.Enqueue(e.Item);
            }
        }
        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        public override string Description => "Extracts info from Strm targets";

        public override Guid Id => _id;

        public sealed override string Name => "Strm Extract";

        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".Images.thumb.png");
        }

        public PluginOptions GetPluginOptions()
        {
            return this.GetOptions();
        }

        protected override void OnOptionsSaved(PluginOptions options)
        {
            logger.Info("MaxConcurrentCount is set to {0}", options.MaxConcurrentCount);
            SemaphoreSlim newSemaphore = new SemaphoreSlim(options.MaxConcurrentCount);
            SemaphoreSlim oldSemaphore = QueueManager.semaphore;
            QueueManager.semaphore = newSemaphore;
            oldSemaphore.Dispose();

            logger.Info("StrmOnly is set to {0}", options.StrmOnly);
            logger.Info("IncludeExtra is set to {0}", options.IncludeExtra);

            logger.Info("CatchupMode is set to {0}", options.CatchupMode);

            if (_currentCatchupMode != options.CatchupMode)
            {
                _currentCatchupMode = options.CatchupMode;

                if (options.CatchupMode)
                {
                    _libraryManager.ItemAdded -= OnItemAdded;
                    _userDataManager.UserDataSaved -= OnUserDataSaved;
                    _userManager.UserCreated -= OnUserCreated;
                    _userManager.UserDeleted -= OnUserDeleted;

                    _libraryManager.ItemAdded += OnItemAdded;
                    _userDataManager.UserDataSaved += OnUserDataSaved;
                    _userManager.UserCreated += OnUserCreated;
                    _userManager.UserDeleted += OnUserDeleted;

                    if (processTask == null || processTask.IsCompleted)
                    {
                        processTask = Task.Run(() => QueueManager.ProcessItemQueueAsync());
                        
                    }
                }
                else
                {
                    Dispose();
                }
            }

            var libraryScope = string.Join(", ", options.LibraryScope
                .Split(',')
                .Select(v => options.LibraryList
                    .FirstOrDefault(option => option.Value == v)?.Name));

            logger.Info("LibraryScope is set to {0}", string.IsNullOrEmpty(libraryScope) ? "ALL" : libraryScope);

            base.OnOptionsSaved(options);
        }

        protected override PluginOptions OnBeforeShowUI(PluginOptions options)
        {
            var libraries = _libraryManager.GetVirtualFolders();

            var list = new List<EditorSelectOption>();

            foreach (var item in libraries)
            {
                list.Add(new EditorSelectOption
                {
                    Value = item.ItemId,
                    Name = item.Name,
                    IsEnabled = true,
                });
            }

            options.LibraryList = list;

            return base.OnBeforeShowUI(options);
        }
    }
}
