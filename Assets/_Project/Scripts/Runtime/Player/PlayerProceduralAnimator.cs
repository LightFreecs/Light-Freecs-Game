using LightMMORPG.Characters;
using LightMMORPG.Combat;
using LightMMORPG.Config;
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
        private float _speed01;

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            if (_graphics == null) _graphics = transform.Find("Graphics");
            if (_model == null && _graphics != null) _model = _graphics.Find("Model");
            if (_model != null) _baseScale = _model.localScale;
            if (_graphics != null) _lastPos = _graphics.position;
            _stats = GetComponent<PlayerStats>();
            if (_model != null) _proc = _model.GetComponent<ProceduralCharacter>();
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
        /// Rough rig pose until the real animation pass: relaxed stance, run cycle swing, flight tuck, block guard,
        /// power-up stance, and two-bone IK towards the attack targets (old capsule-space targets, scaled to the hero).
        /// </summary>
        private void PoseRig(Vector3 l, Vector3 r, Vector3 f, float wl, float wr, float wf)
        {
            var rig = _proc.Rig;
            if (rig == null) return;
            float hs = rig.Height / 2.3f;
            rig.ApplyRestPose();
            float swing = Mathf.Sin(_phase) * _speed01 * (_flyingNow ? 0f : 1f);
            float fly = _flightBlend;
            // legs: run swing about X, knee bends when the leg is behind; flight = trailing, bent knees
            for (int side = -1; side <= 1; side += 2)
            {
                var ul = (CharacterBone)CharacterBones.Side(CharacterBone.LeftUpperLeg, side);
                float s = swing * -side;
                Quaternion qThigh = Quaternion.AngleAxis(-s * 38f + fly * (side < 0 ? 12f : 28f), Vector3.right);
                rig.SetModelRotation(ul, qThigh);
                float knee = Mathf.Max(0f, s) * 55f * _speed01 + fly * (side < 0 ? 25f : 50f) + (_blockingNow ? 15f : 0f);
                Quaternion qKnee = Quaternion.AngleAxis(knee, Vector3.right) * qThigh;
                rig.SetModelRotation(ul + 1, qKnee);
                rig.SetModelRotation(ul + 2, Quaternion.AngleAxis(fly * 30f, Vector3.right) * qKnee);
                rig.Inherit(ul + 3);
                // arms swing opposite to legs
                var ua = (CharacterBone)CharacterBones.Side(CharacterBone.LeftUpperArm, side);
                Quaternion qa = Quaternion.AngleAxis(s * 35f - fly * 25f, Vector3.right) * rig.GetModelRotation(ua);
                rig.SetModelRotation(ua, qa);
                rig.SetModelRotation(ua + 1, Quaternion.AngleAxis(s * 35f - fly * 25f, Vector3.right) * rig.GetModelRotation(ua + 1));
                rig.SetModelRotation(ua + 2, rig.GetModelRotation(ua + 1));
                rig.SetFingerCurl(side, 1f);
            }
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
            _speed01 = Mathf.Clamp01(planar / 9f);
            _stretch = Mathf.Lerp(_stretch, dashing ? 1f : 0f, 1f - Mathf.Exp(-(dashing ? 30f : 8f) * dt));
            _lunge = Mathf.MoveTowards(_lunge, 0f, dt * 5f);
            _recoil = Vector3.MoveTowards(_recoil, Vector3.zero, dt * 3f);

            _phase += dt * Mathf.Lerp(2.2f, 9f, Mathf.Clamp01(planar / 13f));

            // Lean: forward with speed, sideways with strafing; flight leans more (superhero pose).
            float speed01 = Mathf.Clamp01(planar / 13f);
            float leanMax = Mathf.Lerp(_maxLean, 40f, _flightBlend);
            float pitch = Mathf.Clamp(local.z / 13f, -1f, 1f) * leanMax;
            float roll = -Mathf.Clamp(local.x / 13f, -1f, 1f) * leanMax * 0.6f;
            pitch += _lunge * 18f;
            Vector3 recoilLocal = _graphics.InverseTransformDirection(_recoil);
            pitch -= recoilLocal.z * 25f;
            roll += recoilLocal.x * 25f;

            // Bob: idle breathing on ground, slow hover float in the air, step bounce while running.
            float bob = Mathf.Lerp(Mathf.Sin(_phase) * 0.04f, Mathf.Abs(Mathf.Sin(_phase)) * 0.12f, speed01 * (1f - _flightBlend));
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

            _model.localPosition = offset;
            _model.localRotation = Quaternion.Euler(pitch, 0f, roll);
            _model.localScale = Vector3.Scale(_baseScale, scale);
        }
    }
}
