# NowOnTaskbar — Simplified Code Guide

You wrote a C# Windows app without knowing C#. This explains it in JS terms.

---

## The Whole App in 3 Sentences

1. **Program.cs** starts up, creates a tray icon, and connects to Windows to listen for music + notifications.
2. When music plays or a notification arrives, **Program.cs** sends the info to **TaskbarOverlayForm.cs**.
3. **TaskbarOverlayForm.cs** draws text on a transparent window that sits on top of your taskbar.

That's it. Two files do almost everything.

---

## File Map

```
Program.cs            ← Entry point. Like index.js + App.js combined.
  │                     Creates tray icon, listens for media, listens for
  │                     notifications, health checks, auto-start, settings.
  │
  └─▶ TaskbarOverlayForm.cs  ← The UI. Like a React component.
        Draws text, handles animations, positions on taskbar.
        Has NO logic about media or notifications — just shows what it's told.

OverlayConfig.cs      ← Saved settings (font, colors). Like localStorage.
SettingsForm.cs       ← The settings dialog window. Like a React modal.
```

### React-to-WinForms Translation

| React Concept | This Codebase |
|--- |--- |
| `index.js` | `Program.cs` |
| App component | `AppContext` (inside Program.cs) |
| A React component | `TaskbarOverlayForm.cs` |
| `useState` | Private fields (`_title`, `_notifState`) |
| `useEffect` | Event handlers (`OnNotificationChanged`) |
| `setInterval` | `Timer` class |
| `localStorage` | `OverlayConfig` → `settings.json` |
| Modal dialog | `SettingsForm.cs` |
| Styling | GDI drawing (not CSS) |

---

## Data Flow

```
  YouTube plays music
        │
        ▼
  Windows detects: "there's a media session"
        │
        ▼
  Program.cs receives CurrentSessionChanged event
        │
        ├─ Gets song title + artist from Windows
        │
        └─ Calls overlay.SetTitle("Imagine — John Lennon")
              │
              ▼
        TaskbarOverlayForm sets _title field
              │
              └─ Calls Invalidate() ← like React setState → re-render
                    │
                    ▼
              OnPaint() draws text using GDI (pixel-level drawing)
                    │
                    ▼
              Text appears on taskbar
```

---

## Key Variables (Know These)

| Variable | File | What it holds | Like JS |
|---|---|---|---|
| `_title` | TaskbarOverlayForm.cs | Current song text | `useState("")` |
| `_notifState` | TaskbarOverlayForm.cs | `Media` / `NotifIn` / `NotifHold` / `NotifOut` | enum state machine |
| `_notifQueue` | TaskbarOverlayForm.cs | Pending notifications | `Queue<{sender, msg}>` |
| `_mediaManager` | Program.cs | Connection to Windows media API | `useRef(null)` |
| `_notifListener` | Program.cs | Connection to Windows notifications | `useRef(null)` |
| `_config` | Program.cs | Settings from JSON file | `JSON.parse(localStorage)` |
| `_fullScreen` | TaskbarOverlayForm.cs | Is a fullscreen app focused? | `boolean` |

---

## Settings System (JSON Config)

Location: `%AppData%\NowOnTaskbar\settings.json`

```json
{
  "fontFamily": "Segoe UI",
  "fontSize": 9.0,
  "mediaTextAlpha": 255,
  "showBackground": true,
  "backgroundAlpha": 180
}
```

Like `localStorage.getItem("settings")` — but written to a file on disk.

```
SettingsForm.cs (the dialog)
        │
        ├─ User picks font via FontDialog (like HTML <input type="file">)
        ├─ User picks color via ColorDialog (like HTML <input type="color">)
        │
        └─ Save button → ApplyToConfig() → config.Save() → writes JSON
             │
             └─ overlay.ApplyConfig(config) → updates font/colors live
```

---

## How to Add a New Feature

Say you want to add a "Show time remaining" feature:

### Step 1: Find where to add logic

If it's about **song info** → `Program.cs` in `UpdateFromSession()`.
If it's about **display** → `TaskbarOverlayForm.cs` in `OnPaint()`.

### Step 2: Follow existing patterns

```csharp
// Program.cs — getting data
private async Task UpdateFromSession(...) {
    var props = await session.TryGetMediaPropertiesAsync();
    // props.Title, props.Artist — now add your new field
    string timeRemaining = props.PlaybackTime.ToString();
    UITitle($"{title} — {artist} ({timeRemaining})");
}

// TaskbarOverlayForm.cs — showing it
// _title already contains what you set in UITitle()
// OnPaint already draws it
```

