using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using LightMMORPG.Config;
using LightMMORPG.Core;
using LightMMORPG.Input;
using UnityEngine;

namespace LightMMORPG.Player
{
    /// <summary>
    /// Client-side predicted CharacterController movement (FishNet CharacterControllerPrediction demo pattern).
    /// Walk (default) / run (hold Shift) / jump, flight toggle with fly up/down, dash, flash step, block slow-down, charge = stand still.
    /// Movement is relative to the camera yaw sent in the replicate data; locked on, it is relative to the exact bearing of the
    /// opponent, so walking sideways circles them at the same distance.
    /// Fight feel: walking / running / flying accelerate (quick starts, a short skid when stopping, more drift in the air), an
    /// attack's dash into range bursts out and eases into the arrival, a rush (<see cref="QueueRush"/>) is a dash at the
    /// opponent, the first moments of a dash dodge blows (<see cref="IsEvading"/>), and a blow freezes its victim for the
    /// hit-stop before the knockback carries them away.
    /// Server-only hooks for combat: <see cref="ServerApplyKnockback"/>, <see cref="ServerTeleport"/>,
    /// <see cref="ServerSetSpeedMultiplier"/>; their state is in the reconcile so clients get corrected.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : TickNetworkBehaviour
    {
        #region Types
        public struct ReplicateData : IReplicateData
        {
            public Vector2 Move;
            public float CameraYaw;
            public bool Sprint, FlyUp, FlyDown, Block, Charge, FaceAim;
            /// <summary>Holding Heavy (winding up a charged heavy attack): slow walk, no running.</summary>
            public bool Heavy;
            // One-time inputs (accumulated in Update between ticks).
            public bool Jump, Dash, FlashStep, ToggleFlight;
            /// <summary>One-time: an attack's dash into striking range (metres; horizontal unless flying). See <see cref="QueueLunge"/>.</summary>
            public Vector3 Lunge;
            /// <summary>One-time: <see cref="Lunge"/> is a rush, a dash at the opponent (uses the dash and its cooldown, up to 16 m). See <see cref="QueueRush"/>.</summary>
            public bool Rush;

            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        public struct ReconcileData : IReconcileData
        {
            public Vector3 Position;
            public float Yaw;
            public float VerticalVelocity;
            public bool Flying;
            public float DashTimer;
            public Vector3 DashDirection;
            public float DashCooldown;
            public float FlashStepCooldown;
            public Vector3 KnockbackVelocity;
            public float StunTimer;
            public float SpeedMultiplier;
            public float StatSpeed;
            public float LungeTimer;
            public Vector3 LungeVelocity;
            public float LungeDuration;
            public Vector3 MoveVelocity;
            public float FreezeTimer;
            public float EvadeTimer;

            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }
        #endregion

        /// <summary>Raised on the owning client when the local player spawns.</summary>
        public static event System.Action<PlayerMotor> OnLocalPlayerStarted;
        public static PlayerMotor Local { get; private set; }

        [Header("Tuning (stats come from GameConfig)")]
        [SerializeField] private float _gravity = 28f;
        [SerializeField] private float _jumpHeight = 2.4f;
        [SerializeField] private float _turnSpeed = 900f;
        [SerializeField] private float _knockbackDeceleration = 22f;
        [SerializeField] private float _maxFallSpeed = 40f;

        // ---- read-only state for animation / combat ----
        public bool IsFlying => _flying;
        public bool IsBlocking { get; private set; }
        public bool IsCharging { get; private set; }
        /// <summary>Winding up a heavy attack (replicated input): moves at <c>heavyChargeMoveMultiplier</c> of the walk, no running.</summary>
        public bool IsChargingHeavy { get; private set; }
        /// <summary>Owner-side: PlayerCombat sets this while Heavy is held; it goes into the replicated input.</summary>
        public bool OwnerHeavyCharging { get; set; }
        /// <summary>Running (Run / Shift held while moving); otherwise the fighter walks.</summary>
        public bool IsSprinting { get; private set; }
        /// <summary>Dashing, or rushing at the opponent (a rush is a dash).</summary>
        public bool IsDashing => _dashTimer > 0f;
        /// <summary>Carried by an attack's dash into striking range (<see cref="QueueLunge"/>) or by a rush.</summary>
        public bool IsLunging => _lungeTimer > 0f;
        /// <summary>Rushing at the opponent (<see cref="QueueRush"/>).</summary>
        public bool IsRushing => _lungeTimer > 0f && _dashTimer > 0f;
        /// <summary>The first moments of a dash or a rush: blows slip past (<see cref="CombatMath.DashEvadeTime"/>; the server decides).</summary>
        public bool IsEvading => _evadeTimer > 0f;
        /// <summary>Frozen by a blow's hit-stop: the knockback starts once it is over.</summary>
        public bool IsFrozen => _freezeTimer > 0f;
        /// <summary>The dash is off cooldown (predicted on the owner).</summary>
        public bool DashReady => _dashCooldown <= 0f;
        /// <summary>How fast a knockback slows down (m/s²), for guessing where someone sent flying will be.</summary>
        public float KnockbackDeceleration => _knockbackDeceleration;
        /// <summary>Owner: no moving, jumping or dashing until this time (a sword skill and its post-motion delay).</summary>
        public float OwnerFrozenUntil { get; set; }

        /// <summary>
        /// Owner: the next tick dashes this far (metres) — an attack closing the gap to its target, the way anime fighters
        /// cross the distance with every blow. Bursts out (~34 m/s on average) and eases into the arrival, faces the dash,
        /// ignored while stunned or already lunging; it cuts a rush short (the blow takes over).
        /// </summary>
        public void QueueLunge(Vector3 displacement) => _lungeQueued = displacement;

        /// <summary>
        /// Owner: instead of the plain dash, rush this far (metres, up to 16) at the opponent: a dash that stops in striking
        /// range (PlayerCombat picks the target: the lock-on target, or the one just sent flying). Uses the dash and its
        /// cooldown, dodges like a dash; an attack's own lunge queued the same tick wins.
        /// </summary>
        public void QueueRush(Vector3 displacement) => _rushQueued = displacement;

        /// <summary>
        /// Owner: asked when Dash is pressed; returning true means the press was used for something else (PlayerCombat queued
        /// a rush), so no plain dash is queued.
        /// </summary>
        public System.Func<bool> OwnerDashOverride;

        private const float MaxLunge = 10f, LungeSpeed = 34f, MaxRush = 16f, RushSpeed = 46f;
        /// <summary>How hard an attack's lunge / a rush eases out (<see cref="CombatMath.EaseOutStep"/>).</summary>
        private const float LungeEase = 0.8f, RushEase = 0.55f;
        // acceleration (m/s²): a run starts in ~0.1 s and skids ~0.45 m to a stop; the walk stays crisp; the air drifts more
        private const float GroundAccel = 90f, GroundDecel = 70f, AirAccel = 40f, AirDecel = 22f, FlyAccel = 50f, FlyDecel = 34f;
        private float _lungeTimer, _lungeDuration;
        private Vector3 _lungeVelocity, _lungeQueued, _rushQueued;
        public bool IsStunned => _stunTimer > 0f;
        public bool IsGrounded => _cc != null && _cc.isGrounded;
        public float StunRemaining => _stunTimer;
        /// <summary>Actual CharacterController velocity of the last simulated tick (m/s), including knockback.</summary>
        public Vector3 Velocity { get; private set; }
        /// <summary>Simulated facing yaw in degrees (root rotation).</summary>
        public float FacingYaw => _yaw;
        public Vector3 Forward => Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
        /// <summary>Tick time of the most recent flash step (for VFX); 0 if none.</summary>
        public float LastFlashStepTime { get; private set; }
        public PlayerInputReader Input => _input;

        private CharacterController _cc;
        private PlayerInputReader _input;
        private GameConfig _config;

        // simulated state
        private float _yaw;
        private float _verticalVelocity;
        private bool _flying;
        private float _dashTimer;
        private Vector3 _dashDirection;
        private float _dashCooldown;
        private float _flashCooldown;
        private Vector3 _knockback;
        private float _stunTimer;
        private float _speedMultiplier = 1f;
        /// <summary>From the fighter's Speed stat (set once by the server, carried in the reconcile).</summary>
        private float _statSpeed = 1f;
        private float SpeedScale => _speedMultiplier * _statSpeed;
        /// <summary>Walking / running / flying velocity (horizontal), eased towards what the input asks for.</summary>
        private Vector3 _moveVelocity;
        /// <summary>Hit-stop: no movement at all while above 0 (set by the server with a blow's knockback).</summary>
        private float _freezeTimer;
        /// <summary>Dodge window left of the dash / rush in progress.</summary>
        private float _evadeTimer;

        // owner input accumulation
        private bool _jumpQueued, _dashQueued, _flashQueued, _toggleFlightQueued;
        private float _faceAimUntil;
        private ReplicateData _lastTickedReplicateData;

        /// <summary>Owner-side: optional camera yaw provider (set by the third-person camera). Falls back to facing.</summary>
        public static System.Func<float> CameraYawProvider;
        /// <summary>Owner-side: true while locked on (set by the camera); character then faces camera yaw.</summary>
        public static System.Func<bool> LockedOnProvider;
        /// <summary>Owner-side: where the locked-on opponent is (set by the camera), or null; movement then turns about them.</summary>
        public static System.Func<Vector3?> LockTargetProvider;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _input = GetComponent<PlayerInputReader>();
            _config = GameConfig.Load();
            _yaw = transform.eulerAngles.y;
            SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
        }

        public override void OnStartNetwork()
        {
            _yaw = transform.eulerAngles.y;
        }

        public override void OnStartClient()
        {
            if (!IsOwner) return;
            Local = this;
            if (_input != null) _input.InputEnabled = true;
            OnLocalPlayerStarted?.Invoke(this);
        }

        public override void OnStopClient()
        {
            if (Local == this) Local = null;
            if (_input != null) _input.InputEnabled = false;
        }

        /// <summary>Owner-side: face the camera yaw for a while (call when attacking / casting).</summary>
        public void FaceAimFor(float seconds) => _faceAimUntil = Mathf.Max(_faceAimUntil, Time.time + seconds);

        private void Update()
        {
            if (!IsOwner || _input == null) return;
            if (_input.Pressed(GameAction.Jump)) _jumpQueued = true;
            // a dash at the opponent replaces the plain dash when PlayerCombat finds one to rush at
            if (_input.Pressed(GameAction.Dash) && !(OwnerDashOverride != null && OwnerDashOverride())) _dashQueued = true;
            if (_input.Pressed(GameAction.FlashStep)) _flashQueued = true;
            if (_input.Pressed(GameAction.ToggleFlight)) _toggleFlightQueued = true;
        }

        protected override void TimeManager_OnTick() => PerformReplicate(BuildReplicateData());

        protected override void TimeManager_OnPostTick() => CreateReconcile();

        /// <summary>Server: input for an ownerless fighter (the training dummy), read every tick; null = stand still.</summary>
        public System.Func<ReplicateData> ServerBotInput;

        private ReplicateData BuildReplicateData()
        {
            if (!IsOwner) return IsServerStarted && !Owner.IsValid && ServerBotInput != null ? ServerBotInput() : default;
            bool locked = LockedOnProvider != null && LockedOnProvider();
            var rd = new ReplicateData
            {
                CameraYaw = CameraYawProvider != null ? CameraYawProvider() : _yaw,
                FaceAim = locked || Time.time < _faceAimUntil,
                Jump = _jumpQueued,
                Dash = _dashQueued,
                FlashStep = _flashQueued,
                ToggleFlight = _toggleFlightQueued,
            };
            if (_input != null)
            {
                rd.Move = _input.Move;
                rd.Sprint = _input.Run;
                rd.FlyUp = _input.Held(GameAction.Jump);
                rd.FlyDown = _input.Held(GameAction.FlyDown);
                rd.Block = _input.Held(GameAction.Block);
                rd.Charge = _input.Held(GameAction.Charge);
            }
            if (locked) OrbitLockTarget(ref rd);
            rd.Heavy = OwnerHeavyCharging;
            rd.Lunge = _lungeQueued;
            if (_lungeQueued.sqrMagnitude <= 1e-4f && _rushQueued.sqrMagnitude > 1e-4f)
            {
                rd.Lunge = _rushQueued;
                rd.Rush = true;
            }
            if (Time.time < OwnerFrozenUntil)
            {
                rd.Move = Vector2.zero;
                rd.Sprint = rd.Jump = rd.Dash = rd.FlashStep = false;
                if (rd.Rush) { rd.Rush = false; rd.Lunge = Vector3.zero; }   // a sword skill's own dash still goes
            }
            _lungeQueued = _rushQueued = Vector3.zero;
            _jumpQueued = _dashQueued = _flashQueued = _toggleFlightQueued = false;
            return rd;
        }

        /// <summary>
        /// Owner, locked on: move relative to the exact bearing of the opponent (the camera only eases towards it), and turn
        /// a sideways step towards them by half its arc, so walking or running sideways circles them at the same distance
        /// instead of spiralling away.
        /// </summary>
        private void OrbitLockTarget(ref ReplicateData rd)
        {
            Vector3? target = LockTargetProvider != null ? LockTargetProvider() : null;
            if (!target.HasValue) return;
            Vector3 to = target.Value - transform.position;
            to.y = 0f;
            float r = to.magnitude;
            if (r < 1f) return;   // right on top of them: the bearing swings about, keep the camera's
            rd.CameraYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            Vector2 m = Vector2.ClampMagnitude(rd.Move, 1f);
            float len = m.magnitude;
            if (len < 0.05f) return;
            float step = new Vector2(_moveVelocity.x, _moveVelocity.z).magnitude * (float)TimeManager.TickDelta;
            float a = CombatMath.OrbitChordDegrees(step * m.x / len, r) * Mathf.Deg2Rad;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            rd.Move = new Vector2(m.x * c - m.y * s, m.x * s + m.y * c);
        }

        public override void CreateReconcile()
        {
            var rd = new ReconcileData
            {
                Position = transform.position,
                Yaw = _yaw,
                VerticalVelocity = _verticalVelocity,
                Flying = _flying,
                DashTimer = _dashTimer,
                DashDirection = _dashDirection,
                DashCooldown = _dashCooldown,
                FlashStepCooldown = _flashCooldown,
                KnockbackVelocity = _knockback,
                StunTimer = _stunTimer,
                SpeedMultiplier = _speedMultiplier,
                StatSpeed = _statSpeed,
                LungeTimer = _lungeTimer,
                LungeVelocity = _lungeVelocity,
                LungeDuration = _lungeDuration,
                MoveVelocity = _moveVelocity,
                FreezeTimer = _freezeTimer,
                EvadeTimer = _evadeTimer,
            };
            PerformReconcile(rd);
        }

        [Replicate]
        private void PerformReplicate(ReplicateData rd, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
        {
            float delta = (float)TimeManager.TickDelta;
            bool useDefaultForces = false;

            // Spectator future-prediction (demo pattern): repeat the last known input for 1 tick, then idle.
            if (!IsServerStarted && !IsOwner)
            {
                if (state.ContainsTicked())
                {
                    _lastTickedReplicateData.Dispose();
                    _lastTickedReplicateData = rd;
                }
                else if (state.IsFuture())
                {
                    if (rd.GetTick() - _lastTickedReplicateData.GetTick() > 1)
                        useDefaultForces = true;
                    else
                    {
                        rd = _lastTickedReplicateData;
                        rd.Jump = rd.Dash = rd.FlashStep = rd.ToggleFlight = rd.Rush = false;
                        rd.Lunge = Vector3.zero;
                    }
                }
            }

            if (useDefaultForces)
            {
                // Tiny downward force keeps the CharacterController's collision state updated.
                _cc.Move(new Vector3(0f, -1f, 0f) * delta);
                return;
            }

            Simulate(rd, delta);
        }

        private void Simulate(ReplicateData rd, float dt)
        {
            // ---- timers ----
            bool rushing = _lungeTimer > 0f && _dashTimer > 0f;
            // the last tick of a plain dash: it skids out at running speed instead of stopping dead
            bool dashEnds = !rushing && _dashTimer > 0f && _dashTimer - dt <= 1e-5f;
            if (_stunTimer > 0f) _stunTimer = Mathf.Max(0f, _stunTimer - dt);
            if (_dashCooldown > 0f) _dashCooldown = Mathf.Max(0f, _dashCooldown - dt);
            if (_flashCooldown > 0f) _flashCooldown = Mathf.Max(0f, _flashCooldown - dt);
            if (_dashTimer > 0f) _dashTimer = Mathf.Max(0f, _dashTimer - dt);
            if (_lungeTimer > 0f) _lungeTimer = Mathf.Max(0f, _lungeTimer - dt);
            if (_evadeTimer > 0f) _evadeTimer = Mathf.Max(0f, _evadeTimer - dt);

            // ---- hit-stop: frozen for the impact, the knockback waits until it is over ----
            if (_freezeTimer > 0f)
            {
                _freezeTimer = Mathf.Max(0f, _freezeTimer - dt);
                Velocity = Vector3.zero;
                return;
            }

            bool canAct = _stunTimer <= 0f;
            if (!canAct)
            {
                rd.Move = Vector2.zero;
                rd.Jump = rd.Dash = rd.FlashStep = rd.ToggleFlight = rd.Rush = false;
                rd.Sprint = rd.FlyUp = rd.FlyDown = rd.Block = rd.Charge = rd.Heavy = false;
                rd.Lunge = Vector3.zero;
                _dashTimer = 0f;
                _lungeTimer = 0f;
                _evadeTimer = 0f;
            }

            // ---- attack lunge / rush ----
            if (rd.Lunge.sqrMagnitude > 1e-4f)
            {
                bool start = rd.Rush
                    ? _lungeTimer <= 0f && _dashCooldown <= 0f && !(rd.Charge && !rd.Block)
                    : _lungeTimer <= 0f || (_lungeTimer > 0f && _dashTimer > 0f);   // a blow cuts a rush short
                if (start) StartLunge(rd.Lunge, rd.Rush, dt);
            }

            bool grounded = _cc.isGrounded;
            IsBlocking = rd.Block;
            IsCharging = rd.Charge && !rd.Block;
            IsChargingHeavy = rd.Heavy && !IsBlocking && !IsCharging;
            IsSprinting = rd.Sprint && !IsBlocking && !IsCharging && !IsChargingHeavy && rd.Move.sqrMagnitude > 0.01f;

            if (rd.ToggleFlight && !IsCharging)
            {
                _flying = !_flying;
                _verticalVelocity = _flying ? 0f : Mathf.Min(_verticalVelocity, 0f);
            }

            // ---- desired horizontal movement relative to camera yaw ----
            Vector2 move = Vector2.ClampMagnitude(rd.Move, 1f);
            Vector3 wish = Quaternion.Euler(0f, rd.CameraYaw, 0f) * new Vector3(move.x, 0f, move.y);
            // walking is the default; holding Run (Shift) runs (IsSprinting = running)
            float speed = _flying ? _config.flySpeed : (IsSprinting ? _config.runSpeed : _config.walkSpeed);
            speed *= SpeedScale;
            if (IsBlocking) speed *= _config.blockMoveMultiplier;
            if (IsChargingHeavy) speed *= _config.heavyChargeMoveMultiplier;   // planted for the big swing: a slow shuffle
            if (IsCharging) speed = 0f;

            Vector3 moveDir = wish.sqrMagnitude > 0.0001f ? wish.normalized : Forward;

            // ---- dash / flash step ----
            if (rd.Dash && !rd.Rush && !IsCharging && _dashCooldown <= 0f)
            {
                _dashTimer = _config.dashDuration;
                _dashCooldown = _config.dashCooldown;
                _dashDirection = moveDir;
                _evadeTimer = CombatMath.DashEvadeTime;
                _lungeTimer = 0f;   // dashing out of an attack's dash: the dash wins
                dashEnds = false;
            }

            Vector3 flashOffset = Vector3.zero;
            if (rd.FlashStep && !IsCharging && _flashCooldown <= 0f)
            {
                _flashCooldown = _config.flashStepCooldown;
                flashOffset = moveDir * _config.flashStepDistance;
                LastFlashStepTime = Time.time;
                _lungeTimer = 0f;   // vanished: no dash carries on from the new spot
                _dashTimer = 0f;
                dashEnds = false;
            }

            // ---- vertical ----
            if (_flying)
            {
                float v = (rd.FlyUp ? 1f : 0f) - (rd.FlyDown ? 1f : 0f);
                if (IsChargingHeavy) v *= _config.heavyChargeMoveMultiplier;
                float wantV = IsCharging ? 0f : v * _config.flySpeed * SpeedScale;
                float av = Mathf.Abs(wantV) > Mathf.Abs(_verticalVelocity) ? FlyAccel : FlyDecel;
                _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, wantV, av * dt);
            }
            else
            {
                if (grounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
                if (rd.Jump && grounded && !IsCharging)
                    _verticalVelocity = Mathf.Sqrt(2f * _gravity * _jumpHeight);
                _verticalVelocity = Mathf.Max(_verticalVelocity - _gravity * dt, -_maxFallSpeed);
            }

            // ---- walking / running / flying: ease towards what the input asks for ----
            if (dashEnds) _moveVelocity = _dashDirection * (_config.runSpeed * SpeedScale);
            if (_lungeTimer <= 0f && _dashTimer <= 0f)
            {
                Vector3 want = wish * speed;
                bool stopping = want.sqrMagnitude < 1e-4f;
                float a = _flying ? (stopping ? FlyDecel : FlyAccel)
                    : !grounded ? (stopping ? AirDecel : AirAccel)
                    : (stopping ? GroundDecel : GroundAccel);
                _moveVelocity = Vector3.MoveTowards(_moveVelocity, want, a * dt);
            }

            // ---- compose ----
            Vector3 horizontal;
            if (_lungeTimer > 0f)
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(_lungeDuration / dt));
                int k = Mathf.RoundToInt(_lungeTimer / dt);
                horizontal = _lungeVelocity * CombatMath.EaseOutStep(k, n, _dashTimer > 0f ? RushEase : LungeEase);
            }
            else if (_dashTimer > 0f) horizontal = _dashDirection * (_config.dashSpeed * SpeedScale);
            else horizontal = _moveVelocity;
            Vector3 velocity = horizontal + Vector3.up * _verticalVelocity + _knockback;
            _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, _knockbackDeceleration * dt);

