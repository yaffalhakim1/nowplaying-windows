using System.Runtime.InteropServices;
using System.Text;

namespace NowOnTaskbar;

public class TaskbarOverlayForm : Form
{
    private string _title = "";
    private string _artist = "";
    private int _scrollOffset;
    private int _textWidth;
    private bool _idle => string.IsNullOrEmpty(_title);
    private const int _preferredWidth = 160;
    private const int _barHeight = 40;
    private const int _gapFromNeighbor = 8;
    private const int _scrollSpeed = 2;
    private const int _scrollIntervalMs = 50;
    private const int _zBumpIntervalMs = 100;

    private enum NotifState { Media, NotifIn, NotifHold, NotifOut }
    private NotifState _notifState = NotifState.Media;
    private string _notifSender = "";
    private string _notifMessage = "";
    private int _notifTextWidth;
    private int _mediaY;
    private int _notifY;
    private readonly Queue<(string sender, string message)> _notifQueue = new();
    private System.Diagnostics.Stopwatch _animSw = new();
    private bool _wasScrolling;
    private const int _animDurationMs = 250;
    private const int _notifHoldMs = 5000;
    private System.Threading.Timer? _animThreadTimer;
    private readonly System.Windows.Forms.Timer _notifHoldTimer = new();

    private static readonly string[] _systemClasses =
    {
        "Start", "TrayNotifyWnd", "TrayDummySearchControl",
        "ReBarWindow32", "MSTaskSwWClass", "MSTaskListWClass"
    };

    private IntPtr _taskbarHwnd;
    private bool _fullScreen;
    private readonly System.Windows.Forms.Timer _scrollTimer = new();
    private readonly System.Windows.Forms.Timer _reposTimer = new();
    private Font _font = new("Segoe UI", 9, FontStyle.Regular);
    private Color _mediaTextColor = Color.FromArgb(240, 255, 255, 255);
    private Color _notifTextColor = Color.FromArgb(255, 180, 220, 255);
    private bool _showBackground;
    private Color _bgColor = Color.FromArgb(180, 26, 26, 46);
    private Bitmap? _albumArt;
    private const int _albumArtSize = 20;
    private const int _albumArtGap = 6;
    private bool _isPlaying;
    private bool _showAlbumArt = true;
    private bool _twoLineLayout;
    private bool _hideArtist;

    public int AlbumArtSize => _albumArtSize;

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public event Action? LeftClicked;

    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_GETOBJECT = 0x003D;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_NCCALCSIZE = 0x0083;
    private const int HTCLIENT = 1;
    private const int HTTRANSPARENT = -1;
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CHILD = 0x40000000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WM_TASKBARCREATED = 0;

    private struct RECT { public int left, top, right, bottom; public int W => right - left; public int H => bottom - top; }

    private static readonly int WM_TASKBARCREATED_MSG = RegisterWindowMessage("TaskbarCreated");

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int RegisterWindowMessage(string lpString);

    public TaskbarOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        TransparencyKey = Color.Black;
        BackColor = Color.Black;
        DoubleBuffered = true;
        Width = 200;
        Height = _barHeight;

        _scrollTimer.Interval = _scrollIntervalMs;
        _scrollTimer.Tick += (_, _) => { try { _scrollOffset -= _scrollSpeed; Invalidate(); } catch (Exception ex) { Log($"ScrollTimer: {ex.Message}"); } };

        _reposTimer.Interval = 1500;
        _reposTimer.Tick += (_, _) => { try { RepositionWithFullscreenCheck(); } catch (Exception ex) { Log($"ReposTimer: {ex.Message}"); } };

        _animThreadTimer = new System.Threading.Timer(AnimTimerCallback, null, Timeout.Infinite, Timeout.Infinite);

        _notifHoldTimer.Interval = _notifHoldMs;
        _notifHoldTimer.Tick += (_, _) =>
        {
            try
            {
                _notifHoldTimer.Stop();
                _animSw.Restart();
                _notifState = NotifState.NotifOut;
                _animThreadTimer?.Change(0, 8);
            }
            catch (Exception ex) { Log($"NotifHoldTimer: {ex.Message}"); }
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80 | 0x8000000;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _taskbarHwnd = FindWindow("Shell_TrayWnd", null);
        AttachToTaskbar();
        _reposTimer.Start();
        Reposition();
    }

