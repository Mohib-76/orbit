---
name: create-release
description: Cut and publish an Orbit release from dev to main. Lists what's waiting, picks the version, audits every document and the landing page against what changed and fixes gaps, bumps both apps, tests, merges a release PR into main, tags vX.Y.Z, watches the Release workflow, verifies the assets and signature, publishes the GitHub release, syncs main back into dev and ends with a status table of everything done, skipped, pending or failed. Use when the user says /create-release, "release", "ship", "publish a version" or "make a release".
argument-hint: "[optional: version, e.g. 1.2.0]"
---

# Releasing Orbit

```
dev ──▶ release/X.Y.Z (docs audit, site, version bump) ──PR──▶ main ──tag vX.Y.Z──▶ Release workflow ──▶ draft ──▶ published
                                                                 └──────── fast-forward / merge back ──────────▶ dev
```

Pushing a `v*` tag runs `.github/workflows/release.yml`: it tests and builds both apps (Android signed with the
`ORBIT_*` repository secrets), makes the installer, zip, APK and `SHA256SUMS.txt`, and drafts a GitHub release.
`docs/RELEASING.md` is the human version of this process; keep the two consistent.

Keep a running log of every step's outcome as you go; you need it for the final table (step 12).

## Rules

- **Two confirmations**, each showing exactly what will happen: (A) before anything is pushed to `main` or a tag
  is pushed (release plan: version, contents, doc/site changes); (B) before the draft is published. A "yes" to
  one isn't a yes to the other. If the user said up front "publish without asking", B can be skipped.
- Never force-push, never rewrite `main` or `dev`, never move or delete a pushed tag, never delete `dev` or
  `main`. Delete other branches only when the user agrees (offer at the end).
- Stop, don't work around, when: tests fail, CI is red, a merge conflict isn't just `CHANGELOG.md` lines, the
  privacy scan has findings, or the release APK isn't signed with the release key. Report and ask.
- Never publish a release whose APK is signed with the debug key.
- If `CLAUDE.local.md` exists, follow it; never copy anything from it into committed files, the release notes or
  the website.
- Commit messages end with the attribution lines from the session's instructions, if any.

## 0. Preflight

1. `gh auth status`; `git status --short` must be clean (stop otherwise); `git fetch origin --prune --tags`.
2. `dev` exists on origin (`git ls-remote --exit-code --heads origin dev`). If not, the repo hasn't moved to the
   dev model yet: offer to create it from `main` (as in `start-work`) once the pending PRs are handled.
3. Last release: `git describe --tags --abbrev=0 origin/main`. Current versions:
   `windows/Orbit.Windows/Orbit.Windows.csproj` `<Version>`, `android/app/build.gradle.kts` `versionName` /
   `versionCode`. Different versions in the two apps: say so; this release brings them together.
4. Release secrets exist: `gh secret list` shows `ORBIT_KEYSTORE_BASE64`, `ORBIT_KEYSTORE_PASSWORD`,
   `ORBIT_KEY_ALIAS`, `ORBIT_KEY_PASSWORD` (names only). Missing → the workflow fails; stop and point to
   `docs/RELEASING.md`.
5. `git log --oneline origin/dev..origin/main`: commits on `main` that aren't on `dev` (a hotfix, or a release
   that wasn't merged back). They'll be in this release via the merge; mention them.

## 1. What's waiting

Show in one message:

- **On `dev` since the last release:** `git log --oneline --no-merges origin/main..origin/dev` and merged PRs
  (`gh pr list --state merged --base dev --search "merged:>=<last tag date>" --json number,title,author`).
