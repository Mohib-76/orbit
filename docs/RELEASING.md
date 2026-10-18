# Releasing

## One-time setup

### Android signing key

Every update must be signed with the same key, forever. Create it once, back it up somewhere safe
(password manager plus an offline copy), and never commit it.

```powershell
keytool -genkeypair -v -keystore orbit-release.jks -alias orbit -keyalg RSA -keysize 4096 -validity 10000
```

For local release builds, create `android/keystore.properties` (gitignored):

```properties
storeFile=C:/path/to/orbit-release.jks
storePassword=...
keyAlias=orbit
keyPassword=...
```

For CI, add these repository secrets (Settings → Secrets and variables → Actions):

| Secret | Value |
|---|---|
| `ORBIT_KEYSTORE_BASE64` | `[Convert]::ToBase64String([IO.File]::ReadAllBytes("orbit-release.jks"))` |
| `ORBIT_KEYSTORE_PASSWORD` | store password |
| `ORBIT_KEY_ALIAS` | `orbit` |
| `ORBIT_KEY_PASSWORD` | key password |

Without a keystore, local release builds fall back to the debug key (with a warning). Never publish those.

### Windows code signing (recommended)

Unsigned installers trigger SmartScreen warnings until they build reputation. Open-source projects can
get free signing through [SignPath Foundation](https://signpath.org); otherwise use any Authenticode
certificate and sign `Orbit.exe` and `OrbitSetup-*.exe` in the release workflow.

## Each release

1. Update the version in **both** places:
   - `windows/Orbit.Windows/Orbit.Windows.csproj` → `<Version>`
   - `android/app/build.gradle.kts` → `versionName`, and **increase** `versionCode`
2. If the wire protocol changed incompatibly, bump `Proto.Version` (C#) and `Proto.VERSION` (Kotlin) together.
3. Move the *Unreleased* notes in `CHANGELOG.md` under the new version.
4. Commit, then tag and push: `git tag v1.2.0 && git push origin v1.2.0`.
5. The **Release** workflow builds and tests both apps, makes the installer, zip, APK and `SHA256SUMS.txt`, and drafts a GitHub release. Review the draft, then publish it.

## Local builds

```powershell
.\build.ps1 -Installer     # self-contained Windows build + installer (needs Inno Setup 6) and release APK
```
