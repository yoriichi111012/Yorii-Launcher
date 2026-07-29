using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Yorii_Launcher.Helpers
{
    public static class NotificationHelper
    {
        // fire-and-forget toast notification, user doesnt have to click anything
        public static void Show(string title, string message)
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }
    }
}
