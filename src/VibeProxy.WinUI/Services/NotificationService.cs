using System.Security;
using Microsoft.Windows.AppNotifications;

namespace VibeProxy.WinUI.Services;

public sealed class NotificationService
{
    public void Register()
    {
        AppNotificationManager.Default.Register();
    }

    public void Show(string title, string body)
    {
        var safeTitle = SecurityElement.Escape(title) ?? string.Empty;
        var safeBody = SecurityElement.Escape(body) ?? string.Empty;
        var xml = $"<toast><visual><binding template=\"ToastGeneric\"><text>{safeTitle}</text><text>{safeBody}</text></binding></visual></toast>";
        var notification = new AppNotification(xml);
        AppNotificationManager.Default.Show(notification);
    }
}