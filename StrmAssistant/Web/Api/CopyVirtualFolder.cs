using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web
{
    [Route("/Library/VirtualFolders/Copy", "POST")]
    [Authenticated(Roles = "Admin")]
    public class CopyVirtualFolder : IReturnVoid, IReturn
    {
        public string Id { get; set; }
    }
}
