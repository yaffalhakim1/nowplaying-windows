using System.Runtime.InteropServices;
using Windows.Media.Control;
using Windows.System;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace NowOnTaskbar;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var app = new AppContext();
        Application.Run();
    }
}

public class AppContext : ApplicationContext
{
    private TaskbarOverlayForm _overlay = default!;
    private readonly NotifyIcon _trayIcon;
    private GlobalSystemMediaTransportControlsSessionManager? _mediaManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private UserNotificationListener? _notifListener;
    private readonly HashSet<uint> _seenNotifIds = new();
    private bool _notificationsEnabled = true;
    private ToolStripMenuItem _notifMenuItem = default!;

    public AppContext()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Now On Taskbar",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };
        _trayIcon.ContextMenuStrip.Items.Add("Auto-start", null, (_, _) =>
        {
            ToggleAutoStart();
        });
        _notifMenuItem = new ToolStripMenuItem("Notifications") { Checked = true };
        _notifMenuItem.Click += (_, _) => ToggleNotifications();
        _trayIcon.ContextMenuStrip.Items.Add(_notifMenuItem);
        _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) =>
        {
            _trayIcon.Visible = false;
            Application.Exit();
        });

        _overlay = new TaskbarOverlayForm();
        _overlay.Show();

        InitMedia();
        _overlay.BeginInvoke(InitNotifications);
    }

    private void InitMedia()
    {
        try
        {
            var controller = DispatcherQueueController.CreateOnDedicatedThread();
            var queue = controller.DispatcherQueue;

            queue.TryEnqueue(async () =>
            {
                try
                {
                    _mediaManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    _mediaManager.CurrentSessionChanged += OnCurrentSessionChanged;

                    _currentSession = _mediaManager.GetCurrentSession();
                    if (_currentSession != null)
                    {
                        _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                        await UpdateFromSession(_currentSession);
                    }
                }
                catch { }
            });
        }
        catch { }
    }

    private async void InitNotifications()
    {
        try
        {
            _notifListener = UserNotificationListener.Current;
            var access = await _notifListener.RequestAccessAsync();
            if (access != UserNotificationListenerAccessStatus.Allowed) return;

            var existing = await _notifListener.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var n in existing)
                _seenNotifIds.Add(n.Id);

            _notifListener.NotificationChanged += OnNotificationChanged;
        }
        catch { }
    }

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        try
        {
            if (args.ChangeKind != UserNotificationChangedKind.Added) return;
            if (!_notificationsEnabled) return;

            var notif = sender.GetNotification(args.UserNotificationId);
            if (notif == null || !_seenNotifIds.Add(notif.Id)) return;

            var visual = notif.Notification.Visual;
            var binding = visual.GetBinding("ToastGeneric");
            if (binding == null) return;

            var texts = binding.GetTextElements()
                .Select(t => t.Text.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            if (texts.Count == 0) return;

            // texts[0] = app/browser name, texts[1] = sender, texts[2] = message
            // Show last 2 elements as "sender: message", or just single text
            string senderName = texts.Count >= 3 ? texts[1] : (texts.Count >= 2 ? texts[0] : "");
            string message = texts.Count >= 3 ? texts[2] : (texts.Count >= 2 ? texts[1] : texts[0]);

            if (_overlay.IsDisposed) return;
            if (_overlay.InvokeRequired)
            {
                _overlay.BeginInvoke(() => _overlay.ShowNotification(senderName, message));
                return;
            }
            _overlay.ShowNotification(senderName, message);
        }
        catch { }
    }

    private async void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        if (_currentSession != null)
            _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;

        _currentSession = sender.GetCurrentSession();
        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
            await UpdateFromSession(_currentSession);
        }
        else
        {
            UITitle("");
        }
    }

    private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        await UpdateFromSession(sender);
    }

    private async Task UpdateFromSession(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            var title = props?.Title?.Trim();
            var artist = props?.Artist?.Trim();
            var display = !string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(artist)
                ? $"{title} — {artist}"
                : title ?? "";
            UITitle(display);
        }
        catch { }
    }

    private void UITitle(string title)
    {
        if (_overlay.IsDisposed) return;
        if (_overlay.InvokeRequired)
        {
            _overlay.BeginInvoke(() => UITitle(title));
            return;
        }
        _overlay.SetTitle(title);
    }

    private void ToggleNotifications()
    {
        _notificationsEnabled = !_notificationsEnabled;
        _notifMenuItem.Checked = _notificationsEnabled;
        _trayIcon.ShowBalloonTip(1500, "Now On Taskbar",
            _notificationsEnabled ? "Notifications enabled" : "Notifications silenced",
            ToolTipIcon.Info);
    }

    private void ToggleAutoStart()
    {
        try
        {
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            string appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
            var existing = key.GetValue("NowOnTaskbar") as string;

            if (existing == appPath)
            {
                key.DeleteValue("NowOnTaskbar");
                _trayIcon.ShowBalloonTip(2000, "Now On Taskbar",
                    "Auto-start disabled", ToolTipIcon.Info);
            }
            else
            {
                key.SetValue("NowOnTaskbar", appPath);
                _trayIcon.ShowBalloonTip(2000, "Now On Taskbar",
                    "Auto-start enabled", ToolTipIcon.Info);
            }
        }
        catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_currentSession != null)
                _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            if (_mediaManager != null)
                _mediaManager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _overlay?.Dispose();
            _trayIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
