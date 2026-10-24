# Which documents a change must touch

Shared by the `start-work`, `create-pr`, `review-pr` and `create-release` skills. A change is not done until the
documents its area maps to are true again. Read the current text before editing; keep the existing tone (short,
plain sentences, no marketing).

| If the change touches… | Update |
|---|---|
| Anything a user will notice (feature, behaviour, UI text, fix) | `CHANGELOG.md` → a line under `## [Unreleased]` in `### Added` / `### Changed` / `### Fixed` / `### Removed` |
| A new or changed packet, field or setting synced in `orbit.settings` | `docs/PROTOCOL.md` (every packet type, field, direction); old apps must ignore it. Incompatible change → bump `Proto.Version` (`Core/PhoneLink.cs`) and `Proto.VERSION` (`Proto.kt`) together |
| How devices connect, pair, transfer, mirror, or a subsystem's design | `docs/ARCHITECTURE.md` |
| A new feature, a removed feature, or what a feature needs (USB debugging, Wi-Fi, a permission) | `README.md` → Features table, "Requirements by feature", "Known limitations" |
| New data that leaves the device, new permissions, new logs, anything stored | `PRIVACY.md`, and `Diagnostics.cs` / `Diagnostics.kt` redaction |
| New Android permissions or exported components | `AndroidManifest.xml` review + `PRIVACY.md` + README install steps if the user must grant it |
| A new failure mode or setup step users can get stuck on | `docs/TROUBLESHOOTING.md` |
| Build, toolchain, CI, release steps | `CONTRIBUTING.md`, `docs/RELEASING.md`, `build.ps1` comments, `README.md` "Build from source" |
| A new library, bundled binary or borrowed code | `THIRD_PARTY_NOTICES.md` (+ `licenses/`), `tools/fetch-thirdparty.ps1` with a SHA-256 for binaries |
| Architecture rules, file layout, new partial classes / folders, conventions | `CLAUDE.md` (the notes every Claude session reads) |
| A major user-facing feature (one that would earn a card on the website) | `site/index.html` (a screen tile in the features bento, meta description, `og:description`); done at release time by `create-release`. Follow `docs/SITE_DESIGN.md` (screens from the design canvas, sample data only, one line of text per tile) |
| The website's layout, look or motion | `docs/SITE_DESIGN.md` |
| Android user-visible strings | `android/app/src/main/res/values/strings.xml` |

Never put anything from `CLAUDE.local.md` (or any `*.local.md`) into these files.
