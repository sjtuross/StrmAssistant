﻿using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    [Route("/{Web}/modules/shortcuts.js", "GET", IsHidden = true)]
    [Unauthenticated]
    public class GetShortcutMenu
    {
        public string Web { get; set; }
    }
}
