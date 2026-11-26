# Combat plan: anime fight feel + sword style

Owner's ask (2026-11-25): combat should feel smooth, like being in an anime fight, with a sword style like
SAO's sword skills. Focus on the physical fight. References: `D:\Videos\StreamLabs` (Gon vs Genthru, Kirito vs the
Gleam Eyes, SSJ2 Goku vs Vegeta, Gon vs Hisoka, Naruto vs Sasuke, Josuke vs Kira).

What the references share (from contact sheets): fighters cross the gap with every blow (dashes with speed lines),
impact frames (white flash, freeze) on the big hits, close-up camera punch-ins, launches into the scenery, and in
the SAO fight a low pre-motion with a glowing blade, then a system-driven dash and a string of glowing slashes.

## Reference breakdown (overhaul T1, 2026-11-26)
Measured with ffmpeg on the six clips in `D:\Videos\StreamLabs`: per-frame brightness (`signalstats` YAVG: white impact
frames) and frame-to-frame change (YDIF: a held drawing reads ~0, a cut spikes), then frame-by-frame strips at the impacts.
Timestamps are the clip's own (m:ss.ss); "f" = frames at the clip's rate. T2 / T3: cut your own frames at these times.

| Clip (file has the name) | fps | mean shot | impact flashes | what it's good for |
|---|---|---|---|---|
| SSJ2 Goku vs Vegeta | 29.97, drawn on 2s/3s | 0.30 s | 23 | the melee template: trades, clash, knee, launch |
| Gon vs Hisoka | 23.976 | 0.81 s | 62 | dash-in punches, counters, the barrage |
| Kirito vs the Gleam Eyes | 30 | 0.58 s | 15 | sword skill strings, a deflect, the boss's sweeps |
| Naruto vs Sasuke | 23.976 | 1.14 s | 7 | mutual charges, a face punch, 1 vs clones |
| Josuke vs Kira | 59.94 (interpolated from ~24) | n/a | 7 | punch rush, the big punch's freeze |
| Gon vs Genthru | 23.976 | 2.0 s | 6 | the charged finisher (Rock) |

**Goku vs Vegeta** (the closest to our game)
- 0:03.50-0:05.07 **Fist clash**: the two fists pressed together, shaking, 20 f (0.67 s); 0:04.17 one black frame, 0:04.20 one frame of
  lightning, 0:04.23-0:04.53 white with dark silhouettes (10 f, 0.33 s), 7 f fading back, then still locked, with sparks.
- 0:15.73-0:16.20 hands locked, pushing; 0:16.40 **punch to the head**: 2 f flash, the struck drawing held 3 f (0.10 s), Vegeta's head
  snaps sideways, his arm flung out, the body twists away and drifts ~0.6 s (flinch high).
- 0:26.40-0:27.53 **Knee to the gut**: the knee rises slowly (15 f, 0.5 s: readable), contact 0:26.90, sinks in 5 f, white streak flash
  0:27.07 (2 f), held 13 f (0.43 s) with Vegeta folded over the knee (flinch body); the next impact 15 f later (0:27.59); 0:27.57 wide:
  both tumbling on the ground.
- 0:34.30-0:35.47 **Trade**: Vegeta's rising kick from below into Goku's gut (0:34.30-0:34.43; Goku folds, mouth open, 0:34.57), a fist
  in his stomach (0:34.90), Goku's punch into Vegeta's face (white streak 0:35.10, the fist held 3 f), Vegeta knocked back (0:35.23).
  Impacts at 0:34.80 / 0:34.90 / 0:35.10 / 0:35.23: **3-6 f (0.10-0.20 s) apart**.
- 0:49.48 **Driven into the ground**: the clip's brightest flash (2 f), then 20+ f (0.7 s) of near stillness, dust (knockdown).
- Rhythm: bursts of 2-4 blows inside 0.3-1 s, ended by a separation (knockback, launch, clash, a cut to the wide shot), then
  1-2 s of repositioning (flying, circling) before the next burst. The camera cuts every 0.3 s.

**Gon vs Hisoka**
- 0:17.00-0:18.38 **Dash-in punch**: Gon crosses the gap in 4 f (0.17 s), the fist reaches Hisoka at 0:17.17 and presses 14 f
  (0.58 s), impact 0:18.00 (2 f white blur, crack lines), held 8 f (0.33 s). 0:18.42 wide: Gon doubled over with Hisoka's elbow on his
  back: **a committed dash-in got read and countered**.
- 4:35-4:37 **"Rush! Rush! Rush!"**: a blow every ~4 f (0.17 s), from alternating sides, Hisoka's head snapping left / right with each,
  10+ blows; before it (4:33-4:35) Hisoka's head drifting back with debris = being driven back by the blows.
- The most hits of all six (62 flashes); shots average 0.81 s.

