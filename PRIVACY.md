# Privacy

Orbit is built so your data never leaves your own devices.

## What Orbit does not do

- **No servers.** There is no Orbit cloud, relay or account. The Windows app and the Android app talk to each other directly over your USB cable, your phone's hotspot, or your local network.
- **No analytics, telemetry, ads or crash reporting.** Orbit doesn't phone home. The only time it contacts the internet is when you click a link (for example *Report a bug*), which opens your browser.
- **No access by the developers** to anything you sync.

## What moves between your devices

Only between the phone and the PC you paired, encrypted with TLS using keys created on your devices and
pinned at pairing:

| Data | When |
|---|---|
| Phone battery level and charging state | While connected |
| Notification title, text, app name and app icon | When notification mirroring is on, for the apps you allow |
| Clipboard text | PC → phone automatically when auto-sync is on; phone → PC when you send it. Copies marked private by password managers are skipped on the PC |
| Files | Only files you send |
| Screen image, audio and your input | Only while you're mirroring; the phone always shows a LIVE notice with Stop |
| Earbuds state (battery, noise mode, settings) | When the earbuds feature is on |
| Device names, model, app and protocol version | When connecting |

## What is stored, and where

- **Windows:** `%APPDATA%\Orbit`: settings, this PC's TLS key, the paired phone's fingerprint, cached app icons, and a local log (`orbit.log`). The log records connection events and errors, never notification text or clipboard contents.
- **Android:** the app's private storage: settings, the phone's TLS key and the paired PC's fingerprint. Received files go to `Download/Orbit`.
- Clipboard history is kept in memory on the PC only (last 10 items) and is gone when Orbit quits.

*Settings → Reset Orbit* on either device deletes everything Orbit stored there. Uninstalling removes it too
(the Windows uninstaller asks whether to delete settings).

## Permissions (Android)

| Permission | Why |
|---|---|
| Notification access | To mirror notifications (optional) |
| Notifications | To show the connection, transfers and LIVE mirroring notice |
| Nearby devices / Bluetooth | Only for the earbuds feature |
| Ignore battery optimizations | So Orbit stays connected with the screen off (optional) |
| Network, Wi-Fi, foreground service, start at boot | To keep the connection to your PC |
| Write secure settings (granted over USB by the PC, never by the app itself) | Only for wireless mirroring: lets Orbit switch wireless debugging back on |

## Diagnostics

*Export diagnostics* creates a file you choose to share. It contains the local log and app versions,
with device names, IP and MAC addresses, serial numbers and key fingerprints removed. Nothing is sent
anywhere unless you send it.

## Contact

Questions or concerns: open an issue, or for anything sensitive use the repository's private
security reporting (see [SECURITY.md](SECURITY.md)).
