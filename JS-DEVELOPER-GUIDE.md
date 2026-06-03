# NowOnTaskbar — Simplified Code Guide

You wrote a C# Windows app without knowing C#. This explains it in JS terms.

---

## The Whole App in 3 Sentences

1. **Program.cs** starts up, creates a tray icon, and connects to Windows to listen for music + notifications.
2. When music plays or a notification arrives, **Program.cs** sends the info to **TaskbarOverlayForm.cs**.
3. **TaskbarOverlayForm.cs** draws text (and album art thumbnail) on a transparent window that sits on top of your taskbar.

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
        ├─ Gets song title + artist + thumbnail from Windows
        │
        ├─ Calls overlay.SetTitle("Imagine — John Lennon")
        │       │
        │       └─ TaskbarOverlayForm sets _title field
        │
        └─ Calls overlay.SetAlbumArt(bitmap)   ← same pattern as SetTitle
                │
                └─ TaskbarOverlayForm sets _albumArt field
                      │
                      ▼
                OnPaint() draws text AND album art using GDI
                      │
                      ▼
                Text + art thumbnail appear on taskbar
```

---

## Key Variables (Know These)

| Variable | File | What it holds | Like JS |
|---|---|---|---|
| `_title` | TaskbarOverlayForm.cs | Current song text | `useState("")` |
| `_albumArt` | TaskbarOverlayForm.cs | 20x20 thumbnail Bitmap (or null) | `useState(null)` |
| `_notifState` | TaskbarOverlayForm.cs | `Media` / `NotifIn` / `NotifHold` / `NotifOut` | enum state machine |
| `_notifQueue` | TaskbarOverlayForm.cs | Pending notifications | `Queue<{sender, msg}>` |
| `_mediaManager` | Program.cs | Connection to Windows media API | `useRef(null)` |
| `_currentSession` | Program.cs | Active media session (Spotify, YouTube, etc.) | `useRef(null)` |
| `_mediaUpdateSeq` | Program.cs | Sequence counter for coalescing updates | `let seq = 0` |
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

Say you want to add a "Show play/pause state" feature:

### Step 1: Find where to add logic

If it's about **song info** → `Program.cs` in `UpdateFromSession()`.
If it's about **display** → `TaskbarOverlayForm.cs` in `OnPaint()`.

### Step 2: Follow existing patterns

```csharp
// Program.cs — getting data from the session
private async Task UpdateFromSession(...) {
    var props = await session.TryGetMediaPropertiesAsync();
    // props.Title, props.Artist, props.Thumbnail — all available
    UITitle($"{title} — {artist}");
    // For binary data (like thumbnails), follow UIAlbumArt pattern:
    Bitmap? art = GetThumbnail(props);
    UIAlbumArt(art);
}

// Program.cs — marshalling to UI thread (always use this pattern)
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
```

### Two key patterns to copy

**HookSession/UnhookSession** — centralized subscribe/unsubscribe:
```csharp
// Instead of scattered event += / -= everywhere, use helpers:
private void HookSession(GlobalSystemMediaTransportControlsSession session)
{
    try { session.MediaPropertiesChanged += OnMediaPropertiesChanged; }
    catch (Exception ex) { Log($"HookSession: {ex.Message}"); }
}

private void UnhookSession(GlobalSystemMediaTransportControlsSession session)
{
    try { session.MediaPropertiesChanged -= OnMediaPropertiesChanged; }
    catch (Exception ex) { Log($"UnhookSession: {ex.Message}"); }
}
// Adding a new event handler = one line in each helper, not hunting 3+ places
```

**Coalescing loader** — last event always wins (better than throttle):
```csharp
private long _mediaUpdateSeq;

private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, ...)
{
    var mySeq = Interlocked.Increment(ref _mediaUpdateSeq);  // get ticket number
    await Task.Delay(100);                                    // wait for quiet period
    if (mySeq != Interlocked.Read(ref _mediaUpdateSeq)) return; // newer event came, cancel
    await UpdateFromSession(sender);                          // we're still the latest
}
// In JS: like a debounce that guarantees the last call always fires
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
_mediaManager.CurrentSessionChanged -= OnCurrentSessionChanged;

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

