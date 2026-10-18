---
name: release
description: Cut an Orbit release. Lists open pull requests, merges the ones the user picks after review, bumps the version in both apps, moves the changelog, tags vX.Y.Z and watches the Release workflow until the draft GitHub release is ready. Use when the user asks to release, ship, publish a version, or "make a release".
---

# Releasing Orbit

The source of truth is `docs/RELEASING.md`; this is the same process, done carefully. Pushing a `v*` tag runs
`.github/workflows/release.yml`, which builds and tests both apps and **drafts** a GitHub release. Publishing
the draft is always the user's click.

## Rules

- **Stop and ask before every step that can't be undone:** merging a PR, pushing `main`, pushing the tag.
  Show exactly what will happen, then wait for a yes. Approval for one step doesn't cover the next.
- Never force-push, never rewrite `main`, never move or delete an existing tag, never publish the draft
  release, never delete branches unless the user asks.
- Stop (don't work around it) when tests fail, CI is red, or a merge conflict is not just changelog lines.
- Commands: Git Bash or PowerShell as usual. GitHub access is the `gh` CLI. adb is not needed.

## 0. Preflight

1. `gh --version` and `gh auth status`. If `gh` is missing, tell the user to run `winget install GitHub.cli`
   and then `gh auth login` themselves (it's interactive), and stop.
2. `git status --short` must be clean (ignore untracked files the user owns, but mention them).
   `git fetch origin --tags --prune`.
3. Note the last release: `git describe --tags --abbrev=0 origin/main` (annotated tags like `v1.0.0`) and the
   current versions:
   - `windows/Orbit.Windows/Orbit.Windows.csproj` → `<Version>`
   - `android/app/build.gradle.kts` → `versionName`, `versionCode`
   If the two apps disagree, say so; the release must bring them to the same version.

## 1. What's waiting

Show the user, in one message:

- **Already on `main` since the last tag:** `git log --oneline <lastTag>..origin/main`.
- **Open pull requests:**
  `gh pr list --state open --json number,title,headRefName,author,isDraft,mergeable,reviewDecision,statusCheckRollup,updatedAt`
  as a table: number, title, branch, draft?, CI (pass / fail / pending from `statusCheckRollup`), mergeable,
  files changed (`gh pr view N --json files --jq '.files | length'`).
- **Branches pushed without a PR:** `git branch -r --no-merged origin/main` minus the PR branches. Mention
  them; they're not in the release unless the user opens a PR for them.

Ask which PRs go into this release. Drafts, red CI, or conflicting PRs can't be picked until fixed; say why.

## 2. Review each picked PR

For each PR, before merging:

1. Summarize what it changes (`gh pr view N`, `gh pr diff N`), and flag anything risky for this project:
   - the Orbit wire protocol (`docs/PROTOCOL.md`, packet handling in `OrbitCore.Handle` / `Hub.handle`):
     is it backward compatible? If not, `Proto.Version` (C#) and `Proto.VERSION` (Kotlin) must both be bumped,
     and the release is a **major** version;
   - new libraries on Android (release builds are R8-minified: needs a device test of the release APK);
   - the buds driver interfaces, USB tethering, or anything CLAUDE.md marks as dangerous.
2. Offer a deeper review with the `code-review` skill for big or risky PRs.
3. Check its changelog: the PR should add lines under `## [Unreleased]` in `CHANGELOG.md`. If it doesn't,
   propose lines and add them on `main` in step 4.

## 3. Merge (ask first)

Show the merge order and the method, then on a yes, for each PR:

```
gh pr merge N --merge --delete-branch=false
```

(`--merge` keeps each branch's commits, like the history so far. Use squash only if the user asks.)

If a later PR now conflicts (usually both added lines under `## [Unreleased]`):
check out its branch, `git merge origin/main`, keep **both** sets of changelog lines, run the tests, push
the branch, wait for CI to go green, then merge. Any other conflict: stop and show it to the user.

Then `git checkout main && git pull --ff-only`.

## 4. Test `main` locally

- Windows: stop nothing that's running from `%LOCALAPPDATA%\Programs\Orbit`; run
  `dotnet test windows/Orbit.Tests`.
- Android: `cd android; .\gradlew.bat testDebugUnitTest assembleRelease` with
  `JAVA_HOME=%LOCALAPPDATA%\OrbitDevTools\jdk\jdk-21.0.12.1+1` (the default JDK 25 doesn't work).

Any failure: stop and report it.

## 5. Pick the version

Propose it from the `## [Unreleased]` notes and the protocol check (Semantic Versioning):

- **major**: an incompatible wire-protocol change (`Proto.Version` bumped) or a removed feature;
- **minor**: new features or behaviour changes that stay compatible (sections `Added` / `Changed`);
- **patch**: only fixes (`Fixed`).

The user confirms or overrides the number. The tag must be `v` + exactly that version.

## 6. Release commit (ask before pushing)

On `main`:

1. `windows/Orbit.Windows/Orbit.Windows.csproj`: `<Version>X.Y.Z</Version>`.
2. `android/app/build.gradle.kts`: `versionName = "X.Y.Z"` and `versionCode` + 1 (never reuse or lower it;
   Android refuses the update otherwise).
3. If the protocol changed incompatibly and the PR didn't bump it: `Proto.Version` (`Core/PhoneLink.cs`) and
   `Proto.VERSION` (`Proto.kt`) together.
4. `CHANGELOG.md`: rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` (today) and add a fresh, empty
   `## [Unreleased]` above it. Tidy the notes for users: one line per change, no internal jargon.
5. Show `git diff`, then on a yes:

```
git commit -am "Release X.Y.Z"
git tag -a vX.Y.Z -m "Orbit X.Y.Z"
git push origin main
git push origin vX.Y.Z
```

Commit messages end with the attribution lines from the session's instructions, if any.

## 7. Watch the workflow

- `gh run list --workflow release.yml --limit 1`, then `gh run watch <id> --exit-status` (run it in the
  background; it takes several minutes).
- On failure: show the failing job's log (`gh run view <id> --log-failed`) and stop. Don't delete or move
  the tag without the user's say-so; the usual fix is a new patch version.
- On success: replace the draft's generic notes with this version's changelog section:
  `gh release edit vX.Y.Z --notes-file <file with the ## [X.Y.Z] section, plus the install line>`.
  Check the assets are there (`gh release view vX.Y.Z --json assets`): `OrbitSetup-*.exe`, the Windows zip,
  `Orbit-X.Y.Z-android.apk`, `SHA256SUMS.txt`.

## 8. Hand over

Give the user the draft release link (`gh release view vX.Y.Z --json url`) and remind them:

- review and click **Publish** on GitHub;
- if the APK was signed with the debug key (no keystore secrets), don't publish it (see `docs/RELEASING.md`);
- merged branches can be deleted now if they want (`git push origin --delete <branch>`); offer, don't do it.
