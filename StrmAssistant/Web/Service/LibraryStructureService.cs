using System;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web
{
    [Authenticated]
    public class LibraryStructureService : IService, IRequiresRequest
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;

        public LibraryStructureService(ILibraryManager libraryManager)
        {
            _logger = Plugin.Instance.logger;
            _libraryManager = libraryManager;
        }

        public IRequest Request { get; set; }

        public void Post(CopyVirtualFolder request)
        {
            var source = _libraryManager.GetItemById(request.Id);
            var options = _libraryManager.GetLibraryOptions(_libraryManager.GetItemById(request.Id));
            var suffix = new Random().Next(100, 999).ToString();
            _libraryManager.AddVirtualFolder(source.Name + " #" + suffix, options, false);
        }
    }
}
