# Combat plan: anime fight feel + sword style

Owner's ask (2026-11-25): combat should feel smooth, like being in an anime fight, with a sword style like
SAO's sword skills. Focus on the physical fight. References: `D:\Videos\StreamLabs` (Gon vs Genthru, Kirito vs the
Gleam Eyes, SSJ2 Goku vs Vegeta, Gon vs Hisoka, Naruto vs Sasuke, Josuke vs Kira).

What the references share (from contact sheets): fighters cross the gap with every blow (dashes with speed lines),
impact frames (white flash, freeze) on the big hits, close-up camera punch-ins, launches into the scenery, and in
the SAO fight a low pre-motion with a glowing blade, then a system-driven dash and a string of glowing slashes.

## Done (phase 1, code only; compile-checked, not played)
- **Gap-closing lunges** (`PlayerMotor.QueueLunge`, predicted + reconciled): every light / heavy / sword skill dashes the
  fighter into striking range of its target (~34 m/s, light up to 2.6 m, heavy 3.6 m, skills 4.5 to 10 m) and snaps the
  facing to it. Target = lock-on target, else the nearest standing fighter within 65 degrees of the aim
  (`PlayerCombat.MeleeAim` / `AssistTarget`). No target: a short step along the aim.
- **Hits sweep the dash on the server** (`MeleeHits` is a capsule from where the blow started along the dash), so a
  lunging blow lands even if the server hasn't moved the attacker yet.
- **Input buffer**: Light / Heavy pressed up to 0.3 s early still fire when allowed (strings flow).
- **Anime cues** (`VFX/AnimeScreenFx`, `ThirdPersonCamera.Punch`): FOV widens with speed (dash, lunge, being launched),
  punches in on big hits; white impact frame + speed-line burst on big hits; speed lines while dashing / lunging.
- **Sword stance** (X / gamepad Select / SWORD touch button; synced `PlayerCombat.SwordDrawn`): a blade in the right fist
  (`VFX/SwordVisual`: placeholder cubes, glow shell, slash-arc ribbon trail), light swings reach +0.7 m and do x1.1 damage.
- **Sword skills** (`Combat/SwordSkills.cs`): in sword stance, hold Heavy = pre-motion (blade glows in the readied skill's
  colour, flares when the next one is ready), release = the skill: callout, system dash to the target, strikes on a
  schedule, then a post-motion delay (no moving or attacking).
  - Twin Fang (tap): 2 strikes 50 + 70, finisher knockback; 5 energy, 0.9 s cd, 0.2 s delay.
  - Quartet Edge (hold 0.45 s): 4 strikes 40/40/45/85, last one launches; 15 energy, 1.6 s cd, 0.4 s delay.
  - Comet Thrust (hold 1.1 s): one thrust 290 with a 10 m dash, full launch; 25 energy, 2.4 s cd, 0.6 s delay.
  - Not enough energy: a weaker skill, or the normal heavy.

## Next (phase 2): needs Blender (CLAUDE.md rule)
The skills and sword swings still replay the fist moves (light combo 0..3, heavy swing). Key real sword moves in
Blender on `PlayerRig` (`ArtSource/Player/player.blend`) through `tools/attack_keys_blender.py`, check them on the
game's solver (`docs/bench/`), and add a `Sword` moveset to `BasicMovesets` that `PlayerCombat` switches to in sword
stance (`_anim.SetMoveset`). Moves to key:
1. Sword light combo x4: diagonal slash down-right, rising slash back, horizontal sweep, thrust.
2. Sword pre-motion: low stance, blade drawn back at the hip, tip forward (Kirito's reference frames).
3. Twin Fang: two crossing slashes. Quartet Edge: four slashes in a square. Comet Thrust: long lunge thrust.
4. A sword model in Blender to replace the cube blade (keep the glow shell + ribbon trail).
Then: tune lunge distances / hit-stop after the owner plays it; ideas next in line: blade clash (both swing into each
other = sparks + push apart), pursuit dash after a launch (Dash right after a launcher zips to the victim), aerial
juggles.
