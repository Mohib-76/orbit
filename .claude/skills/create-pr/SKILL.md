---
name: create-pr
description: Open an Orbit pull request into dev after running every check a PR needs - privacy and secrets scan of the diff and commit identity, docs and changelog coverage, protocol compatibility, release build and tests for both apps (as CI runs them), and a code review - then commits, pushes and creates the PR with the project template and watches CI. Use when the user says /create-pr, "open a PR", "raise a PR", "submit this", or finishes work on a feature branch.
argument-hint: "[optional: PR title or notes on what was tested]"
---

# Create a pull request into `dev`

The goal is a PR the owner can merge without surprises: nothing personal or secret in it, docs true, both apps
build and pass tests, and a description that says what was tested on which devices.

## Rules

- Target is **`dev`**. Only open a PR into `main` if the user explicitly says it's a hotfix for a released
  version; say that `create-release` will then need to merge `main` back into `dev`.
- **Privacy findings block the PR.** Show them (masked), say which file, and wait for the user. Never print a
  secret or personal value in full, and never "fix" them by rewriting pushed history without asking.
- Don't push, commit or create the PR until the checks are done and the user has seen the summary (one
  confirmation covers commit + push + create; ask again if anything changes after that).
- Never `git add -A` / `git add .` blindly: stage named files after looking at `git status`.
- Never force-push a branch that already has a PR or that someone else may have pulled. `--force-with-lease` on
  your own unpublished branch only, and only if the user asks for a rebase.
- Never skip hooks or CI. If tests fail, stop and report; don't weaken or delete tests to make them pass.
- Commit messages: short imperative subject (like the history: "Media controls both ways and USB modes"),
  a body when useful, ending with the attribution lines from the session's instructions, if any.

## 1. Preflight

1. Current branch: `git rev-parse --abbrev-ref HEAD`. If it's `main` or `dev`, stop: offer to move the work onto
   a new branch with the `start-work` steps (`git switch -c <name>` keeps uncommitted changes).
2. `gh auth status`; `git fetch origin --prune`.
3. `git status --short`. List uncommitted and untracked files. Untracked files: ask which belong to the PR.
4. Is the branch behind `origin/dev`? (`git rev-list --count HEAD..origin/dev`). If so, `git merge origin/dev`
   (or rebase only if the branch was never pushed and the user prefers). Conflicts outside `CHANGELOG.md`: stop
   and show them. `CHANGELOG.md` conflicts: keep both sides' lines.

## 2. Privacy and security scan (blocking)

Run the shared scanner on everything the branch adds, committed or not:

```bash
bash .claude/skills/_shared/privacy-scan.sh origin/dev
```

Then check by hand what a regex can't:

- **Commit identity:** `git log --format='%h %an <%ae> | %cn <%ce>' origin/dev..HEAD`. Every email should be a
  GitHub no-reply address (`…@users.noreply.github.com`). A personal email is a finding: offer to fix it with
  `git config user.email <no-reply>` and, if the branch is unpublished, `git commit --amend --reset-author` /
  a rebase; if published, ask.
- **Logs:** new log lines (`Log.`, `Logger`, `Log.d/i/w/e`, `Debug.WriteLine`) must not print notification text,
  clipboard contents, track titles, file contents, names, full addresses or serials. Diagnostics exports must
  still redact (`Diagnostics.cs` / `Diagnostics.kt`).
- **Risky files changed:** `.gitignore`, `.github/**`, `build.ps1`, `tools/**`, `*.gradle.kts`, `gradle/**`,
  `*.csproj`, `windows/installer/**`, `AndroidManifest.xml`, `.claude/**`. Say why each changed.
