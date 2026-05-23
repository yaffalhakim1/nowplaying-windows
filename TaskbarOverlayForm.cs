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
    private const int _classNameBufferSize = 256;
    private const int _systemWindowMaxWidth = 500;
    private const int _minVisibleWindowWidth = 30;
    private const int _scrollSpeed = 2;
    private const int _scrollIntervalMs = 50;
    private const int _zBumpIntervalMs = 100;

    private static readonly string[] _systemClasses =
    {
        "Start", "TrayNotifyWnd", "TrayDummySearchControl",
        "ReBarWindow32", "MSTaskSwWClass", "MSTaskListWClass"
    };
    private static readonly string _windowsUiPrefix = "Windows.UI.";

    private IntPtr _taskbarHwnd;
    private readonly System.Windows.Forms.Timer _scrollTimer = new();
    private readonly System.Windows.Forms.Timer _reposTimer = new();
    private readonly Font _font = new("Segoe UI", 11, FontStyle.Regular);

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

        Width = _idle
            ? Math.Min(_preferredWidth, maxW)
            : Math.Min(Math.Max(_textWidth + 30, 80), maxW);

        Height = _barHeight;

        int rightmostLeft = FindRightmostNeighbor(tr);
        int x = rightmostLeft - Width - _gapFromNeighbor;
        int yCenter = tr.top + (tr.H - _barHeight) / 2;

        Location = new Point(tr.left + x, yCenter);
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);
    }

    private int FindRightmostNeighbor(RECT taskbarRect)
    {
        int rightmostEdge = 0;
        int leftOfRightmost = taskbarRect.W;
        var clsSb = new StringBuilder(_classNameBufferSize);
        var child = IntPtr.Zero;

        while ((child = FindWindowEx(_taskbarHwnd, child, null, null)) != IntPtr.Zero)
        {
            GetWindowRect(child, out var cr);
            int len = GetClassName(child, clsSb, _classNameBufferSize);
            string cls = len > 0 ? clsSb.ToString(0, len) : "";

            int w = cr.W;
            int childRight = cr.right - taskbarRect.left;

            if (!IsSystemWindow(cls, w) && childRight > rightmostEdge)
            {
                rightmostEdge = childRight;
                leftOfRightmost = cr.left - taskbarRect.left;
            }
        }

        return rightmostEdge > 0 ? leftOfRightmost : FindFallbackPosition(taskbarRect);
    }

    private static bool IsSystemWindow(string className, int width)
    {
        if (className.StartsWith(_windowsUiPrefix)) return true;
        foreach (var c in _systemClasses)
            if (className == c) return true;
        return width < _minVisibleWindowWidth || width > _systemWindowMaxWidth;
    }

    private int FindFallbackPosition(RECT taskbarRect)
    {
        var trayHwnd = FindWindowEx(_taskbarHwnd, IntPtr.Zero, "TrayNotifyWnd", null);
        if (trayHwnd != IntPtr.Zero)
        {
            GetWindowRect(trayHwnd, out var trayRect);
            return trayRect.left - taskbarRect.left - Width - _gapFromNeighbor;
        }
        return taskbarRect.W - Width - 120;
    }

    public void SetTitle(string title)
    {
        if (IsDisposed) return;
        _title = title;

        try
        {
            using var g = CreateGraphics();
            _textWidth = TextRenderer.MeasureText(g, _idle ? "♫  Waiting..." : $"♫  {title}", _font).Width;
        }
        catch
        {
            _textWidth = title.Length * 12;
        }

        if (!_idle && _textWidth > Width - 10)
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
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;

        if (_idle)
        {
            DrawText(g, "♫  Waiting...", Color.FromArgb(180, 200, 200, 200));
            return;
        }

        var display = $"♫  {_title}";
        if (_textWidth <= Width)
            DrawTextCentered(g, display, Color.FromArgb(240, 255, 255, 255));
        else
            DrawScrollingText(g, display);
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
        var color = Color.FromArgb(240, 255, 255, 255);
        TextRenderer.DrawText(g, text, _font, new Rectangle(_scrollOffset, 0, _textWidth + 60, Height), color, Color.Transparent,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, text, _font, new Rectangle(_scrollOffset + _textWidth + 60, 0, _textWidth + 60, Height), color, Color.Transparent,
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
        }
        base.Dispose(disposing);
    }
}