            if (flashOffset != Vector3.zero) _cc.Move(flashOffset); // swept, so it stops at walls
            _cc.Move(velocity * dt);
            Velocity = _cc.velocity;

            // ---- facing ----
            float targetYaw = _yaw;
            if (_lungeTimer > 0f && _lungeVelocity.x * _lungeVelocity.x + _lungeVelocity.z * _lungeVelocity.z > 1e-4f)
                targetYaw = Mathf.Atan2(_lungeVelocity.x, _lungeVelocity.z) * Mathf.Rad2Deg;
            else if (rd.FaceAim) targetYaw = rd.CameraYaw;
            else if (_dashTimer > 0f) targetYaw = Mathf.Atan2(_dashDirection.x, _dashDirection.z) * Mathf.Rad2Deg;
            else if (wish.sqrMagnitude > 0.01f) targetYaw = Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg;
            _yaw = Mathf.MoveTowardsAngle(_yaw, targetYaw, _turnSpeed * dt);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        /// <summary>
        /// Starts an attack's dash (or a rush) over a whole number of ticks, so the distance comes out exact whatever the
        /// easing. A rush is also a dash: it takes the dash's cooldown and dodge window, and the two end together.
        /// </summary>
        private void StartLunge(Vector3 lunge, bool rush, float dt)
        {
            Vector3 l = Vector3.ClampMagnitude(_flying ? lunge : new Vector3(lunge.x, 0f, lunge.z), rush ? MaxRush : MaxLunge);
            if (l.sqrMagnitude < 1e-4f) return;
            float ticks = Mathf.Max(1f, Mathf.Round(Mathf.Clamp(l.magnitude / (rush ? RushSpeed : LungeSpeed), 0.06f, rush ? 0.36f : 0.24f) / dt));
            _lungeTimer = _lungeDuration = ticks * dt;
            _lungeVelocity = l / _lungeTimer;
            Vector3 flat = new Vector3(l.x, 0f, l.z);
            if (rush)
            {
                _dashTimer = _lungeTimer;
                _dashCooldown = _config.dashCooldown;
                _dashDirection = flat.sqrMagnitude > 1e-4f ? flat.normalized : Forward;
                _evadeTimer = CombatMath.DashEvadeTime;
            }
            else
            {
                _dashTimer = 0f;
                _evadeTimer = 0f;
            }
            if (flat.sqrMagnitude > 1e-4f) _yaw = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
        }

