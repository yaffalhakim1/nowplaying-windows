# NowOnTaskbar — Project Guide

## Overview

C# WinForms app that shows **now-playing** text from any media app (Chrome, YouTube, Spotify, Edge, VLC, etc.) directly on the Windows taskbar. Also shows **system notifications** with Android-style slide animation. Uses Windows built-in APIs — no browser extension needed.

## Architecture

```
Program.cs             # Entry point → AppContext (ApplicationContext)
└── AppContext         # Owns tray icon + media + notification lifecycle
    ├── InitMedia()    # Spawns DispatcherQueue for WinRT media manager
    │   ├── GlobalSystemMediaTransportControlsSessionManager.RequestAsync()
    │   ├── CurrentSessionChanged → switch session
    │   └── MediaPropertiesChanged → update overlay
    ├── InitNotifications()  # UserNotificationListener for toast notifications
    │   ├── RequestAccessAsync() → permission dialog (first run)
    │   └── NotificationChanged → extract sender + message
    ├── ToggleAutoStart() → HKCU\Software\Microsoft\Windows\CurrentVersion\Run
    ├── ToggleNotifications() → tray menu, enables/disables notif listener
    └── UITitle() → cross-thread invoke to overlay

TaskbarOverlayForm.cs  # Transparent GDI overlay form positioned on taskbar
├── Reposition()       # 100ms timer: find Shell_TrayWnd → enumerate children
├── SetTitle()         # Now-playing text
├── ShowNotification() # Triggers slide-down animation state machine
├── AnimTick()         # 125fps System.Threading.Timer, cubic ease-out
├── OnPaint() → GDI TextRenderer (ClearTypeGridFit)
├── OnMouseClick() → right-click exit
└── CreateParams → ExStyle WS_EX_LAYERED | WS_EX_TRANSPARENT

@startuml
state Media : "♫ Imagine — John Lennon"
state NotifIn <<slideDown>>
state NotifHold : "✉ Mom: Dinner at 7?"
state NotifOut <<slideUp>>
[*] -> Media
Media -> NotifIn : notification arrives
NotifIn -> NotifHold : t >= 250ms
NotifHold -> NotifOut : t >= 5s
NotifOut --> Media : t >= 250ms
@enduml
```

## Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point, AppContext, media session listener, notification listener, auto-start, notif toggle |
| `TaskbarOverlayForm.cs` | Transparent overlay form: GDI rendering, positioning, scroll + slide animation |
| `NowOnTaskbar.csproj` | .NET 9 WinForms + WinRT (`net9.0-windows10.0.19041.0`) + SxS manifest |
| `Package.appxmanifest` | Sparse package identity for `userNotificationListener` capability |
| `NowOnTaskbar.manifest` | SxS manifest linking exe to package identity |
| `register-sparse.ps1` | One-time script: cert + MSIX + register package |

## Media Detection Pipeline

1. `DispatcherQueueController.CreateOnDedicatedThread()` → required by WinRT
2. `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` → singleton session manager
3. `CurrentSessionChanged` → attach to active session's `MediaPropertiesChanged`
4. `TryGetMediaPropertiesAsync()` → read `Title` + `Artist`
5. Display: `"♫  Title — Artist"` on taskbar

## Notification Pipeline

1. `UserNotificationListener.Current.RequestAccessAsync()` → permission dialog (one-time)
2. `NotificationChanged` event → `GetNotification(id)` → `Visual.GetBinding("ToastGeneric")`
3. `GetTextElements()` → extract sender (text[1]) + message (text[2])
4. `ShowNotification(sender, message)` → triggers animation state machine

### Package Identity Requirement

Notifications require `userNotificationListener` capability, which needs package identity. Grant via sparse package:

```powershell
# One-time setup (run register-sparse.ps1):
New-SelfSignedCertificate -Type CodeSigning -Subject "CN=NowOnTaskbar"
MakeAppx pack /p NowOnTaskbar.msix /f map.txt
SignTool sign /fd SHA256 /a /s MY /sha1 <thumbprint> NowOnTaskbar.msix
Add-AppxPackage -Path NowOnTaskbar.msix -ExternalLocation <app-dir>
```

## Taskbar Overlay Positioning

- Find `Shell_TrayWnd` via `FindWindow("Shell_TrayWnd", null)`
- Enumerate children via `FindWindowEx` loop
- Filter out system windows (`TrayNotifyWnd`, `Start`, `MSTaskListWClass`, `Windows.UI.*`, etc.)
- **Centered taskbar**: overlay on far left edge (detected by Start button position > 20%)
- **Left-aligned**: rightmost non-system child → place to its left (8px gap)
- Fallback: position left of `TrayNotifyWnd` or hardcoded offset
- Re-position every 100ms + bump Z-order via `SetWindowPos(HWND_TOP)`
- Width adjusts to text (media: `_textWidth + 30`, notif: `_notifTextWidth + 40`)

## Rendering

- `TransparencyKey = Color.Black` + `BackColor = Color.Black` → background invisible
- `TextRenderer.DrawText` with `ClearTypeGridFit` → smooth, matches taskbar clock
- `Segoe UI 9pt` — same as Windows taskbar clock
- **Media text**: `"♫  Title — Artist"`, centered or scrolling
- **Notification text**: `"✉  Sender: Message"`, blue tint (`Color.FromArgb(255, 180, 220, 255)`)
- Long titles scroll horizontally (50ms timer, 2px/tick), **paused during notification animation**

