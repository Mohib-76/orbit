# Changelog

All notable changes are listed here. Versions follow [Semantic Versioning](https://semver.org).

## [Unreleased]

## [1.2.0] - 2026-10-20

### Added
- Install over USB: the Windows app now carries the Android app. Plug in the phone with USB debugging on and click **Install on phone** on the pairing screen; Orbit installs and pairs in one go. This works where Android blocks installing Orbit from a browser ("App blocked to protect your device"). When the phone has an older Orbit, Settings offers **Update over USB**.

### Changed
- The screen mirroring window on Windows follows the app's light or dark theme instead of always being dark.
- The website offers only the Windows download for now; the Windows app installs the phone app over USB.

## [1.1.0] - 2026-10-19

### Added
- Media controls both ways: see what's playing on the other device (title, artist, album art, position) and play, pause, skip or seek it. On the PC: a card on Home, a row in the tray, a play/pause shortcut (Ctrl+Alt+P) and, optionally, the phone's player in the Windows media controls so media keys reach it. On the phone: a card on Home and a system media player for the PC's playback.
- USB modes: choose what the cable does (charging only, USB tethering or file sharing) and switch any time from Home or the tray on either device. File sharing opens the phone in File Explorer. Asks before turning off tethering while the laptop is online through the phone.

### Changed
- "Auto USB tethering" is now "When the cable is connected" (Charging only / USB tethering / File sharing), synced between both apps. If auto tethering was off, the new setting starts at Charging only.
- The tray's earbuds card also shows the phone's current track, with Previous · Play/Pause · Next and a seek slider.
- The tray's "Drop files here" box is gone (Send file and dropping onto the flyout still work); the notifications list gets the room.
- New packets `orbit.media.state`, `orbit.media.art`, `orbit.media.cmd`, `orbit.usb.state`, `orbit.usb.control` and `orbit.usb.set`; `orbit.settings` gains `usbMode` and `mediaWanted`. Older apps ignore them (protocol version unchanged).
- Earbuds work with any Bluetooth buds: battery (as Windows or Android reports it), connection and "Play here".
  Noise control, sound and tap settings stay for realme / OnePlus / OPPO buds, and only the controls a pair
  of buds reports are shown (no noise-control buttons on buds without it).
- Earbuds brand protocols are now drivers, so other brands can be added; `orbit.buds.state` describes its
  controls (`noiseModes`, `toggles`, `control`, `driver`, `batteryMain`) and still works with older versions.
- Android: the Noise control Quick Settings tile opens a picker with the buds' modes instead of cycling them.
- Choosing buds automatically uses the Bluetooth device type (earbuds / headphones) instead of "buds" in the name.

### Fixed
- Notifications with line breaks no longer stretch the tray and Home lists; each shows one line of title and text.

## [1.0.0] - 2026-10-18

First public release.

### Features
- Pairing by QR code or over USB; mutual TLS with keys pinned at pairing.
- Connects over USB cable, USB tethering, the phone's hotspot, the same Wi-Fi, or a manual address.
- Clipboard sync (PC → phone automatic, phone → PC with one tap), skipping private copies from password managers.
- Notification mirroring with app icons, dismiss sync, per-app filter, do not disturb, hidden-text mode and lock-screen silence.
- File transfer both ways with progress and resume.
- Phone battery and charging status with a configurable low-battery alert.
- Screen mirroring with input, audio, screenshots and recording (based on scrcpy), over USB or Wi-Fi.
- Auto USB tethering when the phone is plugged in.
- Earbuds control for realme / OnePlus buds on both devices, with a Quick Settings tile and notification on Android.
- Configurable keyboard shortcuts on Windows; theme choice on both apps.
- About screens with open-source licenses, redacted diagnostics export and reset on both apps.
- Protocol version handshake, with a warning when one app needs updating.