**Naruto vs Sasuke**
- 0:25.00-0:25.60 **Mutual charge** (split screen), then the fists meet (0:30-0:32.5, a clash).
- 1:47.67-1:48.50 **Face punch**: the fist crosses the cheek in 2-3 f, the head snaps back and stays back 16 f (0.67 s).
- 1:48.5-1:51 **Sasuke vs clones**: each clone's attack takes 6-8 f (0.25-0.33 s); he ducks, kicks one away per beat.

**Kirito vs the Gleam Eyes**
- 1:41.23-1:42.07 **Deflect**: Asuna's thrust meets the boss's blade: 2-3 f flash (1:41.47), the blade slides past in a 4 f streak,
  Asuna flips back over it (12+ f).
- 2:05.00-2:06.30 **Starburst Stream**: dash in 0.33 s, then strikes every 2-4 f (0.07-0.13 s) with glowing trails and red wound marks,
  no freeze per strike; the string ends in one long still (15+ f, 0.5 s, from 2:06.30) on the wound.
- 0:24-0:36 the boss's sweeps send the players spinning / sprawling across the floor.

**Josuke vs Kira**
- 0:03.0-0:05: punches traded every ~5 f at 60 fps (0.08 s).
- 0:13.40-0:14.03 **"Dora!"**: the fist arrives in a 2-frame smear, then stays in Killer Queen's face 30 f (0.5 s) over red speed lines.

**Gon vs Genthru**
- 1:38-1:46 **Rock**, the charge: the fist glows across several cuts (cinematic, ~8 s).
- 1:46.50-1:48.17 the impact shakes in place for 41 f (1.7 s; the drawings alternate every 1-2 f), then a cut to Genthru's face.
- 1:09.32 explosion: one black frame, then white brightening for 10 f (0.42 s).

