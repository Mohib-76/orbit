# Changelog

All notable changes are listed here. Versions follow [Semantic Versioning](https://semver.org).

## [Unreleased]

### Changed
- Earbuds work with any Bluetooth buds: battery (as Windows or Android reports it), connection and "Play here".
  Noise control, sound and tap settings stay for realme / OnePlus / OPPO buds, and only the controls a pair
  of buds reports are shown (no noise-control buttons on buds without it).
- Earbuds brand protocols are now drivers, so other brands can be added; `orbit.buds.state` describes its
  controls (`noiseModes`, `toggles`, `control`, `driver`, `batteryMain`) and still works with older versions.
- Choosing buds automatically uses the Bluetooth device type (earbuds / headphones) instead of "buds" in the name.

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
