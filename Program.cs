using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Foundation;
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
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private TaskbarOverlayForm _overlay = default!;
    private readonly NotifyIcon _trayIcon;
    private readonly OverlayConfig _config;
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
    private long _mediaUpdateSeq;
    private ToolStripMenuItem _playPauseMenuItem = default!;
    private ToolStripMenuItem _prevMenuItem = default!;
    private ToolStripMenuItem _nextMenuItem = default!;
    private ToolStripMenuItem _albumArtMenuItem = default!;
    private ToolStripMenuItem _layoutMenuItem = default!;

    private void HookSession(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }
        catch (Exception ex) { Log($"HookSession: {ex.Message}"); }
    }

    private void UnhookSession(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }
        catch (Exception ex) { Log($"UnhookSession: {ex.Message}"); }
    }
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
        _config = OverlayConfig.Load();
        _notificationsEnabled = _config.NotificationsEnabled;

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Now On Taskbar",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };
        _trayIcon.ContextMenuStrip.Items.Add("Settings", null, (_, _) => OpenSettings());
        _prevMenuItem = new ToolStripMenuItem("⏮ Previous") { Enabled = false };
        _prevMenuItem.Click += (_, _) => TryMediaOp(s => s.TrySkipPreviousAsync());
        _trayIcon.ContextMenuStrip.Items.Add(_prevMenuItem);
        _playPauseMenuItem = new ToolStripMenuItem("▶ Play") { Enabled = false };
        _playPauseMenuItem.Click += (_, _) => TryPlayPause();
        _trayIcon.ContextMenuStrip.Items.Add(_playPauseMenuItem);
        _nextMenuItem = new ToolStripMenuItem("⏭ Next") { Enabled = false };
        _nextMenuItem.Click += (_, _) => TryMediaOp(s => s.TrySkipNextAsync());
        _trayIcon.ContextMenuStrip.Items.Add(_nextMenuItem);
        _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _trayIcon.ContextMenuStrip.Items.Add("Auto-start", null, (_, _) =>
        {
            ToggleAutoStart();
        });
        _notifMenuItem = new ToolStripMenuItem("Notifications") { Checked = _notificationsEnabled };
        _notifMenuItem.Click += (_, _) => ToggleNotifications();
        _trayIcon.ContextMenuStrip.Items.Add(_notifMenuItem);
        _albumArtMenuItem = new ToolStripMenuItem("Album Art") { Checked = _config.ShowAlbumArt };
        _albumArtMenuItem.Click += (_, _) => ToggleAlbumArt();
        _trayIcon.ContextMenuStrip.Items.Add(_albumArtMenuItem);
        _layoutMenuItem = new ToolStripMenuItem("Two-line Layout") { Checked = _config.TwoLineLayout };
        _layoutMenuItem.Click += (_, _) => ToggleLayout();
        _trayIcon.ContextMenuStrip.Items.Add(_layoutMenuItem);
        _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) =>
        {
            _trayIcon.Visible = false;
            Application.Exit();
        });

        _overlay = new TaskbarOverlayForm();
        _overlay.ApplyConfig(_config);
        _overlay.LeftClicked += OnOverlayClicked;
        _overlay.Show();

        InitMedia();
        Log("Constructor: queuing InitNotifications");
        _overlay.BeginInvoke(InitNotifications);

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        _healthTimer = new System.Windows.Forms.Timer { Interval = 120_000 };
        _healthTimer.Tick += (_, _) => { try { CheckHealthThenReinit(); } catch (Exception ex) { Log($"HealthTimer: {ex.Message}"); } };
        _healthTimer.Start();
    }

    private void InitMedia()
    {
        if (_mediaReinitializing) { Log("InitMedia: blocked by guard"); return; }
        _mediaReinitializing = true;
        Log("InitMedia: enter");

        try
        {
            if (_mediaDispatcher == null)
            {
                _mediaDispatcher = DispatcherQueueController.CreateOnDedicatedThread();
                Log("InitMedia: created dispatcher");
            }

            var queue = _mediaDispatcher.DispatcherQueue;
            queue.TryEnqueue(async () =>
            {
                try
                {
                    if (_currentSession != null)
                    {
                        UnhookSession(_currentSession);
                        _currentSession = null;
                    }
                    if (_mediaManager != null)
                    {
                        try { _mediaManager.SessionsChanged -= OnSessionsChanged; } catch (Exception ex) { Log($"InitMedia: unsub SessionsChanged failed: {ex.Message}"); }
                        try { _mediaManager.CurrentSessionChanged -= OnCurrentSessionChanged; } catch (Exception ex) { Log($"InitMedia: unsub CurrentSessionChanged failed: {ex.Message}"); }
                        _mediaManager = null;
                    }

                    _mediaManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    _mediaManager.SessionsChanged += OnSessionsChanged;
                    _mediaManager.CurrentSessionChanged += OnCurrentSessionChanged;
                    Log("InitMedia: got session manager");

                    _currentSession = _mediaManager.GetCurrentSession();
                    Log($"InitMedia: current session = {(_currentSession != null ? _currentSession.SourceAppUserModelId : "null")}");
                    if (_currentSession != null)
                    {
                        HookSession(_currentSession);
                        await UpdateFromSession(_currentSession);
                    }
                }
                catch (Exception ex) { Log($"InitMedia: inner dispatch failed: {ex.Message}"); }
            });
        }
        catch (Exception ex) { Log($"InitMedia: outer dispatch failed: {ex.Message}"); }
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
                try { _notifListener.NotificationChanged -= OnNotificationChanged; } catch (Exception ex) { Log($"InitNotifications: unsub failed: {ex.Message}"); }
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
            var hresult = Marshal.GetHRForException(ex);
            Log($"InitNotifications: EXCEPTION {ex.GetType().Name}: {ex.Message} (HRESULT=0x{hresult:X8})");
            ShowNotifError(ex is OperationCanceledException
                ? "Listener timed out (5s). COM object likely dead."
                : $"Error: {(!string.IsNullOrEmpty(ex.Message) ? ex.Message : $"Unknown (HRESULT=0x{hresult:X8})")}");
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
        catch (Exception ex) { Log($"ShowNotifError failed: {ex.Message}"); }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            try { InitMedia(); } catch (Exception ex) { Log($"OnPowerModeChanged InitMedia failed: {ex.Message}"); }
            try { if (!_overlay.IsDisposed) _overlay.BeginInvoke(new Action(InitNotifications)); } catch (ObjectDisposedException) { } catch (InvalidOperationException) { } catch (Exception ex) { Log($"OnPowerModeChanged InitNotifications failed: {ex.Message}"); }
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            try { InitMedia(); } catch (Exception ex) { Log($"OnSessionSwitch InitMedia failed: {ex.Message}"); }
            try { if (!_overlay.IsDisposed) _overlay.BeginInvoke(new Action(InitNotifications)); } catch (ObjectDisposedException) { } catch (InvalidOperationException) { } catch (Exception ex) { Log($"OnSessionSwitch InitNotifications failed: {ex.Message}"); }
        }
    }

    private void OnOverlayClicked()
    {
        try
        {
            var session = _currentSession;
            if (session == null) return;
            var aumid = session.SourceAppUserModelId;
            if (string.IsNullOrEmpty(aumid)) return;

            var appName = aumid.Contains('!') ? aumid.Split('!')[1] : aumid;

            Task.Run(() =>
            {
                try
                {
                    var processes = Process.GetProcessesByName(appName);
                    var hWnd = processes
                        .Select(p => p.MainWindowHandle)
                        .FirstOrDefault(h => h != IntPtr.Zero);
                    if (hWnd == IntPtr.Zero) return;
                    if (!_overlay.IsDisposed)
                        _overlay.BeginInvoke(() => SetForegroundWindow(hWnd));
                }
                catch (Exception ex) { Log($"OnOverlayClicked: Task failed: {ex.Message}"); }
            });
        }
        catch (Exception ex) { Log($"OnOverlayClicked failed: {ex.Message}"); }
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
                UITitle("", "");
        }
        catch (Exception ex) { Log($"OnSessionsChanged: {ex.Message}"); }
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
                try { _overlay.BeginInvoke(() => _overlay.ShowNotification(senderName, message)); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }
            _overlay.ShowNotification(senderName, message);
        }
        catch (Exception ex) { Log($"OnNotificationChanged: {ex.Message}"); }
    }

    private async void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        try
        {
            if (_currentSession != null)
                UnhookSession(_currentSession);

            _currentSession = sender.GetCurrentSession();
            if (_currentSession != null)
            {
                HookSession(_currentSession);
                await UpdateFromSession(_currentSession);
            }
            else
            {
                UITitle("", "");
            }
        }
        catch (Exception ex) { Log($"OnCurrentSessionChanged: {ex.Message}"); }
    }

    private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        var mySeq = Interlocked.Increment(ref _mediaUpdateSeq);
        try
        {
            await Task.Delay(100);
            if (mySeq != Interlocked.Read(ref _mediaUpdateSeq)) return;
            await UpdateFromSession(sender);
        }
        catch (Exception ex) { Log($"OnMediaPropertiesChanged: {ex.Message}"); }
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        try
        {
            var info = sender.GetPlaybackInfo();
            var isPlaying = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            UIPlaybackState(isPlaying);
        }
        catch (Exception ex) { Log($"OnPlaybackInfoChanged: {ex.Message}"); }
    }

    private async Task UpdateFromSession(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            var title = props?.Title?.Trim() ?? "";
            var artist = props?.Artist?.Trim() ?? "";
            UITitle(title, artist);

            Bitmap? art = null;
            try
            {
                var thumb = props?.Thumbnail;
                if (thumb != null)
                {
                    using var stream = await thumb.OpenReadAsync();
                    using var ms = new MemoryStream();
                    using (var s = stream.AsStreamForRead())
                        await s.CopyToAsync(ms);
                    ms.Position = 0;
                    var raw = new Bitmap(ms);
                    art = new Bitmap(raw, _overlay.AlbumArtSize, _overlay.AlbumArtSize);
                    raw.Dispose();
                }
            }
            catch (Exception ex) { Log($"UpdateFromSession: thumbnail failed: {ex.GetType().Name}: {ex.Message}"); art = null; }
            UIAlbumArt(art);

            try
            {
                var info = session.GetPlaybackInfo();
                var isPlaying = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                UIPlaybackState(isPlaying);
            }
            catch (Exception ex) { Log($"UpdateFromSession: playback info failed: {ex.Message}"); }
        }
        catch (Exception ex) { Log($"UpdateFromSession: {ex.Message}"); }
    }

    private void UITitle(string title, string artist = "")
    {
        if (_overlay.IsDisposed) return;
        if (_overlay.InvokeRequired)
        {
            try { _overlay.BeginInvoke(() => UITitle(title, artist)); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            return;
        }
        _overlay.SetTitle(title, artist);
    }

    private void UIAlbumArt(Bitmap? art)
    {
        if (_overlay.IsDisposed) return;
        if (_overlay.InvokeRequired)
        {
            try { _overlay.BeginInvoke(() => UIAlbumArt(art)); }
            catch (ObjectDisposedException) { art?.Dispose(); }
            catch (InvalidOperationException) { art?.Dispose(); }
            return;
        }
        _overlay.SetAlbumArt(art);
    }

    private void UIPlaybackState(bool isPlaying)
    {
        if (_overlay.IsDisposed) return;
        if (_overlay.InvokeRequired)
        {
            try { _overlay.BeginInvoke(() => UIPlaybackState(isPlaying)); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            return;
        }
        _overlay.SetPlaybackState(isPlaying);
        UpdatePlayPauseMenuItem(isPlaying);
    }

    private void UpdatePlayPauseMenuItem(bool isPlaying)
    {
        if (_trayIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            try { _trayIcon.ContextMenuStrip.BeginInvoke(() => UpdatePlayPauseMenuItem(isPlaying)); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            return;
        }

        var session = _currentSession;
        _playPauseMenuItem.Text = isPlaying ? "⏸ Pause" : "▶ Play";
        _playPauseMenuItem.Enabled = session != null;
        _prevMenuItem.Enabled = session != null;
        _nextMenuItem.Enabled = session != null;
    }

    private void TryPlayPause()
    {
        try
        {
            var session = _currentSession;
            if (session == null) return;
            var info = session.GetPlaybackInfo();
            var playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            if (playing)
                session.TryPauseAsync().AsTask().ContinueWith(t => Log($"TryPause failed: {t.Exception?.Message}"), TaskContinuationOptions.OnlyOnFaulted);
            else
                session.TryPlayAsync().AsTask().ContinueWith(t => Log($"TryPlay failed: {t.Exception?.Message}"), TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (Exception ex) { Log($"TryPlayPause: {ex.Message}"); }
    }

    private void TryMediaOp(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> op)
    {
        try
        {
            var session = _currentSession;
            if (session == null) return;
            op(session).AsTask().ContinueWith(t => Log($"TryMediaOp failed: {t.Exception?.Message}"), TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (Exception ex) { Log($"TryMediaOp: {ex.Message}"); }
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_config);
        if (form.ShowDialog() == DialogResult.OK)
        {
            form.ApplyToConfig();
            _config.NotificationsEnabled = _notificationsEnabled;
            _config.Save();
            _overlay.ApplyConfig(_config);
        }
    }

    private void ToggleNotifications()
    {
        _notificationsEnabled = !_notificationsEnabled;
        _notifMenuItem.Checked = _notificationsEnabled;
        _trayIcon.ShowBalloonTip(1500, "Now On Taskbar",
            _notificationsEnabled ? "Notifications enabled" : "Notifications silenced",
            ToolTipIcon.Info);
    }

    private void ToggleAlbumArt()
    {
        _config.ShowAlbumArt = !_config.ShowAlbumArt;
        _albumArtMenuItem.Checked = _config.ShowAlbumArt;
        _config.Save();
        _overlay.ApplyConfig(_config);
        _trayIcon.ShowBalloonTip(1500, "Now On Taskbar",
            _config.ShowAlbumArt ? "Album art shown" : "Album art hidden",
            ToolTipIcon.Info);
    }

    private void ToggleLayout()
    {
        _config.TwoLineLayout = !_config.TwoLineLayout;
        _layoutMenuItem.Checked = _config.TwoLineLayout;
        _config.Save();
        _overlay.ApplyConfig(_config);
        _trayIcon.ShowBalloonTip(1500, "Now On Taskbar",
            _config.TwoLineLayout ? "Two-line layout" : "Single-line layout",
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
        catch (Exception ex) { Log($"ToggleAutoStart: {ex.Message}"); }
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

                try
                {
                    dispatcher.DispatcherQueue.TryEnqueue(async () =>
                    {
                        try
                        {
                            if (oldSession != null)
                                UnhookSession(oldSession);
                            if (oldManager != null)
                            {
                                oldManager.SessionsChanged -= OnSessionsChanged;
                                oldManager.CurrentSessionChanged -= OnCurrentSessionChanged;
                            }
                        }
                        catch (Exception ex) { Log($"Dispose: unsub failed: {ex.Message}"); }

                        try
                        {
                            await dispatcher.ShutdownQueueAsync();
                        }
                        catch (Exception ex) { Log($"Dispose: shutdown failed: {ex.Message}"); }
                    });
                }
                catch (Exception ex) { Log($"Dispose: TryEnqueue failed: {ex.Message}"); }
            }

            _config.NotificationsEnabled = _notificationsEnabled;
            _config.Save();
            _overlay?.Dispose();
            _trayIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
