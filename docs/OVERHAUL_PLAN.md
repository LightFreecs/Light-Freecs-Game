# Overhaul plan: combat, movement, customization (owner's ask, 2026-11-26)

Goal: fast, responsive, anime-style 3D action combat with the tactical depth and mid-fight adaptability of competitive
Minecraft PvP (kits, hotbar swaps, attack pacing, reading the opponent and adapting).

The owner's ask, in short:
1. **Combat feel from the reference clips** in `D:\Videos\StreamLabs` (Gon vs Genthru, Kirito vs the Gleam Eyes, SSJ2 Goku
   vs Vegeta, Gon vs Hisoka, Naruto vs Sasuke, Josuke vs Kira): their combo strings, hit reactions and pacing. Faster
   exchanges, crisp hit-stop, sweeping attacks that break guards or open the opponent, reactive dodges.
2. **Smooth movement**: walking, running, dashing, dodging and attack recovery blend with no sluggish or clunky transitions.
3. **Stamina / fatigue**: continuous attacking, dodging and heavy techniques tire you out: no button-mashing, deliberate pacing.
4. **Archetypes and counterplay**: e.g. a defensive counter-heavy style vs aggressive rushdown; read the opponent and adapt.
5. **Main-menu customization**: equip clothes, weapons and fighting styles.
6. **Progression**: a loadout pool of unlocked weapons, styles and battle items to bring into a match; more gear by level.
7. **Mid-fight quick inventory**: switch weapons, styles or items during the battle when a plan isn't working.

## Rules for every task
Each task runs in its own session, one at a time (only one Unity process can open the project). The main session starts the
next one when a task is pushed; a task session doesn't start other sessions.
- `CLAUDE.md` applies: work on `main`. Every animation **and every model** (clothes, weapons, props) is made in Blender
  (`blender` MCP, Blender 4.5 in `D:\Blender`). Animations are keyed on `PlayerRig` in `ArtSource/Player/player.blend` through
  `tools/attack_keys_blender.py` and checked on the game's solver on a `docs/bench/` page before they go in. Moves traced from
  a reference are keyed from its measured frames, not interpreted. Blender can't be reached: stop and ask the owner.
- Only the headless compile check (`docs/ARCHITECTURE.md`, "Compile check"). The owner tests by hand.
- **Commit and push `main` at every compile-clean milestone**, not only at the end, so the owner can test what's done even if
  usage runs out mid-task. Running low: stop at a clean point, commit, push, and write what's left under the task here.
- At the end: tick the task under "Status" below, add a dated note to `docs/ARCHITECTURE.md` ("Status") with the tuning knobs
  (where + what), commit, push, and send the phone notification.
- Keep the existing rules unless a task changes them (`docs/ARCHITECTURE.md` "Gameplay rules", `docs/COMBAT_PLAN.md`).
  Names already taken: "Exhaustion" is the aura-reserve debuff, "Guard" the block meter. The new resource is **Stamina** and
  its empty state **Winded**.

## Design
- **Pacing**: an exchange is a short burst (a string, a dash, a block or a dodge), then a breather while stamina comes back. A
  full light string plus a dash costs about half the bar, so mashing runs dry and gets punished; reading the opponent pays.
- **Openings**: guards break (Guard meter, sweeps, guard-crush heavies), whiffs are punishable (full recovery), hits can be
  cancelled into a dash or block (smooth strings), perfect dodges and parries hand the defender the turn.
- **Styles** change how you win, not just numbers, and each one has a readable stance so the opponent can tell which it is.
  Counterplay triangle: Rushdown beats Breaker (too fast to wind up on), Breaker beats Counter (armor and guard crush beat
  parries and turtling), Counter beats Rushdown (parries punish mashing and barrages). Freestyle is the even middle.
- **Adapting**: a loadout of 2 weapons, 2 styles and 3 battle items is picked in the menu; the in-match hotbar swaps between
  them (like Minecraft hotbar switching: sword to axe against a shield, a pearl to escape, a potion to reset).

## Tasks

