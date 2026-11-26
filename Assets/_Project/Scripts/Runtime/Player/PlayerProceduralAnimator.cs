using LightMMORPG.Characters;
using LightMMORPG.Combat;
using LightMMORPG.Config;
using LightMMORPG.Core;
using LightMMORPG.VFX;
using UnityEngine;

namespace LightMMORPG.Player
{
    /// <summary>
    /// Placeholder procedural animation on Graphics/Model (Graphics itself is FishNet's smoothed graphical object, so we
    /// never touch it). Driven only by synced/predicted state (motor flags + the smoothed Graphics movement), so it looks
    /// the same on every client: idle bob, lean into movement, flight pose, dash stretch, block crouch, charge tremble,
    /// stun wobble. Session 4 hooks: <see cref="PlayLunge"/> (attack swing) and <see cref="PlayRecoil"/> (got hit).
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class PlayerProceduralAnimator : MonoBehaviour
    {
        [SerializeField] private Transform _graphics;
        [SerializeField] private Transform _model;
        [SerializeField] private float _maxLean = 14f;

        private PlayerMotor _motor;
        private Vector3 _lastPos;
        private Vector3 _smoothedVelocity;
        private float _stretch;
        private float _lunge;
        private Vector3 _recoil;
        private float _flightBlend;
        private float _phase;
        private Vector3 _baseScale = Vector3.one;

        // session 4: limbs (fists + foot spheres), attacks, hit-stop, KO fall
        private PlayerStats _stats;
        private Transform _handL, _handR, _foot;
        private AttackKind _attackKind;
        private int _attackIndex;
        private float _attackT = 1f;
        private float _attackDuration = 0.25f;
        private float _hitStopUntil;
        private float _down;
        private static readonly Vector3 HandLRest = new(-0.55f, 1.25f, 0.25f);
        private static readonly Vector3 HandRRest = new(0.55f, 1.25f, 0.25f);
        private static readonly Vector3 FootRest = new(0.22f, 0.15f, 0.05f);

        // procedural rig (when Graphics/Model has a ProceduralCharacter)
        private ProceduralCharacter _proc;
        private bool _flyingNow, _blockingNow, _chargingNow;

        // basic attacks from a moveset (Attack Bench): the equipped set's move for each combo hit, a guard between hits
        private string _movesetId = BasicMovesets.DefaultId;
        private AttackMove _move, _fromMove;
        private float _moveT, _fromT, _moveBlend = 1f, _moveWeight, _guardHold, _hs = 1f;
        private readonly AttackPose _pose = new(), _fromPose = new(), _blend = new();
        private Vector3 _rootOffset;
        private Quaternion _rootTurn = Quaternion.identity;
        private LimbSpeedLines _speedLines;

        // walking / running from a locomotion style (Locomotion Bench): walk by default, run with Shift, blended by speed.
        // Locked on (facing the opponent) the walk sidesteps / backpedals and the run turns towards the travel direction or,
        // behind 120°, runs backwards (UpdateLocomotion).
        private string _locomotionId = LocomotionStyles.DefaultId;
        private float _gaitPhase, _locoW, _runBlend, _strideRun = 1f, _strideBack = 1f, _travelYaw, _airTime, _backW, _warpHips;
        private bool _gaitHold, _runFace, _backRun;
        private GaitPart _walkA, _walkB;
        private readonly AttackPose _gaitWalk = new(), _gaitRun = new(), _gaitPose = new(), _basePose = new(), _gaitTmp = new(), _gaitMirror = new();
        private readonly Quaternion[] _restRot = new Quaternion[CharacterBones.Count];
        private ToonFaceAnimator _face;
        private float _faceLookup;

        // hit reactions: where the hit landed and which way it pushed (rig space), how hard, how long ago
        private float _hrT = 9f, _hrDur = 0.4f, _hrStrength;
        private Vector3 _hrPoint, _hrPush;

        // the charged heavy (the moveset's Heavy): hold = WindUp, its last pose held and coiling towards Coiled with the charge
        // (trembling); release = Swing from the held pose. Every client: the owner from its input, the others from RPCs.
        private enum HeavyPhase : byte { None, WindUp, Swing }
        private HeavyPhase _heavy;
        private float _heavyT, _heavyCharge, _heavyMaxCharge = CombatConstants.HeavyMaxChargeTime;
        private bool _heavyFull, _heavyFlashPending;
        private readonly AttackPose _heavyFrom = new();
        // a pose to cross-fade from when an action ends early (a called-off wind-up, a light attack cutting into the swing)
        private readonly AttackPose _fadeFrom = new();
        private bool _fadeFromHeld;
        private const float FadeDefault = 0.06f, FadeStanceOut = 0.2f;
        private float _fadeTime = FadeDefault;

        // holding Charge: the moveset's power-up stance (its keys drop the fighter into it, the last one is held)
        private bool _powerUp;
        private float _chargeT;

        // sent flying by a charged heavy: HitPoses.Launched turned to fly back along the push, held for the hang time
        private float _launchT = 9f, _launchDur, _launchStrength, _launchYaw;
        private readonly AttackPose _launchPose = new();
        private const float LaunchIn = 0.08f, LaunchOut = 0.3f;

        /// <summary>The basic-attack moveset this fighter plays (default <see cref="BasicMovesets.DefaultId"/>; later picked from an inventory).</summary>
        public BasicMoveset Moveset => BasicMovesets.Get(_movesetId);
        /// <summary>The id of <see cref="Moveset"/> (the sword stance switches it to <see cref="BasicMovesets.SwordId"/> and back).</summary>
        public string MovesetId => _movesetId;
        public void SetMoveset(string id) => _movesetId = string.IsNullOrEmpty(id) ? BasicMovesets.DefaultId : id;

        /// <summary>The heavy being wound up or swung (kept from its start, so switching movesets mid-swing can't swap it).</summary>
        private HeavyAttack HeavyNow => _heavyAtk ?? Moveset.Heavy;
        private HeavyAttack _heavyAtk;

        /// <summary>
        /// A strike is live: the playing move (or heavy swing) is inside its <see cref="AttackMove.Streak"/> window. The sword's slash
        /// ribbon only grows then, so wind-ups and recoveries leave no smear.
        /// </summary>
        public bool InStrike => _heavy == HeavyPhase.Swing ? InWindow(HeavyNow?.Swing, _heavyT) : _move != null && InWindow(_move, _moveT);

        private static bool InWindow(AttackMove m, float t) => m != null && m.Streak.x >= 0f && t >= m.Streak.x && t <= m.Streak.y;

        /// <summary>Plays one given move (a sword skill's strike), cross-faded from what's on screen, with the striking limb's speed lines.</summary>
        public void PlayStrike(AttackMove m)
        {
            if (_proc == null || m == null) return;
            StartMove(m);
            if (_speedLines != null) _speedLines.Begin();
        }

        /// <summary>
        /// How this fighter walks and runs (default <see cref="LocomotionStyles.DefaultId"/>). Several running styles are planned:
        /// a style is an entry in <see cref="LocomotionStyles.All"/>; set it on every client, like the moveset and the appearance.
        /// </summary>
        public LocomotionStyle Locomotion => LocomotionStyles.Get(_locomotionId);
        public void SetLocomotionStyle(string id) => _locomotionId = string.IsNullOrEmpty(id) ? LocomotionStyles.DefaultId : id;

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            if (_graphics == null) _graphics = transform.Find("Graphics");
            if (_model == null && _graphics != null) _model = _graphics.Find("Model");
            if (_model != null) _baseScale = _model.localScale;
            if (_graphics != null) _lastPos = _graphics.position;
            _stats = GetComponent<PlayerStats>();
            _heavyMaxCharge = Mathf.Max(0.01f, GameConfig.Load().heavyMaxChargeTime);
            if (_model != null) _proc = _model.GetComponent<ProceduralCharacter>();
            if (_proc != null && _graphics != null && !Application.isBatchMode && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                _speedLines = LimbSpeedLines.Create(_graphics);
            if (_model != null && _proc == null)
            {
                var cfg = GameConfig.Load();
                _handL = Limb("HandL", HandLRest, 0.3f, cfg.fighterSkinMaterial);
                _handR = Limb("HandR", HandRRest, 0.3f, cfg.fighterSkinMaterial);
                _foot = Limb("Foot", FootRest, 0.3f, cfg.fighterAccentMaterial);
            }
        }

        private Transform Limb(string limbName, Vector3 pos, float size, Material mat)
        {
            var existing = _model.Find(limbName);
            if (existing != null) return existing;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = limbName;
            Destroy(go.GetComponent<Collider>());
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            go.transform.SetParent(_model, false);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * size;
            return go.transform;
        }

        /// <summary>
        /// Procedural punch / cast: light = the moveset's combo hit <paramref name="index"/>; heavy = the charged swing with charge
        /// <paramref name="index"/> / 255 (<see cref="PlayHeavySwing"/>); surge = the moveset's burst; blast, beam = the older cast poses.
        /// </summary>
        public void PlayAttack(AttackKind kind, int index)
        {
            if (kind == AttackKind.Heavy) { PlayHeavySwing(index / 255f); return; }
            if (kind == AttackKind.Light && _proc != null)
            {
                // basic attack: the moveset's move for this combo hit, cross-faded from the hit before (or from the heavy it cuts into)
                StartMove(Moveset.Move(index));
                if (_speedLines != null) _speedLines.Begin();
                return;
            }
            if (kind == AttackKind.Surge && _proc != null && Moveset.Surge != null) { StartMove(Moveset.Surge); return; }
            if (_heavy != HeavyPhase.None) { _heavy = HeavyPhase.None; SetHeavyFace(false); }
            PlayCastPose(kind, index);
        }

        /// <summary>Plays a moveset move, cross-faded from the move before it (or from the heavy it cuts into).</summary>
        private void StartMove(AttackMove m)
        {
            if (_heavy != HeavyPhase.None) StopHeavy(0f);
            if (_move != null) { _fromMove = _move; _fromT = _moveT; _moveBlend = 0f; }
            else { _fromMove = null; if (!_fadeFromHeld) _moveBlend = 1f; }
            _fadeTime = FadeDefault;   // a strike right after letting go of the power-up doesn't wait for the slow fade
            _move = m;
            _moveT = 0f;
            _attackT = 1f;
        }

        /// <summary>The older procedural poses (blast, beam; every attack on a fighter without a rig).</summary>
        private void PlayCastPose(AttackKind kind, int index)
        {
            _move = null; _fromMove = null;   // takes over from the basic attack (a cast while powering up reaches out of the stance instead)
            if (!_powerUp) { _moveWeight = 0f; _fadeFromHeld = false; }
            _attackKind = kind;
            _attackIndex = index;
            _attackT = 0f;
            _attackDuration = kind == AttackKind.Heavy ? 0.45f : (kind == AttackKind.Light && index == 3 ? 0.35f : 0.25f);
            PlayLunge(kind == AttackKind.Heavy ? 1f : (kind == AttackKind.Light ? 0.55f + 0.15f * index : 0.35f));
        }

        /// <summary>Winding up a heavy (the fist drawn back), on any client.</summary>
        public bool IsWindingUpHeavy => _heavy == HeavyPhase.WindUp;

        /// <summary>How charged the heavy being wound up looks (0..1, from how long it has been held); 0 otherwise. Flares the aura.</summary>
        public float HeavyCharge01 => _heavy == HeavyPhase.WindUp ? _heavyCharge : 0f;

        /// <summary>
        /// Heavy pressed: the moveset's wind-up pulls the fist back and holds it there, coiling deeper and trembling harder as the
        /// charge builds (full after <c>heavyMaxChargeTime</c>, marked by a flash at the fist). Gritted teeth.
        /// </summary>
        public void PlayHeavyWindUp()
        {
            if (_proc == null || Moveset.Heavy == null) return;
            _heavyAtk = Moveset.Heavy;
            _move = null; _fromMove = null;   // a fade still running (the power-up being let go) carries on into the wind-up
            _attackT = 1f;
            _heavy = HeavyPhase.WindUp;
            _heavyT = 0f; _heavyCharge = 0f; _heavyFull = false;
            SetHeavyFace(true);
        }

        /// <summary>
        /// Heavy released with this charge (0..1): the swing, from whatever pose the wind-up was holding (barely drawn back after a
        /// tap, fully coiled after a long hold). Without a rig: the old overhead swing.
        /// </summary>
        public void PlayHeavySwing(float charge01)
        {
            // the swing that belongs to the wind-up on screen (the set can't change under it), else the set's own
            var h = _heavy == HeavyPhase.WindUp && _heavyAtk != null ? _heavyAtk : Moveset.Heavy;
            if (_proc == null || h == null)
            {
                _heavy = HeavyPhase.None;
                PlayCastPose(AttackKind.Heavy, 0);
                return;
            }
            _heavyAtk = h;
            charge01 = Mathf.Clamp01(charge01);
            if (_heavy == HeavyPhase.WindUp) SampleHeavyHeld(h, _heavyFrom);
            else
            {
                // the wind-up wasn't seen here (joined late): start from the drawn-back pose for this charge
                h.WindUp.Sample(h.WindUp.Length, _heavyFrom);
                AttackPose.Lerp(_heavyFrom, h.Coiled, AttackMove.Ease(KeyEase.Smooth, charge01), _heavyFrom);
            }
            _move = null; _fromMove = null; _fadeFromHeld = false;
            _attackT = 1f;
            _heavy = HeavyPhase.Swing;
            _heavyT = 0f;
            _heavyCharge = charge01;
            SetHeavyFace(true);
            if (_speedLines != null) _speedLines.Begin();
        }

        /// <summary>The wind-up was called off (hit, guard, power-up, refused): ease back to the stance, no swing.</summary>
        public void CancelHeavy()
        {
            if (_heavy == HeavyPhase.WindUp) StopHeavy(0.15f);
        }

        /// <summary>Ends the heavy where it is, cross-fading from its current pose into whatever comes next.</summary>
        private void StopHeavy(float guardHold)
        {
            var h = HeavyNow;
            if (h != null && _heavy != HeavyPhase.None)
            {
                SampleHeavy(h, _fadeFrom);
                _fadeFromHeld = true;
                _moveBlend = 0f;
            }
            _heavy = HeavyPhase.None;
            _guardHold = guardHold;
            SetHeavyFace(false);
        }

        private void SampleHeavy(HeavyAttack h, AttackPose o)
        {
            if (_heavy == HeavyPhase.WindUp) SampleHeavyHeld(h, o);
            else h.Swing.SampleFrom(_heavyFrom, _heavyT, o);
        }

        /// <summary>The wind-up at its current time and charge: pulled back, coiling deeper with the charge, trembling with the strain.</summary>
        private void SampleHeavyHeld(HeavyAttack h, AttackPose o)
        {
            h.WindUp.Sample(_heavyT, o);
            float c = _heavyCharge;
            if (c > 0f) AttackPose.Lerp(o, h.Coiled, AttackMove.Ease(KeyEase.Smooth, c), o);
            float a = 0.003f + 0.012f * c * c, t = Time.time;
            o.RootPos += new Vector3(Mathf.Sin(t * 53f), 0f, Mathf.Cos(t * 47f)) * a;
            Vector3 shake = new Vector3(Mathf.Sin(t * 61f), Mathf.Cos(t * 59f), 0f) * a;
            if (h.Limb == StrikeLimb.HandL) o.ArmL += shake; else o.ArmR += shake;
        }

        private void AdvanceHeavy(float dt)
        {
            _heavyT += dt;
            _moveWeight = Mathf.Min(1f, _moveWeight + dt / 0.06f);
            if (_heavy == HeavyPhase.WindUp)
            {
                _heavyCharge = CombatMath.Charge01(_heavyT, _heavyMaxCharge);
                if (_heavyCharge >= 1f && !_heavyFull) { _heavyFull = true; _heavyFlashPending = true; }
                return;
            }
            var h = HeavyNow;
            if (h == null || _heavyT >= h.Swing.Length)
            {
                // the swing's last key is the guard: hold it a moment, then relax (AdvanceMove)
                _heavy = HeavyPhase.None;
                _guardHold = 0.35f;
                SetHeavyFace(false);
            }
        }

        /// <summary>Gritted teeth while winding up and swinging a heavy; back to neutral after.</summary>
        private void SetHeavyFace(bool on)
        {
            if (_face == null && _model != null) _face = _model.GetComponentInChildren<ToonFaceAnimator>();
            if (_face == null) return;
            bool angry = _face.Holding && _face.Expression == ToonExpression.Angry;
            if (on && !angry) _face.Play(ToonExpression.Angry, true);
            else if (!on && angry) _face.Play(ToonExpression.Neutral);
        }

        /// <summary>
        /// Sent flying by a charged heavy (<paramref name="impact"/> 0..1 = its charge): the body folds into
        /// <see cref="HitPoses.Launched"/>, turned to fly back along <paramref name="worldPush"/> (attacker → victim) facing the
        /// attacker, held for that launch's hang time (<see cref="CombatMath.HeavyAirTime"/>), then eased out as the fighter lands
        /// and skids. Ends whatever this fighter was doing.
        /// </summary>
        public void PlayLaunch(Vector3 worldPush, float impact)
        {
            if (_proc == null || _proc.Rig == null || _graphics == null) return;
            worldPush.y = 0f;
            Vector3 push = worldPush.sqrMagnitude > 1e-6f ? _graphics.InverseTransformDirection(worldPush.normalized) : Vector3.back;
            push.y = 0f;
            if (push.sqrMagnitude < 1e-6f) push = Vector3.back;
            _launchYaw = Mathf.Atan2(-push.x, -push.z) * Mathf.Rad2Deg;
            impact = Mathf.Clamp01(impact);
            _launchStrength = Mathf.Lerp(0.6f, 1f, impact);
            _launchDur = CombatMath.HeavyAirTime(impact);
            _launchT = 0f;
            if (_heavy != HeavyPhase.None) { _heavy = HeavyPhase.None; SetHeavyFace(false); }
            _move = null; _fromMove = null; _fadeFromHeld = false; _moveWeight = 0f;
        }

        /// <summary>Freeze the pose for a moment (local visual hit-stop).</summary>
        public void HitStop(float seconds) => _hitStopUntil = Mathf.Max(_hitStopUntil, Time.time + seconds);

        private void AnimateLimbs(float dt)
        {
            if (_handL == null && _proc == null) return;
            _attackT = Mathf.Min(1f, _attackT + dt / _attackDuration);
            AdvanceMove(dt);
            // 0 → 1 → 0 strike curve, fast out, slower return
            float k = _attackT < 0.35f ? _attackT / 0.35f : 1f - (_attackT - 0.35f) / 0.65f;
            k = Mathf.Clamp01(k);
            Vector3 l = HandLRest, r = HandRRest, f = FootRest;
            if (_attackT < 1f)
            {
                switch (_attackKind)
                {
                    case AttackKind.Light:
                        if (_attackIndex == 0) l = Vector3.Lerp(HandLRest, new Vector3(-0.15f, 1.45f, 1.25f), k);
                        else if (_attackIndex == 1) r = Vector3.Lerp(HandRRest, new Vector3(0.15f, 1.45f, 1.25f), k);
                        else if (_attackIndex == 2) f = Vector3.Lerp(FootRest, new Vector3(0.1f, 1.2f, 1.25f), k);
                        else
                        {
                            l = Vector3.Lerp(HandLRest, new Vector3(-0.12f, 1.35f, 1.4f), k);
                            r = Vector3.Lerp(HandRRest, new Vector3(0.12f, 1.35f, 1.4f), k);
                        }
                        break;
                    case AttackKind.Heavy:
                        // wind up overhead then hammer down-forward
                        Vector3 up = new Vector3(0.3f, 2.9f, -0.1f), hit = new Vector3(0.1f, 1.1f, 1.5f);
                        r = _attackT < 0.4f ? Vector3.Lerp(HandRRest, up, _attackT / 0.4f) : Vector3.Lerp(hit, HandRRest, (_attackT - 0.55f) / 0.45f);
                        if (_attackT >= 0.4f && _attackT < 0.55f) r = Vector3.Lerp(up, hit, (_attackT - 0.4f) / 0.15f);
                        l = Vector3.Lerp(HandLRest, r + new Vector3(-0.3f, 0f, 0f), k * 0.8f);
                        break;
                    case AttackKind.Blast:
                        r = Vector3.Lerp(HandRRest, new Vector3(0.2f, 1.4f, 1.2f), k);
                        break;
                    case AttackKind.Beam:
                        l = Vector3.Lerp(HandLRest, new Vector3(-0.12f, 1.35f, 1.2f), k);
                        r = Vector3.Lerp(HandRRest, new Vector3(0.12f, 1.35f, 1.2f), k);
                        break;
                    case AttackKind.Surge:
                        l = Vector3.Lerp(HandLRest, new Vector3(-0.9f, 1.0f, 0.1f), k);
                        r = Vector3.Lerp(HandRRest, new Vector3(0.9f, 1.0f, 0.1f), k);
                        break;
                }
            }
            // charging beam: both palms forward
            var combat = _stats != null ? _stats.GetComponent<PlayerCombat>() : null;
            if (combat != null && combat.IsChargingBeam)
            {
                l = new Vector3(-0.12f, 1.35f, 0.75f);
                r = new Vector3(0.12f, 1.35f, 0.75f);
            }
            if (_proc != null)
            {
                bool lActive = _attackT < 1f && l != HandLRest, rActive = _attackT < 1f && r != HandRRest, fActive = _attackT < 1f && f != FootRest;
                bool beam = combat != null && combat.IsChargingBeam;
                PoseRig(l, r, f, lActive || beam ? Mathf.Max(k, beam ? 1f : 0f) : 0f, rActive || beam ? Mathf.Max(k, beam ? 1f : 0f) : 0f, fActive ? k : 0f);
                return;
            }
            if (_handL == null) return;
            float idle = Mathf.Sin(_phase) * 0.04f;
            _handL.localPosition = l + Vector3.up * idle;
            _handR.localPosition = r - Vector3.up * idle;
            _foot.localPosition = f;
        }

        /// <summary>
        /// The rig's pose: relaxed stance, flight tuck, the walk / run of the locomotion style, block guard, two-bone IK towards
        /// the older attack targets (blast, beam), then the moveset on top (basic attacks, heavy, power-up stance, surge).
        /// </summary>
        private void PoseRig(Vector3 l, Vector3 r, Vector3 f, float wl, float wr, float wf)
        {
            var rig = _proc.Rig;
            if (rig == null) return;
            float hs = rig.Height / 2.3f;
            rig.ApplyRestPose();
            float fly = _flightBlend;
            // flight = trailing legs with bent knees, arms back; a block bends the knees a little
            for (int side = -1; side <= 1; side += 2)
            {
                var ul = (CharacterBone)CharacterBones.Side(CharacterBone.LeftUpperLeg, side);
                Quaternion qThigh = Quaternion.AngleAxis(fly * (side < 0 ? 12f : 28f), Vector3.right);
                rig.SetModelRotation(ul, qThigh);
                float knee = fly * (side < 0 ? 25f : 50f) + (_blockingNow ? 15f : 0f);
                Quaternion qKnee = Quaternion.AngleAxis(knee, Vector3.right) * qThigh;
                rig.SetModelRotation(ul + 1, qKnee);
                rig.SetModelRotation(ul + 2, Quaternion.AngleAxis(fly * 30f, Vector3.right) * qKnee);
                rig.Inherit(ul + 3);
                if (fly <= 0f) continue;
                var ua = (CharacterBone)CharacterBones.Side(CharacterBone.LeftUpperArm, side);
                rig.SetModelRotation(ua, Quaternion.AngleAxis(-fly * 25f, Vector3.right) * rig.GetModelRotation(ua));
                rig.SetModelRotation(ua + 1, Quaternion.AngleAxis(-fly * 25f, Vector3.right) * rig.GetModelRotation(ua + 1));
                rig.SetModelRotation(ua + 2, rig.GetModelRotation(ua + 1));
                rig.SetFingerCurl(side, 1f);
            }
            PoseLocomotion(rig, hs);
            // the guard, then the older attack targets on top. Hinge IK from the posed shoulder, the collarbone following, elbows
            // kept out of the torso (AttackPoseSolver.ArmTo); the plain two-bone IK from the bind shoulder used before twisted
            // forearms and missed the target once the body leant or turned.
            if (_blockingNow)
            {
                // cross block in front of the face, the left forearm in front of the right one (before: at chest height,
                // both forearms 2 cm apart, each fist sinking into the other arm)
                AttackPoseSolver.ArmTo(rig, -1, new Vector3(0.10f, 1.84f, 0.40f) * hs, 1f, hs);
                AttackPoseSolver.ArmTo(rig, 1, new Vector3(-0.10f, 1.95f, 0.29f) * hs, 1f, hs);
            }
            AttackPoseSolver.ArmTo(rig, -1, l * hs, wl, hs);
            AttackPoseSolver.ArmTo(rig, 1, r * hs, wr, hs);
            AttackPoseSolver.LegTo(rig, 1, f * hs, wf, hs);
            _hs = hs;
            ApplyMove(rig, hs);
            if (_powerUp)
            {
                // a blast or beam cast while powering up still reaches out of the stance
                AttackPoseSolver.ArmTo(rig, -1, l * hs, wl, hs);
                AttackPoseSolver.ArmTo(rig, 1, r * hs, wr, hs);
            }
            ApplyLaunch(rig, hs);
            ApplyHitReaction(rig, hs);
        }

        // ------------------------------------------------------------------ basic attacks (movesets)

        private void AdvanceMove(float dt)
        {
            var set = Moveset;
            bool down = _stats != null && _stats.IsDown.Value;
            bool surging = _move != null && _move == set.Surge;   // the burst plays out even over a held charge, then the stance comes back
            bool interrupted = _blockingNow || (_chargingNow && !surging) || down;
            _moveBlend = Mathf.Min(1f, _moveBlend + dt / _fadeTime);
            if (_moveBlend >= 1f) _fadeTime = FadeDefault;
            bool powerUp = _chargingNow && !down && !surging && _proc != null && set.Charge != null;
            if (powerUp != _powerUp) SetPowerUp(powerUp);
            if (_heavy != HeavyPhase.None)
            {
                if (_powerUp) { _heavy = HeavyPhase.None; SetHeavyFace(false); }   // the stance fades in from what's on screen
                else if (interrupted) StopHeavy(0f);
                else { AdvanceHeavy(dt); if (_heavy != HeavyPhase.None) return; }
            }
            if (_move != null && interrupted) { _move = null; _fromMove = null; _guardHold = 0f; }
            if (_powerUp)
            {
                _chargeT += dt;
                _moveWeight = Mathf.Min(1f, _moveWeight + dt / 0.08f);
                return;
            }
            if (_move != null)
            {
                _moveT += dt;
                if (_fromMove != null) _fromT += dt;
                _moveWeight = Mathf.Min(1f, _moveWeight + dt / 0.05f);
                if (_moveT >= _move.Length) { _move = null; _fromMove = null; _guardHold = 0.35f; }
            }
            else if (_moveWeight > 0f)
            {
                // hold the guard a moment after the combo, then relax to the normal stance (fast if something else takes over)
                _guardHold -= dt;
                if (interrupted) _moveWeight = Mathf.Max(0f, _moveWeight - dt / 0.08f);
                else if (_guardHold <= 0f) _moveWeight = Mathf.Max(0f, _moveWeight - dt / 0.3f);
            }
        }

        /// <summary>
        /// Into / out of the power-up stance: whatever is on screen cross-fades into what comes next (the stance, the surge, or on
        /// letting go the guard on its way back to the relaxed stance, slower so the wide stance doesn't snap shut).
        /// </summary>
        private void SetPowerUp(bool on)
        {
            _powerUp = on;
            if (on) _chargeT = 0f;
            else _guardHold = 0.05f;
            if (_moveWeight <= 0f) return;
            AttackPose.Lerp(_pose, _pose, 0f, _fadeFrom);
            _fadeFromHeld = true;
            _moveBlend = 0f;
            _fadeTime = !on && _move == null ? FadeStanceOut : FadeDefault;
        }

        /// <summary>
        /// Blends the current move (or the guard) over the base pose (stance, walk or run, flight), all in POSE space: the
        /// body's turns from the base pose's own, the fists from its wrists, the feet from its feet. Every in-between pose is
        /// solved and kept out of the torso (slerping arm rotations swept the fists through the body). While walking, running
        /// or flying the legs keep going and only the upper body strikes.
        /// </summary>
        private void ApplyMove(CharacterRig rig, float hs)
        {
            float w = AttackMove.Ease(KeyEase.Smooth, _moveWeight);
            if (w <= 0f) { _rootOffset = Vector3.zero; _rootTurn = Quaternion.identity; _fadeFromHeld = false; return; }
            var set = Moveset;
            if (_powerUp && set.Charge != null)
            {
                set.Charge.Sample(_chargeT, _pose);
                // in the air the legs keep the flight pose and the hips don't drop, so the fists stay with the chest
                float airborne = Mathf.Max(_locoW, _flightBlend);
                _pose.ArmL -= _pose.HipsPos * airborne;
                _pose.ArmR -= _pose.HipsPos * airborne;
            }
            else if (_heavy != HeavyPhase.None && HeavyNow != null) SampleHeavy(HeavyNow, _pose);
            else if (_move != null)
            {
                _move.Sample(_moveT, _pose);
                if (_fromMove != null && _moveBlend < 1f)
                {
                    _fromMove.Sample(_fromT, _fromPose);
                    AttackPose.Lerp(_fromPose, _pose, AttackMove.Ease(KeyEase.Smooth, _moveBlend), _pose);
                }
            }
            else AttackPose.Lerp(set.Guard, set.Guard, 0f, _pose);
            // an action that ended early (a called-off wind-up, a heavy cut short by a jab) fades out instead of popping
            if (_fadeFromHeld)
            {
                if (_moveBlend < 1f) AttackPose.Lerp(_fadeFrom, _pose, AttackMove.Ease(KeyEase.Smooth, _moveBlend), _pose);
                else _fadeFromHeld = false;
            }
            BlendOverBase(rig, hs, _pose, w, w * (1f - Mathf.Max(_locoW, _flightBlend)));
        }

        /// <summary>
        /// Blends an attack-space pose (weight <paramref name="w"/>, the legs <paramref name="legW"/>) over the base pose (stance,
        /// walk or run, flight) in pose space: the body's turns from the base pose's own, the fists from its wrists, the feet from
        /// its feet; then solves the rig and sets the model's root offset / turn.
        /// </summary>
        private void BlendOverBase(CharacterRig rig, float hs, AttackPose target, float w, float legW)
        {
            Vector3 wristL = rig.ModelPosition(CharacterBone.LeftHand), wristR = rig.ModelPosition(CharacterBone.RightHand);
            var bp = _basePose;
            var b = _blend;
            AttackPose.Lerp(bp, target, w, b);
            // locked on, the legs keep stepping towards the travel direction under the attack: the hips keep half of their turn
            // that way (the chest still strikes square), so the legs don't twist round under a pelvis facing the opponent
            float keep = 0.5f * _warpHips * w;
            b.HipsYaw += keep; b.TorsoYaw -= keep;
            b.RootPos = target.RootPos * w; b.RootYaw = target.RootYaw * w; b.RootPitch = target.RootPitch * w; b.RootRoll = target.RootRoll * w;
            Quaternion rq = b.RootRotation;
            Vector3 rp = b.RootPos * hs;
            Vector3 ToAttack(Vector3 m) => (rq * m + rp) / hs;
            b.ArmL = Vector3.Lerp(ToAttack(wristL), target.ArmL, w);
            b.ArmR = Vector3.Lerp(ToAttack(wristR), target.ArmR, w);
            b.HipsPos = Vector3.Lerp(bp.HipsPos, target.HipsPos, legW);
            b.LegL = Vector3.Lerp(ToAttack(bp.LegL * hs), target.LegL, legW);
            b.LegR = Vector3.Lerp(ToAttack(bp.LegR * hs), target.LegR, legW);
            b.FootYawL = Mathf.Lerp(bp.FootYawL + b.RootYaw, target.FootYawL, legW); b.FootPitchL = Mathf.Lerp(bp.FootPitchL, target.FootPitchL, legW);
            b.FootYawR = Mathf.Lerp(bp.FootYawR + b.RootYaw, target.FootYawR, legW); b.FootPitchR = Mathf.Lerp(bp.FootPitchR, target.FootPitchR, legW);
            AttackPoseSolver.Apply(rig, b, hs);
            _rootOffset = rp;
            _rootTurn = rq;
        }

        /// <summary>Sent flying (<see cref="PlayLaunch"/>): the launched pose over the base pose, legs and all, for the hang time.</summary>
        private void ApplyLaunch(CharacterRig rig, float hs)
        {
            if (_launchT >= _launchDur + LaunchOut) return;
            float w = Smooth01(0f, LaunchIn, _launchT) * (1f - Smooth01(_launchDur, _launchDur + LaunchOut, _launchT)) * _launchStrength;
            if (w <= 0.001f) return;
            AttackPose.Lerp(HitPoses.Launched, HitPoses.Launched, 0f, _launchPose);
            TurnPose(_launchPose, _launchYaw);
            BlendOverBase(rig, hs, _launchPose, w, w);
        }

        /// <summary>Turns a whole attack-space pose about the vertical through its root: limb targets, feet, head and root together.</summary>
        private static void TurnPose(AttackPose p, float yaw)
        {
            if (Mathf.Abs(yaw) < 0.01f) return;
            Quaternion q = Quaternion.AngleAxis(yaw, Vector3.up);
            p.RootPos = q * p.RootPos; p.RootYaw += yaw; p.HeadYaw += yaw;
            p.ArmL = q * p.ArmL; p.ArmR = q * p.ArmR; p.LegL = q * p.LegL; p.LegR = q * p.LegR;
            p.FootYawL += yaw; p.FootYawR += yaw;
        }

        // ------------------------------------------------------------------ walking / running (locomotion styles)

        private static readonly AttackPose ZeroPose = new();

        private static float Smooth01(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Gait state from the smoothed movement (so it looks the same on every client): how much the fighter walks / runs
        /// (<see cref="_locoW"/>), walk ↔ run by speed, the step phase (advances with the distance covered, so planted feet stay
        /// put), and the direction of travel relative to the facing (<see cref="_travelYaw"/>: 0 ahead, +90 right, ±180 back).
        /// The facing only differs from the travel while locked on (or aiming after an attack): then the walk sidesteps or
        /// backpedals (<see cref="WalkParts"/>), and the run turns towards the travel direction or, past 120° (back below 105°),
        /// runs backwards, with a quick hip flip between the two (<see cref="_backW"/>). Mid-air and during a dash the stride
        /// holds; flying, stunned or down = no steps.
        /// </summary>
        private void UpdateLocomotion(float dt, Vector3 local, float planar, bool flying, bool dashing, bool stunned)
        {
            if (_proc == null || _proc.Rig == null) { _locoW = 0f; return; }
            float hs = _proc.Rig.Height / 2.3f;
            var style = Locomotion;
            bool grounded = _motor == null || _motor.IsGrounded;
            _airTime = grounded || flying ? 0f : _airTime + dt;
            bool airborne = _airTime > 0.12f || dashing;
            bool down = _stats != null && _stats.IsDown.Value;
            bool canStep = !flying && !stunned && !down;
            float v = planar / hs;   // design metres per second
            float target = canStep ? Smooth01(0.25f, 1f, v) : 0f;
            if (airborne && canStep) target = _locoW;
            _locoW = Mathf.Lerp(_locoW, target, 1f - Mathf.Exp(-14f * dt));
            if (_locoW < 0.002f && target <= 0f) _locoW = 0f;
            _runBlend = Smooth01(style.Walk.Speed * 1.15f, style.Run.Speed * 0.85f, v);
            if (v > 0.3f)
                _travelYaw = Mathf.DeltaAngle(0f, Mathf.LerpAngle(_travelYaw, Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-12f * dt)));
            float away = Mathf.Abs(_travelYaw);
            if (_backRun ? away < 105f : away > 120f) _backRun = !_backRun;
            if (style.RunBack == null) _backRun = false;
            _backW = Mathf.MoveTowards(_backW, _backRun ? 1f : 0f, dt / 0.2f);
            WalkParts(style, v);
            _strideRun = style.Run.StrideScale(v);
            _strideBack = style.RunBack != null ? style.RunBack.StrideScale(v) : 1f;
            float walkStep = _walkA.Weight * _walkA.Gait.StepLength * _walkA.Stride;
            if (_walkB.Weight > 0f) walkStep += _walkB.Weight * _walkB.Gait.StepLength * _walkB.Stride;
            float runStep = style.Run.StepLength * _strideRun;
            if (style.RunBack != null) runStep = Mathf.Lerp(runStep, style.RunBack.StepLength * _strideBack, _backW);
            float step = Mathf.Lerp(walkStep, runStep, _runBlend);
            _gaitHold = !canStep || airborne;
            if (!_gaitHold) _gaitPhase = Mathf.Repeat(_gaitPhase + dt * v / step, 2f);
            UpdateRunFace();
        }

        /// <summary>One of the walk's directions in the current blend: which gait (mirrored = the sidestep to the left), how far it is turned, its weight and stride.</summary>
        private struct GaitPart
        {
            public GaitCycle Gait;
            public bool Mirror;
            public float Turn, Weight, Stride;
        }

        /// <summary>
        /// The walk for the travel direction: forward, sidestep right, backpedal or sidestep left (the right one mirrored), the
        /// nearest one turned the rest of the way to the exact direction. Within 15° of a diagonal it cross-fades with its
        /// neighbour, turned to the same direction, so the blend can't skate (50 / 50 on the diagonal itself).
        /// </summary>
        private void WalkParts(LocomotionStyle style, float v)
        {
            float c = Mathf.Round(_travelYaw / 90f) * 90f, off = _travelYaw - c;
            float t = Smooth01(30f, 60f, Mathf.Abs(off));
            _walkA = Part(style, c, off, 1f - t, v);
            if (t > 0f)
            {
                float n = c + (off > 0f ? 90f : -90f);
                _walkB = Part(style, n, _travelYaw - n, t, v);
            }
            else _walkB.Weight = 0f;
        }

        private static GaitPart Part(LocomotionStyle style, float cardinal, float turn, float weight, float v)
        {
            int dir = Mathf.RoundToInt(Mathf.DeltaAngle(0f, cardinal) / 90f);   // 0 ahead, 1 right, -1 left, ±2 back
            var p = new GaitPart { Gait = style.Walk, Turn = turn, Weight = weight };
            var g = dir == 0 ? style.Walk : dir == 1 || dir == -1 ? style.WalkSide : style.WalkBack;
            if (g != null) { p.Gait = g; p.Mirror = dir == -1; }
            else p.Turn = cardinal + turn;   // a style without the locked-on gaits turns its walk all the way instead
            p.Stride = p.Gait.StrideScale(v);
            return p;
        }

        /// <summary>Gritted-teeth "Determined" face while running, like the clip; back to neutral when the run ends.</summary>
        private void UpdateRunFace()
        {
            if (_face == null && Time.time >= _faceLookup)
            {
                _face = _model.GetComponentInChildren<ToonFaceAnimator>();
                _faceLookup = Time.time + 1f;
            }
            if (_face == null) return;
            bool running = _locoW > 0.6f && _runBlend > 0.6f;
            if (running == _runFace) return;
            _runFace = running;
            if (running) { if (!_face.Holding) _face.Play(ToonExpression.Determined, true); }
            else if (_face.Holding && _face.Expression == ToonExpression.Determined) _face.Play(ToonExpression.Neutral);
        }

        /// <summary>
        /// Lays the walk / run over the base pose (rest + flight) in pose space, from that pose's own wrists and ankles, and
        /// keeps the result in <see cref="_basePose"/> for the basic attacks (their legs carry on stepping).
        /// </summary>
        private void PoseLocomotion(CharacterRig rig, float hs)
        {
            var bp = _basePose;
            AttackPose.Lerp(ZeroPose, ZeroPose, 0f, bp);
            bp.ArmL = rig.ModelPosition(CharacterBone.LeftHand) / hs; bp.ArmR = rig.ModelPosition(CharacterBone.RightHand) / hs;
            bp.LegL = rig.ModelPosition(CharacterBone.LeftFoot) / hs; bp.LegR = rig.ModelPosition(CharacterBone.RightFoot) / hs;
            _warpHips = 0f;
            if (_locoW <= 0f) return;
            for (int i = 0; i < CharacterBones.Count; i++) _restRot[i] = rig.GetModelRotation((CharacterBone)i);
            Vector3 restHips = rig[CharacterBone.Hips].localPosition;
            var style = Locomotion;
            float walkHips = 0f, runHips = 0f;
            // the walk: its direction(s) for the travel direction, each turned to it exactly
            if (_runBlend < 1f)
            {
                SampleWalkPart(_walkA, _gaitWalk);
                walkHips = _walkA.Turn * WalkHips * _walkA.Weight;
                if (_walkB.Weight > 0f)
                {
                    SampleWalkPart(_walkB, _gaitTmp);
                    AttackPose.Lerp(_gaitWalk, _gaitTmp, _walkB.Weight, _gaitWalk);
                    walkHips += _walkB.Turn * WalkHips * _walkB.Weight;
                }
            }
            // the run: turned towards the travel direction (the hips all the way, the chest part of the way, the eyes on the
            // opponent), or the backwards run behind 120°, with a quick hip flip between them
            if (_runBlend > 0f)
            {
                float back = style.RunBack != null ? AttackMove.Ease(KeyEase.Smooth, _backW) : 0f;
                float turnRun = Mathf.Clamp(_travelYaw, -130f, 130f);
                float turnBack = Mathf.Clamp(_travelYaw - (_travelYaw >= 0f ? 180f : -180f), -75f, 75f);
                if (back < 1f)
                {
                    style.Run.Sample(_gaitPhase, _strideRun, _gaitRun);
                    Warp(_gaitRun, turnRun, RunHips, RunChest);
                }
                if (back > 0f)
                {
                    var o = back < 1f ? _gaitTmp : _gaitRun;
                    style.RunBack.Sample(_gaitPhase, _strideBack, o);
                    Warp(o, turnBack, WalkHips, WalkChest);
                    if (back < 1f) AttackPose.Lerp(_gaitRun, _gaitTmp, back, _gaitRun);
                }
                runHips = Mathf.Lerp(turnRun * RunHips, turnBack * WalkHips, back);
            }
            if (_runBlend <= 0f) AttackPose.Lerp(_gaitWalk, _gaitWalk, 0f, _gaitPose);
            else if (_runBlend >= 1f) AttackPose.Lerp(_gaitRun, _gaitRun, 0f, _gaitPose);
            else AttackPose.Lerp(_gaitWalk, _gaitRun, _runBlend, _gaitPose);
            _warpHips = Mathf.Lerp(walkHips, runHips, _runBlend) * _locoW;
            AttackPose.Lerp(bp, _gaitPose, _locoW, bp);
            AttackPoseSolver.Apply(rig, bp, hs);
            // the first quarter of the blend goes per bone from the rest pose, so the IK's own elbows never pop the arms
            float k = Mathf.Clamp01(_locoW * 4f);
            if (k < 1f)
            {
                rig[CharacterBone.Hips].localPosition = Vector3.Lerp(restHips, rig[CharacterBone.Hips].localPosition, k);
                for (int i = 0; i < CharacterBones.Count; i++)
                    rig.SetModelRotation((CharacterBone)i, Quaternion.Slerp(_restRot[i], rig.GetModelRotation((CharacterBone)i), k));
            }
            bp.ArmL = rig.ModelPosition(CharacterBone.LeftHand) / hs; bp.ArmR = rig.ModelPosition(CharacterBone.RightHand) / hs;
        }

        /// <summary>One walk direction at the current phase (the sidestep to the left = the right one mirrored, a step later), turned to the travel direction.</summary>
        private void SampleWalkPart(GaitPart part, AttackPose o)
        {
            if (part.Mirror)
            {
                part.Gait.Sample(_gaitPhase + 1f, part.Stride, _gaitMirror);
                AttackPose.Mirror(_gaitMirror, o);
            }
            else part.Gait.Sample(_gaitPhase, part.Stride, o);
            Warp(o, part.Turn, WalkHips, WalkChest);
        }

        // how much of a turn towards the travel direction the hips and the chest take (the feet always take all of it): walking
        // stays square to the opponent, running turns the hips all the way and the chest just over half
        private const float WalkHips = 0.5f, WalkChest = 0.1f, RunHips = 1f, RunChest = 0.6f;

        /// <summary>
        /// Turns a gait pose to travel <paramref name="turn"/>° off its own direction (about the root): the feet and their slide
        /// all the way, so planted feet never skate; the hips <paramref name="hips"/> of it and the chest <paramref name="chest"/>
        /// (the rest is a twist of the spine back towards the opponent), the arms with the chest. The head keeps looking ahead at
        /// the opponent (the solver keeps it within 55° of the chest).
        /// </summary>
        private static void Warp(AttackPose p, float turn, float hips, float chest)
        {
            if (Mathf.Abs(turn) < 0.01f) return;
            Quaternion all = Quaternion.AngleAxis(turn, Vector3.up), h = Quaternion.AngleAxis(turn * hips, Vector3.up), c = Quaternion.AngleAxis(turn * chest, Vector3.up);
            p.LegL = all * p.LegL; p.LegR = all * p.LegR;
            p.FootYawL += turn; p.FootYawR += turn;
            p.HipsPos = h * p.HipsPos;
            p.HipsYaw += turn * hips; p.TorsoYaw -= turn * (hips - chest);
            p.ArmL = c * p.ArmL; p.ArmR = c * p.ArmR;
        }

        /// <summary>Forward lunge/punch pose (0..1 strength), decays quickly.</summary>
        public void PlayLunge(float strength = 1f) => _lunge = Mathf.Max(_lunge, strength);

        /// <summary>
        /// Got hit at <paramref name="worldPoint"/> (where the fist / foot / blast landed), pushed along
        /// <paramref name="worldPush"/> (attacker → victim). The body reacts around that spot: a head shot snaps the head back
        /// and away, a chest hit rocks and twists the upper body away, a gut punch folds the fighter over the fist with the
        /// hips knocked back, a leg kick buckles that knee. Hits off-centre twist the body away from the struck side.
        /// <paramref name="strength"/> ~1 for a jab, ~1.6 for a finisher / heavy; blocked hits are small.
        /// </summary>
        public void PlayHitReaction(Vector3 worldPoint, Vector3 worldPush, float strength, bool big)
        {
            var rig = _proc != null ? _proc.Rig : null;
            if (rig == null) return;
            var rt = rig.transform;
            _hrPoint = rt.InverseTransformPoint(worldPoint);
            worldPush.y = 0f;
            Vector3 push = worldPush.sqrMagnitude > 1e-6f ? rt.InverseTransformDirection(worldPush.normalized) : Vector3.back;
            push.y = 0f;
            _hrPush = push.sqrMagnitude > 1e-6f ? push.normalized : Vector3.back;
            _hrStrength = strength;
            _hrDur = big ? 0.62f : 0.38f;
            _hrT = 0f;
            if (big && strength > 0.5f)
            {
                if (_face == null) _face = _model.GetComponentInChildren<ToonFaceAnimator>();
                if (_face != null && !_face.Holding) _face.Play(ToonExpression.Surprised);
            }
        }

        /// <summary>Rotates a bone and everything below it by q (model space), keeping the rig's cached model rotations right.</summary>
        private static void RotateSubtree(CharacterRig rig, CharacterBone root, Quaternion q)
        {
            int r = (int)root;
            for (int i = r; i < CharacterBones.Count; i++)
            {
                bool inside = i == r;
                for (int pi = CharacterBones.Parent(i); !inside && pi >= r; pi = CharacterBones.Parent(pi)) inside = pi == r;
                if (inside) rig.SetModelRotation((CharacterBone)i, q * rig.GetModelRotation((CharacterBone)i));
            }
        }

        /// <summary>Tips a bone (and what hangs off it) away from the impact: about cross(joint → hit point, push).</summary>
        private void BendAway(CharacterRig rig, CharacterBone bone, float degrees)
        {
            if (Mathf.Abs(degrees) < 0.01f) return;
            Vector3 r = _hrPoint - rig.ModelPosition(bone);
            Vector3 axis = Vector3.Cross(r, _hrPush);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(Vector3.up, _hrPush);
            RotateSubtree(rig, bone, Quaternion.AngleAxis(degrees, axis.normalized));
        }

        /// <summary>The hit reaction on top of the finished pose (see <see cref="PlayHitReaction"/>): snaps in, eases out.</summary>
        private void ApplyHitReaction(CharacterRig rig, float hs)
        {
            if (_hrT >= _hrDur) return;
            float rise = Smooth01(0f, 0.05f, _hrT);
            float fall = 1f - Smooth01(0.05f, _hrDur, _hrT);
            float w = Mathf.Min(rise, fall) * _hrStrength;
            if (w <= 0.001f) return;
            float h = _hrPoint.y / Mathf.Max(0.5f, rig.Height);   // 0 feet .. 1 top of the head
            float side = Mathf.Clamp(_hrPoint.x / (0.18f * rig.Height), -1f, 1f);
            Vector3 away = Vector3.Cross(Vector3.up, _hrPush).normalized;   // tips the top of the body along the push
            if (h > 0.8f)
            {
                // head shot: the head snaps back and away, the neck and chest follow a little
                BendAway(rig, CharacterBone.UpperChest, 5f * w);
                BendAway(rig, CharacterBone.Neck, 12f * w);
                BendAway(rig, CharacterBone.Head, 24f * w);
            }
            else if (h > 0.6f)
            {
                // chest: the upper body rocks and twists away, the head lags (whiplash)
                BendAway(rig, CharacterBone.Spine, 6f * w);
                BendAway(rig, CharacterBone.Chest, 9f * w);
                BendAway(rig, CharacterBone.UpperChest, 10f * w);
                RotateSubtree(rig, CharacterBone.Head, Quaternion.AngleAxis(-8f * w, away));
            }
            else if (h > 0.4f)
            {
                // gut: folds over the fist (upper body forward, towards the attacker), hips knocked back, head comes forward
                var hips = rig[CharacterBone.Hips];
                hips.localPosition += _hrPush * (0.06f * hs * w);
                RotateSubtree(rig, CharacterBone.Spine, Quaternion.AngleAxis(side * -_hrPush.z * 10f * w, Vector3.up));
                RotateSubtree(rig, CharacterBone.Chest, Quaternion.AngleAxis(-14f * w, away));
                RotateSubtree(rig, CharacterBone.UpperChest, Quaternion.AngleAxis(-10f * w, away));
                RotateSubtree(rig, CharacterBone.Head, Quaternion.AngleAxis(-6f * w, away));
                for (int s = -1; s <= 1; s += 2)
                    RotateSubtree(rig, (CharacterBone)CharacterBones.Side(CharacterBone.LeftLowerLeg, s), Quaternion.AngleAxis(10f * w, Vector3.right));
            }
            else
            {
                // legs: the struck knee buckles, the hips drop and the body leans over that side
                int s = side < 0f ? -1 : 1;
                var hips = rig[CharacterBone.Hips];
                hips.localPosition += Vector3.down * (0.05f * hs * w);
                BendAway(rig, (CharacterBone)CharacterBones.Side(CharacterBone.LeftUpperLeg, s), 10f * w);
                RotateSubtree(rig, (CharacterBone)CharacterBones.Side(CharacterBone.LeftLowerLeg, s), Quaternion.AngleAxis(24f * w, Vector3.right));
                RotateSubtree(rig, CharacterBone.Spine, Quaternion.AngleAxis(-s * 7f * w, Vector3.forward));
            }
        }

        /// <summary>Knocked back along a world direction (strength ~1).</summary>
        public void PlayRecoil(Vector3 worldDirection, float strength = 1f)
        {
            worldDirection.y = 0f;
            _recoil = worldDirection.normalized * strength;
        }

        private void LateUpdate()
        {
            if (_graphics == null || _model == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (Time.time < _hitStopUntil)
            {
                _lastPos = _graphics.position;
                return; // hold the pose
            }
            bool down = _stats != null && _stats.IsDown.Value;
            _down = Mathf.MoveTowards(_down, down ? 1f : 0f, dt * (down ? 2.5f : 4f));

            Vector3 vel = (_graphics.position - _lastPos) / dt;
            _lastPos = _graphics.position;
            if (vel.sqrMagnitude > 900f) vel = Vector3.zero; // teleport / flash step
            _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, vel, 1f - Mathf.Exp(-10f * dt));

            Vector3 local = _graphics.InverseTransformDirection(_smoothedVelocity);
            float planar = new Vector2(local.x, local.z).magnitude;

            bool flying = _motor != null && _motor.IsFlying;
            bool dashing = _motor != null && _motor.IsDashing;
            bool blocking = _motor != null && _motor.IsBlocking;
            bool charging = _motor != null && _motor.IsCharging;
            bool stunned = _motor != null && _motor.IsStunned;

            _flightBlend = Mathf.MoveTowards(_flightBlend, flying ? 1f : 0f, dt * 3f);
            _flyingNow = flying; _blockingNow = blocking; _chargingNow = charging;
            _stretch = Mathf.Lerp(_stretch, dashing ? 1f : 0f, 1f - Mathf.Exp(-(dashing ? 30f : 8f) * dt));
            _lunge = Mathf.MoveTowards(_lunge, 0f, dt * 5f);
            _recoil = Vector3.MoveTowards(_recoil, Vector3.zero, dt * 3f);
            _hrT += dt;
            _launchT += dt;

            _phase += dt * Mathf.Lerp(2.2f, 9f, Mathf.Clamp01(planar / 13f));
            UpdateLocomotion(dt, local, planar, flying, dashing, stunned);

            // Lean: forward with speed, sideways with strafing; flight leans more (superhero pose). With the rig the walk / run
            // carries its own lean and bounce, so the whole-model lean and the step bob only remain in the air (and without a rig).
            float speed01 = Mathf.Clamp01(planar / 13f);
            float gait = _proc != null ? _locoW * (1f - _flightBlend) : 0f;
            float leanMax = Mathf.Lerp(_maxLean, 40f, _flightBlend);
            float pitch = Mathf.Clamp(local.z / 13f, -1f, 1f) * leanMax * (1f - gait);
            float roll = -Mathf.Clamp(local.x / 13f, -1f, 1f) * leanMax * 0.6f * (1f - gait);
            pitch += _lunge * 18f;
            Vector3 recoilLocal = _graphics.InverseTransformDirection(_recoil);
            pitch -= recoilLocal.z * 25f;
            roll += recoilLocal.x * 25f;

            // Bob: idle breathing on ground, slow hover float in the air, step bounce while running (capsule only).
            float stepBob = _proc != null ? 0f : Mathf.Abs(Mathf.Sin(_phase)) * 0.12f;
            float bob = Mathf.Lerp(Mathf.Sin(_phase) * 0.04f, stepBob, _proc != null ? _locoW : speed01 * (1f - _flightBlend));
            bob = Mathf.Lerp(bob, Mathf.Sin(Time.time * 2f) * 0.15f + 0.2f, _flightBlend);
            float crouch = blocking ? -0.12f : 0f;

            Vector3 offset = new Vector3(0f, bob + crouch, _lunge * 0.35f);
            if (charging) offset += new Vector3(Mathf.Sin(Time.time * 61f), 0f, Mathf.Cos(Time.time * 53f)) * 0.025f;
            if (stunned) roll += Mathf.Sin(Time.time * 18f) * 8f;

            // Dash stretch: longer along travel, thinner across. Block squashes slightly.
            float s = _stretch;
            Vector3 scale = new Vector3(1f - 0.25f * s, 1f - 0.12f * s, 1f + 0.6f * s);
            if (blocking) scale = Vector3.Scale(scale, new Vector3(1.05f, 0.92f, 1.05f));

            AnimateLimbs(dt);

            // KO: topple backwards, sink a little, then shrink/fade out during the last second before respawn
            if (_down > 0f)
            {
                float fade = _stats != null && _stats.IsDown.Value ? Mathf.Clamp01(_stats.DownRemaining.Value) : 1f;
                pitch = Mathf.Lerp(pitch, -85f, _down);
                offset = Vector3.Lerp(offset, new Vector3(0f, 0.35f, -0.8f), _down);
                scale = Vector3.Lerp(scale, scale * Mathf.Lerp(0.05f, 1f, fade), _down);
                roll = Mathf.Lerp(roll, 0f, _down);
            }

            _model.localPosition = offset + _rootOffset;
            _model.localRotation = Quaternion.Euler(pitch, 0f, roll) * _rootTurn;
            _model.localScale = Vector3.Scale(_baseScale, scale);

            // speed lines on the striking limb (frozen with the pose during hit-stop, since this point isn't reached then)
            if (_speedLines != null && _proc != null && _proc.Rig != null)
            {
                var heavyNow = HeavyNow;
                bool swing = _heavy == HeavyPhase.Swing && heavyNow != null;
                _speedLines.Tick(_proc.Rig, swing ? heavyNow.Swing : _move, swing ? _heavyT : _moveT, dt, _hs);
            }

            // full charge: a flash at the drawn-back fist, so the player knows letting go now hits hardest
            if (_heavyFlashPending)
            {
                _heavyFlashPending = false;
                var rig = _proc != null ? _proc.Rig : null;
                var h = HeavyNow;
                if (rig != null && h != null && _heavy == HeavyPhase.WindUp)
                {
                    int side = h.Limb == StrikeLimb.HandL || h.Limb == StrikeLimb.FootL ? -1 : 1;
                    Vector3 fist = rig.transform.TransformPoint(AttackPoseSolver.FistCentre(rig, side, _hs));
                    var cfg = GameConfig.Load();
                    CombatFx.SpawnFlash(fist, 0.55f * _hs, cfg.impactMaterial, 0.22f);
                }
            }
        }
    }
}