### 6. "Why does the app die silently?"

WinForms has no global error handler like `window.onerror`. If an exception escapes a timer tick handler or event handler, the app crashes with no log. That's why every tick handler and `BeginInvoke` call is wrapped in try/catch:

```csharp
// Bad: if Invalidate() throws, the timer stops and app dies
_scrollTimer.Tick += (_, _) => { _scrollOffset -= _scrollSpeed; Invalidate(); };

// Good: catch and log, app stays alive
_scrollTimer.Tick += (_, _) => {
    try { _scrollOffset -= _scrollSpeed; Invalidate(); }
    catch (Exception ex) { Log($"ScrollTimer: {ex.Message}"); }
};
```

In JS, `window.onerror` catches unhandled errors. In WinForms, you need explicit try/catch everywhere.

### 7. "What is `namespace`?"

```csharp
namespace NowOnTaskbar;  // file-scoped namespace (C# 10+)
```

This is like a module scope. Everything in this file belongs to the `NowOnTaskbar` namespace. It's how C# organizes code — like a folder, but for types (classes, enums, structs). You don't need to import files from the same namespace.

In JS terms:
```js
// JS doesn't have namespaces, but this is similar to:
export namespace NowOnTaskbar {
    export class TaskbarOverlayForm { ... }
}
```

Or think of it as: every file in this project is implicitly in the same "folder" called `NowOnTaskbar`.

### 8. "What is `using var`?"

```csharp
using var g = CreateGraphics();  // auto-disposes when scope ends
```

This is NOT an import. It's a **disposal pattern** — like `try/finally` but automatic. When `g` goes out of scope (end of method), `.Dispose()` is called to free resources.

In JS terms:
```js
// C# using var is like:
let g;
try {
    g = createGraphics();
    // use g
} finally {
    g?.dispose();  // always runs, even if error
}
```

You'll see this with `Graphics`, `Stream`, `Bitmap`, `Font` — anything that holds unmanaged resources (file handles, GDI objects, memory).

### 9. "What is `var`?"

```csharp
var title = props?.Title;  // C# figures out the type: string
var count = 5;             // C# figures out: int
```

`var` is **type inference** — the compiler knows the type from the right side. It's still strongly typed (unlike JS `let`), you just don't write the type explicitly.

In JS terms:
```js
// C# var is like TypeScript's type inference:
const title = props?.Title;  // TS infers: string
const count = 5;             // TS infers: number
```

The difference: in C#, once the type is inferred, it can't change. `var x = 5; x = "hello";` is a compile error.

### 10. "What is `?.` and `??`?"

```csharp
props?.Title       // null-conditional: if props is null, return null; otherwise return Title
title ?? "default" // null-coalescing: if title is null, use "default"
title ??= "default" // null-coalescing assignment: only assign if currently null
```

In JS:
```js
props?.title       // same! optional chaining
title ?? "default" // same! nullish coalescing
```

They're identical to the JS versions. C# got them around the same time JS did.

### 11. "What is `[DllImport]`?"

```csharp
[DllImport("user32.dll")]
private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
```

The `[DllImport("user32.dll")]` part is an **attribute** — metadata attached to the method. It tells the runtime "this method lives in user32.dll (a C library), call it via P/Invoke."

In JS terms:
```js
// It's like a decorator that registers a native binding:
@native('user32.dll', 'FindWindowW')
function FindWindow(className, windowName) { ... }
```

Other common attributes in this code:
- `[STAThread]` — marks Main() as Single-Threaded Apartment (required for COM)
- `[Guid("...")]` — assigns a unique ID to a class (for COM interop)

### 12. "What is `private` / `public` / `static`?"

```csharp
private int _count = 0;        // only this class can access it (like #count in JS)
public void SetTitle(...) { }  // any code can call it (like a normal method)
static class Program { }       // no instances needed, like a module with only static methods
private static extern IntPtr FindWindow(...);  // static = belongs to the class, not an instance
```

