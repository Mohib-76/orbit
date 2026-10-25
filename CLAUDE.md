# Orbit: notes for Claude sessions

Orbit links a Windows PC and an Android phone: clipboard, notification mirroring, file transfer, phone
battery, auto USB tethering, realme/OnePlus earbuds control and screen mirroring. Read `README.md`
(features, how it connects) and `docs/PROTOCOL.md` (every packet) before changing behavior.
Machine- and device-specific notes live in `CLAUDE.local.md` (gitignored), if present. Never copy
anything from it into committed files.

This is a public, AGPL-3.0 open-source project: don't commit personal data (device serials, MAC
addresses, IPs, names, local paths), secrets (signing keys, passwords), or third-party binaries
(`windows/Orbit.Windows/ThirdParty/**` is fetched by `tools/fetch-thirdparty.ps1`).

## Branches and skills
- Feature branches are cut from `dev` and come back through a PR into `dev`. `main` only holds released code: a
  `release/X.Y.Z` branch from `dev` is merged into `main`, tagged `vX.Y.Z`, and `main` is merged back into `dev`.
- Project skills in `.claude/skills/`: `/start-work` (branch from `dev`, docs plan), `/create-pr` (privacy scan,
  docs, builds, tests, review, PR into `dev`), `/review-pr <n>` (security/privacy first, quality comments only),
  `/create-release` (docs and website audit, version bump, tag, publish, report). Shared helpers live in
  `.claude/skills/_shared/` (`docs-map.md`, `privacy-scan.sh`).

## Architecture
- **The phone is the TLS server** (TCP 47800, UDP discovery 47801). **The PC always dials out**: adb forward (127.0.0.1:47810), then every interface gateway, then a manual address, then the last IP, then a UDP broadcast. This is deliberate (no Windows Firewall rules, instant on tether/hotspot). Don't reverse it.
- Wire format: newline-delimited JSON `{"type":"orbit.x","body":{}}`, mutual TLS with self-signed certs pinned at pairing. File bytes go on a separate TLS connection (`orbit.data`) the PC opens; transfers resume by offset.
- `orbit.hello` / `orbit.pair` carry `proto` (protocol version, `Proto.Version` / `Proto.VERSION`) and `app` (app version). Bump the protocol version for incompatible changes; both sides warn on a mismatch.
- **New feature = new packet types.** Add them to `docs/PROTOCOL.md`, handle them in `OrbitCore.Handle` (Windows) and `Hub.handle` (Android), and keep both sides symmetric where it makes sense. Unknown packet types must be ignored, not fatal.
- **Settings that must match on both devices** (`usbMode`, `budsEnabled`) travel in `orbit.settings`; copy that pattern. (`autoUsbTethering` is still sent for 1.0 apps.)
- **Phone remote is PC-owned.** The PC sends what the phone may do (`orbit.remote.caps`) and re-checks every `orbit.remote.run` / `orbit.volume.set` against `Settings` (`OrbitCore.Remote.cs`). The phone sends a command's id, never a command line; every remote action toasts on the PC. Links (`orbit.url`) are http(s) only on both sides.
- USB modes (charging / tethering / file sharing) are switched by the PC over adb (`OrbitCore.Usb.cs`); the phone reports its port from the `USB_STATE` broadcast (`usb/Usb.kt`). Never switch when the current mode can't be read. Media controls: `OrbitCore.Media.cs` + `Core/Media/` and `media/Media.kt`; each side must leave its own session (the peer's player) out of what it publishes.
- Install over USB: the Windows app ships the release APK as `phone\Orbit.apk` (passed with `-p:OrbitPhoneApk=` by `build.ps1` and `release.yml`, whose Windows job therefore waits for the Android job) and installs/updates it with `adb install -r --user current` (`OrbitCore.PhoneApp.cs`). This is the way around Play Protect blocking browser installs of apps with a notification listener.
- Earbuds: two levels. Any buds get the OS's view (connected, one battery level: Android's hidden `getBatteryLevel` / `BATTERY_LEVEL_CHANGED`, Windows' `DEVPKEY_Bluetooth_Battery` on the Hands-Free node) plus "Play here". Brands Orbit knows get a **driver** (`BudsDriver.kt` / `IBudsDriver.cs`; today only `HeyMelody` for realme/OnePlus/OPPO) that talks RFCOMM to the buds, only when the OS already has them connected (never page them). Each side publishes `orbit.buds.state`, which describes its controls (`noiseModes`, `toggles`); if only one side holds the controls (`control`), the other relays `orbit.buds.cmd`. The UI shows only what the buds reported. HeyMelody protocol facts come from Gadgetbridge's Oppo/realme support (AGPL-3.0); see `THIRD_PARTY_NOTICES.md`.
- Screen mirroring uses scrcpy's own server (`windows/Orbit.Windows/ThirdParty/scrcpy`, v5.0.1, plus its LGPL FFmpeg DLLs). The PC runs it through adb and decodes; the phone app only shows the LIVE notice/tile and relays Stop / View only (`orbit.mirror.*`). The version string passed to the server must match the bundled jar (`MirrorSession.ServerVersion`). Read scrcpy's `app/src/control_msg.c` and `demuxer.c` before changing `Core/Mirror/`.
- Privacy rules: logs must never contain notification text, clipboard contents or track titles; diagnostics exports redact device names and addresses (`Diagnostics.cs` / `Diagnostics.kt`).

