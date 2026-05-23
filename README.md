# Now On Taskbar 🎵

Show what's playing from YouTube, Spotify, or any media app — directly on your Windows taskbar. Text floats on the taskbar surface with no background, no browser extension, no config.

<p align="center">
  <img src="screenshot/example.png" alt="Now playing text on Windows taskbar" width="600">
</p>

## Features

- ⚡ **Zero setup** — detects media from any app automatically (Chrome, Edge, Spotify, VLC, etc.)
- 🪟 **Native look** — text floats directly on the taskbar, no background, no popup
- 🚫 **No browser extension** — uses Windows built-in `GlobalSystemMediaTransportControlsSessionManager` API
- 🧩 **Plays nice with TrafficMonitor** — auto-positions beside any existing taskbar app
- 🔄 **Auto-scroll** — long titles scroll smoothly
- 🔁 **Auto-start** — enables via tray menu, runs at login
- 🪶 **Single file** — ~25MB exe, no install

> **First of its kind.** No other modern tool shows "now playing" text directly on the Windows taskbar surface. macOS has it built-in. Linux has KDE panel widgets. Windows had nothing — until now.

## Prerequisites

- Windows 10 (1809+) or Windows 11
**👉 Must install [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)** — click "Download x64" under Run desktop apps section. The regular .NET Runtime will NOT work.

## Quick Start

### 🖥️ Download & Run (1 minute)

1. Download **NowOnTaskbar.exe** from [Releases](../../releases)
2. Run it anywhere — no install needed
4. Play a YouTube video or Spotify song → title appears on your taskbar

**Only 1 file. No install. No config. No browser extension.**

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
| Auto-start | Right-click tray icon → **Auto-start** |
| Exit | Right-click tray icon → **Exit** |

That's it. No configuration, no browser extensions, no permissions.

## How It Works

```
Windows Media API (WinRT)           Taskbar Overlay (GDI)
        │                                │
   ┌────▼────┐                      ┌────▼──────┐
   │   Any   │── title changes ──►  │           │
   │  media  │                      │ ♫ Song —  │  ← floats on taskbar
   │   app   │                      │   Artist   │     no background
   └─────────┘                      └───────────┘
```

1. `GlobalSystemMediaTransportControlsSessionManager` monitors all media sessions system-wide
2. On title change: `MediaPropertiesChanged` → reads `Title` + `Artist`
3. Overlay draws text via GDI `TextRenderer` (sharp, ClearType-free, matches taskbar font)
4. Color-key transparency (`TransparencyKey = Black`) hides the background — only text pixels render
5. Every 100ms, `SetWindowPos` bumps Z-order to stay above the taskbar

## Tech Stack

| Layer | Tech |
|-------|------|
| Framework | .NET 9 WinForms |
| Media API | `Windows.Media.Control` (WinRT) |
| Rendering | GDI `TextRenderer` + `SingleBitPerPixelGridFit` |
| Transparency | Color-key (`TransparencyKey = Black`) |
| Position | Enumerates `Shell_TrayWnd` children, finds rightmost neighbor |
| Z-order | `SetWindowPos(HWND_TOP)` @ 100ms timer |

## Project Structure

```
NowOnTaskbar/
├── Program.cs            # Entry point, media session listener, auto-start
├── TaskbarOverlayForm.cs # Transparent GDI text overlay on taskbar
├── NowOnTaskbar.csproj   # .NET 9 + WinRT + WinForms
└── README.md
```

## Acknowledgments

- Inspired by [TrafficMonitor](https://github.com/zhongyang219/TrafficMonitor) — the OG taskbar info display
- Uses Windows built-in media session API — no reverse engineering required

## License

MIT
