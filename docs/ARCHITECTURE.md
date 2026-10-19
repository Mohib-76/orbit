# How Orbit works

For the packet-by-packet reference see [PROTOCOL.md](PROTOCOL.md).

## The connection

The phone runs a small TLS server on port **47800**. The PC always dials out to it, so Windows Firewall
never needs an inbound rule. The PC tries every route at once and uses whichever answers first:

1. **USB cable**: `adb forward` to the phone (needs USB debugging). Works even with no network.
2. **USB tethering / phone hotspot**: the phone *is* the PC's network gateway, so the PC dials the gateway. This takes well under a second.
3. **Manual address**: an IP or host name entered in Settings, for networks where nothing else works (VPNs, guest Wi-Fi that isolates devices).
4. **Shared Wi-Fi**: a UDP broadcast on **47801** that only a phone paired with this PC answers, plus the last known IP.

The PC retries every 1.5 s, and immediately when Windows reports a network change, so plugging in or
switching on the hotspot reconnects without the user doing anything.

Both sides present self-signed certificates (mutual TLS); each pins the other's SHA-256 fingerprint at
pairing. Pairing proves possession of a one-time token from the QR code with an HMAC bound to both
fingerprints, so a man-in-the-middle can't relay it. `orbit.hello` carries a protocol version so
mismatched apps can tell the user which side to update.

## USB modes

While the cable is in, the phone's USB port does one of three things: **charging only**, **USB tethering**
(the laptop goes online through the phone) or **file sharing** (MTP: the phone shows up in File Explorer).
Android runs only one at a time and doesn't let ordinary apps switch, so the **PC** does it through `adb`
(bundled with the Windows app): `svc usb setFunctions rndis`, `mtp`, or nothing for charging. It needs USB
debugging on the phone.

- The user picks a default ("When the cable is connected", synced between both apps); the PC applies it once
  per plug-in and never switches when it can't read the current mode.
- Either side can switch any time from Home or the tray. The phone asks the PC (`orbit.usb.set`).
- The phone reports its port from Android's sticky `USB_STATE` broadcast, so a mode picked from Android's own
  USB menu shows up on both sides and isn't undone.
- Leaving tethering asks first when the laptop is online through the phone. USB mirroring blocks switching,
  since a switch makes USB reconnect for a moment.

## Media controls

Each side publishes the media sessions its system knows about (Android `MediaSessionManager`, through the
notification access Orbit already has; Windows `GlobalSystemMediaTransportControlsSessionManager`) and runs
play / pause / skip / seek for the other. The phone shows the PC's player on Home and as a system media
player (a `MediaSession` with a media-style notification; it goes away after 10 minutes paused). The PC
shows the phone's player on Home and in the tray, and can put it in the Windows media controls through a
silent `MediaPlayer`'s `SystemMediaTransportControls`, so media keys reach the phone while it plays. Each side
leaves its own session out of what it publishes, so nothing echoes back. Album art travels once per track.

## Earbuds

There are two levels. **Any** Bluetooth buds get what the OS knows: connected or not, one battery level
(Android's `BATTERY_LEVEL_CHANGED` / hidden `getBatteryLevel`, Windows' `DEVPKEY_Bluetooth_Battery`) and
**Play here**. Brands Orbit knows also get a **driver** (`BudsDriver.kt` / `IBudsDriver.cs`) for their
controls; `orbit.buds.state` says which controls the buds reported, and both apps only show those. Buds are
picked by their Bluetooth device type (earbuds / headphones). Today there is one driver, `HeyMelody`:

