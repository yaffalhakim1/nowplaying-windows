# Changelog

## [1.5.0] - 2026-06-02

### Added
- Click overlay — brings media source app to foreground (Spotify, Chrome, etc.)

## [1.4.3] - 2026-06-01

### Fixed
- COM HRESULT logging on notification event hook failure (issue #3)

## [1.4.2] - 2026-06-01

### Fixed
- Scroll flicker — enabled DoubleBuffered to eliminate black background flash

## [1.4.1] - 2026-06-01

### Fixed
- MSIX build error (error 80080204) — added `unvirtualizedResources` capability
- Text antialiasing with chroma key — added configurable TransparencyKey color in Settings

### Added
- Chroma Key color picker in Settings dialog

## [1.3.0] - 2026-06-01

### Fixed
- Desktop click hides overlay — exclude Progman/WorkerW from fullscreen detection
- register-sparse.ps1 parser error — single quotes + UTF-8 BOM + warning comment
- Empty error balloon — show exception type name when message is empty

### Changed
- Default text readability — background enabled, full opacity text (alpha 255)

## [1.2.0] - 2026-06-01

### Added
- Settings dialog — font, color, opacity customization
- Config persistence via JSON file at `%AppData%\NowOnTaskbar\settings.json`
- Centered taskbar fallback: left of Start button instead of far left edge

### Fixed
- Overlap with Windows widgets button on centered taskbar

## [1.1.0] - 2026-06-01

### Added
- Notification reader with slide animation
- Fullscreen detection
- Notification queue (prevents overlapping animations)

### Changed
- Health timer: lightweight ping before reinit (reduces log noise)

## [1.0.0] - 2026-06-01

### Added
- Media detection via `GlobalSystemMediaTransportControlsSessionManager`
- Taskbar overlay with GDI text rendering
- Scroll animation for long titles
- Tray icon with auto-start toggle
- COM broker health monitoring (PowerModeChanged, SessionSwitch)
- COM reinit guards and 5-second timeouts
- Sparse package support for notification access
