using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using LightMMORPG.Config;
using LightMMORPG.Input;
using UnityEngine;

namespace LightMMORPG.Player
{
    /// <summary>
    /// Client-side predicted CharacterController movement (FishNet CharacterControllerPrediction demo pattern).
    /// Run / sprint / jump, flight toggle with fly up/down, dash, flash step, block slow-down, charge = stand still.
    /// Movement is relative to the camera yaw sent in the replicate data.
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
            // One-time inputs (accumulated in Update between ticks).
            public bool Jump, Dash, FlashStep, ToggleFlight;

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
        public bool IsSprinting { get; private set; }
        public bool IsDashing => _dashTimer > 0f;
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

        // owner input accumulation
        private bool _jumpQueued, _dashQueued, _flashQueued, _toggleFlightQueued;
        private float _faceAimUntil;
        private ReplicateData _lastTickedReplicateData;

        /// <summary>Owner-side: optional camera yaw provider (set by the third-person camera). Falls back to facing.</summary>
        public static System.Func<float> CameraYawProvider;
        /// <summary>Owner-side: true while locked on (set by the camera); character then faces camera yaw.</summary>
        public static System.Func<bool> LockedOnProvider;

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
            if (_input.Pressed(GameAction.Dash)) _dashQueued = true;
            if (_input.Pressed(GameAction.FlashStep)) _flashQueued = true;
            if (_input.Pressed(GameAction.ToggleFlight)) _toggleFlightQueued = true;
        }

        protected override void TimeManager_OnTick() => PerformReplicate(BuildReplicateData());

        protected override void TimeManager_OnPostTick() => CreateReconcile();

        private ReplicateData BuildReplicateData()
        {
            if (!IsOwner) return default;
            var rd = new ReplicateData
            {
                CameraYaw = CameraYawProvider != null ? CameraYawProvider() : _yaw,
                FaceAim = (LockedOnProvider != null && LockedOnProvider()) || Time.time < _faceAimUntil,
                Jump = _jumpQueued,
                Dash = _dashQueued,
                FlashStep = _flashQueued,
                ToggleFlight = _toggleFlightQueued,
            };
            if (_input != null)
            {
                rd.Move = _input.Move;
                rd.Sprint = _input.Held(GameAction.Sprint);
                rd.FlyUp = _input.Held(GameAction.Jump);
                rd.FlyDown = _input.Held(GameAction.FlyDown);
                rd.Block = _input.Held(GameAction.Block);
                rd.Charge = _input.Held(GameAction.Charge);
            }
            _jumpQueued = _dashQueued = _flashQueued = _toggleFlightQueued = false;
            return rd;
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
                        rd.Jump = rd.Dash = rd.FlashStep = rd.ToggleFlight = false;
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
            if (_stunTimer > 0f) _stunTimer = Mathf.Max(0f, _stunTimer - dt);
            if (_dashCooldown > 0f) _dashCooldown = Mathf.Max(0f, _dashCooldown - dt);
            if (_flashCooldown > 0f) _flashCooldown = Mathf.Max(0f, _flashCooldown - dt);
            if (_dashTimer > 0f) _dashTimer = Mathf.Max(0f, _dashTimer - dt);

            bool canAct = _stunTimer <= 0f;
            if (!canAct)
            {
                rd.Move = Vector2.zero;
                rd.Jump = rd.Dash = rd.FlashStep = rd.ToggleFlight = false;
                rd.Sprint = rd.FlyUp = rd.FlyDown = rd.Block = rd.Charge = false;
                _dashTimer = 0f;
            }

            bool grounded = _cc.isGrounded;
            IsBlocking = rd.Block;
            IsCharging = rd.Charge && !rd.Block;
            IsSprinting = rd.Sprint && !IsBlocking && !IsCharging && rd.Move.sqrMagnitude > 0.01f;

            if (rd.ToggleFlight && !IsCharging)
            {
                _flying = !_flying;
                _verticalVelocity = _flying ? 0f : Mathf.Min(_verticalVelocity, 0f);
            }

            // ---- desired horizontal movement relative to camera yaw ----
            Vector2 move = Vector2.ClampMagnitude(rd.Move, 1f);
            Vector3 wish = Quaternion.Euler(0f, rd.CameraYaw, 0f) * new Vector3(move.x, 0f, move.y);
            float speed = _flying ? _config.flySpeed : (IsSprinting ? _config.sprintSpeed : _config.runSpeed);
            speed *= _speedMultiplier;
            if (IsBlocking) speed *= _config.blockMoveMultiplier;
            if (IsCharging) speed = 0f;

            Vector3 moveDir = wish.sqrMagnitude > 0.0001f ? wish.normalized : Forward;

            // ---- dash / flash step ----
            if (rd.Dash && !IsCharging && _dashCooldown <= 0f)
            {
                _dashTimer = _config.dashDuration;
                _dashCooldown = _config.dashCooldown;
                _dashDirection = moveDir;
            }

            Vector3 flashOffset = Vector3.zero;
            if (rd.FlashStep && !IsCharging && _flashCooldown <= 0f)
            {
                _flashCooldown = _config.flashStepCooldown;
                flashOffset = moveDir * _config.flashStepDistance;
                LastFlashStepTime = Time.time;
            }

            // ---- vertical ----
            if (_flying)
            {
                float v = (rd.FlyUp ? 1f : 0f) - (rd.FlyDown ? 1f : 0f);
                _verticalVelocity = IsCharging ? 0f : v * _config.flySpeed * _speedMultiplier;
            }
            else
            {
                if (grounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
                if (rd.Jump && grounded && !IsCharging)
                    _verticalVelocity = Mathf.Sqrt(2f * _gravity * _jumpHeight);
                _verticalVelocity = Mathf.Max(_verticalVelocity - _gravity * dt, -_maxFallSpeed);
            }

            // ---- compose ----
            Vector3 horizontal = _dashTimer > 0f ? _dashDirection * (_config.dashSpeed * _speedMultiplier) : wish * speed;
            Vector3 velocity = horizontal + Vector3.up * _verticalVelocity + _knockback;
            _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, _knockbackDeceleration * dt);

            if (flashOffset != Vector3.zero) _cc.Move(flashOffset); // swept, so it stops at walls
            _cc.Move(velocity * dt);
            Velocity = _cc.velocity;

            // ---- facing ----
            float targetYaw = _yaw;
            if (rd.FaceAim) targetYaw = rd.CameraYaw;
            else if (_dashTimer > 0f) targetYaw = Mathf.Atan2(_dashDirection.x, _dashDirection.z) * Mathf.Rad2Deg;
            else if (wish.sqrMagnitude > 0.01f) targetYaw = Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg;
            _yaw = Mathf.MoveTowardsAngle(_yaw, targetYaw, _turnSpeed * dt);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
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

            // Disable the CharacterController before moving it, otherwise its physics position lags a tick.
            _cc.enabled = false;
            transform.SetPositionAndRotation(rd.Position, Quaternion.Euler(0f, rd.Yaw, 0f));
            _cc.enabled = true;
        }

        // ---------------- server hooks (session 4: combat) ----------------

        /// <summary>Server: push the player (m/s, decays over time) and stun them (no input) for stunSeconds.</summary>
        [Server]
        public void ServerApplyKnockback(Vector3 velocity, float stunSeconds)
        {
            if (!_flying && velocity.y > 0f)
            {
                _verticalVelocity = Mathf.Max(_verticalVelocity, velocity.y);
                velocity.y = 0f;
            }
            _knockback = velocity;
            _stunTimer = Mathf.Max(_stunTimer, stunSeconds);
            _dashTimer = 0f;
        }

        /// <summary>Server: instantly move the player (respawn). Clears velocities, flight and stun.</summary>
        [Server]
        public void ServerTeleport(Vector3 position)
        {
            _knockback = Vector3.zero;
            _verticalVelocity = 0f;
            _dashTimer = 0f;
            _stunTimer = 0f;
            _flying = false;
            _cc.enabled = false;
            transform.position = position;
            _cc.enabled = true;
        }

        /// <summary>Server: movement speed multiplier (Power Surge +20% = 1.2). 1 = normal.</summary>
        [Server]
        public void ServerSetSpeedMultiplier(float multiplier) => _speedMultiplier = Mathf.Max(0.01f, multiplier);
    }
}
