# Project LightMMORPG

A 3D anime fighting MMORPG with shonen-style powers, built in a cel-shaded anime style.
Target platforms: **Windows** and **iOS** first, **Android** later.

## Tech stack
- **Unity 6** (6000.5.1f1) with the Universal Render Pipeline (URP)
- **FishNet** for networking: server-authoritative, client-side prediction, dedicated servers
- **Input System** for keyboard/mouse, gamepad and touch
- **Cinemachine 3** for the third-person and lock-on camera
- **GitHub Actions + GameCI** for Windows, Linux server and iOS builds

## Getting started
1. Install **Unity Hub** and Unity **6000.5.1f1**, adding the *Windows Build Support (IL2CPP)*, *iOS Build Support* and *Linux Dedicated Server Build Support* modules.
2. In Unity Hub, sign in and activate a free **Personal** license (Preferences → Licenses).
3. Install Git LFS (`git lfs install`), then clone this repo.
4. Open the folder in Unity Hub (Add → Add project from disk).

## Project layout
```
Assets/_Project/
  Scripts/    Core, Networking, Player, Combat, Abilities, Camera, Input, UI
  Data/       ScriptableObject definitions (characters, abilities)
  Art/        Shaders, materials, VFX
  Prefabs/    Player, projectiles, arena props
  Scenes/     Bootstrap, MainMenu, Arena_01
Assets/Tests/EditMode   Unit tests for combat and ability logic
```

## CI builds (GitHub Actions)
| Workflow | Trigger | Output |
|---|---|---|
| `tests.yml` | push / PR | EditMode test results |
| `build-windows.yml` | push to `main` / manual | Windows client + Linux dedicated server |
| `build-ios.yml` | manual | Xcode project + unsigned `.xcarchive` |

### Required repository secrets
Set these under **Settings → Secrets and variables → Actions**:
- `UNITY_EMAIL`, `UNITY_PASSWORD`: your Unity account
- `UNITY_LICENSE`: the contents of your `Unity_lic.ulf` file (Windows: `C:\ProgramData\Unity\Unity_lic.ulf`), created after activating a Personal license

For signed iOS builds (installing on an iPhone or uploading to TestFlight) you also need an Apple Developer account, plus signing secrets that we'll add later.