## Notification Animation (Android-style)

- **125fps** via `System.Threading.Timer` (8ms interval) + `BeginInvoke` to UI thread
- **Time-based** interpolation via `Stopwatch` (not fixed steps)
- **Cubic ease-out**: `1 - (1-t)³` for smooth deceleration
- 250ms slide in / 5s hold / 250ms slide out
- Media text slides up (y: 0 → -40), notification slides up from below (y: 40 → 0)
- Scroll timer paused during animation, restored on return

## Auto-start

- Registry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\NowOnTaskbar`
- Tray menu toggle → compare current path, add/remove value
- Balloon notification on toggle

## Notifications Toggle

- Tray menu item "Notifications" with checkmark
- `_notificationsEnabled` flag checked in `OnNotificationChanged`
- Balloon confirms state change

## Tech Stack

- **.NET 9** WinForms + WinRT (`net9.0-windows10.0.19041.0`)
- **Windows.Media.Control** (`GlobalSystemMediaTransportControlsSessionManager`)
- **Windows.UI.Notifications.Management** (`UserNotificationListener`)
- **GDI** `TextRenderer` + `ClearTypeGridFit`
- **color-key transparency** (`TransparencyKey = Black`)
- **Win32 P/Invoke**: `FindWindow`, `FindWindowEx`, `GetWindowRect`, `SetWindowPos`, `GetClassName`
- **Sparse MSIX** for package identity (self-signed cert, MakeAppx, SignTool)

## Build & Run

```powershell
# Build
dotnet build -c Release

# Publish single-file (framework-dependent)
dotnet publish -c Release -o publish-single /p:PublishSingleFile=true --no-self-contained

# Run
.\publish-single\NowOnTaskbar.exe

# Register notification identity (one-time)
.\register-sparse.ps1
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
- State machine pattern for UI animations (state enum + timer callback)

## Gotchas

- **WinRT requires DispatcherQueue** — cannot call `RequestAsync()` from WinForms UI thread directly
- **Empty catch blocks intentional** — media APIs throw on systems without media playing
- **Transparency + click-through** = `WS_EX_LAYERED | WS_EX_TRANSPARENT` (0x80 | 0x8000000)
- **Z-order fighting** — taskbar regularly re-orders children, so 100ms Z-bump timer is required
- **System window filtering** — must skip narrow/wide windows that are part of taskbar chrome
- **Requires .NET 9 Desktop Runtime** — regular .NET Runtime will not work
- **Target framework** `net9.0-windows10.0.19041.0` — Windows 10 1809+ / Windows 11
- **Scroll math** — second copy offset = `_scrollOffset + _textWidth + 60` for seamless loop
- **Notification text extraction** — Chrome/Edge toasts: text[1] = sender, text[2] = message
- **UserNotificationListener** — requires package identity (sparse MSIX), not available to unpackaged apps
- **Self-signed cert trust** — must be installed to Machine\TrustedPeople (admin required, one-time)
- **System.Threading.Timer** for animation — safe via `BeginInvoke`, avoid touching UI state from callback

## COM Broker Health & Recovery

WinRT objects (`GlobalSystemMediaTransportControlsSessionManager`, `UserNotificationListener`) talk to out-of-process COM brokers. When the broker dies (sleep, lock, random disconnection), events silently stop firing — no exception, no notification. The app stays alive in the tray but becomes a zombie.

### Recovery Triggers

| Trigger | Recovery |
|---------|----------|
| `PowerModeChanged.Resume` | Force-reinit both media + notif |
| `SessionSwitch.SessionUnlock` | Force-reinit both media + notif |
| `SessionsChanged` event | Clear title if current session disappeared |
| 2min health timer | Lightweight check → reinit only if dead |

### Health Check Logic

- **Media**: enqueue `GetSessions()` on dispatcher thread → if throws, the manager is dead → `InitMedia()`
- **Notifications**: call `GetAccessStatus()` synchronously → if throws or `!= Allowed` → `InitNotifications()`
- If healthy, no log, no action — avoids the noise of the old 2min blind-reinit

### Rollback

```powershell
git checkout healthy-2min-timer   # revert to pre-recovery state
```

### Logging

`%AppData%\NowOnTaskbar\log.txt` — only logs reinit events (not healthy ticks). Check for repeated "dead" entries as a sign of persistent broker failure.

## Code Audit (MVP+)

> Load the `senior-protocols` skill at the start of every session — same state-machine patterns apply to WinForms (notification states, COM broker lifecycle) as to React.

Every commit must pass these checks:

### Error Handling
- [ ] Zero empty `catch { }` blocks — every catch must log with operation context
- [ ] No silent failure — if a COM operation fails, log the HRESULT or exception type
- [ ] Balloon/UI errors include actionable info (not just "Error: Unknown")

### Resilience
- [ ] COM broker failure paths covered (health check recovers within 2min)
- [ ] Cross-thread calls use `BeginInvoke` or `Invoke`
- [ ] Reinit guards prevent cascading restarts

### Cleanliness
- [ ] No dead code, no commented-out blocks
- [ ] No magic numbers without named constants
- [ ] Version bumped in `.csproj` and `CHANGELOG.md` if releasing

Run `Select-String -Path "*.cs" -Pattern "catch\s*\{\s*\}"` before pushing to find any new empty catches.