No build step, no bundler, no npm. `dotnet build` compiles everything.

---

## Common Gotchas for JS Devs

### 1. "Why can't I just use CSS?"

There's no DOM. No `document.createElement`. WinForms draws pixels directly with GDI — like Canvas2D but older. You position elements with pixel coordinates (`x=0, y=0`), not CSS.

### 2. "Why cross-thread errors?"

Some Windows APIs (like media detection) MUST run on a specific thread. If they touch the UI from the wrong thread, your app crashes silently. That's what `BeginInvoke()` is for — it's like `postMessage` to the UI thread:

```csharp
// From background thread:
overlay.BeginInvoke(() => {
    overlay.SetTitle("..."); // SAFE: this runs on UI thread
});
```

### 3. "Events look weird"

```csharp
// This:
_mediaManager.CurrentSessionChanged += OnCurrentSessionChanged;

// Is like this in JS:
mediaManager.addEventListener('currentSessionChanged', onCurrentSessionChanged);

// And this:
mediaManager.CurrentSessionChanged -= OnCurrentSessionChanged;

// Is like this in JS:
mediaManager.removeEventListener('currentSessionChanged', onCurrentSessionChanged);
```

### 4. "Where are imports?"

```csharp
using Windows.Media.Control;  // Like: import { GSMTCSessionManager } from 'windows.media.control'
using System.Runtime.InteropServices;  // Built-in, no import needed
```

NuGet packages (in `.csproj`) = npm packages (in `package.json`).

### 5. "What is `async void`?"

```csharp
async void OnNotificationChanged(...)  // fire-and-forget, like an event handler
async Task UpdateFromSession(...)      // awaitable, like an async function
```

`async void` = no Promise returned (for event handlers). `async Task` = returns a Promise.

---

## If You See an Error

| Error | Likely Cause | Fix |
|---|---|---|
| "Access denied" | register-sparse.ps1 not run | Run the script as admin |
| "Listener timed out" | COM object died (sleep/lock) | Should recover in 2min automatically |
| "Error: Unknown (COMException)" | Windows didn't give a reason | Check log.txt for details |
| Parser error in ps1 | Smart quotes from copy-paste | Download RAW file from GitHub |
| Overlay disappears on desktop click | Was a bug, fixed in v1.3 | Update to latest |

---

## The 2-Minute Health Timer

Both media + notification APIs talk to Windows services that can silently disconnect. The app stays in tray but stops working. The 2-minute timer checks if they're still alive:

```
  Every 2 minutes:
        │
        ├─ Ping media: try GetSessions()
        │   ├─ Throws → COM is dead → reconnect
        │   └─ OK → do nothing
        │
        └─ Ping notifications: try GetAccessStatus()
            ├─ Throws → COM is dead → reconnect
            └─ OK → do nothing
```

Like a WebSocket health check — but for Windows COM objects.

---

## Files You'll Never Touch

| File | What it does | Don't touch because |
|---|---|---|
| `NowOnTaskbar.csproj` | Project config | Like package.json + tsconfig combined |
| `Package.appxmanifest` | Notification permissions | Like Chrome extension manifest.json |
| `register-sparse.ps1` | Setup script | Run once, forget exists |
| `.gitignore` | Git ignore rules | Standard config |
| `AGENTS.md` | AI assistant rules | For LLM context only |

---

## Quick Reference: C# for JS Devs

```csharp
// Variable
private int _count = 0;           // let count = 0
private string _name = "foo";     // const name = "foo"
private bool _flag;               // let flag

// String interpolation
$"Hello {_name}"                  // `Hello ${name}`

// Null check
_name ?? "default"                // name ?? "default"
_name?.Length                     // name?.length

// If
if (_notifState == NotifState.Media) { }

// For each
foreach (var c in items) { }      // for (const c of items) { }

// Async
async Task Foo() {                // async function foo() {
    await Bar();                   //   await bar()
}                                  // }

// Class
public class Foo {                // class Foo {
    public void Bar() { }         //     bar() { }
}                                  // }

// Dictionary
Dictionary<string, int> map = new();
map["key"] = 5;                    // map = {}; map["key"] = 5
