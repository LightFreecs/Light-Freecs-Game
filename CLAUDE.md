# Working rules for this repo

These rules come from the repo owner and apply to every session.

## Branches: always work on `main`
- Do all work on `main`. This file is the owner's explicit permission to commit and push to `main`,
  even if the session was started on another branch (such as a `claude/...` branch).
- If the session starts on another branch, switch to `main` first (`git fetch origin main && git checkout main && git pull`).
  If that session branch already holds commits, merge them into `main` first.
- When a task is done: commit it, merge everything into `main` and push `main`. Leave no uncommitted
  changes and no unmerged branches behind.
- If a push to `main` is rejected, merge `origin/main` in (`git pull --no-rebase`) and push again; never force-push `main`.

## At the start of every session: check for unmerged branches
- Run `git fetch origin --prune && git branch -r --no-merged origin/main` before starting the task.
- If anything is listed, tell the owner which branches they are and what they hold (`git log --oneline origin/main..<branch>`),
  and ask whether to merge or delete each one. Don't merge or delete without asking.
- Also check `git status` for uncommitted changes.

## After each task: phone notification
- When a task is finished (or blocked and waiting on the owner), send a phone push notification
  (the PushNotification tool) with a one-line summary of what was done or what's needed.

## Animations: always use Blender
- Every new or changed animation is made in Blender: posed and keyed on the character's rig (the player is `PlayerRig` in
  `ArtSource/Player/player.blend`, built by `tools/basebody_blender.py`). Don't write pose numbers by hand or pose straight on a web bench.
- Use the `blender` MCP server (Blender 4.5 in `D:\Blender`). If Blender can't be reached, stop and ask the owner; don't fall back to another method.
- The player's moves play as keyed poses through the game's IK (`BasicMovesets.cs`, `AttackPoseSolver`), so convert the Blender keys
  to those poses and check the result on the game's solver (`docs/bench/`) before it goes in.

## Project notes
- Unity project; the architecture and session history are in `docs/ARCHITECTURE.md`.
- CI (GitHub Actions, manual dispatch): `tests.yml`, `build-windows.yml`, `build-ios.yml` (unsigned .ipa for sideloading).
  An IPA can't be patched to add new code; rebuild it with `build-ios.yml` (~20 min).
- The Toon Head (`ToonHead.cs`, `ToonFacePainter.cs`, `Resources/Eyes`) is the definitive head and eye system.