    private void AttachToTaskbar()
    {
        if (_taskbarHwnd == IntPtr.Zero) return;
        try
        {
            SetParent(Handle, _taskbarHwnd);
            int style = GetWindowLong(Handle, GWL_STYLE);
            style = (style & ~WS_POPUP) | WS_CHILD;
            SetWindowLong(Handle, GWL_STYLE, style);
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE);
            Log("AttachToTaskbar: attached as child of Shell_TrayWnd");
        }
        catch (Exception ex) { Log($"AttachToTaskbar: {ex.Message}"); }
    }

    private void RepositionWithFullscreenCheck()
    {
        var fg = GetForegroundWindow();
        bool wasFull = _fullScreen;
        _fullScreen = fg != Handle && fg != IntPtr.Zero && IsFullScreenApp(fg);

        if (_fullScreen)
        {
            if (!wasFull) Visible = false;
            return;
        }

        if (wasFull)
        {
            Visible = !_idle;
            if (!_idle) Reposition();
        }
        else
        {
            Reposition();
        }
    }

    private static bool IsFullScreenApp(IntPtr hWnd)
    {
        var clsSb = new StringBuilder(256);
        int len = GetClassName(hWnd, clsSb, 256);
        string cls = len > 0 ? clsSb.ToString(0, len) : "";
        if (cls == "Progman" || cls == "WorkerW")
            return false;

        GetWindowRect(hWnd, out var r);
        var screen = Screen.FromHandle(hWnd);
        var b = screen.Bounds;
        return r.left <= b.Left && r.top <= b.Top && r.right >= b.Right && r.bottom >= b.Bottom;
    }

    private void Reposition()
    {
        if (!IsHandleCreated || IsDisposed) return;

        if (_taskbarHwnd == IntPtr.Zero)
            _taskbarHwnd = FindWindow("Shell_TrayWnd", null);

        if (_taskbarHwnd == IntPtr.Zero) return;

        GetWindowRect(_taskbarHwnd, out var tr);
        int maxW = Math.Min(tr.W / 3, 280);

        if (_notifState != NotifState.Media)
            Width = Math.Max(Width, Math.Min(_notifTextWidth + 40, maxW));
        else if (_idle)
            Width = Math.Min(_preferredWidth, maxW);
        else
        {
            int artW = (_showAlbumArt && _albumArt != null) ? _albumArtSize + _albumArtGap : 0;
            Width = Math.Min(Math.Max(_textWidth + 30 + artW, 80), maxW);
        }

        Height = _barHeight;

        int x = FindLeftOfTrayArea(tr);
        int yCenter = tr.top + (tr.H - _barHeight) / 2;

        Location = new Point(tr.left + x, yCenter);
        Log($"Reposition: x={x}, y={yCenter}, w={Width}, taskbar=({tr.left},{tr.top},{tr.W},{tr.H})");
    }

    private int FindLeftOfTrayArea(RECT taskbarRect)
    {
        var startHwnd = FindWindowEx(_taskbarHwnd, IntPtr.Zero, "Start", null);
        bool isCentered = startHwnd != IntPtr.Zero;
        int startLeft = 0;
        if (isCentered)
        {
            GetWindowRect(startHwnd, out var startRect);
            startLeft = startRect.left - taskbarRect.left;
            isCentered = startLeft > taskbarRect.W * 0.2;
        }

        if (!isCentered)
            return RightOfRightmostChild(taskbarRect);

        // centered taskbar → try right side first, fallback to left of Start
        var trayHwnd = FindWindowEx(_taskbarHwnd, IntPtr.Zero, "TrayNotifyWnd", null);
        if (trayHwnd != IntPtr.Zero)
        {
            GetWindowRect(trayHwnd, out var trayRect);
            int candidateX = trayRect.left - taskbarRect.left - Width;

            if (!HasChildInZone(trayRect.left - taskbarRect.left, candidateX))
                return candidateX;
        }

        // fallback → left of Start button (avoids widgets button)
        return Math.Max(startLeft - Width - _gapFromNeighbor, _gapFromNeighbor);
    }

    private bool HasChildInZone(int zoneRight, int zoneLeft)
    {
        var clsSb = new StringBuilder(256);
        var child = IntPtr.Zero;
        GetWindowRect(_taskbarHwnd, out var tr);

        while ((child = FindWindowEx(_taskbarHwnd, child, null, null)) != IntPtr.Zero)
        {
            int len = GetClassName(child, clsSb, 256);
            string cls = len > 0 ? clsSb.ToString(0, len) : "";
            if (IsSystemWindow(cls, 0)) continue;

            GetWindowRect(child, out var cr);

            int childRight = cr.right - tr.left;
            int childLeft = cr.left - tr.left;

            if (childRight > zoneLeft && childLeft < zoneRight)
                return true;
        }

        return false;
    }

    private int RightOfRightmostChild(RECT taskbarRect)
    {
        var clsSb = new StringBuilder(256);
        var child = IntPtr.Zero;
        int rightmostRight = 0;
        int leftOfRightmost = taskbarRect.W;

        while ((child = FindWindowEx(_taskbarHwnd, child, null, null)) != IntPtr.Zero)
        {
            GetWindowRect(child, out var cr);
            int len = GetClassName(child, clsSb, 256);
            string cls = len > 0 ? clsSb.ToString(0, len) : "";
            int w = cr.W;
            int childRight = cr.right - taskbarRect.left;

            if (!IsSystemWindow(cls, w) && childRight > rightmostRight)
            {
                rightmostRight = childRight;
                leftOfRightmost = cr.left - taskbarRect.left;
            }
        }

        if (rightmostRight > 0)
            return leftOfRightmost - Width - _gapFromNeighbor;

        // fallback: left of tray
        var trayHwnd = FindWindowEx(_taskbarHwnd, IntPtr.Zero, "TrayNotifyWnd", null);
        if (trayHwnd != IntPtr.Zero)
        {
            GetWindowRect(trayHwnd, out var trayRect);
            return trayRect.left - taskbarRect.left - Width - _gapFromNeighbor;
        }
        return taskbarRect.W - Width - 120;
    }

    private static bool IsSystemWindow(string className, int width)
    {
        if (className.StartsWith("Windows.UI.")) return true;
        foreach (var c in _systemClasses)
            if (className == c) return true;
        return width < 30 || width > 500;
    }

    public void SetTitle(string title, string artist = "")
    {
        if (IsDisposed) return;
        Log($"SetTitle: title='{title}', artist='{artist}'");
        _title = title;
        _artist = artist;

        if (_idle)
        {
            _scrollOffset = 0;
            _scrollTimer.Stop();
            Invalidate();
            return;
        }

        int artOffset = (_showAlbumArt && _albumArt != null) ? _albumArtSize + _albumArtGap : 0;

        try
        {
            using var g = CreateGraphics();
            if (_twoLineLayout)
            {
                var icon = _isPlaying ? "▶" : "⏸";
                var artistText = _hideArtist ? "" : _artist;
                if (string.IsNullOrEmpty(artistText))
                    _textWidth = TextRenderer.MeasureText(g, $"{icon}  {title}", _font).Width;
                else
                {
                    var w1 = TextRenderer.MeasureText(g, $"{icon}  {artistText}", _font).Width;
                    var w2 = TextRenderer.MeasureText(g, $"{icon}  {title}", _font).Width;
                    _textWidth = Math.Max(w1, w2);
                }
            }
            else
            {
                var artistText = _hideArtist ? "" : _artist;
                var display = string.IsNullOrEmpty(artistText) ? title : $"{artistText} — {title}";
                _textWidth = TextRenderer.MeasureText(g, $"♫  {display}", _font).Width;
            }
        }
        catch
        {
            _textWidth = (title.Length + artist.Length) * 12;
        }

        if (_textWidth > Width - 10 - artOffset && !_twoLineLayout)
        {
            _scrollOffset = Width - artOffset;
            _scrollTimer.Start();
        }
        else
        {
            _scrollOffset = 0;
            _scrollTimer.Stop();
        }

        Reposition();
        Invalidate();
    }

    public void SetAlbumArt(Bitmap? bitmap)
    {
        if (IsDisposed) return;
        _albumArt?.Dispose();
        _albumArt = bitmap;
        if (_albumArt == null && _showAlbumArt)
        {
            try
            {
                var fb = new Bitmap(_albumArtSize, _albumArtSize);
                using var g = Graphics.FromImage(fb);
                g.Clear(Color.Black);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                TextRenderer.DrawText(g, "♫", _font, new Rectangle(0, 0, _albumArtSize, _albumArtSize),
                    Color.FromArgb(120, 255, 255, 255), Color.Transparent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                _albumArt = fb;
            }
            catch { }
        }
        if (!_idle) SetTitle(_title, _artist);
        else Invalidate();
    }

    public void SetPlaybackState(bool isPlaying)
    {
        if (IsDisposed) return;
        _isPlaying = isPlaying;
        if (!_idle) SetTitle(_title, _artist);
    }

    public void ApplyConfig(OverlayConfig config)
    {
        _font.Dispose();
        _font = new Font(config.FontFamily, config.FontSize, (FontStyle)config.FontStyle);
        _mediaTextColor = Color.FromArgb(config.MediaTextAlpha, Color.FromArgb(config.MediaTextColorArgb));
        _notifTextColor = Color.FromArgb(config.NotifTextAlpha, Color.FromArgb(config.NotifTextColorArgb));
        _showBackground = config.ShowBackground;
        _bgColor = Color.FromArgb(config.BackgroundAlpha, Color.FromArgb(config.BackgroundColorArgb));
        TransparencyKey = Color.FromArgb(config.TransparencyKeyArgb);
        _showAlbumArt = config.ShowAlbumArt;
        _twoLineLayout = config.TwoLineLayout;
        _hideArtist = config.HideArtist;

        if (!_idle)
            SetTitle(_title, _artist);
        Reposition();
        Invalidate();
    }

    public void ShowNotification(string sender, string message)
    {
        if (IsDisposed) return;
        Log($"ShowNotification: sender='{sender}', message='{message}'");

        if (_notifState != NotifState.Media)
        {
            _notifQueue.Enqueue((sender, message));
            return;
        }

        StartNotification(sender, message);
    }

    private void StartNotification(string sender, string message)
    {
        _notifSender = sender;
        _notifMessage = message;

        var display = $"✉  {(string.IsNullOrEmpty(sender) ? "" : $"{sender}: ")}{message}";
        _notifTextWidth = TextRenderer.MeasureText(display, _font).Width;

        var screen = Screen.PrimaryScreen;
        var maxW = screen != null ? Math.Min(_notifTextWidth + 40, screen.WorkingArea.Width / 2) : _notifTextWidth + 40;
        if (_notifTextWidth + 20 > Width)
            Width = Math.Max(Width, Math.Min(_notifTextWidth + 40, maxW));

        if (_notifState == NotifState.Media)
        {
            _wasScrolling = _scrollTimer.Enabled;
            _scrollTimer.Stop();
        }

        _animSw.Restart();
        _mediaY = 0;
        _notifY = 40;
        _notifState = NotifState.NotifIn;
        Reposition();
        _animThreadTimer?.Change(0, 8);
    }

    private void AnimTick()
    {
        float dt = (float)_animSw.Elapsed.TotalMilliseconds;
        float t = Math.Min(dt / _animDurationMs, 1f);
        float eT = 1f - (1f - t) * (1f - t) * (1f - t);

        switch (_notifState)
        {
            case NotifState.NotifIn:
                _mediaY = (int)Math.Round(-40 * eT);
                _notifY = (int)Math.Round(40 * (1f - eT));
                if (t >= 1f)
                {
                    _animThreadTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                    _mediaY = -40;
                    _notifY = 0;
                    _notifState = NotifState.NotifHold;
                    _notifHoldTimer.Start();
                }
                break;

            case NotifState.NotifOut:
                _mediaY = (int)Math.Round(-40 * (1f - eT));
                _notifY = (int)Math.Round(40 * eT);
                if (t >= 1f)
                {
                    _animThreadTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                    if (_notifQueue.Count > 0)
                    {
                        var next = _notifQueue.Dequeue();
                        StartNotification(next.sender, next.message);
                        return;
                    }
                    _mediaY = 0;
                    _notifY = 40;
                    _notifState = NotifState.Media;
                    if (_wasScrolling && !_idle)
                        _scrollTimer.Start();
                }
                break;
        }

        Invalidate();
    }

    private void AnimTimerCallback(object? state)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(AnimTick); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
        catch (Exception ex) { Log($"AnimTimerCallback: {ex.Message}"); }
    }

    private static void Log(string message)
    {
        try
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NowOnTaskbar", "log.txt");
            var dir = Path.GetDirectoryName(logPath);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Overlay] {message}\n");
        }
        catch { }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Right)
        {
            var result = MessageBox.Show("Exit Now On Taskbar?", "Now On Taskbar",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
                Application.Exit();
        }
        else
        {
            File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NowOnTaskbar", "log.txt"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] OnMouseClick: left click at ({e.X},{e.Y})\n");
            LeftClicked?.Invoke();
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_TASKBARCREATED_MSG)
        {
            Log("WndProc: TaskbarCreated detected, re-attaching");
            _taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            AttachToTaskbar();
            Reposition();
            return;
        }

        if (m.Msg == WM_GETOBJECT || m.Msg == WM_NCCALCSIZE || m.Msg == WM_WINDOWPOSCHANGING)
        {
            m.Result = IntPtr.Zero;
            return;
        }

        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if (m.Result == (IntPtr)HTTRANSPARENT)
                m.Result = (IntPtr)HTCLIENT;
            return;
        }

        if (m.Msg == WM_LBUTTONDOWN)
        {
            LeftClicked?.Invoke();
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (_showBackground)
        {
            using var bgBrush = new SolidBrush(_bgColor);
            g.FillRectangle(bgBrush, ClientRectangle);
        }

        if (_notifState != NotifState.Media)
        {
            if (!_idle)
                DrawMediaText(g, _mediaY);
            DrawNotifText(g, _notifY);
        }
        else if (!_idle)
        {
            DrawMediaText(g, 0);
        }
    }

    private void DrawMediaText(Graphics g, int yOffset)
    {
        int artOffset = 0;
        if (_showAlbumArt && _albumArt != null)
        {
            int artY = yOffset + (Height - _albumArtSize) / 2;
            g.DrawImage(_albumArt, 0, artY, _albumArtSize, _albumArtSize);
            artOffset = _albumArtSize + _albumArtGap;
        }

        if (_twoLineLayout)
        {
            var icon = _isPlaying ? "▶" : "⏸";
            var artistText = _hideArtist ? "" : _artist;
            var line1 = string.IsNullOrEmpty(artistText) ? "" : $"{icon}  {artistText}";
            var line2 = string.IsNullOrEmpty(_title) ? "" : $"{icon}  {_title}";

            if (string.IsNullOrEmpty(artistText))
            {
                var rect = new Rectangle(artOffset, yOffset, Width - artOffset, Height);
                TextRenderer.DrawText(g, line2, _font, rect, _mediaTextColor, Color.Transparent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            else
            {
                var rect1 = new Rectangle(artOffset, yOffset, Width - artOffset, Height / 2);
                var rect2 = new Rectangle(artOffset, yOffset + Height / 2, Width - artOffset, Height / 2);
                TextRenderer.DrawText(g, line1, _font, rect1, _mediaTextColor, Color.Transparent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, line2, _font, rect2, _mediaTextColor, Color.Transparent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix);
            }
        }
        else
        {
            var artistText = _hideArtist ? "" : _artist;
            var display = string.IsNullOrEmpty(artistText) ? _title : $"{artistText} — {_title}";
            if (_showAlbumArt && _albumArt != null)
            {
                // album art replaces icon
            }
            else
            {
                display = $"♫  {display}";
            }

            if (_textWidth <= Width - artOffset)
            {
                var rect = new Rectangle(artOffset, yOffset, Width - artOffset, Height);
                TextRenderer.DrawText(g, display, _font, rect, _mediaTextColor, Color.Transparent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            else
            {
                DrawScrollingTextAt(g, display, yOffset, artOffset);
            }
        }
    }

    private void DrawNotifText(Graphics g, int yOffset)
    {
        var display = string.IsNullOrEmpty(_notifSender)
            ? $"✉  {_notifMessage}"
            : $"✉  {_notifSender}: {_notifMessage}";
        TextRenderer.DrawText(g, display, _font, new Rectangle(0, yOffset, Width, Height),
            _notifTextColor, Color.Transparent,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    private void DrawText(Graphics g, string text, Color color)
    {
        TextRenderer.DrawText(g, text, _font, new Rectangle(0, 0, Width, Height), color, Color.Transparent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    private void DrawTextCentered(Graphics g, string text, Color color)
    {
        TextRenderer.DrawText(g, text, _font, new Rectangle(0, 0, Width, Height), color, Color.Transparent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void DrawScrollingText(Graphics g, string text)
    {
        DrawScrollingTextAt(g, text, 0, 0);
    }

    private void DrawScrollingTextAt(Graphics g, string text, int yOffset, int xOffset = 0)
    {
        TextRenderer.DrawText(g, text, _font, new Rectangle(xOffset + _scrollOffset, yOffset, _textWidth + 60, Height), _mediaTextColor, Color.Transparent,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, text, _font, new Rectangle(xOffset + _scrollOffset + _textWidth + 60, yOffset, _textWidth + 60, Height), _mediaTextColor, Color.Transparent,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        if (_scrollOffset + _textWidth + 60 < 0)
            _scrollOffset += _textWidth + 60;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.Black);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _font.Dispose();
            _scrollTimer.Dispose();
            _reposTimer.Dispose();
            _animThreadTimer?.Dispose();
            _notifHoldTimer.Dispose();
            _albumArt?.Dispose();
        }
        base.Dispose(disposing);
    }
}