In JS terms:
```js
class Foo {
    #count = 0;           // private (JS private field)
    setTitle() { }        // public (default in JS)
}
// static is the same in JS:
class Bar {
    static helper() { }   // Bar.helper(), no instance needed
}
```

`static` in C# is the same as `static` in JS — it belongs to the class itself, not to instances.

### 13. "What is `out`?"

```csharp
GetWindowRect(hwnd, out var rect);  // fills rect with the result
```

`out` means "this method will fill in this variable." It's like returning multiple values, but the caller declares the variable.

In JS terms:
```js
// C# out is like destructuring a return value:
const rect = GetWindowRect(hwnd);
// But in C#, the method writes directly into the variable you pass

// Or think of it as:
let rect;
({ rect } = getRectFromWindow(hwnd));  // destructuring
```

### 14. "What is `Invalidate()`?"

```csharp
overlay.Invalidate();  // tells Windows "this area needs repainting"
```

`Invalidate()` marks the form's area as "dirty" — Windows will send a `WM_PAINT` message, which triggers `OnPaint()`. It's like calling `requestAnimationFrame()` or setting React state — it schedules a re-render.

In JS terms:
```js
// C# Invalidate() is like:
requestAnimationFrame(() => onPaint());
// or
this.setState({});  // triggers re-render
```

It doesn't repaint immediately — it just queues the repaint. The actual drawing happens when the message pump processes the `WM_PAINT` message.

### 15. "What is `Interlocked`?"

```csharp
var mySeq = Interlocked.Increment(ref _mediaUpdateSeq);
if (mySeq != Interlocked.Read(ref _mediaUpdateSeq)) return;
```

`Interlocked` is for atomic operations — thread-safe math on a variable. Like `Atomics.add()` in JS. It guarantees that even if 10 threads increment `_mediaUpdateSeq` at the same time, each gets a unique number.

This is the **coalescing loader** pattern: each event gets a ticket number, waits 100ms, then checks if it's still the latest. If not (newer event arrived), it cancels itself. Like a debounce that guarantees the last call always wins.

In JS:
```js
let seq = 0;
async function handleMediaChange(session) {
    const mySeq = ++seq;
    await delay(100);
    if (mySeq !== seq) return;  // newer event, cancel
    await updateFromSession(session);
}
```

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

// Type inference (var)
var title = "hello";              // const title = "hello"  (type inferred as string)
var count = 5;                    // const count = 5        (type inferred as int)

// String interpolation
$"Hello {_name}"                  // `Hello ${name}`

// Null check
_name ?? "default"                // name ?? "default"
_name?.Length                     // name?.length
_name ??= "fallback"              // name ??= "fallback"   (assign only if null)

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

// Access modifiers
private int _x;                   // #x (private field)
public void Foo() { }             // foo() { } (public method)
static void Bar() { }             // static bar() { } (class method)

// Using (disposal)
using var g = CreateGraphics();   // auto-dispose: try { g = ... } finally { g.dispose() }
using var ms = new MemoryStream(); // same: auto-disposes when scope ends

// Namespace
namespace NowOnTaskbar;           // module scope — everything in this file belongs to it

// Attributes
[DllImport("user32.dll")]         // like a decorator: tells runtime this is a native function
[STAThread]                       // metadata: marks Main() as single-threaded apartment

// Out parameter
GetWindowRect(hwnd, out var rect); // like destructuring: method fills in the variable

// Invalidate
overlay.Invalidate();             // requestAnimationFrame() / setState({}) — schedules repaint

// Interlocked (thread-safe counter)
Interlocked.Increment(ref _seq);                                   // Atomics.add(seq, 1)
Interlocked.Read(ref _seq);                                        // Atomics.load(seq)

// HookSession/UnhookSession pattern
HookSession(session);             // centralized subscribe: session.on('change', handler)
UnhookSession(session);           // centralized unsubscribe: session.off('change', handler)
```
