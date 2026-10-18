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

## Auto USB tethering

Android doesn't let ordinary apps switch tethering on. The **PC** does it instead, through `adb`
(bundled with the Windows app): when an authorized phone with Orbit installed is plugged in, it runs
`svc usb setFunctions rndis`, which makes Android start USB tethering. Orbit only ever turns tethering
**on**, and leaves it alone if it's already on. It needs USB debugging on the phone.

## Earbuds

realme / OnePlus "HeyMelody"-family buds speak a vendor protocol over classic Bluetooth RFCOMM
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
  Core/OrbitCore.Mirror*.cs   screen mirroring: sessions, device choice, wireless pairing
  Core/Adb.cs                 USB: adb track-devices, forward, tethering
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
  mirror/                     LIVE notification, Mirror tile, wireless-debugging helpers
  ui/                         Compose screens (design tokens in Theme.kt)
tools/fetch-thirdparty.ps1    downloads and verifies scrcpy/FFmpeg/adb binaries
```
