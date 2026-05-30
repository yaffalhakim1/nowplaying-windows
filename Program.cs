using Microsoft.Win32;
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
    private DispatcherQueueController? _mediaDispatcher;
    private UserNotificationListener? _notifListener;
    private readonly HashSet<uint> _seenNotifIds = new();
    private bool _notificationsEnabled = true;
    private ToolStripMenuItem _notifMenuItem = default!;
    private bool _notifReinitializing;
    private bool _mediaReinitializing;
    private readonly System.Windows.Forms.Timer _healthTimer;
    private readonly object _notifLock = new();
    private readonly string _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NowOnTaskbar", "log.txt");

    private void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(_logPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { }
    }

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
        Log("Constructor: queuing InitNotifications");
        _overlay.BeginInvoke(InitNotifications);

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        _healthTimer = new System.Windows.Forms.Timer { Interval = 120_000 };
        _healthTimer.Tick += (_, _) => CheckHealthThenReinit();
        _healthTimer.Start();
    }

    private void InitMedia()
    {
        if (_mediaReinitializing) return;
        _mediaReinitializing = true;

        try
        {
            if (_mediaDispatcher == null)
            {
                _mediaDispatcher = DispatcherQueueController.CreateOnDedicatedThread();
            }

            var queue = _mediaDispatcher.DispatcherQueue;
            queue.TryEnqueue(async () =>
            {
                try
                {
                    if (_currentSession != null)
                    {
                        try { _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged; } catch { }
                        _currentSession = null;
                    }
                    if (_mediaManager != null)
                    {
                        try { _mediaManager.SessionsChanged -= OnSessionsChanged; } catch { }
                        try { _mediaManager.CurrentSessionChanged -= OnCurrentSessionChanged; } catch { }
                        _mediaManager = null;
                    }

                    _mediaManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    _mediaManager.SessionsChanged += OnSessionsChanged;
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
        finally
        {
            _mediaReinitializing = false;
        }
    }

    private async void InitNotifications()
    {
        Log("InitNotifications: enter");
        if (_notifReinitializing) { Log("InitNotifications: blocked by guard"); return; }
        _notifReinitializing = true;

        try
        {
            if (_notifListener != null)
            {
                try { _notifListener.NotificationChanged -= OnNotificationChanged; } catch { }
                _notifListener = null;
            }

            _notifListener = UserNotificationListener.Current;
            Log("InitNotifications: got listener, requesting access...");

            var accessCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var accessTask = _notifListener.RequestAccessAsync().AsTask().WaitAsync(accessCts.Token);
            var access = await accessTask;
            Log($"InitNotifications: access={access}");

            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                _notifListener = null;
                ShowNotifError($"Access denied: {access}. Run register-sparse.ps1 as admin.");
                return;
            }

            var existingCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var existing = await _notifListener.GetNotificationsAsync(NotificationKinds.Toast)
                .AsTask().WaitAsync(existingCts.Token);
            Log($"InitNotifications: got {existing.Count} existing");

            lock (_notifLock)
            {
                _seenNotifIds.Clear();
                foreach (var n in existing)
                    _seenNotifIds.Add(n.Id);
            }

            _notifListener.NotificationChanged += OnNotificationChanged;
            Log("InitNotifications: subscribed OK");
        }
        catch (Exception ex)
        {
            _notifListener = null;
            Log($"InitNotifications: EXCEPTION {ex.GetType().Name}: {ex.Message}");
            ShowNotifError(ex is OperationCanceledException
                ? "Listener timed out (5s). COM object likely dead."
                : $"Error: {ex.Message}");
        }
        finally
        {
            _notifReinitializing = false;
            Log("InitNotifications: exit");
        }
    }

    private void ShowNotifError(string message)
    {
        try { _trayIcon.ShowBalloonTip(3000, "Notifications Failed", message, ToolTipIcon.Warning); }
        catch { }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            try { InitMedia(); } catch { }
            try { if (!_overlay.IsDisposed) _overlay.BeginInvoke(new Action(InitNotifications)); } catch { }
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            try { InitMedia(); } catch { }
            try { if (!_overlay.IsDisposed) _overlay.BeginInvoke(new Action(InitNotifications)); } catch { }
        }
    }

    private void CheckHealthThenReinit()
    {
        CheckMediaHealth();
        CheckNotifHealth();
    }

    private void CheckMediaHealth()
    {
        try
        {
            if (_mediaManager == null || _mediaDispatcher == null)
            {
                Log("Health: media null, reinitializing");
                InitMedia();
                return;
            }

            _mediaDispatcher.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    _mediaManager.GetSessions();
                }
                catch
                {
                    Log("Health: media dead, reinitializing");
                    InitMedia();
                }
            });
        }
        catch { InitMedia(); }
    }

    private void CheckNotifHealth()
    {
        try
        {
            if (_notifListener == null)
            {
                Log("Health: notif null, reinitializing");
                InitNotifications();
                return;
            }

            var status = _notifListener.GetAccessStatus();
            if (status == UserNotificationListenerAccessStatus.Allowed)
                return;

            Log($"Health: notif access={status}, reinitializing");
            InitNotifications();
        }
        catch
        {
            Log("Health: notif dead, reinitializing");
            InitNotifications();
        }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        try
        {
            var session = sender.GetCurrentSession();
            if (session == null)
                UITitle("");
        }
        catch { }
    }

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        try
        {
            UserNotification? notif = null;
            lock (_notifLock)
            {
                if (args.ChangeKind == UserNotificationChangedKind.Removed)
                {
                    _seenNotifIds.Remove(args.UserNotificationId);
                    return;
                }
                if (args.ChangeKind != UserNotificationChangedKind.Added) return;
                if (!_notificationsEnabled) return;

                notif = sender.GetNotification(args.UserNotificationId);
                if (notif == null) return;
                if (!_seenNotifIds.Add(notif.Id)) return;

                if (_seenNotifIds.Count > 500)
                    _seenNotifIds.Clear();
            }

            Log($"OnNotificationChanged: id={notif.Id}");

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
        try
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
        catch { }
    }

    private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        try { await UpdateFromSession(sender); } catch { }
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
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            _healthTimer.Dispose();

            if (_mediaDispatcher != null)
            {
                var oldSession = _currentSession;
                var oldManager = _mediaManager;
                var dispatcher = _mediaDispatcher;

                dispatcher.DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        if (oldSession != null)
                            oldSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                        if (oldManager != null)
                        {
                            oldManager.SessionsChanged -= OnSessionsChanged;
                            oldManager.CurrentSessionChanged -= OnCurrentSessionChanged;
                        }
                    }
                    catch { }

                    try
                    {
                        await dispatcher.ShutdownQueueAsync();
                    }
                    catch { }
                });
            }

            _overlay?.Dispose();
            _trayIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
