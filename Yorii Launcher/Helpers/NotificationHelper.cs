using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Yorii_Launcher.Helpers
{
    public static class NotificationHelper
    {
        // fire-and-forget toast notification, user doesnt have to click anything
        public static void Show(string title, string message, bool silent = false)
        {
            var builder = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message);

            if (silent)
                builder.MuteAudio();

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
    }
}
