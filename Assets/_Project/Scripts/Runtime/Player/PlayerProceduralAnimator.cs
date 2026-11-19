using LightMMORPG.Characters;
using LightMMORPG.Combat;
using LightMMORPG.Config;
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

        // walking / running from a locomotion style (Locomotion Bench): walk by default, run with Shift, blended by speed
        private string _locomotionId = LocomotionStyles.DefaultId;
        private float _gaitPhase, _locoW, _runBlend, _strideW = 1f, _strideR = 1f, _travelYaw, _airTime;
        private bool _gaitBackwards, _gaitHold, _runFace;
        private readonly AttackPose _gaitWalk = new(), _gaitRun = new(), _gaitPose = new(), _basePose = new();
        private readonly Quaternion[] _restRot = new Quaternion[CharacterBones.Count];
        private ToonFaceAnimator _face;
        private float _faceLookup;

        /// <summary>The basic-attack moveset this fighter plays (default <see cref="BasicMovesets.DefaultId"/>; later picked from an inventory).</summary>
        public BasicMoveset Moveset => BasicMovesets.Get(_movesetId);
        public void SetMoveset(string id) => _movesetId = string.IsNullOrEmpty(id) ? BasicMovesets.DefaultId : id;

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

        /// <summary>Procedural punch/kick/cast: light combo = jab L, jab R, kick, double-fist finisher; heavy = big swing.</summary>
        public void PlayAttack(AttackKind kind, int index)
        {
            if (kind == AttackKind.Light && _proc != null)
            {
                // basic attack: the moveset's move for this combo hit, cross-faded from the hit before
                var m = Moveset.Move(index);
                if (_move != null) { _fromMove = _move; _fromT = _moveT; _moveBlend = 0f; } else { _fromMove = null; _moveBlend = 1f; }
                _move = m;
                _moveT = 0f;
                _attackT = 1f;
                if (_speedLines != null) _speedLines.Begin();
                return;
            }
            _move = null; _fromMove = null; _moveWeight = 0f;   // any other action takes over from the basic attack
            _attackKind = kind;
            _attackIndex = index;
            _attackT = 0f;
            _attackDuration = kind == AttackKind.Heavy ? 0.45f : (kind == AttackKind.Light && index == 3 ? 0.35f : 0.25f);
            PlayLunge(kind == AttackKind.Heavy ? 1f : (kind == AttackKind.Light ? 0.55f + 0.15f * index : 0.35f));
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
        /// The rig's pose: relaxed stance, flight tuck, the walk / run of the locomotion style, block guard, power-up stance,
        /// two-bone IK towards the older attack targets (heavy, blast, beam, surge), then the basic-attack moveset on top.
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
            // guard / power-up stances (IK), then attacks on top
            Vector3 pole(int side) => new Vector3(side * 0.6f, 0.4f, -0.7f) * hs + Vector3.up * 1.2f * hs;
            if (_blockingNow)
            {
                rig.SolveTwoBone(CharacterBone.LeftUpperArm, new Vector3(0.06f, 1.62f, 0.32f) * hs, pole(-1), 1f);
                rig.SolveTwoBone(CharacterBone.RightUpperArm, new Vector3(-0.06f, 1.7f, 0.3f) * hs, pole(1), 1f);
            }
            else if (_chargingNow)
            {
                rig.SolveTwoBone(CharacterBone.LeftUpperArm, new Vector3(-0.42f, 1.0f, 0.12f) * hs, pole(-1), 1f);
                rig.SolveTwoBone(CharacterBone.RightUpperArm, new Vector3(0.42f, 1.0f, 0.12f) * hs, pole(1), 1f);
            }
            rig.SolveTwoBone(CharacterBone.LeftUpperArm, l * hs, pole(-1), wl);
            rig.SolveTwoBone(CharacterBone.RightUpperArm, r * hs, pole(1), wr);
            rig.SolveTwoBone(CharacterBone.RightUpperLeg, f * hs, new Vector3(0.1f, 1.2f, 1.5f) * hs, wf);
            _hs = hs;
            ApplyMove(rig, hs);
        }

        // ------------------------------------------------------------------ basic attacks (movesets)

        private void AdvanceMove(float dt)
        {
            bool interrupted = _blockingNow || _chargingNow || (_stats != null && _stats.IsDown.Value);
            if (_move != null && interrupted) { _move = null; _fromMove = null; _guardHold = 0f; }
            if (_move != null)
            {
                _moveT += dt;
                if (_fromMove != null) _fromT += dt;
                _moveBlend = Mathf.Min(1f, _moveBlend + dt / 0.06f);
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
        /// Blends the current move (or the guard) over the base pose (stance, walk or run, flight), all in POSE space: the
        /// body's turns from the base pose's own, the fists from its wrists, the feet from its feet. Every in-between pose is
        /// solved and kept out of the torso (slerping arm rotations swept the fists through the body). While walking, running
        /// or flying the legs keep going and only the upper body strikes.
        /// </summary>
        private void ApplyMove(CharacterRig rig, float hs)
        {
            float w = AttackMove.Ease(KeyEase.Smooth, _moveWeight);
            if (w <= 0f) { _rootOffset = Vector3.zero; _rootTurn = Quaternion.identity; return; }
            var set = Moveset;
            if (_move != null)
            {
                _move.Sample(_moveT, _pose);
                if (_fromMove != null && _moveBlend < 1f)
                {
                    _fromMove.Sample(_fromT, _fromPose);
                    AttackPose.Lerp(_fromPose, _pose, AttackMove.Ease(KeyEase.Smooth, _moveBlend), _pose);
                }
            }
            else AttackPose.Lerp(set.Guard, set.Guard, 0f, _pose);

            Vector3 wristL = rig.ModelPosition(CharacterBone.LeftHand), wristR = rig.ModelPosition(CharacterBone.RightHand);
            float legW = w * (1f - Mathf.Max(_locoW, _flightBlend));
            var bp = _basePose;
            var b = _blend;
            AttackPose.Lerp(bp, _pose, w, b);
            b.RootPos = _pose.RootPos * w; b.RootYaw = _pose.RootYaw * w; b.RootPitch = _pose.RootPitch * w; b.RootRoll = _pose.RootRoll * w;
            Quaternion rq = b.RootRotation;
            Vector3 rp = b.RootPos * hs;
            Vector3 ToAttack(Vector3 m) => (rq * m + rp) / hs;
            b.ArmL = Vector3.Lerp(ToAttack(wristL), _pose.ArmL, w);
            b.ArmR = Vector3.Lerp(ToAttack(wristR), _pose.ArmR, w);
            b.HipsPos = Vector3.Lerp(bp.HipsPos, _pose.HipsPos, legW);
            b.LegL = Vector3.Lerp(ToAttack(bp.LegL * hs), _pose.LegL, legW);
            b.LegR = Vector3.Lerp(ToAttack(bp.LegR * hs), _pose.LegR, legW);
            b.FootYawL = Mathf.Lerp(bp.FootYawL + b.RootYaw, _pose.FootYawL, legW); b.FootPitchL = Mathf.Lerp(bp.FootPitchL, _pose.FootPitchL, legW);
            b.FootYawR = Mathf.Lerp(bp.FootYawR + b.RootYaw, _pose.FootYawR, legW); b.FootPitchR = Mathf.Lerp(bp.FootPitchR, _pose.FootPitchR, legW);
            AttackPoseSolver.Apply(rig, b, hs);
            _rootOffset = rp;
            _rootTurn = rq;
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
        /// put), the direction of travel relative to the facing (strafing, backing off while locked on). Mid-air and during a
        /// dash the stride holds; flying, stunned or down = no steps.
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
            {
                float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                _gaitBackwards = Mathf.Abs(yaw) > (_gaitBackwards ? 100f : 115f);
                if (_gaitBackwards) yaw = Mathf.DeltaAngle(180f, yaw);
                _travelYaw = Mathf.LerpAngle(_travelYaw, Mathf.Clamp(yaw, -75f, 75f), 1f - Mathf.Exp(-12f * dt));
            }
            _strideW = style.Walk.StrideScale(v);
            _strideR = style.Run.StrideScale(v);
            float step = Mathf.Lerp(style.Walk.StepLength * _strideW, style.Run.StepLength * _strideR, _runBlend);
            _gaitHold = !canStep || airborne;
            if (!_gaitHold) _gaitPhase = Mathf.Repeat(_gaitPhase + dt * v / step * (_gaitBackwards ? -1f : 1f), 2f);
            UpdateRunFace();
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
            if (_locoW <= 0f) return;
            for (int i = 0; i < CharacterBones.Count; i++) _restRot[i] = rig.GetModelRotation((CharacterBone)i);
            Vector3 restHips = rig[CharacterBone.Hips].localPosition;
            var style = Locomotion;
            style.Walk.Sample(_gaitPhase, _strideW, _gaitWalk);
            style.Run.Sample(_gaitPhase, _strideR, _gaitRun);
            AttackPose.Lerp(_gaitWalk, _gaitRun, _runBlend, _gaitPose);
            TurnSteps(_gaitPose, _travelYaw);
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

        /// <summary>Points the stride along the direction of travel (strafing while locked on); the body keeps facing ahead.</summary>
        private static void TurnSteps(AttackPose p, float yaw)
        {
            if (Mathf.Abs(yaw) < 0.5f) return;
            float s = Mathf.Sin(yaw * Mathf.Deg2Rad), c = Mathf.Cos(yaw * Mathf.Deg2Rad);
            p.LegL = new Vector3(p.LegL.x + s * p.LegL.z, p.LegL.y, c * p.LegL.z);
            p.LegR = new Vector3(p.LegR.x + s * p.LegR.z, p.LegR.y, c * p.LegR.z);
            p.FootYawL += yaw; p.FootYawR += yaw;
            p.HipsYaw += yaw * 0.35f; p.TorsoYaw -= yaw * 0.35f;
        }

        /// <summary>Forward lunge/punch pose (0..1 strength), decays quickly.</summary>
        public void PlayLunge(float strength = 1f) => _lunge = Mathf.Max(_lunge, strength);

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
            if (_speedLines != null && _proc != null && _proc.Rig != null) _speedLines.Tick(_proc.Rig, _move, _moveT, dt, _hs);
        }
    }
}
