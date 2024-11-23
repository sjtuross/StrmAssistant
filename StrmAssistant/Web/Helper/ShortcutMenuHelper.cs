using MediaBrowser.Controller.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace StrmAssistant.Web
{
    internal static class ShortcutMenuHelper
    {
        public static string ModifiedShortcutsString { get; private set; }

        public static MemoryStream StrmAssistantJs { get; private set; }

        public static void Initialize(IServerConfigurationManager configurationManager)
        {
            try
            {
                StrmAssistantJs = GetResourceStream("strmassistant.js");
                ModifyShortcutMenu(configurationManager);
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Debug("ShortcutMenuHelper - Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
            }
        }

        private static MemoryStream GetResourceStream(string resourceName)
        {
            var name = typeof(Plugin).Namespace + ".Web.Resources." + resourceName;
            var manifestResourceStream = typeof (ShortcutMenuHelper).GetTypeInfo().Assembly.GetManifestResourceStream(name);
            var destination = new MemoryStream((int) manifestResourceStream.Length);
            manifestResourceStream.CopyTo(destination);
            return destination;
        }

        private static void ModifyShortcutMenu(IServerConfigurationManager configurationManager)
        {
            string shortcutsJs;
            var shortcutsJsStream = GetResourceStream("shortcuts.js");
            shortcutsJsStream.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(shortcutsJsStream))
            {
                shortcutsJs = reader.ReadToEnd();
            }

            const string injectShortcutCommand = @"
const strmAssistantCommandSource = {
    getCommands: function(options) {
        const locale = this.globalize.getCurrentLocale().toLowerCase();
        const commandName = (locale === 'zh-cn') ? '\u590D\u5236' : (['zh-hk', 'zh-tw'].includes(locale) ? '\u8907\u8F38' : 'Copy');
        if (options.items?.length === 1 && options.items[0].LibraryOptions && options.items[0].Type === 'VirtualFolder') {
            return [{ name: commandName, id: 'strmassistant', icon: 'content_copy' }];
        }
        return [];
    },
    executeCommand: function(command, items) {
        return require(['components/strmassistant/strmassistant']).then(responses => responses[0].copy(items[0].Id));
    }
};

setTimeout(() => {
    Emby.importModule('./modules/common/globalize.js').then(globalize => {
        strmAssistantCommandSource.globalize = globalize;
        Emby.importModule('./modules/common/itemmanager/itemmanager.js').then(itemmanager => {
            itemmanager.registerCommandSource(strmAssistantCommandSource);
        });
    });
}, 3000);
    ";
            
            ModifiedShortcutsString = shortcutsJs + injectShortcutCommand;
        }
    }
}
