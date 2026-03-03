using System;
using System.Windows.Forms;

namespace EcoFlowMonitor.Actions
{
    public static class NotificationAction
    {
        private static NotifyIcon _sharedIcon;

        public static void SetSharedIcon(NotifyIcon icon) => _sharedIcon = icon;

        /// <summary>
        /// Shows a balloon tip via the system tray icon (always available) and,
        /// on Windows 10+, also attempts a native toast notification.
        /// </summary>
        public static void Show(string title, string body)
        {
            // Balloon tip works on all Windows versions with a tray icon
            _sharedIcon?.ShowBalloonTip(5000, title, body, ToolTipIcon.Warning);

            // Attempt modern toast on Windows 10+
            if (Environment.OSVersion.Version.Major >= 10)
            {
                TryShowToast(title, body);
            }
        }

        private static void TryShowToast(string title, string body)
        {
            try
            {
                var builder = new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
                    .AddText(title)
                    .AddText(body);
                builder.Show();
            }
            catch
            {
                // Toast unavailable (missing package, sandbox restriction, etc.)
                // The balloon tip shown above is the fallback.
            }
        }
    }
}
