# NowOnTaskbar — Project Guide

## Overview

C# WinForms app that shows **now-playing** text from any media app (Chrome, YouTube, Spotify, Edge, VLC, etc.) directly on the Windows taskbar. Uses Windows built-in `GlobalSystemMediaTransportControlsSessionManager` API — no browser extension needed.

## Architecture

```
Program.cs             # Entry point → AppContext (ApplicationContext)
└── AppContext         # Owns tray icon + media session lifecycle
    └── InitMedia()    # Spawns DispatcherQueue for WinRT media manager
        ├── GlobalSystemMediaTransportControlsSessionManager.RequestAsync()
        ├── CurrentSessionChanged → switch session
        └── MediaPropertiesChanged → update overlay
    ├── ToggleAutoStart() → HKCU\Software\Microsoft\Windows\CurrentVersion\Run
    └── UITitle() → cross-thread invoke to overlay

TaskbarOverlayForm.cs  # Transparent GDI overlay form positioned on taskbar
├── Reposition()       # 100ms timer: find Shell_TrayWnd → enumerate children → position right of rightmost non-system window
├── SetTitle()         # Receives text, measures width, toggles scroll timer
├── OnPaint() → GDI TextRenderer (SingleBitPerPixelGridFit)
├── OnMouseClick() → right-click exit
└── CreateParams → ExStyle WS_EX_LAYERED | WS_EX_TRANSPARENT
```

## Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point, AppContext with tray icon, media session listener, auto-start via registry |
| `TaskbarOverlayForm.cs` | Transparent overlay form: GDI rendering, taskbar positioning, scroll animation |
| `NowOnTaskbar.csproj` | .NET 9 WinForms + WinRT (`net9.0-windows10.0.19041.0`) |

## Media Detection Pipeline

1. `DispatcherQueueController.CreateOnDedicatedThread()` → required by WinRT
2. `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` → singleton session manager
3. `CurrentSessionChanged` → attach to active session's `MediaPropertiesChanged`
4. `TryGetMediaPropertiesAsync()` → read `Title` + `Artist`
5. Display: `"♫  Title — Artist"` on taskbar

## Taskbar Overlay Positioning

- Find `Shell_TrayWnd` via `FindWindow("Shell_TrayWnd", null)`
- Enumerate children via `FindWindowEx` loop
- Filter out system windows (`TrayNotifyWnd`, `Start`, `MSTaskListWClass`, `Windows.UI.*`, etc.)
- Find rightmost non-system child → place overlay to its left (with 8px gap)
- Fallback: position left of `TrayNotifyWnd` or hardcoded offset
- Re-position every 100ms + bump Z-order via `SetWindowPos(HWND_TOP)`
- Overlay width: min 80px / max `min(taskbarWidth/3, 280px)`, sized to text

## Rendering

- `TransparencyKey = Color.Black` + `BackColor = Color.Black` → background invisible
- `TextRenderer.DrawText` with `SingleBitPerPixelGridFit` → sharp, matches taskbar font feel
- `Segoe UI 11pt` — same as Windows taskbar
- Long titles scroll horizontally (50ms timer, 2px/tick)
- Two copies drawn for seamless wrap-around scroll

## Auto-start

- Registry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\NowOnTaskbar`
- Tray menu toggle → compare current path, add/remove value
- Balloon notification on toggle

## Tech Stack

- **.NET 9** WinForms + WinRT (`net9.0-windows10.0.19041.0`)
- **Windows.Media.Control** (`GlobalSystemMediaTransportControlsSessionManager`)
- **GDI** `TextRenderer` + `SingleBitPerPixelGridFit`
- **color-key transparency** (`TransparencyKey = Black`)
- **Win32 P/Invoke**: `FindWindow`, `FindWindowEx`, `GetWindowRect`, `SetWindowPos`, `GetClassName`

## Build & Run

```powershell
dotnet build -c Release
.\bin\Release\net9.0-windows10.0.19041.0\NowOnTaskbar.exe
```

## Conventions

- `namespace NowOnTaskbar;` — file-scoped
- `_camelCase` private fields, `PascalCase` methods / constants
- `_` prefix for constants, `private const` for all constants
- `CreateParams` override for window style flags
- `BeginInvoke` for cross-thread UI updates
- Empty `catch { }` for WinRT API failures (expected on some systems)
- `static readonly` for arrays of system class names
- All P/Invoke at class level with `[DllImport]`

## Gotchas

- **WinRT requires DispatcherQueue** — cannot call `RequestAsync()` from WinForms UI thread directly
- **Empty catch blocks intentional** — media APIs throw on systems without media playing
- **Transparency + click-through** = `WS_EX_LAYERED | WS_EX_TRANSPARENT` (0x80 | 0x8000000)
- **Z-order fighting** — taskbar regularly re-orders children, so 100ms Z-bump timer is required
- **System window filtering** — must skip narrow/wide windows that are part of taskbar chrome
- **Requires .NET 9 Desktop Runtime** — regular .NET Runtime will not work
- **Target framework** `net9.0-windows10.0.19041.0` — Windows 10 1809+ / Windows 11
- **Scroll math** — second copy offset = `_scrollOffset + _textWidth + 60` for seamless loop