### T1. Reference breakdown + pacing core (code only, no new poses)
1. **Study the six clips** with ffmpeg (on PATH; frames go in the session's scratchpad, never the repo). Per fight: the combo
   strings (what follows what), the hit reaction to each kind of blow, timing in frames (wind-up, impact, freeze, recovery),
   and the rhythm (blows per exchange, the pause between exchanges, how an exchange ends: launch, clash, separation,
   knockdown). Write it into `docs/COMBAT_PLAN.md` ("Reference breakdown") with clip timestamps, so T2 and T3 key from it.
2. **Stamina** (`Core/StaminaPool.cs`, pure like `EnergyPool` / `GuardMeter`; numbers in `CombatConstants`): `PlayerStats`
   gets `Stamina` and `Winded` SyncVars, server-authoritative. Starting numbers, to tune from the clips' rhythm: max 100;
   regen 30/s after 0.6 s without spending (x1.4 standing still, x0.6 running or flying, x0.35 blocking or charging). Costs:
   light 6, heavy 10 (+8 at full charge), sword skills 12 / 20 / 28 (on top of energy), dash 14, rush 18, flash step 22. A dash
   that actually dodges a blow refunds 10 (perfect dodge).
   - Short of stamina: a light still comes out as a tired swing (x0.6 damage, no finisher knockback); everything else is
     refused and the HUD bar flashes.
   - **Winded** (stamina hits 0, until it refills to 40): no dash / rush / flash step / skills, heavy = tap only, attack
     recovery x1.3, guard damage taken x1.5. HUD shows WINDED (like EXHAUSTED). The winded pose comes in T2.
   - Prediction: dash / rush / flash step are predicted in `PlayerMotor`; owner and server must agree on the stamina gate (no
     predicted dash the server then cancels), e.g. stamina carried in the motor's predicted state and reconciled.
   - HUD stamina bar (`GameHud`, `HudBar`). The sparring dummy (`TrainingDummy`, `CombatAutoPilot`) spends stamina by the same
     rules and backs off when it runs low.
3. **Recovery cancels** (smooth strings): after a blow's impact frame, Dash, Block or Jump cuts the rest of its recovery (a
   dash-cancel costs +4 stamina); a whiffed blow keeps its full recovery. Movement input in the last ~35 % of a recovery hands
   over to locomotion with a short crossfade (~0.1 s) instead of finishing the pose. Blend timing is code; new poses are not.
4. Re-tune hit-stop and hitstun (`CombatMath.HitStop`, `CombatConstants`) to the measured frames: crisp on lights, long on
   finishers and launches.

### T2. Hit reactions (Blender)
Keyed on `PlayerRig`, traced from the clip frames T1's breakdown points to: flinch high (head snaps back), flinch body (folds
over), stagger (guard broken or parried: arms flung open, stumbling back), spin-out (hit by a sweep), knockdown (on the back)
plus get-up, tech recovery (Dash as you land = flip back to your feet, costs stamina), winded (bent over, breathing hard).
Keep `HitPoses.Launched` unless the clips say otherwise. Export into the game (`HitPoses`), check on a bench, play them by hit
type in `PlayerProceduralAnimator` (today: a procedural recoil + Launched); the server's hit message carries the reaction type.

### T3. Techniques from the clips (Blender + code)
Starting list; change it to what T1's breakdown actually shows. Each move keyed in Blender, bench-checked, rules on the server.
- **Sweep** (guard breaker): fists = a low spinning sweep kick, sword = a wide sweeping slash. Readable wind-up, x3 guard
  damage, knocks down a standing or blocking target (spin-out / knockdown), loses to a jump or a dash. Input: Heavy while
  holding Block (or better, if the clips suggest it).
