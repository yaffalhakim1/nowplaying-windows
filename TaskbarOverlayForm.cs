using System.Runtime.InteropServices;
using System.Text;

namespace NowOnTaskbar;

public class TaskbarOverlayForm : Form
{
    private string _title = "";
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
    private readonly System.Windows.Forms.Timer _scrollTimer = new();
    private readonly System.Windows.Forms.Timer _reposTimer = new();
    private readonly Font _font = new("Segoe UI", 9, FontStyle.Regular);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;

    private struct RECT { public int left, top, right, bottom; public int W => right - left; public int H => bottom - top; }

    public TaskbarOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        TransparencyKey = Color.Black;
        BackColor = Color.Black;
        Width = 200;
        Height = _barHeight;

        _scrollTimer.Interval = _scrollIntervalMs;
        _scrollTimer.Tick += (_, _) => { _scrollOffset -= _scrollSpeed; Invalidate(); };

        _reposTimer.Interval = _zBumpIntervalMs;
        _reposTimer.Tick += (_, _) => Reposition();

        _animThreadTimer = new System.Threading.Timer(AnimTimerCallback, null, Timeout.Infinite, Timeout.Infinite);

        _notifHoldTimer.Interval = _notifHoldMs;
        _notifHoldTimer.Tick += (_, _) =>
        {
            _notifHoldTimer.Stop();
            _animSw.Restart();
            _notifState = NotifState.NotifOut;
            _animThreadTimer?.Change(0, 8);
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
        _reposTimer.Start();
        Reposition();
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
            Width = Math.Min(Math.Max(_textWidth + 30, 80), maxW);

        Height = _barHeight;

        int x = FindLeftOfTrayArea(tr);
        int yCenter = tr.top + (tr.H - _barHeight) / 2;

        Location = new Point(tr.left + x, yCenter);
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);
    }

    private int FindLeftOfTrayArea(RECT taskbarRect)
    {
        var startHwnd = FindWindowEx(_taskbarHwnd, IntPtr.Zero, "Start", null);
        if (startHwnd != IntPtr.Zero)
        {
            GetWindowRect(startHwnd, out var startRect);
            int startLeft = startRect.left - taskbarRect.left;
            if (startLeft > taskbarRect.W * 0.2)
                return _gapFromNeighbor; // centered → far left edge
        }

        // left-aligned → right of rightmost non-system child
        return RightOfRightmostChild(taskbarRect);
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

    public void SetTitle(string title)
    {
        if (IsDisposed) return;
        _title = title;

        if (_idle)
        {
            _scrollOffset = 0;
            _scrollTimer.Stop();
            Invalidate();
            return;
        }

        try
        {
            using var g = CreateGraphics();
            _textWidth = TextRenderer.MeasureText(g, $"♫  {title}", _font).Width;
        }
        catch
        {
            _textWidth = title.Length * 12;
        }

        if (_textWidth > Width - 10)
        {
            _scrollOffset = Width;
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

    public void ShowNotification(string sender, string message)
    {
        if (IsDisposed) return;
        _notifSender = sender;
        _notifMessage = message;

        var display = $"✉  {(string.IsNullOrEmpty(sender) ? "" : $"{sender}: ")}{message}";
        _notifTextWidth = TextRenderer.MeasureText(display, _font).Width;

        var screen = Screen.PrimaryScreen;
        var maxW = screen != null ? Math.Min(_notifTextWidth + 40, screen.WorkingArea.Width / 2) : _notifTextWidth + 40;
        if (_notifTextWidth + 20 > Width)
            Width = Math.Max(Width, Math.Min(_notifTextWidth + 40, maxW));

        // Pause scroll during animation
        _wasScrolling = _scrollTimer.Enabled;
        _scrollTimer.Stop();

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
        BeginInvoke(AnimTick);
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
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

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
        var display = $"♫  {_title}";
        if (_textWidth <= Width)
            TextRenderer.DrawText(g, display, _font, new Rectangle(0, yOffset, Width, Height),
                Color.FromArgb(240, 255, 255, 255), Color.Transparent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        else
            DrawScrollingTextAt(g, display, yOffset);
    }

    private void DrawNotifText(Graphics g, int yOffset)
    {
        var display = string.IsNullOrEmpty(_notifSender)
            ? $"✉  {_notifMessage}"
            : $"✉  {_notifSender}: {_notifMessage}";
        TextRenderer.DrawText(g, display, _font, new Rectangle(0, yOffset, Width, Height),
            Color.FromArgb(255, 180, 220, 255), Color.Transparent,
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
        DrawScrollingTextAt(g, text, 0);
    }

    private void DrawScrollingTextAt(Graphics g, string text, int yOffset)
    {
        var color = Color.FromArgb(240, 255, 255, 255);
        TextRenderer.DrawText(g, text, _font, new Rectangle(_scrollOffset, yOffset, _textWidth + 60, Height), color, Color.Transparent,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, text, _font, new Rectangle(_scrollOffset + _textWidth + 60, yOffset, _textWidth + 60, Height), color, Color.Transparent,
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
        }
        base.Dispose(disposing);
    }
}
