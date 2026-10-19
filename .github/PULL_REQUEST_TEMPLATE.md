<!-- Pull requests go into `dev` (not `main`); see CONTRIBUTING.md. -->

## What and why

<!-- What does this change, and what problem does it solve? Link the issue if there is one. -->

## How I tested it

<!-- Devices (phone model, Android version, Windows version) and what you tried. -->

## Checklist

- [ ] `dotnet test windows/Orbit.Tests` and `gradlew testDebugUnitTest lintDebug` pass
- [ ] New packets are documented in `docs/PROTOCOL.md` and unknown packets stay non-fatal
- [ ] No personal data, secrets or binaries added; logs don't contain notification or clipboard content
- [ ] UI works in light and dark themes, and icon-only buttons have accessible names
- [ ] `CHANGELOG.md` updated (Unreleased section) if users will notice the change