### Windows: `windows/Orbit.Windows` (WPF, .NET 8, `net8.0-windows10.0.19041.0`)
- `Core/OrbitCore*.cs`: singleton state and logic as partial classes (`.cs` connection and session, `.Transfers.cs`, `.Buds.cs`, `.Media.cs`, `.Usb.cs`, `.PhoneApp.cs`, `.Remote.cs` links / volume / power / commands, `.Input.cs` touchpad / keyboard / slides via `InputInjector`, `.Mirror*.cs`). The UI reads `OrbitCore.Instance` (alias `App.Core`).
- `Core/Adb.cs` (adb track-devices, forward, shell), `Core/PhoneLink.cs` (framing, TLS dial, protocol version), `Core/Platform.cs` (toasts, clipboard listener and global hotkeys, autostart, QR), `Core/Hotkeys.cs`, `Core/Diagnostics.cs`, `Core/Buds/`, `Core/Media/`, `Core/Mirror/`.
- `UI/MainWindow` (custom chrome and nav rail; pages in `UI/Pages/`, created lazily in `Show()`), `UI/TrayIcon.cs`, `UI/TrayFlyout`, `UI/MirrorWindow`, `UI/AboutWindow`.
- Threading: mutate observable state only via `Ui.Run(...)`. Pages mostly refresh from code-behind on events rather than through MVVM.
- Data: `%APPDATA%\Orbit` (settings.json, identity.pfx, orbit.log). Read `orbit.log` when debugging.
- Default hotkeys (configurable in Settings): Ctrl+Alt+C send clipboard, Ctrl+Alt+B cycle noise mode, Ctrl+Alt+M mirror, Ctrl+Alt+P play/pause the phone, Ctrl+Alt+L open the copied link on the phone.
- Mirroring internals: keep effects (DropShadow etc.) off anything containing the live video. Camera mode is `MirrorOptions.Camera` (`video_source=camera`, never audio; Android 12+). Full screen in `MirrorWindow` reparents the toolbar into `FloatHost` and zeroes the WindowChrome caption. Video is pipelined (decode thread hands the newest frame to a convert thread). D3D11VA decode exists behind `Settings.MirrorHardwareDecode` (off).

### Android: `android/` (Kotlin, Compose, package `io.github.mohib76.orbit`, minSdk 29)
- `Hub.kt`: app-wide state (StateFlows) and packet handling. `Server.kt`: TLS server, pairing, discovery. `Transfers.kt`, `OrbitService.kt` (foreground service, battery), `NotifListener.kt`, `Components.kt` (Application, receivers, clipboard tile and activity), `Prefs.kt`, `Diagnostics.kt`.
- `buds/`, `media/`, `mirror/`, `remote/` (`Remote.kt` state, `RemoteInput.kt` touchpad / keyboard / slides, `Links.kt` link notification and "Open on PC" share target), `usb/`, `ui/` (`Theme.kt` colors/Manrope/`Icons`, `Components.kt`, `Screens.kt`, `BudsUi.kt`, `PcMediaUi.kt`, `RemoteUi.kt`, `InputUi.kt`, `UsbUi.kt`, `MirrorUi.kt`, `AboutUi.kt`). Navigation is plain state in `MainActivity`/`MainScreen`.
- Logcat tags: `Orbit`, `OrbitBuds`.

## UI conventions
- Font: Manrope (bundled, OFL). Accent `#4B4FE6`. Light and dark themes (system by default, overridable in Settings).
- Windows: brushes in `Themes/Light.xaml` / `Dark.xaml`; styles and icon geometries (24×24 stroke paths drawn with `<ui:Ic Data=...>`) in `Themes/Styles.xaml`. Use `DynamicResource` for colors. Styles: `CardBox`, `Primary`, `Secondary`, `Small`, `DangerBtn`, `Link`, `IconBtn`, `Switch`, `Segment`, `Drop`, `Nav`, `Progress`.
- **WPF gotcha:** a Style must never share a key with a Brush (we hit this with `Card` and `Danger`); the `DynamicResource` resolves to the Style and throws at render time.
- Android: use `LocalOrbit.current` colors and the helpers in `ui/Components.kt` (`Tile`, `PillButton`, `SettingRow`, `OrbitSwitch`, `IconChip`, `SectionLabel`).
- New user-visible Android text goes in `res/values/strings.xml` where practical.

## Build and test
- First time: `.\tools\fetch-thirdparty.ps1` (downloads scrcpy server + FFmpeg DLLs and Android platform-tools, verifies SHA-256).
- `.\build.ps1` builds both release apps into `dist\`; `-Install` also installs the APK. Release builds are minified (R8), so re-test after adding libraries. Release signing reads `android/keystore.properties` or `ORBIT_KEYSTORE*` env vars; without them the release APK is signed with the debug key (local testing only).
- Android needs JDK 17–21. Debug build: `cd android; .\gradlew.bat assembleDebug`; unit tests: `.\gradlew.bat testDebugUnitTest`.
- Windows: `dotnet build windows/Orbit.Windows`; tests: `dotnet test windows/Orbit.Tests`. Stop `Orbit.exe` before rebuilding (it locks the output).
- CI: `.github/workflows/ci.yml` (every push/PR) and `release.yml` (tags `v*`).
- Verifying on a PC: UI Automation (`System.Windows.Automation`), `PrintWindow` screenshots, `orbit.log`. `TogglePattern.Toggle()` doesn't raise `Click`, so bind switches to `Checked`/`Unchecked`.
- Verifying on a phone: `adb shell screencap`, `uiautomator dump`, `logcat -s Orbit:* OrbitBuds:*`.