**What the game takes from it** (T1 uses the numbers; T2 / T3 key the poses from the timestamps above)
- Hit-stop: a light blow's impact drawing holds 2-3 f (0.07-0.10 s); a finisher's flash + held drawing ~0.17 s; the big blows hold
  0.33-0.5 s (Gon's dash-in punch, the knee, "Dora!"). Only the biggest shake (Rock, the clash).
- Trades and strings: blows 0.10-0.20 s apart inside a burst; a barrage ~0.17 s (Hisoka) or faster (JoJo). The opponent's head
  stays snapped back 0.4-0.7 s after a clean face punch.
- Exchanges: a burst of 2-4 blows, then a separation and a 1-2 s breather. A committed dash-in that gets read is countered.
- Hit reactions for T2: flinch high = head snaps back / sideways (DBZ 0:16.40, Naruto 1:47.7, Hisoka 4:35 alternating); flinch body
  = folds over the blow (DBZ 0:26.90 knee, DBZ 0:34.57, Hisoka 0:18.42); knockdown (DBZ 0:49.48, the tumble 0:27.57); stagger after
  a deflect (SAO 1:41.47); spin-out from a sweep (SAO 0:24-0:36).
- Techniques for T3: clash (DBZ 0:03.50 fists, Naruto 0:30, SAO 1:41.47 blades), barrage (Hisoka 4:35, JoJo 0:03), dash-in punch
  (Hisoka 0:17.00), rising knee / kick into the gut (DBZ 0:26.40, 0:34.30), driven into the ground (DBZ 0:49.48).

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

## Done (phase 2: the sword moveset, 2026-11-26; keyed in Blender, checked on the bench, compile-checked, not played)
Every sword move is posed and keyed on `PlayerRig` in Blender (`ArtSource/Player/player.blend`, actions `Sword.*`), exported
with `tools/attack_keys_blender.py` (`-- export sword`) into `BasicMovesets.Sword` (`BuildSword`) and checked on the game's
solver in `docs/bench/sword-bench.html` (+ `sword-bench.json`, the same keys).
- **New pose field `AttackPose.WristL / WristR`**: the wrist bent sideways (+ towards the little finger). A blade fixed ~71°
  off the forearm can't thrust or reach along the arm; with the wrist it swings into line. Solver step after the fist roll
  (`AttackPoseSolver.WristAxis`), in the tool (`c.ArmL/R["Wrist"]`) and the bench. Zero for every fist move (unchanged).
- **The blade** (`VFX/SwordVisual`): placed in the hand's own frame from the bind pose (rides the fist roll and the wrist);
  its flat now faces the palm and its edges run along the forearm, like a real grip, so cuts go edge first. The slash ribbon
  only grows while a strike is live (`PlayerProceduralAnimator.InStrike`, the move's `Streak` window): no smear on wind-ups.
- **Light combo** (sword stance): Diagonal slash (a backhand from over the left shoulder down to the right hip, hit 0.10 s),
  Rising slash (low right up across to over the left shoulder, 0.10 s), Horizontal sweep (left to right at chest height,
  0.10 s), Thrust (finisher: drawn back to the hip, a lunge with the lead foot stepping in, 0.13 s).
- **Pre-motion** (held Heavy): the lead foot steps out into a deep crouch, the sword hand drawn back at the right hip with
  the tip forward, the left hand reaching out; coils deeper with the charge (the heavy's hold + tremble).
- **Skills** (`BasicMoveset.Skills`, `SwordSkills.StrikeMove`): Twin Fang = up over the right shoulder and down to the left
  hip, then over the left shoulder and down to the right (an X); Quartet Edge = a square (across the top, down the left
  side, across the bottom, up the right side as the launcher); Comet Thrust = from the coiled pre-motion a long lunging
  thrust held through the dash (also the sword's plain heavy when there's no energy for a skill).
- **Hook-up**: `PlayerCombat` switches the animator to the `Sword` set while the sword is drawn (owner predicted, others from
  the SyncVar) and back to the fighter's own set when it's sheathed; `Moveset` (server timing, strike points) is the sword
  set while `SwordDrawn`. The animator keeps the heavy it started (`HeavyNow`) so a switch can't swap it mid-swing; no
  sheathing during a skill. Charge and Surge stay Rush's.
- **Method**: each key = body + fist placed from the posed shoulder, then the elbow, wrist and fist roll searched so the blade
  points where the cut needs it with its edge along the blade's real travel; whole moves solved together (no elbow flips,
  blade spins or body clipping between keys, checked on 4 in-between frames per segment). At each hit the fist is still on
  the side the cut comes from and the blade reaches across the target (the blade leads).
- **Measured** (bench, on the game's solver): edge lead 0.75-0.84 on the light slashes, 0.77-0.83 on Twin Fang and Quartet
  1 / 4, 0.48 / 0.59 on Quartet 2 / 3; the blade clears the fighter's own body everywhere (closest 1 cm); worst upper-arm
  twist 60°; elbow pops ≤ 2.6 cm. Bench solver = the tool's within 0.0012 mm on all 69 keys.

## Next
The combat / movement / customization overhaul (owner, 2026-11-26) takes over from here: **`docs/OVERHAUL_PLAN.md`**
(tasks T1-T9, one session each). The items below are folded into it.
1. A sword model in Blender to replace the cube blade (keep the glow shell + ribbon trail; the blade's frame is
   `SwordVisual`'s and `SwordProxy` in `player.blend`). → overhaul T8.
2. After the owner plays it: tune lunge distances / hit-stop, the skills' timing, the slashes' look on the Toon Head + hair.
3. Sword stance's own idle and run (the sword arm uses the unarmed run today: the hand opens on the swing back). → T9.
4. Ideas next in line: blade clash (both swing into each other = sparks + push apart), aerial juggles. → T3.

## Done (fight feel + movement, 2026-11-26; code only, compile-checked, not played)
Owner: "it does not feel smooth and the movement doesn't help the battle system". No animation was changed.
- **Blows land on their impact frame**: light hits resolve when the move's fist arrives (`AttackMove.Hit`), like the heavy;
  a blow is lost when its attacker is struck first (trades go to the faster blow).
- **Hit-stop that stops**: the victim's body freezes on the server for the blow's hit-stop (`CombatMath.HitStop`) and the
  knockback starts after it, so a frozen pose no longer slides; every client freezes the poses as long. Block-stop too.
- **Smooth movement**: walking / running / flying accelerate (a run starts in ~0.1 s, skids ~0.45 m to a stop), a dash skids
  out at run speed, attack lunges and rushes burst out and ease into the arrival.
- **Rush**: locked on, Dash with no stick or towards the opponent dashes into striking range (up to 16 m); a jab pressed
  during it comes out on arrival. **Pursuit** (the idea above): for 1.1 s after your finisher / heavy / beam / a skill's launch
  lands, Dash chases the victim, aimed where they will be. Back / sideways dashes stay plain dashes.
- **Dodge**: the first 0.15 s of a dash or rush slips past blows and blasts, with an afterimage.
- **Lock-on circling**: sidesteps follow the circle round the opponent (same distance) instead of spiralling away.
- **Afterimages** (`VFX/Afterimage.cs`): dash / rush trails, a copy where a flash step vanished, a bright one on a dodge.
- **Sparring dummy**: rushes in from range now and then, sometimes dodges your opening blow (heavies / skills more often).
Tuning: `PlayerMotor` (GroundAccel / GroundDecel / AirAccel / FlyAccel ..., MaxRush, RushSpeed, LungeEase, RushEase),
`CombatMath.DashEvadeTime` and `HitStop`, `PlayerCombat.Movement.cs` (PursuitWindow, RushMinGap, RushAimAngle).
Not done: a remote attacker's own hit-stop isn't predicted (it shows ~one round trip after the impact frame).
