using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MediaBrowser.Controller.Configuration;

namespace StrmAssistant.Web.Helper
{
    internal static class ShortcutMenuHelper
    {
        public static string ModifiedShortcutsString { get; private set; }

        public static MemoryStream StrmAssistantJs { get; private set; }

        public static void Initialize(IServerConfigurationManager configurationManager)
        {
            StrmAssistantJs = GetResourceStream("strmassistant.js");
            ModifyShortcutMenu(configurationManager);
        }

        private static MemoryStream GetResourceStream(string resourceName)
        {
            var name = typeof(Plugin).Namespace + ".Web.Resources." + resourceName;
            var manifestResourceStream = typeof (ShortcutMenuHelper).GetTypeInfo().Assembly.GetManifestResourceStream(name);
            var destination = new MemoryStream((int) manifestResourceStream.Length);
            manifestResourceStream.CopyTo((Stream) destination);
            return destination;
        }

        private static void ModifyShortcutMenu(IServerConfigurationManager configurationManager)
        {
            var dashboardSourcePath = configurationManager.Configuration.DashboardSourcePath ??
                                      Path.Combine(configurationManager.ApplicationPaths.ApplicationResourcesPath,
                                          "dashboard-ui");

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
            var dataExplorer2Assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Emby.DataExplorer2");

            ModifiedShortcutsString = File.ReadAllText(Path.Combine(dashboardSourcePath, "modules", "shortcuts.js")) +
                                      injectShortcutCommand;

            if (dataExplorer2Assembly != null)
            {
                var contextMenuHelperType = dataExplorer2Assembly.GetType("Emby.DataExplorer2.Api.ContextMenuHelper");
                var modifiedShortcutsProperty = contextMenuHelperType?.GetProperty("ModifiedShortcutsString",
                    BindingFlags.Static | BindingFlags.Public);
                var setMethod = modifiedShortcutsProperty?.GetSetMethod(true);

                if (modifiedShortcutsProperty?.GetValue(null) is string originalValue && setMethod != null)
                {
                    ModifiedShortcutsString = originalValue + injectShortcutCommand;
                    setMethod.Invoke(null, new object[] { ModifiedShortcutsString });
                }
            }
        }
    }
}