- **Security-sensitive code:** TLS / certificate pinning / pairing (`Server.kt`, `PhoneLink.cs`), anything that
  accepts input from the peer (file names → path traversal, sizes, JSON), new listening sockets on Windows
  (forbidden: the PC always dials out), `Process.Start` / adb shell commands built from strings, USB mode
  switching (never switch when the current mode can't be read).

Report every finding with what to do about it. Don't continue to step 5 until the user has decided on each.

## 3. Docs and changelog coverage

Using `.claude/skills/_shared/docs-map.md` and `git diff --stat origin/dev...HEAD` (plus uncommitted changes):

- For each area the diff touches, check the mapped document changed too, and that what it says is now true.
- **New packet types:** list `"orbit.…"` strings added in code on either side and confirm each is in
  `docs/PROTOCOL.md`; handled (or deliberately ignored) in both `OrbitCore.Handle` and `Hub.handle`; unknown
  types still non-fatal. Incompatible change → `Proto.Version` and `Proto.VERSION` bumped together; flag that the
  next release must be **major**.
- **CHANGELOG.md:** user-visible change → a line under `## [Unreleased]` (create the section at the top if
  missing). Written for users, one line per change.
- Android user-visible strings in `strings.xml` where practical; WPF styles never share a key with a Brush
  (`grep -h 'x:Key=' windows/Orbit.Windows/Themes/*.xaml | sort | uniq -d` must print nothing new).

Make the missing doc updates yourself (they're part of the PR), then show the user what you added.

## 4. Build and test (as CI does)

Run both; they're independent, so start them in parallel (background) and wait for both.

- **Windows** (stop a dev build of `Orbit.exe` from this repo's `bin/` if it locks the output; never kill the
  installed copy without asking):
  ```
  dotnet build windows/Orbit.Windows -c Release
  dotnet test windows/Orbit.Tests -c Release
  ```
  Needs `windows/Orbit.Windows/ThirdParty/` populated: run `.\tools\fetch-thirdparty.ps1` once if it's empty.
- **Android** (JDK 17–21: if `JAVA_HOME` isn't set, use the JDK under `%LOCALAPPDATA%\OrbitDevTools\jdk\` the way
  `build.ps1` does; the machine default may be too new):
  ```
  cd android; .\gradlew.bat assembleDebug testDebugUnitTest lintDebug --console=plain
  ```
  If the PR adds or upgrades a library, also `assembleRelease` (R8 minification breaks reflection-heavy
  libraries) and say the release APK needs a device test.

Record pass/fail and test counts. Any failure: stop, show the relevant output, offer to fix.

If the change is visible on a device and one is connected, offer a quick check (install the debug APK, run the
Windows build), following the device rules in `CLAUDE.local.md` if present. Never toggle USB tethering, Wi-Fi or
Bluetooth on the owner's devices.

## 5. Code review

Run the `code-review` skill on the branch diff (target `origin/dev...HEAD` plus uncommitted changes) at
`medium` effort, or `high` for large or protocol/security changes. Then:

- Fix real bugs it finds (show the user each fix).
- Note style-only suggestions; apply them only if cheap and clearly better.
- Re-run the affected tests after fixes.

## 6. Summary and confirmation

Show one table, then the proposed PR title and body:

| Check | Result |
|---|---|
| Branch / base | `feat/…` → `dev`, up to date with `origin/dev` |
| Privacy & secrets scan | clean / N findings (resolved how) |
| Commit identity | all no-reply / … |
| Risky files | none / list |
| Docs & changelog | updated: … / not needed because … |
| Protocol | no change / new packets (compatible) / **incompatible – Proto bumped** |
| Windows build + tests | pass (N tests) / fail |
| Android build + tests + lint | pass (N tests) / fail |
| Code review | N findings, M fixed |

PR title: short, user-facing ("Buds tile: pick the noise mode instead of cycling"). Body follows
`.github/PULL_REQUEST_TEMPLATE.md`: **What and why**, **How I tested it** (devices by model and Android/Windows
version only, never serials or names), **Protocol** if packets changed, **Not tested**, and the checklist with
boxes ticked only for what was actually verified.

Ask: "Commit, push and open the PR into dev?"

## 7. Commit, push, create

On yes:

1. Stage the named files, commit (message as in Rules). Run the privacy scan once more on the final commit range
   if anything was added after step 2.
2. `git push -u origin <branch>`.
3. Write the body to a temp file in the scratchpad, then
   `gh pr create --base dev --head <branch> --title "<title>" --body-file <file>`.
   If a PR already exists for the branch (`gh pr view --json url` succeeds), update it with `gh pr edit` instead.
4. Watch CI in the background: `gh pr checks <n> --watch --fail-fast`. On failure, fetch the log
   (`gh run view <id> --log-failed`), explain, and offer to fix.

## 8. Hand over

The PR link, CI status, the summary table from step 6 with any updates, and what's still untested on hardware.
