# Contributing to Orbit

Thanks for helping! Bug reports, device test results, translations, docs and code are all welcome.

## Reporting bugs

Use the **Bug report** issue form. The most useful things to include:

- Phone model and Android version, Windows version, and the Orbit version on both (Settings → About).
- How they're connected (USB tethering, hotspot, same Wi-Fi, USB cable only).
- A diagnostics export from both apps (Settings → Export diagnostics). It's already redacted, but look it over before you attach it.

Security problems: see [SECURITY.md](SECURITY.md) instead.

## Development setup

You need Windows 10/11, the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), a JDK 17–21
and the Android SDK (Android Studio installs it; set `sdk.dir` in `android/local.properties` or
`ANDROID_HOME`).

```powershell
.\tools\fetch-thirdparty.ps1                 # once: scrcpy server, FFmpeg, adb (checksum-verified)
dotnet build windows\Orbit.Windows            # Windows app
dotnet test windows\Orbit.Tests               # Windows tests
cd android; .\gradlew.bat assembleDebug testDebugUnitTest
```

Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/PROTOCOL.md](docs/PROTOCOL.md) first.

## Guidelines

- **Both apps speak one protocol.** A feature that crosses devices adds packet types to `docs/PROTOCOL.md` and handles them in `OrbitCore.Handle` (Windows) and `Hub.handle` (Android). Unknown packets must be ignored, never fatal. Bump the protocol version (`Proto.Version` / `Proto.VERSION`) only for incompatible changes.
- **Keep the PC dialing out.** The phone is the server; don't add listening sockets on Windows.
- **Privacy first.** Never log notification text, clipboard contents, file contents or full addresses. Anything new in logs must survive `Diagnostics` redaction.
- **Match the UI.** Use the existing styles (`Themes/Styles.xaml`, `ui/Components.kt`) and both light and dark themes. In WPF, never give a Style the same key as a Brush.
- **Accessibility.** Give icon-only buttons a name (`AutomationProperties.Name` / `contentDescription`), and keep text readable at large font sizes.
- **No binaries or personal data in git**: no keystores, device serials, MAC/IP addresses, or third-party DLLs.
- Add or update tests for protocol and parsing code (`windows/Orbit.Tests`, `android/app/src/test`).

## Pull requests

1. Fork, create a branch, and keep the change focused.
2. Make sure `dotnet test` and `gradlew testDebugUnitTest lintDebug` pass; CI runs them too.
3. Describe what you tested and on which devices.
4. By contributing, you agree your work is licensed under the AGPL-3.0, like the rest of Orbit.

## Translations

Android user-facing text is moving into `android/app/src/main/res/values/strings.xml`; Windows text into
resource files. Help moving strings is very welcome, as are new languages once that's done.