        [Reconcile]
        private void PerformReconcile(ReconcileData rd, Channel channel = Channel.Unreliable)
        {
            _yaw = rd.Yaw;
            _verticalVelocity = rd.VerticalVelocity;
            _flying = rd.Flying;
            _dashTimer = rd.DashTimer;
            _dashDirection = rd.DashDirection;
            _dashCooldown = rd.DashCooldown;
            _flashCooldown = rd.FlashStepCooldown;
            _knockback = rd.KnockbackVelocity;
            _stunTimer = rd.StunTimer;
            _speedMultiplier = rd.SpeedMultiplier <= 0f ? 1f : rd.SpeedMultiplier;
            _statSpeed = rd.StatSpeed <= 0f ? 1f : rd.StatSpeed;
            _lungeTimer = rd.LungeTimer;
            _lungeVelocity = rd.LungeVelocity;
            _lungeDuration = rd.LungeDuration;
            _moveVelocity = rd.MoveVelocity;
            _freezeTimer = rd.FreezeTimer;
            _evadeTimer = rd.EvadeTimer;

            // Disable the CharacterController before moving it, otherwise its physics position lags a tick.
            _cc.enabled = false;
            transform.SetPositionAndRotation(rd.Position, Quaternion.Euler(0f, rd.Yaw, 0f));
            _cc.enabled = true;
        }

