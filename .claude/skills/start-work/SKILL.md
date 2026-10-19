---
name: start-work
description: Start a piece of Orbit work on a fresh branch cut from dev. Checks the tree is clean, syncs dev with origin (creating dev from main the first time), names the branch feat/fix/docs/chore-…, switches to it and lists which documents the work will have to update. Use when the user says /start-work, "start working on X", "new feature", "new branch", or begins a task while sitting on main or dev.
argument-hint: "<what you're about to work on>"
---

# Start work on a branch from `dev`

Orbit's branch model:

```
feature branches ──PR──▶ dev ──release PR──▶ main ──tag vX.Y.Z──▶ GitHub release
     (feat/…, fix/…)      (integration)        (released code only)
```

- Every change starts from `origin/dev` and comes back to `dev` through a pull request (`/create-pr`).
- `main` only moves at release time (`/create-release`), so `main` is always the last released code.
- Hotfix for a released bug: still branch from `dev` unless the user explicitly asks for a hotfix off `main`.

`$ARGUMENTS` is the user's description of the work. If it's empty, ask what they're about to work on (one
question) before anything else.

## Rules

- One checkout only: never create extra `git worktree` folders. If another Claude session is clearly working in
  this checkout (uncommitted changes you didn't make, a rebase/merge in progress), stop and ask.
- Never discard the user's changes: no `git reset --hard`, `git checkout -- .`, `git clean`, or `stash drop`.
- Don't push anything except, the very first time, the new `dev` branch (and only after the user says yes).
- If `CLAUDE.local.md` exists, follow its device and design notes, but never copy anything from it into a file
  that gets committed.

## 1. Preflight

1. `git status --short` and `git rev-parse --abbrev-ref HEAD`.
   - A merge, rebase or cherry-pick in progress (`.git/MERGE_HEAD`, `.git/rebase-merge`): stop and tell the user.
   - Uncommitted changes: show them and ask which of these to do:
     **carry them** onto the new branch (they follow the `git switch`), **commit them** on the current branch first,
     or **stash** them (`git stash push -u -m "start-work: <current branch>"`; tell the user the stash name).
2. `git fetch origin --prune --tags`.

## 2. Make sure `dev` exists and is current

- `git ls-remote --exit-code --heads origin dev`.
  - **Missing** (first run): explain that `dev` will be created from `origin/main` and pushed, ask, then
    `git branch dev origin/main && git push -u origin dev`. Suggest (don't do) setting `dev` as the PRs' default
    target in GitHub settings or keeping `main` default; the skills pass `--base dev` explicitly either way.
- Local `dev`: if it exists, fast-forward it: `git switch dev && git merge --ff-only origin/dev`.
  If it has local commits not on `origin/dev`, stop and show them (`git log origin/dev..dev`); don't push them.
- Check `git log --oneline origin/dev..origin/main`. Commits on `main` that `dev` lacks usually mean a release
  wasn't merged back. Tell the user; `create-release` normally syncs `main` back into `dev`.

## 3. Name and create the branch

- Pick the prefix from the work: `feat/` (new feature), `fix/` (bug), `docs/`, `chore/` (build, CI, deps,
  tooling), `refactor/`, `test/`. Then 2–5 lowercase words joined by `-`, no personal names or device models:
  `feat/media-seek-bar`, `fix/buds-reconnect-loop`.
- Propose the name in one line and go ahead unless the user objects. If it already exists
  (`git rev-parse --verify --quiet <name>` or `git ls-remote --heads origin <name>`), ask whether to continue on
  the existing branch (`git switch <name>`; then `git merge origin/dev` if it's behind) or pick a new name.
- Create it from the remote tip, not a possibly stale local `dev`:
  `git switch -c <name> origin/dev --no-track` (no upstream yet; `create-pr` pushes it with `-u`).

## 4. Plan the documentation with the work

Read `README.md`, `CLAUDE.md` and, for anything crossing devices, `docs/PROTOCOL.md`, then the doc map in
`.claude/skills/_shared/docs-map.md`. From the user's description, list the documents this work will most likely
have to update, as a short checklist, for example:

```
Docs this work will need:
- [ ] CHANGELOG.md – Unreleased › Added: "<user-facing line>"
- [ ] docs/PROTOCOL.md – new orbit.foo.* packets
- [ ] README.md – Features table row
- [ ] CLAUDE.md – new Core/Foo/ folder
```

Keep the list in mind for the rest of the session and tick items off as you make the change. Update the docs in
the same branch as the code, not later. A CHANGELOG line is needed for anything a user would notice.

For UI work, look at the design first (the owner's design source is named in `CLAUDE.local.md` when present;
otherwise ask for a design before building new screens).

## 5. Hand over

Tell the user, briefly:

- the branch name and that it's based on `origin/dev` at `<short sha>`;
- what happened to any uncommitted changes (carried / committed / stashed as …);
- the docs checklist;
- that `/create-pr` will run the checks and open the pull request into `dev` when they're done.
