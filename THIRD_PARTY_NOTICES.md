# Third-party notices

Orbit is licensed under the GNU Affero General Public License v3.0 (see `LICENSE`).
It includes or depends on the following third-party work. Full license texts are in `licenses/`.

## Shipped with the Windows app

| Component | Version | License | Notes |
|---|---|---|---|
| [scrcpy](https://github.com/Genymobile/scrcpy) server (`scrcpy-server`) | 5.0.1 | Apache-2.0 (`licenses/Apache-2.0.txt`) | Copyright © 2018 Genymobile, © 2018–2025 Romain Vimont. Pushed to the phone for screen mirroring. Not modified. |
| [FFmpeg](https://ffmpeg.org) (`avcodec-63`, `avformat-63`, `avutil-61`, `swresample-7`) | 9.0.2 | LGPL-2.1-or-later (`licenses/LGPL-2.1.txt`) | The LGPL build distributed in the official scrcpy 5.0.1 Windows release. Unmodified and dynamically linked; you can replace the DLLs with your own build. Source: https://ffmpeg.org/download.html (and scrcpy's build scripts in `app/deps/`). |
| Android SDK Platform-Tools: `adb.exe`, `AdbWinApi.dll`, `AdbWinUsbApi.dll` | as shipped in scrcpy 5.0.1 | Apache-2.0 (Android Open Source Project) | Redistributed exactly as in the official scrcpy Windows release. Source: https://android.googlesource.com/platform/packages/modules/adb |
| [QRCoder](https://github.com/codebude/QRCoder) | 1.6.0 | MIT (`licenses/QRCoder-MIT.txt`) | |
| [NAudio.Wasapi](https://github.com/naudio/NAudio) | 2.2.1 | MIT (`licenses/NAudio-MIT.txt`) | |
| [Windows Community Toolkit](https://github.com/CommunityToolkit/WindowsCommunityToolkit) (`Microsoft.Toolkit.Uwp.Notifications`) | 7.1.3 | MIT (`licenses/WindowsCommunityToolkit-MIT.md`) | |
| .NET 8 runtime (self-contained builds) | 8.x | MIT | https://github.com/dotnet/runtime |

These binaries are **not stored in this repository**; `tools/fetch-thirdparty.ps1` downloads the
official scrcpy release and verifies its SHA-256 before extracting them.

## Shipped with the Android app

| Component | License | Notes |
|---|---|---|
| AndroidX, Jetpack Compose, Kotlin, kotlinx.coroutines | Apache-2.0 | |
| [Bouncy Castle](https://www.bouncycastle.org) (`bcpkix-jdk18on`, `bcprov-jdk18on`) | MIT-style Bouncy Castle License (`licenses/BouncyCastle.html`) | Generates the phone's self-signed TLS certificate. |
| Google Play services code scanner (`play-services-code-scanner`) | Proprietary ([Google APIs Terms of Service](https://developers.google.com/terms)) | Used only to scan the pairing QR code. Because of this dependency the current Android build is not eligible for F-Droid; pairing over USB works without it. |

## Both apps

| Component | License | Notes |
|---|---|---|
| [Manrope](https://github.com/sharanda/manrope) font | SIL Open Font License 1.1 (`licenses/Manrope-OFL.txt`) | Copyright 2018 The Manrope Project Authors. Also self-hosted by the website (`site/fonts/`). |

## Website (`site/`)

| Component | License | Notes |
|---|---|---|
| [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono) font | SIL Open Font License 1.1 (`licenses/JetBrainsMono-OFL.txt`) | Copyright 2020 The JetBrains Mono Project Authors. Self-hosted in `site/fonts/`. |

## Protocol knowledge and acknowledgements

- **Earbuds control** (realme / OnePlus "Heymelody" buds, `buds/` on both platforms) is based on the
  protocol work in [Gadgetbridge](https://codeberg.org/Freeyourgadget/Gadgetbridge)
  (`service/devices/oppo/*`, AGPL-3.0), cross-checked with independent analysis of the vendor app.
  Thank you to the Gadgetbridge contributors.
- **Screen mirroring** follows scrcpy's documented client/server protocol (`doc/develop.md`).
- **The Orbit protocol** is inspired by [KDE Connect](https://invent.kde.org/network/kdeconnect-kde)'s
  design (newline-delimited JSON packets, pinned self-signed certificates). No KDE Connect code is used.

## Trademarks

Orbit is an independent project. It is not affiliated with, endorsed by, or sponsored by realme,
OnePlus, OPPO, Samsung, Google, Microsoft, Genymobile or the KDE project. Product names are used only
to describe compatibility, and remain the property of their owners.