        // ---------------- server hooks (session 4: combat) ----------------

        /// <summary>
        /// Server: push the player (m/s, decays over time) and stun them (no input) for stunSeconds. With
        /// <paramref name="freezeSeconds"/> (the blow's hit-stop) they first hold still that long, then the push starts; the
        /// stun covers the freeze too. Their own walking / dashing stops.
        /// </summary>
        [Server]
        public void ServerApplyKnockback(Vector3 velocity, float stunSeconds, float freezeSeconds = 0f)
        {
            if (!_flying && velocity.y > 0f)
            {
                _verticalVelocity = Mathf.Max(_verticalVelocity, velocity.y);
                velocity.y = 0f;
            }
            freezeSeconds = Mathf.Clamp(freezeSeconds, 0f, 0.5f);
            _knockback = velocity;
            _freezeTimer = Mathf.Max(_freezeTimer, freezeSeconds);
            _stunTimer = Mathf.Max(_stunTimer, stunSeconds + freezeSeconds);
            _dashTimer = 0f;
            _lungeTimer = 0f;
            _evadeTimer = 0f;
            _moveVelocity = Vector3.zero;
        }

        /// <summary>
        /// Server: stun (no input) for at least stunSeconds without touching the knockback, so a fighter knocked out mid-air by
        /// a launching blow keeps flying and lands instead of stopping dead.
        /// </summary>
        [Server]
        public void ServerStun(float stunSeconds)
        {
            _stunTimer = Mathf.Max(_stunTimer, stunSeconds);
            _dashTimer = 0f;
            _evadeTimer = 0f;
        }

        /// <summary>Server: instantly move the player (respawn). Clears velocities, flight and stun.</summary>
        [Server]
        public void ServerTeleport(Vector3 position)
        {
            _knockback = Vector3.zero;
            _verticalVelocity = 0f;
            _moveVelocity = Vector3.zero;
            _dashTimer = 0f;
            _lungeTimer = 0f;
            _stunTimer = 0f;
            _freezeTimer = 0f;
            _evadeTimer = 0f;
            _flying = false;
            _cc.enabled = false;
            transform.position = position;
            _cc.enabled = true;
        }

        /// <summary>Server: movement speed multiplier (Power Surge +20% = 1.2). 1 = normal.</summary>
        [Server]
        public void ServerSetSpeedMultiplier(float multiplier) => _speedMultiplier = Mathf.Max(0.01f, multiplier);

        /// <summary>Server: permanent movement speed multiplier from the fighter's Speed stat (stacks with Power Surge).</summary>
        [Server]
        public void ServerSetStatSpeed(float multiplier) => _statSpeed = Mathf.Clamp(multiplier, 0.5f, 2f);
    }
}