- **Open PRs into `dev`:** number, title, author, draft?, CI, mergeable. Ask whether to wait for any of them
  (they're reviewed with `review-pr` and merged by the owner) or release without them.
- **Open PRs into `main`** (other than a release PR): these bypass `dev`. Suggest retargeting each
  (`gh pr edit <n> --base dev`). Merge into `main` directly only if the user asks; then review each like
  `review-pr` does, merge with `gh pr merge <n> --merge`, and resolve `CHANGELOG.md`-only conflicts by keeping
  both sides (test the branch after resolving, push, wait for CI).
- `CHANGELOG.md` → the `## [Unreleased]` section on `origin/dev`.

If `dev` has nothing new since the last tag, say so and stop.

## 2. Pick the version

From the Unreleased notes and the diff (`git diff <lastTag>..origin/dev`), propose (Semantic Versioning):

- **major**: incompatible protocol (`Proto.Version` / `Proto.VERSION` changed since the last tag:
  `git diff <lastTag>..origin/dev -- windows/Orbit.Windows/Core/PhoneLink.cs android/app/src/main/java/**/Proto.kt`)
  or a removed feature;
- **minor**: new features / compatible behaviour changes (`Added`, `Changed`);
- **patch**: fixes only.

`$ARGUMENTS` may already name the version; check it's higher than the last tag. The tag is `v` + the version.

## 3. Release branch

`git switch -c release/X.Y.Z origin/dev --no-track`. All of steps 4–6 happen here.

## 4. Documentation audit (fix what's wrong)

Goal: after this release every document describes the app as it now is. Use
`.claude/skills/_shared/docs-map.md` and the diff since the last tag. Check each item and record the outcome:

1. **CHANGELOG.md**: every user-visible change in `git log <lastTag>..HEAD` has a line; no internal jargon,
   one line per change, grouped under `### Added` / `### Changed` / `### Fixed` / `### Removed`.
   Missing lines: add them.
2. **README.md**: the Features table, "What needs what", "Known limits" and "Install" match the new features
   (a new capability → a row; a lifted limit → remove it; a replaced setting → new name).
3. **docs/PROTOCOL.md**: every packet type in code is documented:
   ```bash
   comm -23 <(grep -rhoE '"orbit\.[a-z0-9.]+"' windows/Orbit.Windows android/app/src/main | tr -d '"' | sort -u) \
            <(grep -oE 'orbit\.[a-z0-9.]+' docs/PROTOCOL.md | sort -u)
   ```
   prints the undocumented ones (should be empty). Fields in `orbit.settings` documented too.
4. **docs/ARCHITECTURE.md**: new subsystems (a new `Core/<Area>/` folder or Android package) have a section;
   changed behaviour (e.g. something that "only ever turns on") is no longer contradicted.
5. **PRIVACY.md**: new permissions in `AndroidManifest.xml` since the last tag
   (`git diff <lastTag>..HEAD -- android/app/src/main/AndroidManifest.xml`), new data shared between devices or
   stored, new logs: described.
6. **docs/TROUBLESHOOTING.md**: new features with prerequisites or known failure modes have an entry.
7. **THIRD_PARTY_NOTICES.md** / `licenses/`: new dependencies since the last tag
   (`git diff <lastTag>..HEAD -- '*.gradle.kts' '*.csproj' tools/fetch-thirdparty.ps1`) listed with license.
8. **CLAUDE.md**: file layout, partial classes, folders, default hotkeys and settings it lists still exist and
   new ones are listed (check paths it names: `test -e`). **CONTRIBUTING.md**, **docs/RELEASING.md**: still
   describe the real process.
9. Relative links in the Markdown files resolve (`[..](docs/…)`, `#anchors` you can check by heading).

Make the fixes in the docs' existing style. Keep a list: "README: added Media controls row", etc.

## 5. Landing page (`site/index.html`)

The page fetches the latest version from the GitHub API and links to `releases/latest`, so no version edits are
needed. Update content only when the release adds a **major user-facing feature** (one that deserves a tile next
to Clipboard / Files / Screen mirroring / Notifications / Earbuds in the features bento) or removes one. Read
`docs/SITE_DESIGN.md` first:

- Add/adjust an `<article class="tile …">` in `.bento`: a 1–3 word `<h3>`, one short line `<p>`, and a screen of the
  feature rendered from the design canvas as WebP in `site/img/` (sample data only: replace personal or real-world
  details in a local copy before rendering). Keep each bento row's spans adding up to 12 (`t5` + `t7`, `t6` + `t6`, `t12`).
  No screen designed yet: ask the owner for one rather than drawing a fake UI.
- Update `<meta name="description">`, `og:description` and the hero copy if they list features.
- Don't change the design, fonts, colors or scripts; don't add trackers or external resources.
- Check it renders: headless screenshot with Edge/Chrome, e.g.
  `msedge --headless=new --disable-gpu --window-size=1280,2600 --screenshot=<scratchpad>\site.png file:///<repo>/site/index.html`
  and a 390-px-wide one for mobile, then look at both images.

Minor or fix-only releases: record "Landing page: no change needed (no new major feature)".

## 6. Version bump, checks, tests

1. `windows/Orbit.Windows/Orbit.Windows.csproj` → `<Version>X.Y.Z</Version>`.
2. `android/app/build.gradle.kts` → `versionName = "X.Y.Z"`, `versionCode` + 1 (never reuse or lower it).
3. Protocol incompatible and not bumped yet → `Proto.Version` (`Core/PhoneLink.cs`) and `Proto.VERSION`
   (`Proto.kt`) together, and the version must be major.
4. `CHANGELOG.md`: rename `## [Unreleased]` → `## [X.Y.Z] - YYYY-MM-DD` (today) and put an empty
   `## [Unreleased]` above it.
5. Privacy scan of everything going to `main`:
   `bash .claude/skills/_shared/privacy-scan.sh origin/main` (covers the release branch plus uncommitted edits).
   Findings: stop and ask.
6. Tests and release builds, in parallel in the background:
   - `dotnet test windows/Orbit.Tests -c Release` and `dotnet publish windows/Orbit.Windows -c Release -r win-x64 --self-contained false -o <scratchpad>\win`
     (don't touch the installed copy of Orbit);
   - `cd android; .\gradlew.bat testDebugUnitTest lintDebug assembleRelease --console=plain` (JDK 17–21; if
     `JAVA_HOME` isn't set use the JDK under `%LOCALAPPDATA%\OrbitDevTools\jdk\`).
   Any failure: stop.
7. Commit: `git add` the named files, `git commit -m "Release X.Y.Z"` (body: one line per doc/site fix).

## 7. Confirmation A, then merge into `main` and tag

Show the plan: version, PRs/commits included, doc and site changes, test results, and the commands below. Ask.
On yes:

```bash
git push -u origin release/X.Y.Z
gh pr create --base main --head release/X.Y.Z --title "Release X.Y.Z" --body-file <scratchpad file: the changelog section + doc/site changes>
gh pr checks <n> --watch --fail-fast          # must be green
gh pr merge <n> --merge                        # merge commit; keeps history like before
git fetch origin && git switch main && git merge --ff-only origin/main
git tag -a vX.Y.Z -m "Orbit X.Y.Z" origin/main
git push origin vX.Y.Z
```

## 8. Watch the Release workflow

- `gh run list --workflow release.yml --limit 1 --json databaseId,headBranch,status`, then
  `gh run watch <id> --exit-status` in the background (it takes several minutes).
- Failure: `gh run view <id> --log-failed`, explain, stop. Don't move or delete the tag; the usual fix is a new
  patch version after fixing on `dev`.

## 9. Verify the draft

1. Notes: write the `## [X.Y.Z]` section of `CHANGELOG.md` (without its heading) plus an **Install** line
   ("Windows: run `OrbitSetup-X.Y.Z.exe`. Android: install `Orbit-X.Y.Z-android.apk`. Check downloads against
   `SHA256SUMS.txt`.") to a scratchpad file; `gh release edit vX.Y.Z --notes-file <file> --title "Orbit X.Y.Z"`.
2. Assets: `gh release view vX.Y.Z --json assets,isDraft` lists `OrbitSetup-X.Y.Z.exe`,
   `Orbit-X.Y.Z-windows-x64.zip`, `Orbit-X.Y.Z-android.apk`, `SHA256SUMS.txt`.
3. Download them to a new, empty scratchpad folder (`gh release download vX.Y.Z -D <dir>`) and check
   `sha256sum -c SHA256SUMS.txt`.
4. APK signature: `apksigner verify --print-certs <apk>` (Android SDK `build-tools/*/apksigner.bat`). The
   certificate SHA-256 must equal the previous release's APK (download it the same way) and must not be the
   Android debug certificate (`CN=Android Debug`). Different or debug → stop, don't publish.
5. `versionName` in the APK equals X.Y.Z (`aapt2 dump badging` or `apksigner` output) and the installer name
   carries X.Y.Z.

## 10. Confirmation B, then publish

Show the draft URL, notes and asset check results. On yes:
`gh release edit vX.Y.Z --draft=false --latest`. Confirm with `gh release view vX.Y.Z --json isDraft,url`.

## 11. After publishing

1. **Merge back into `dev`**: if `git merge-base --is-ancestor origin/dev origin/main`, fast-forward:
   `git push origin origin/main:refs/heads/dev`. Otherwise open a PR `main` → `dev`
   ("Merge release X.Y.Z back into dev"), wait for CI, merge it with `--merge`.
2. **Website**: if `site/` changed, the Website workflow deploys on the push to `main`:
   `gh run list --workflow pages.yml --limit 1`, watch it, then check the live page
   (`curl -s https://mohib-76.github.io/orbit/ | grep -c "<new card title>"`). The page's "Latest" version label
   updates by itself from the API.
3. Switch back to `dev` locally and fast-forward it.
4. Offer (don't do) to delete merged branches: `release/X.Y.Z` and the feature branches merged in this release
   (`git push origin --delete <branch>`, `git branch -d <branch>`).

## 12. Report

End with a table of everything, so the user can give feedback on the release:

| # | Step | Status | Details |
|---|---|---|---|
| 1 | Preflight (clean tree, secrets, versions) | ✅ Done | 1.0.0 → 1.1.0 |
| 2 | PRs included | ✅ Done | #5, #7 |
| 3 | PRs left out | ⏭️ Skipped | #8 (draft) |
| 4 | Docs audit: README | ✅ Done | added Media controls row |
| … | Docs audit: each other document | ✅ / ⏭️ No change needed | … |
| … | Landing page | ✅ / ⏭️ | … |
| … | Privacy scan | ✅ | clean |
| … | Windows tests / Android tests / lint / release builds | ✅ / ❌ | counts |
| … | Release PR, merge, tag | ✅ | #9, vX.Y.Z |
| … | Release workflow | ✅ / ❌ | run link |
| … | Assets, checksums, APK signature | ✅ / ❌ | … |
| … | Published | ✅ / ⏳ Pending your OK | URL |
| … | dev synced with main | ✅ / ⏳ | … |
| … | Website deployed | ✅ / ⏭️ | … |
| … | Not tested on hardware | ⏳ Pending | from the PRs' "Not tested" sections |
| … | Branch cleanup | ⏳ Offered | … |

Statuses: ✅ Done · ⏭️ Skipped (with the reason) · ⏳ Pending (who/what it waits for) · ❌ Failed (with the
error and the suggested fix). Then ask the user for feedback on anything they want changed for next time.
