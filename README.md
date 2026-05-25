# Now On Taskbar 🎵

Show what's playing from YouTube, Spotify, or any media app — directly on your Windows taskbar. Text floats on the taskbar surface with no background, no browser extension, no config.

Also shows **system notifications** (WhatsApp, Teams, etc.) with Android-style slide animation. Music text slides up, notification slides in, holds 5 seconds, slides back out.

<p align="center">
  <img src="screenshot/example.png" alt="Now playing text on Windows taskbar" width="600">
</p>

## Features

- ⚡ **Zero setup** — detects media from any app automatically (Chrome, Edge, Spotify, VLC, etc.)
- 🔔 **Notification reader** — sees all system toasts, shows them on the taskbar with slide animation
- 🪟 **Native look** — text floats directly on the taskbar, no background, no popup
- 🚫 **No browser extension** — uses Windows built-in `GlobalSystemMediaTransportControlsSessionManager` + `UserNotificationListener` APIs
- 🧩 **Plays nice with TrafficMonitor** — auto-positions beside any existing taskbar app
- 🎯 **Centered taskbar supported** — detects alignment, positions on far left edge for centered layouts
- 🔄 **Auto-scroll** — long titles scroll smoothly
- 🔇 **Notifications toggle** — right-click tray → Notifications, silence when you want
- 🔁 **Auto-start** — enables via tray menu, runs at login
- 🪶 **Single file** — ~25MB exe, no install

## Prerequisites

- Windows 10 (1809+) or Windows 11
**👉 Must install [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)** — click "Download x64" under Run desktop apps section. The regular .NET Runtime will NOT work.
- **For notifications:** One-time setup via `register-sparse.ps1` (requires admin once)

## Quick Start

### 🖥️ Download & Run (1 minute)

1. Download **NowOnTaskbar.exe** from [Releases](../../releases)
2. Run it anywhere — no install needed
3. Play a YouTube video or Spotify song → title appears on your taskbar

**For notifications (optional):**
```powershell
.\register-sparse.ps1    # one-time: grants notification access
```

### ⌨️ Or Build from Source

```powershell
git clone https://github.com/yaffalhakim1/nowplaying-windows.git
cd nowplaying-windows
dotnet build -c Release
.\bin\Release\net9.0-windows10.0.19041.0\NowOnTaskbar.exe
```

### Usage

| Action | How |
|--------|-----|
| Start | Run `NowOnTaskbar.exe` |
| Use | Play anything — title appears on taskbar |
| Notifications | Receive any toast, shows animated on taskbar |
| Silence notifications | Right-click tray icon → uncheck **Notifications** |
| Auto-start | Right-click tray icon → **Auto-start** |
| Exit | Right-click tray icon → **Exit** |

## Notification Setup (One-Time)

Notifications need a sparse package identity. Run once after install:

```powershell
.\register-sparse.ps1
```

This creates a self-signed cert, builds a thin `.msix` identity package, and registers it. Windows will ask "Let NowOnTaskbar read your notifications?" → click Allow.

## How It Works

```
Windows APIs                         Taskbar Overlay (GDI)
        │                                │
   ┌────┴────┐                     ┌─────┴──────┐
   │  Media  │── title ──────────► │            │
   │ Session │                     │ ♫ Song —   │
   └─────────┘                     │   Artist    │
                                   └────────────┘
   ┌────────────┐                        │
   │ Windows    │── toast arrives ──►    ▼
   │ Toast API  │              ┌─────────────────┐
   └────────────┘              │ ✉ Mom: Dinner   │  ← slides in
                               │     at 7?       │     5s hold
                               └─────────────────┘      slides out
```

### Media
1. `GlobalSystemMediaTransportControlsSessionManager` monitors all media sessions system-wide
2. On title change: reads `Title` + `Artist`
3. Overlay draws text via GDI `TextRenderer`

### Notifications
1. `UserNotificationListener` reads system toast notifications
2. Extracts sender + message from toast XML
3. Triggers slide animation: media slides up, notification slides in
4. Cubic ease-out, 125fps, 5s hold

## Tech Stack

| Layer | Tech |
|-------|------|
| Framework | .NET 9 WinForms |
| Media API | `Windows.Media.Control` (WinRT) |
| Notification API | `Windows.UI.Notifications.Management` (WinRT) |
| Rendering | GDI `TextRenderer` with ClearType |
| Animation | `System.Threading.Timer` @ 8ms + `Stopwatch` + cubic ease-out |
| Transparency | Color-key (`TransparencyKey = Black`) |
| Position | Enumerates `Shell_TrayWnd` children |
| Z-order | `SetWindowPos(HWND_TOP)` @ 100ms timer |
| Package identity | Sparse MSIX (MakeAppx + SignTool) |

## Project Structure

```
NowOnTaskbar/
├── Program.cs            # Entry point, media + notification listeners, auto-start
├── TaskbarOverlayForm.cs # Transparent GDI overlay, scroll + slide animation
├── NowOnTaskbar.csproj   # .NET 9 + WinRT + WinForms
├── Package.appxmanifest  # Sparse package identity for notification access
├── NowOnTaskbar.manifest # SxS manifest linking exe to package identity
├── register-sparse.ps1   # One-time notification setup
├── AGENTS.md             # AI project guide
└── README.md
```

## Acknowledgments

- Inspired by [TrafficMonitor](https://github.com/zhongyang219/TrafficMonitor) — the OG taskbar info display
- Thanks to [@nadialvy](https://github.com/nadialvy) for inspiration and support
- Uses Windows built-in media + notification APIs — no reverse engineering required

## License

MIT
