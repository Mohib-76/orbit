# Orbit

**Your Android phone and Windows PC, in one orbit.** Clipboard, notifications, files, battery, screen
mirroring and earbuds controls, over the connections you already use: a USB cable, your phone's hotspot,
or the same Wi-Fi. End-to-end encrypted, no account, no cloud.

**Website:** https://mohib-76.github.io/orbit/

[![CI](https://github.com/Mohib-76/orbit/actions/workflows/ci.yml/badge.svg)](https://github.com/Mohib-76/orbit/actions/workflows/ci.yml)
[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](LICENSE)

## Features

| | |
|---|---|
| **Pairing** | Scan a QR code, or click **Pair over USB** when the phone is plugged in |
| **Clipboard** | Copy on the PC, paste on the phone automatically. Phone → PC with one tap (button, Quick Settings tile, notification, or share text to Orbit). Private copies from password managers are skipped |
| **Notifications** | Phone notifications as Windows notifications with app icons; dismiss on the PC clears them on the phone. Choose apps, hide text, pause, or silence while the PC is locked |
| **Files** | Drag and drop onto Orbit, or *Share → Orbit* on the phone. Progress, and automatic resume if the link drops |
| **Battery** | Phone battery, charging and "full in …" in the window, tray and flyout, with a low-battery alert |
| **Screen mirroring** | See and control the phone on the PC: mouse, keyboard, audio, screenshots, recording. Over USB, or Wi-Fi after a one-time setup |
| **Auto USB tethering** | Plug the phone in and the laptop goes online through it |
| **Earbuds** | Battery, noise control (Off / Transparency / ANC), sound features and tap actions for realme and OnePlus buds, on both devices |

### What needs what

| Feature | Needs |
|---|---|
| Clipboard, notifications, files, battery | Nothing extra: USB tethering, hotspot or same Wi-Fi |
| Pair over USB, auto USB tethering, mirroring over USB | **USB debugging** on the phone ([how](docs/TROUBLESHOOTING.md#turn-on-usb-debugging)) |
| Mirroring over Wi-Fi | Wireless debugging (one-time QR setup in Orbit) and the same Wi-Fi network |
| Earbuds | realme / OnePlus buds that use the realme Link or HeyMelody app (tested: realme Buds T310), paired over Bluetooth |

## Install

**Requirements:** Windows 10 (1809) or 11, x64 · Android 10 or newer.

1. **Windows:** download `OrbitSetup-<version>.exe` from [Releases](https://github.com/Mohib-76/orbit/releases) and run it. Orbit lives in the tray.
2. **Android:** download `Orbit-<version>-android.apk` from the same release and install it (allow installs from your browser or file manager when asked).
3. Open Orbit on the phone and grant the permissions it asks for (notification access and "stay connected" matter most).
4. Pair: plug in with USB debugging on and click **Pair over USB** on the PC, or tap **Scan QR code** on the phone and point it at the PC.

Check the SHA-256 of what you downloaded against `SHA256SUMS.txt` in the release.

Having trouble? See [Troubleshooting](docs/TROUBLESHOOTING.md). Samsung, Xiaomi and some other phones
stop background apps aggressively; [dontkillmyapp.com](https://dontkillmyapp.com) has per-brand fixes.

## Privacy

Orbit has no servers, no account and no analytics. Your data only moves between your own two devices,
over TLS with keys pinned when you pair. Details: [PRIVACY.md](PRIVACY.md). Security reports:
[SECURITY.md](SECURITY.md).

## Build from source

Windows with the .NET 8 SDK and JDK 17–21:

```powershell
.\tools\fetch-thirdparty.ps1   # scrcpy server, FFmpeg and adb binaries (checksum-verified)
.\build.ps1                    # both apps into .\dist
dotnet test windows\Orbit.Tests
cd android; .\gradlew.bat testDebugUnitTest
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how it works and [docs/PROTOCOL.md](docs/PROTOCOL.md)
for the wire protocol. Contributions are welcome: read [CONTRIBUTING.md](CONTRIBUTING.md).

## Known limits

- Clipboard sync is text only.
- Android only lets the app you're using read the clipboard, so phone → PC needs a tap. PC → phone is automatic.
- One phone per PC (and one PC per phone) for now.
- Orbit can't switch on your phone's hotspot (Android doesn't allow apps to), but connects as soon as the laptop joins it.
- The QR scanner uses Google Play services, so this build isn't on F-Droid yet. Pair over USB works without it.

## License

Orbit is free software under the [GNU Affero General Public License v3.0](LICENSE). It builds on
[scrcpy](https://github.com/Genymobile/scrcpy), [FFmpeg](https://ffmpeg.org), protocol work from
[Gadgetbridge](https://codeberg.org/Freeyourgadget/Gadgetbridge) and ideas from
[KDE Connect](https://kdeconnect.kde.org); see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
Orbit is not affiliated with realme, OnePlus, OPPO, Samsung, Google, Microsoft or Genymobile.