realme / OnePlus / OPPO "HeyMelody"-family buds speak a vendor protocol over classic Bluetooth RFCOMM
(UUID `0000079a-d102-11e1-9b23-00025b00a5a5`, "OPPOv1" framing). The protocol knowledge comes from
[Gadgetbridge](https://codeberg.org/Freeyourgadget/Gadgetbridge)'s Oppo/realme support, cross-checked
against the vendor app. Both apps speak it **directly**: Android uses `BluetoothSocket`, Windows uses
`Windows.Devices.Bluetooth.Rfcomm`. Each one only opens the control channel when the buds are already
connected to it, so Orbit never pulls the buds away from the other device.

- With dual-device connection, the phone and the PC both hold a control channel. Changes made on one (or by touching the buds) are pushed to the other.
- If only one device has the buds, the other shows its view and **relays** commands through the Orbit link (`orbit.buds.cmd`).
- **Play here** pauses media on the other device and starts it on this one. The buds follow whichever device is playing.
- Only one app per device can hold the buds' control channel, so close other buds-control apps (including the vendor app) while using Orbit's.

## Screen mirroring

Mirroring works the way [scrcpy](https://github.com/Genymobile/scrcpy) does, and reuses its device-side
server (v5.0.1, fetched by `tools/fetch-thirdparty.ps1`): the PC pushes `scrcpy-server` to the phone over
**adb**, starts it with `app_process` (so it runs as the shell user and can capture the screen and inject
input), and reaches its sockets through an `adb forward`. The phone encodes H.264 in hardware; the PC
decodes it with FFmpeg (the LGPL DLLs from scrcpy's release), converts it to RGB and draws it in a WPF
window. Audio is raw PCM played through WASAPI. Input goes back as scrcpy control messages. The Orbit app
itself isn't in the video path; it shows the LIVE notification and tile and relays Stop / View only.

- **USB** works with no setup when USB debugging is authorized.
- **Wireless**: a one-time setup pairs Android's *Wireless debugging* with the PC by QR code, like Android Studio (`WIFI:T:ADB;S:…;P:…;;`, then `adb pair`). After that adb finds the phone over mDNS. The phone and PC must be on the same Wi-Fi; Android doesn't offer wireless debugging over the phone's own hotspot.
- After mirroring once, the PC grants Orbit `WRITE_SECURE_SETTINGS` over adb, so the phone can switch wireless debugging back on by itself after a reboot or Wi-Fi change.
- The stream never runs silently: the PC refuses to start unless the Orbit link is up (so the phone can show its notice), and stops if the link is gone for 20 s.
- **View only** is enforced by the PC (it stops sending input). **Stop** is also a request to the PC; over Wi-Fi the phone can force it by restarting wireless debugging if the PC doesn't respond within 3 s.
- Screens that apps mark as secure (banking, passwords) show as black on the PC. That's Android, not Orbit.

## Project layout

```
windows/Orbit.Windows/        WPF app (.NET 8)
  Core/OrbitCore.cs           discovery, pairing, session, clipboard, notifications
  Core/OrbitCore.Transfers.cs file transfer with resume
  Core/OrbitCore.Buds.cs      earbuds (direct + relayed), media handoff
  Core/OrbitCore.Media.cs     media controls both ways (Core/Media/: sessions, Windows media controls)
  Core/OrbitCore.Usb.cs       USB modes: charging, tethering, file sharing
  Core/OrbitCore.Mirror*.cs   screen mirroring: sessions, device choice, wireless pairing
  Core/Adb.cs                 USB: adb track-devices, forward, shell
  Core/PhoneLink.cs           wire format, TLS dialing, protocol version
  Core/Hotkeys.cs             configurable global shortcuts
  Core/Diagnostics.cs         redacted diagnostics export, reset
  Core/Buds/                  buds protocol and RFCOMM controller
  Core/Mirror/                scrcpy client: session, FFmpeg decoder, YUV→RGB, control messages, MP4 recorder
  ThirdParty/                 fetched binaries (not in git)
  UI/                         main window, pages, tray icon and flyout, mirror windows, About
windows/Orbit.Tests/          unit tests (xUnit)
windows/installer/Orbit.iss   Inno Setup installer
android/app/src/main/java/io/github/mohib76/orbit/
  Server.kt                   TLS server, pairing, UDP discovery responder
  Hub.kt                      app state and actions
  Transfers.kt                file transfer with resume (saves to Download/Orbit)
  OrbitService.kt             foreground service, battery, notifications
  NotifListener.kt            notification mirroring
  Diagnostics.kt              diagnostics share, reset, local addresses
  buds/                       earbuds protocol, controller, service, tile
  media/                      media sessions both ways, the PC media player
  usb/                        USB port state, USB mode requests
  mirror/                     LIVE notification, Mirror tile, wireless-debugging helpers
  ui/                         Compose screens (design tokens in Theme.kt)
tools/fetch-thirdparty.ps1    downloads and verifies scrcpy/FFmpeg/adb binaries
```
