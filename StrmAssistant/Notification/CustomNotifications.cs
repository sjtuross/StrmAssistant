using Emby.Notifications;
using MediaBrowser.Controller;
using System.Collections.Generic;
using StrmAssistant.Properties;

namespace StrmAssistant.Notification
{
    public class CustomNotifications : INotificationTypeFactory
    {
        private readonly IServerApplicationHost _appHost;

        public CustomNotifications(IServerApplicationHost appHost) => _appHost = appHost;

        public List<NotificationTypeInfo> GetNotificationTypes(string language)
        {
            return new List<NotificationTypeInfo>
            {
                new NotificationTypeInfo
                {
                    Id = "favorites.update",
                    Name = Resources.Notification_CatchupUpdate_EventName,
                    CategoryId = "strm.assistant",
                    CategoryName = Resources.PluginOptions_EditorTitle_Strm_Assistant
                },
                new NotificationTypeInfo
                {
                    Id = "introskip.update",
                    Name = Resources.Notification_IntroSkipUpdate_EventName,
                    CategoryId = "strm.assistant",
                    CategoryName = Resources.PluginOptions_EditorTitle_Strm_Assistant
                }
            };
        }
    }
}
