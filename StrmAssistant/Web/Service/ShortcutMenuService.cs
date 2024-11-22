extern alias SystemMemory;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;
using StrmAssistant.Web.Helper;

namespace StrmAssistant.Web.Service
{
    [Unauthenticated]
    public class ShortcutMenuService : IService, IRequiresRequest
    {
        private readonly IHttpResultFactory _resultFactory;

        public ShortcutMenuService(IHttpResultFactory resultFactory)
        {
            _resultFactory = resultFactory;
        }

        public IRequest Request { get; set; }

        public object Get(GetStrmAssistantJs request)
        {
            return _resultFactory.GetResult(Request, (SystemMemory::System.ReadOnlyMemory<byte>) ShortcutMenuHelper.StrmAssistantJs.GetBuffer(), "application/x-javascript");
        }

        public object Get(GetShortcutMenu request)
        {
            return _resultFactory.GetResult(
                SystemMemory::System.MemoryExtensions.AsSpan(ShortcutMenuHelper.ModifiedShortcutsString),
                "application/x-javascript");
        }
    }
}
