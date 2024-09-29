using Emby.Notifications;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Session;
using StrmAssistant.Properties;
using System;
using System.Linq;
using System.Threading;

namespace StrmAssistant
{
    public class NotificationApi
    {
        private readonly ILogger _logger;
        private readonly INotificationManager _notificationManager;
        private readonly IUserManager _userManager;
        private readonly ISessionManager _sessionManager;

        public NotificationApi(INotificationManager notificationManager, IUserManager userManager, ISessionManager sessionManager)
        {
            _logger = Plugin.Instance.logger;
            _notificationManager = notificationManager;
            _userManager = userManager;
            _sessionManager = sessionManager;
        }

        public void FavoritesUpdateSendNotification(BaseItem item)
        {
            Resources.Culture = Thread.CurrentThread.CurrentUICulture;

            var users = Plugin.LibraryApi.GetUsersByFavorites(item);
            foreach (var user in users)
            {
                var request = new NotificationRequest
                {
                    Title =
                        Resources.PluginOptions_EditorTitle_Strm_Assistant,
                    EventId = "favorites.update",
                    User = user,
                    Item = item,
                    Description =
                        string.Format(
                            Resources.Notification_CatchupUpdate_EventDescription.Replace("\\n",
                                Environment.NewLine), item.Path, user)
                };
                _notificationManager.SendNotification(request);
            }
        }

        public async void IntroUpdateSendNotification(Episode episode, SessionInfo session, string introStartTime,
            string introEndTime)
        {
            Resources.Culture = Thread.CurrentThread.CurrentUICulture;

            if (CanDisplayMessage(session))
            {
                var message = new MessageCommand
                {
                    Header = Resources.PluginOptions_EditorTitle_Strm_Assistant,
                    Text = string.Format(
                        Resources.Notification_IntroUpdate_Message, episode.FindSeriesName(), episode.FindSeasonName()),
                    TimeoutMs = 500
                };
                await _sessionManager.SendMessageCommand(session.Id, session.Id, message, CancellationToken.None);
            }

            var request = new NotificationRequest
            {
                Title =
                    Resources.PluginOptions_EditorTitle_Strm_Assistant,
                EventId = "introskip.update",
                User = _userManager.GetUserById(session.UserInternalId),
                Item = episode,
                Session = session,
                Description = string.Format(
                    Resources.Notification_IntroUpdate_Description.Replace("\\n", Environment.NewLine),
                    episode.FindSeriesName(), episode.FindSeasonName(), introStartTime, introEndTime,
                    session.UserName)
            };
            _notificationManager.SendNotification(request);
        }

        public async void CreditsUpdateSendNotification(Episode episode, SessionInfo session, string creditsDuration)
        {
            Resources.Culture = Thread.CurrentThread.CurrentUICulture;

            if (CanDisplayMessage(session))
            {
                var message = new MessageCommand
                {
                    Header = Resources.PluginOptions_EditorTitle_Strm_Assistant,
                    Text = string.Format(
                        Resources.Notification_CreditsUpdate_Message, episode.FindSeriesName(), episode.FindSeasonName()),
                    TimeoutMs = 500
                };
                await _sessionManager.SendMessageCommand(session.Id, session.Id, message, CancellationToken.None);
            }

            var request = new NotificationRequest
            {
                Title =
                    Resources.PluginOptions_EditorTitle_Strm_Assistant,
                EventId = "introskip.update",
                User = _userManager.GetUserById(session.UserInternalId),
                Item = episode,
                Session = session,
                Description = string.Format(
                    Resources.Notification_CreditsUpdate_Description.Replace("\\n", Environment.NewLine),
                    episode.FindSeriesName(), episode.FindSeasonName(), creditsDuration, session.UserName)

            };
            _notificationManager.SendNotification(request);
        }

        private bool CanDisplayMessage(SessionInfo session)
        {
            return session.SupportedCommands.Contains("DisplayMessage");
        }
    }
}