- **Launcher, air chase, spike**: after a launch the pursuit dash (exists) reaches the victim in the air; Heavy in the air
  spikes them down (ground bounce, tech-able with T2's recovery).
- **Clash / cross-counter** (Goku vs Vegeta, Naruto vs Sasuke): two blows that meet within ~0.1 s bounce off each other
  (flash, sparks, both pushed apart, short hit-stop); heavy against heavy is a big clash.
- **Feint**: Block during a heavy wind-up cancels it (8 stamina): baits a parry or a dodge.

### T4. Fighting styles (code + Blender stances and signature moves)
`Core/FightingStyles.cs` (data) and a server-validated `Style` SyncVar. Each style = modifiers + a signature technique + its own
stance idle (keyed in Blender, readable at a glance). A temporary key cycles styles until T6's hotbar; the sparring dummy can
play a style so the owner can practise reads in training.
- **Freestyle** (default): no changes.
- **Rushdown**: speed +10 %, dash / rush stamina -30 %, light recovery -15 %, rush range +25 %; max guard -25 %. Signature:
  Barrage (hold Light on the third hit: rapid blows that cost stamina each and end in a push; Josuke's barrage).
- **Counter**: Parry (Block pressed at most 0.15 s before a blow lands: no damage, the attacker is staggered 0.7 s, your next
  blow is a counter hit x1.5), perfect-dodge window 0.15 to 0.22 s, guard recovery +50 %; dash stamina +20 %, light damage -10 %.
- **Breaker**: armored heavy wind-ups (absorb one light blow at half damage), guard damage x2, its Sweep knocks down through a
  block, max HP +10 %; stamina regen -15 %, dash distance -15 %.

### T5. Gear, loadout, unlocks and the main-menu LOADOUT screen (code + UI)
- `Core/Gear.cs`: `GearKind { Weapon, Style, Item, Outfit }` and a catalog (id, kind, name, description, unlock level, stats /
  tags). Starting table: weapons Fists 1, Sword 1 (T8 adds more); styles Freestyle 1, Rushdown 3, Counter 5, Breaker 7; items
  Stamina Draught 1, Healing Salve 4, Smoke Bomb 6, Blink Charm 9, Iron Tonic 12 (effects in T6); outfits Default 1 (+ T7's).
- `Loadout`: 2 weapons, 2 styles, 3 items, 1 outfit; validated (unlocked, no duplicates, defaults fill gaps).
  `Progression.Unlocks` lists the gear (new `UnlockKind`s) so level-ups and the results screen show it.
- `AccountRecord.loadout` (bump `AccountStore.FileVersion`, migrate old files; online accounts too).
- Main menu: a LOADOUT screen in the menu's style (`MainMenuUI` state, `MenuUi`, `MenuTheme`): the pool as a grid (locked gear
  dimmed with "Lv N"), the slots, a description panel; the menu fighter holds the chosen weapon. Customize gets a Clothes tab
  once T7 has outfits.
- In the match: the loadout goes to the server at spawn (`PlayerIdentity`), validated against the account level. Training mode
  unlocks everything, so the owner can test all gear.

### T6. In-match quick inventory + battle items (code + Blender props and use animations)
- Hotbar on the HUD: weapons 1 / 2, styles 1 / 2, items 1-3, with cooldown sweeps and charges. PC: X = the other weapon
  (replaces the sword toggle), V = the other style, 4 / 5 / 6 = items, mouse wheel = next weapon. Gamepad: Select tap = the other
  weapon, Select hold = a radial on the right stick for styles and items. Touch: hotbar buttons. All rebindable in Settings.
- Rules: a weapon swap is a 0.3 s draw (not during an attack or stun; you're open), a style swap has a 4 s cooldown and a short
  stance flourish, items have charges per life and cooldowns; all server-authoritative.
- Items: Stamina Draught (+60 stamina, ends Winded), Healing Salve (heals 200 over 4 s, a hit cancels it), Smoke Bomb (a cloud
  that breaks lock-on on you for 3 s, plus a back-step), Blink Charm (thrown, you teleport where it lands, like an ender pearl),
  Iron Tonic (guard refill, 30 % less guard damage for 8 s). Props modeled in Blender; drink / throw keyed in Blender.

### T7. Outfits (Blender models)
3-4 original outfits modeled in Blender on the base body (`tools/basebody_blender.py`, `player.blend`), skinned to
`PlayerRig`, toon materials (`LightMMORPG/ToonLit`): e.g. a fighter's gi, a short jacket with pants, a long coat, a sleeveless
top. Worn in the menu and in matches (synced), chosen in Customize (Clothes tab) and the loadout, unlocked by level.

### T8. Weapons (Blender models + movesets)
First a real sword model to replace the cube blade (keep the glow shell and ribbon; `COMBAT_PLAN.md` "Next" 1). Then one new
weapon per task, each a Blender model plus a moveset keyed in Blender, bench-checked, with a catalog entry: e.g. a spear
(reach, sweeps; suits Breaker), twin daggers (fast strings; suits Rushdown).

### T9. Movement polish (Blender)
Sword stance idle and run (`COMBAT_PLAN.md` "Next" 3), dodge / sidestep poses for dashes, landings and recovery blends, and
whatever the owner reports as clunky after playing T1-T6.

## Status
- [x] T1 Reference breakdown + pacing core: done 2026-11-26 (compile-checked, not played). Breakdown in `COMBAT_PLAN.md`;
  stamina / Winded / recovery cancels / hit-stop as specified above. Knobs: `CombatConstants` (Stamina*, Winded*, TiredDamageMultiplier),
  `CombatMath.HitStopLight / HitStopBig / HitStopCharge`, `PlayerProceduralAnimator.HandoverFade / HandoverShare`, `TrainingDummy.LowStamina`.
  Details: `ARCHITECTURE.md` Status 2026-11-26 (T1).
- T2 Hit reactions: not started
- T3 Techniques from the clips: not started
- T4 Fighting styles: not started
- T5 Gear, loadout, unlocks, LOADOUT screen: not started
- T6 Quick inventory + battle items: not started
- T7 Outfits: not started
- T8 Weapons: not started
- T9 Movement polish: not started
