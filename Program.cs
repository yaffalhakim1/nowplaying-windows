using System.Runtime.InteropServices;
using Windows.Media.Control;
using Windows.System;

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
        _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) =>
        {
            _trayIcon.Visible = false;
            Application.Exit();
        });

        _overlay = new TaskbarOverlayForm();
        _overlay.Show();

        InitMedia();
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
