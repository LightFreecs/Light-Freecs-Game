# Project LightMMORPG

An online 3D anime-powers fighting game (shonen-style energy blasts, beams, flight, combos) with a cel-shaded look.
Current state: a playable **online combat prototype** — one arena, up to 4 players, host/join over the network.
Target platforms: **Windows** and **iOS** (Android later).

## Tech stack
- **Unity 6** (6000.5.1f1), Universal Render Pipeline (URP 17.5), custom toon shaders + bloom
- **FishNet 4.7.4** (embedded in `Packages/`): server-authoritative, client-side prediction, UDP transport (Tugboat)
- **Input System** (keyboard/mouse, gamepad, touch), runtime-built uGUI HUD
- **GitHub Actions + GameCI** for tests, Windows + Linux server builds, and an unsigned iOS `.ipa`

Design notes, build commands and per-session status live in [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## How to play (Windows)
- Double-click the **LightMMORPG** desktop shortcut. It runs [`tools/Play.ps1`](tools/Play.ps1), which rebuilds
  `Builds\Windows\LightMMORPG.exe` if any project file changed since the last build (takes a few minutes; close the
  Unity editor first), then starts the game.
- Or from PowerShell in the repo folder: `powershell -ExecutionPolicy Bypass -File tools\Play.ps1`
  (extra arguments go to the game, e.g. `tools\Play.ps1 -touchui`).
- In the menu:
  - **Host** — start a match on this PC and play in it (server + player).
  - **Join** — type the host's IP address (default `127.0.0.1` = same PC) and press Join.
  - **Server Only** — run a match without playing in it.
- **Esc** frees the mouse; click in the game window to lock it again.
- Start the game with `-touchui` to get the phone touch controls on PC (useful for testing the mobile layout).

### Controls
| Action | Keyboard / mouse | Gamepad | Touch (iPhone / `-touchui`) |
|---|---|---|---|
| Move | WASD | Left stick | Floating joystick (left half) |
| Look / aim | Mouse | Right stick | Drag on the right half |
| Sprint | Left Shift | L3 | SPRINT |
| Jump / fly up | Space | A | JUMP |
| Toggle flight | F | D-pad up | FLY |
| Fly down | Left Ctrl | D-pad down | DOWN |
| Dash | Q | B | DASH |
| Flash step | E | LB | FLASH |
| Light attack (combo) | Left mouse | X | LIGHT |
| Heavy attack | Right mouse | Y | HEAVY |
| Block (hold) | R | LT | BLOCK |
| Charge energy (hold) | C | D-pad left | CHARGE |
| Energy Blast | 1 | RT | BLAST |
| Charged Beam (hold, release to fire) | 2 | RB | BEAM |
| Power Surge | 3 | D-pad right | SURGE |
| Lock-on | Tab | R3 | LOCK |

Knock a fighter's HP to 0 for a KO; they respawn after 5 s at another spawn point. The scoreboard (top-right) counts
KOs and deaths.

## Multiplayer on two PCs
1. Both PCs need the game: run `tools\Play.ps1` on each, or copy the whole `Builds\Windows` folder to the second PC.
2. On PC 1 press **Host**. Find its local IP: `ipconfig` → *IPv4 Address* (e.g. `192.168.1.20`).
3. On PC 2 type that IP and press **Join**.
4. The game uses **UDP port 7770**. When Windows asks, allow LightMMORPG on **Private networks**. If PC 2 can't
   connect, allow the port on the host PC (PowerShell as administrator):
   ```powershell
   New-NetFirewallRule -DisplayName "LightMMORPG 7770" -Direction Inbound -Protocol UDP -LocalPort 7770 -Action Allow -Profile Private
   ```
   Both PCs must be on the same network. Playing over the internet needs port 7770/UDP forwarded on the host's router
   (or a VPN such as Tailscale/ZeroTier) — then join with the host's public/VPN IP.

## Working on the project
1. Install **Unity Hub** and Unity **6000.5.1f1** with *Windows Build Support (IL2CPP)*, *iOS Build Support* and
   *Linux Dedicated Server Build Support*.
2. In Unity Hub, sign in and activate a free **Personal** license.
3. `git lfs install`, clone, then open the folder in Unity Hub (Add → Add project from disk).
4. Generated content (materials, prefabs, `Arena_01` scene, build settings) comes from the **LightMMORPG → Build All**
   menu (`ProjectBuilder.BuildAll`); it is idempotent, so re-running it leaves `git status` clean.

```
Assets/_Project/
  Scripts/Core/       pure C# game rules (health, energy, combos, cooldowns, combat math)
  Scripts/Runtime/    networking, player, camera, combat, VFX, input, UI
  Editor/             ProjectBuilder (generates assets/scenes, Windows build)
  Art/                toon shaders, materials, volume profile
Assets/Tests/EditMode  30 unit tests for the core rules
tools/Play.ps1         rebuild-if-changed launcher
```

## CI builds (GitHub Actions)
| Workflow | Trigger | Output |
|---|---|---|
| `tests.yml` | push to `main`, pull requests, manual | EditMode test results (check run) |
| `build-windows.yml` | push to `main`, manual | `LightMMORPG-Windows` client + `LightMMORPG-Server-Linux` dedicated server |
| `build-ios.yml` | manual | `LightMMORPG-iOS-ipa` (unsigned `.ipa` for sideloading) |

All three need the Unity secrets below; until they're added the workflows will fail at the Unity step.

### Adding the 3 repository secrets (one-time)
1. Make sure Unity Hub is signed in and has an active Personal license. This creates the license file
   **`C:\ProgramData\Unity\Unity_lic.ulf`** (`C:\ProgramData` is hidden: paste the path into Explorer's address bar).
2. On GitHub open the repo → **Settings → Secrets and variables → Actions → New repository secret**, and add:
   - `UNITY_LICENSE` — open `Unity_lic.ulf` in Notepad, select all, and paste the **entire contents** (XML).
   - `UNITY_EMAIL` — the email of your Unity account.
   - `UNITY_PASSWORD` — your Unity account password.
3. Never commit the `.ulf` file (it's in `.gitignore`). If Unity later says the license was returned/expired, re-activate
   in Unity Hub and update `UNITY_LICENSE` with the new file contents.

## Testing on iPhone (sideloading with Sideloadly)
1. GitHub → **Actions → Build iOS → Run workflow** (branch `main`). It takes ~30–60 min (Linux export, then macOS compile).
2. Open the finished run, download the **LightMMORPG-iOS-ipa** artifact, and unzip it → `LightMMORPG-unsigned.ipa`.
3. Plug the iPhone into the PC with a cable (trust the computer on the phone; iTunes/Apple Devices drivers must be installed).
4. Open **Sideloadly**, drag the `.ipa` in, enter your Apple ID, press **Start**.
5. On the iPhone: **Settings → General → VPN & Device Management** → trust your Apple ID's developer profile.
   On iOS 16+ also enable **Settings → Privacy & Security → Developer Mode** (the phone restarts).
6. Launch the app and **Join** the PC's IP (both on the same Wi-Fi, PC hosting).

With a free Apple ID the app expires after 7 days — re-run Sideloadly to refresh it. Bundle ID: `com.lightfreecs.lightmmorpg`.
The repo is public, so Actions minutes (macOS included) are free.

## Known limitations / next steps
- **No lag compensation** yet: hits are checked against the server's current positions, so high ping makes melee feel
  late. Next: rewind-based hit validation.
- **Placeholder art**: capsule-style fighters, procedural sky, primitive arena; no audio.
- **No accounts or persistence**: names are random, nothing is saved between matches; no matchmaking/lobby — join by IP.
- **One arena, one character kit**; no character select.
- **Android is not set up** (no build target/CI yet).
- iOS build is unverified on a real device until the first CI run + sideload.
- Not yet hand-tested: VFX/HUD look, touch on a real phone/notch, block/guard-break and Power Surge in a live match.
