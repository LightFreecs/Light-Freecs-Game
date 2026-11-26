using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using LightMMORPG.Cameras;
using LightMMORPG.Characters;
using LightMMORPG.Config;
using LightMMORPG.Core;
using LightMMORPG.Input;
using LightMMORPG.Player;
using LightMMORPG.VFX;
using UnityEngine;

namespace LightMMORPG.Combat
{
    public enum AttackKind : byte { Light, Heavy, Blast, Beam, Surge }

    /// <summary>
    /// Melee + powers. Owner reads input, plays its own swing/VFX immediately and sends a <c>[ServerRpc]</c> with the aim
    /// direction (lock-on target if any, else camera forward). The server validates (alive, not stunned, cooldowns,
    /// energy, combo window), resolves hits (sphere overlap / simulated projectile / spherecast beam) through
    /// <see cref="PlayerStats.ServerApplyHit"/>, and tells other clients to play the same visuals (<c>ExcludeOwner</c>).
    /// The server half of every attack is a public <c>Server*</c> method, so a server-driven fighter (the sparring
    /// <see cref="TrainingDummy"/>) attacks through exactly the same rules. Melee hits report where the striking fist / foot
    /// really is at the move's impact frame (<see cref="StrikePoint(Vector3, AttackMove)"/>), so the victim reacts where it was hit.
    /// The heavy is held: pressing it winds up (the fist is pulled back, movement slows), releasing it swings; damage,
    /// knockback (up to sending the victim flying) and hitstun grow with the hold time up to <c>heavyMaxChargeTime</c>.
    /// </summary>
    public sealed partial class PlayerCombat : NetworkBehaviour
    {
        public static PlayerCombat Local { get; private set; }
        /// <summary>Owner-side aim override (smoke-test autopilot). Null = camera.</summary>
        public static System.Func<Vector3?> AimOverride;

        private GameConfig _config;
        private PlayerStats _stats;
        private PlayerMotor _motor;
        private PlayerInputReader _input;
        private PlayerProceduralAnimator _anim;

        // ---- owner-side prediction (UI + local gating only; the server decides) ----
        private float _nextLightLocal;
        private int _localComboIndex;
        private float _localComboTime = -10f;
        private float _heavyReadyLocal, _blastReadyLocal, _beamReadyLocal, _surgeReadyLocal;
        private bool _beamCharging;
        private float _beamChargeStart;
        private bool _heavyCharging;
        private float _heavyChargeStart;

        // ---- server state ----
        private ComboChain _combo;
        private double _nextLightServer;
        private Cooldown _heavyCd, _blastCd, _beamCd, _surgeCd;
        private bool _serverBeamCharging;
        private double _serverBeamStart;
        private bool _serverHeavyCharging;
        private double _serverHeavyStart;
        // a released heavy waits for its fist to land (the swing's Hit time) before it is resolved
        private bool _heavyPending;
        private double _heavyHitAt;
        private Vector3 _heavyAim;
        private float _heavyChargeTime;
        private struct Projectile { public Vector3 Pos, Dir; public float Speed, Life, Radius, Damage; }
        private readonly List<Projectile> _projectiles = new();
        private readonly Collider[] _overlap = new Collider[16];
        private readonly HashSet<PlayerStats> _hitSet = new();
        private readonly AttackPose _strikePose = new();
        private CharacterController _cc;

        // ---- visuals ----
        private BeamChargeVisual _chargeVisual;

        // ---- feel: input buffer, melee assist (gap-closing lunges), sword stance + sword skills ----
        private const float BufferTime = 0.3f, LightLunge = 2.6f, HeavyLunge = 3.6f, SwordDamageMul = 1.1f;
        private float _lightBufferedAt = -10f, _heavyBufferedAt = -10f;
        private float _busyUntilLocal, _heavyCdDurLocal, _windupSeenAt, _playStart;
        private bool _swordLocal;
        private int _lastPreTier, _playTier = -1, _playNext;
        private SwordVisual _swordVis;
        private readonly SyncVar<bool> _sword = new();
        private struct SkillHit { public double At; public int Index; }
        private readonly List<SkillHit> _skillHits = new();
        private int _skillTier;
        private Vector3 _skillAim, _skillFrom, _skillLunge, _heavyFrom, _heavySweep;
        private double _skillReadyServer, _serverBusyUntil;

        private SwordVisual SwordVis => _swordVis != null ? _swordVis : (_swordVis = SwordVisual.For(this));
        /// <summary>Sword stance (synced): light swings reach further, Heavy becomes sword skills.</summary>
        public bool SwordDrawn => _sword.Value;

        public bool IsChargingBeam => _beamCharging;
        public float BeamCharge01 => _beamCharging && _config.chargedBeam != null && _config.chargedBeam.maxChargeTime > 0f
            ? Mathf.Clamp01((Time.time - _beamChargeStart) / _config.chargedBeam.maxChargeTime) : 0f;
        /// <summary>Owner: Heavy is held (winding up).</summary>
        public bool IsChargingHeavy => _heavyCharging;
        /// <summary>Owner: how charged the held heavy is (0 = a tap, 1 = full); 0 when not held.</summary>
        public float HeavyCharge01 => _heavyCharging ? CombatMath.Charge01(Time.time - _heavyChargeStart, _config.heavyMaxChargeTime) : 0f;

        /// <summary>The basic-attack moveset this fighter uses (its animator's; the default on a server without one).</summary>
        private BasicMoveset Moveset => _anim != null ? _anim.Moveset : BasicMovesets.Get(BasicMovesets.DefaultId);

        private void Awake()
        {
            _config = GameConfig.Load();
            _stats = GetComponent<PlayerStats>();
            _motor = GetComponent<PlayerMotor>();
            _input = GetComponent<PlayerInputReader>();
            _anim = GetComponent<PlayerProceduralAnimator>();
            _cc = GetComponent<CharacterController>();
            _combo = new ComboChain(_config.lightComboDamage, _config.comboWindow);
            _heavyCd = new Cooldown(Mathf.Max(0f, _config.heavyCooldown - 0.15f)); // slack: the owner gates on the full cooldown
            _blastCd = new Cooldown(Def(AttackKind.Blast)?.cooldown ?? 0.35f);
            _beamCd = new Cooldown(Def(AttackKind.Beam)?.cooldown ?? 4f);
            _surgeCd = new Cooldown(Def(AttackKind.Surge)?.cooldown ?? 20f);
            _heavyCdDurLocal = _config.heavyCooldown;
            InitFightMovement();   // rush / pursuit on Dash, afterimages (PlayerCombat.Movement.cs)
            _sword.OnChange += (prev, next, asServer) =>
            {
                if (asServer && !IsClientStarted) return;   // a dedicated server draws nothing
                SwordVis.SetDrawn(next);
            };
        }

        public override void OnStartClient()
        {
            if (IsOwner) Local = this;
        }

        public override void OnStopClient()
        {
            if (Local == this) Local = null;
            SetChargeVisual(false);
        }

        public override void OnStartServer() => TimeManager.OnTick += ServerTick;

        public override void OnStopServer()
        {
            if (TimeManager != null) TimeManager.OnTick -= ServerTick;
        }

        private AbilityDefinition Def(AttackKind k) => k switch
        {
            AttackKind.Blast => _config.energyBlast,
            AttackKind.Beam => _config.chargedBeam,
            AttackKind.Surge => _config.powerSurge,
            _ => null
        };

        // ---------------- HUD queries (owner) ----------------

        /// <summary>Remaining cooldown fraction 1 → 0 (owner-side estimate).</summary>
        public float Cooldown01(AttackKind k)
        {
            float ready, dur;
            switch (k)
            {
                case AttackKind.Heavy: ready = _heavyReadyLocal; dur = _heavyCdDurLocal; break;
                case AttackKind.Blast: ready = _blastReadyLocal; dur = _blastCd.Duration; break;
                case AttackKind.Beam: ready = _beamReadyLocal; dur = _beamCd.Duration; break;
                case AttackKind.Surge: ready = _surgeReadyLocal; dur = _surgeCd.Duration; break;
                default: return 0f;
            }
            return dur <= 0f ? 0f : Mathf.Clamp01((ready - Time.time) / dur);
        }

        public float EnergyCost(AttackKind k) => Def(k) != null ? Def(k).energyCost : 0f;
        public float AuraCost(AttackKind k) => Def(k) != null ? Def(k).auraCost : 0f;
        /// <summary>Enough energy and enough aura reserve (energy attacks spend both; the aura never comes back this battle).</summary>
        public bool CanAfford(AttackKind k) => _stats != null && _stats.Energy.Value >= EnergyCost(k) && _stats.Aura.Value >= AuraCost(k);

        // ---------------- owner input ----------------

        private Vector3 Aim()
        {
            Vector3? o = AimOverride?.Invoke();
            if (o.HasValue && o.Value.sqrMagnitude > 1e-4f) return o.Value.normalized;
            var cam = ThirdPersonCamera.Instance;
            return cam != null ? cam.AimDirection : _motor.Forward;
        }

        private Vector3 LocalChest() => VisualRoot().position + Vector3.up * 1.3f;

        private Transform VisualRoot()
        {
            var g = transform.Find("Graphics");
            return g != null ? g : transform;
        }

        private void Update()
        {
            UpdateSwordVisuals();
            if (!IsOwner || _input == null || !_input.InputEnabled) return;
            bool canAct = !_stats.IsDown.Value && !_motor.IsStunned;
            float now = Time.time;
            if (_input.Pressed(GameAction.Sword) && !_heavyCharging)
            {
                _swordLocal = !_swordLocal;
                SwordVis.SetDrawn(_swordLocal);
                CmdSetSword(_swordLocal);
            }
            // buffered: a press a little before the next swing is allowed still lands, so strings flow
            if (_input.Pressed(GameAction.Light)) _lightBufferedAt = now;
            if (_input.Pressed(GameAction.Heavy)) _heavyBufferedAt = now;

            if (_beamCharging && !canAct) CancelBeam();
            // hit, knocked out, guarding, powering up or a menu opening calls the wind-up off (no swing)
            if (_heavyCharging && (!canAct || PlayerInputReader.MenuOpen || _input.Held(GameAction.Block) || _input.Held(GameAction.Charge)))
                CancelHeavy();

            if (!canAct || now < _busyUntilLocal) return;   // a sword skill and its post-motion delay hold everything

            // a jab pressed during a rush waits for the arrival, so the blow comes out as you get there
            if (_motor.IsRushing && now - _lightBufferedAt <= BufferTime) _lightBufferedAt = now;
            if (!_heavyCharging && !_motor.IsRushing && now - _lightBufferedAt <= BufferTime && now >= _nextLightLocal)
            {
                _lightBufferedAt = -10f;
                _nextLightLocal = now + CombatMath.LightAttackInterval;
                if (now - _localComboTime > _config.comboWindow) _localComboIndex = 0;
                int idx = _localComboIndex;
                _localComboIndex = (idx + 1) % Mathf.Max(1, _config.lightComboDamage.Length);
                _localComboTime = now;
                Vector3 aim = MeleeAim(LightLunge, 0.35f, out Vector3 lunge, out bool assisted);
                if (!assisted) _motor.FaceAimFor(0.6f);
                _motor.QueueLunge(lunge);
                if (_anim != null) _anim.PlayAttack(AttackKind.Light, idx);
                CmdLight(aim, lunge);
            }
            else if (!_heavyCharging && !_beamCharging && now - _heavyBufferedAt <= BufferTime && now >= _heavyReadyLocal
                     && !_input.Held(GameAction.Block) && !_input.Held(GameAction.Charge))
            {
                // hold to wind up: the fist is pulled back and the charge builds until the button is let go
                _heavyCharging = true;
                _heavyChargeStart = now;
                _heavyBufferedAt = -10f;
                _motor.FaceAimFor(0.3f);
                if (_anim != null) _anim.PlayHeavyWindUp();
                CmdHeavyCharge(true);
            }
            if (_heavyCharging)
            {
                _motor.FaceAimFor(0.3f);
                if (!_input.Held(GameAction.Heavy))
                {
                    // let go: swing. The charge (seconds held, capped) sets damage, knockback and hitstun on the server.
                    float charge = Mathf.Min(now - _heavyChargeStart, _config.heavyMaxChargeTime);
                    _heavyCharging = false;
                    if (!(_swordLocal && TryReleaseSwordSkill(now, charge)))
                    {
                        _heavyReadyLocal = now + _config.heavyCooldown;
                        _heavyCdDurLocal = _config.heavyCooldown;
                        _nextLightLocal = Mathf.Max(_nextLightLocal, now + 0.3f);   // a jab can't cut the punch off before it lands
                        Vector3 aim = MeleeAim(HeavyLunge, 0.8f, out Vector3 lunge, out bool assisted);
                        if (!assisted) _motor.FaceAimFor(0.8f);
                        _motor.QueueLunge(lunge);
                        if (_anim != null) _anim.PlayHeavySwing(CombatMath.Charge01(charge, _config.heavyMaxChargeTime));
                        CmdHeavyRelease(aim, charge, lunge);
                    }
                }
            }
            _motor.OwnerHeavyCharging = _heavyCharging;
            if (_heavyCharging) return;   // nothing else while the fist is drawn back

            if (_input.Pressed(GameAction.Blast) && now >= _blastReadyLocal && CanAfford(AttackKind.Blast))
            {
                _blastReadyLocal = now + _blastCd.Duration;
                _motor.FaceAimFor(0.5f);
                Vector3 aim = Aim();
                if (_anim != null) _anim.PlayAttack(AttackKind.Blast, 0);
                var d = Def(AttackKind.Blast);
                CombatFx.SpawnBlastVisual(this, LocalChest() + aim * 0.8f, aim, d.speed, d.duration, d.radius);
                CmdBlast(aim);
            }

            if (_input.Pressed(GameAction.Beam) && !_beamCharging && now >= _beamReadyLocal && CanAfford(AttackKind.Beam))
            {
                _beamCharging = true;
                _beamChargeStart = now;
                SetChargeVisual(true);
                CmdBeamCharge(true);
            }
            if (_beamCharging)
            {
                _motor.FaceAimFor(0.3f);
                if (!_input.Held(GameAction.Beam))
                {
                    var d = Def(AttackKind.Beam);
                    float charge = Mathf.Min(now - _beamChargeStart, d.maxChargeTime);
                    _beamCharging = false;
                    SetChargeVisual(false);
                    _beamReadyLocal = now + d.cooldown;
                    Vector3 aim = Aim();
                    Vector3 origin = LocalChest() + aim * 0.6f;
                    float len = BeamLength(origin, aim, d.radius, d.range, out _);
                    if (_anim != null) _anim.PlayAttack(AttackKind.Beam, 0);
                    CombatFx.SpawnBeamVisual(origin, aim, len, d.maxChargeTime > 0f ? charge / d.maxChargeTime : 1f);
                    CmdBeamFire(aim, charge);
                }
            }

            if (_input.Pressed(GameAction.Surge) && now >= _surgeReadyLocal && !_stats.SurgeActive && CanAfford(AttackKind.Surge))
            {
                _surgeReadyLocal = now + _surgeCd.Duration;
                if (_anim != null) _anim.PlayAttack(AttackKind.Surge, 0);
                CmdSurge();
            }
        }

        private void CancelBeam()
        {
            _beamCharging = false;
            SetChargeVisual(false);
            CmdBeamCharge(false);
        }

        private void CancelHeavy()
        {
            _heavyCharging = false;
            _motor.OwnerHeavyCharging = false;
            if (_anim != null) _anim.CancelHeavy();
            CmdHeavyCharge(false);
        }

        private void SetChargeVisual(bool on)
        {
            if (on)
            {
                if (_chargeVisual == null) _chargeVisual = BeamChargeVisual.Create(this);
                _chargeVisual.Begin();
            }
            else if (_chargeVisual != null) _chargeVisual.End();
        }

        // ---------------- melee assist + sword skills (owner / all clients) ----------------

        /// <summary>
        /// Owner: melee assist. Aims at the lock-on target, else the nearest standing fighter in front within reach, and
        /// gives the dash that carries this fighter into striking range of it — anime fighters cross the gap with every blow.
        /// With no one in reach: a short step along the camera aim. <paramref name="assisted"/> = a target was found.
        /// </summary>
        private Vector3 MeleeAim(float maxLunge, float idleStep, out Vector3 lunge, out bool assisted)
        {
            Vector3 aim = Aim();
            Vector3 me = LocalChest();
            PlayerStats target = AssistTarget(me, aim, (maxLunge + 2.5f) * DesignScale);
            assisted = target != null;
            if (assisted)
            {
                Vector3 to = target.transform.position + Vector3.up * 1.3f - me;
                if (!_motor.IsFlying) to.y = 0f;
                float dist = to.magnitude;
                if (dist > 1e-3f)
                {
                    float contact = (_swordLocal ? 1.9f : 1.15f) * DesignScale;
                    lunge = to / dist * Mathf.Clamp(dist - contact, 0.12f, maxLunge * DesignScale);
                    return to / dist;
                }
            }
            Vector3 step = _motor.IsFlying ? aim : new Vector3(aim.x, 0f, aim.z).normalized;
            lunge = step * idleStep * DesignScale;
            return aim;
        }

        private PlayerStats AssistTarget(Vector3 me, Vector3 aim, float range)
        {
            var cam = ThirdPersonCamera.Instance;
            if (cam != null && cam.CurrentTarget != null)
            {
                var locked = cam.CurrentTarget.GetComponent<PlayerStats>();
                if (locked != null && !locked.IsDown.Value && (locked.transform.position + Vector3.up * 1.3f - me).magnitude <= range * 1.5f)
                    return locked;
            }
            Vector3 flatAim = new Vector3(aim.x, 0f, aim.z).normalized;
            PlayerStats best = null;
            float bestScore = float.MaxValue;
            foreach (var s in PlayerStats.All)
            {
                if (s == null || s == _stats || s.IsDown.Value) continue;
                Vector3 to = s.transform.position + Vector3.up * 1.3f - me;
                float d = to.magnitude;
                if (d > range) continue;
                Vector3 flat = new Vector3(to.x, 0f, to.z);
                float ang = flat.sqrMagnitude > 1e-4f ? Vector3.Angle(flatAim, flat) : 0f;
                if (ang > 65f) continue;
                float score = d * (1f + ang / 45f);
                if (score < bestScore) { bestScore = score; best = s; }
            }
            return best;
        }

        /// <summary>Owner, sword stance: the held pre-motion fires the skill it readied. False = not enough energy (plain heavy).</summary>
        private bool TryReleaseSwordSkill(float now, float charge)
        {
            int tier = SwordSkills.TierFor(charge);
            while (tier > 0 && _stats.Energy.Value < SwordSkills.All[tier].Energy) tier--;
            var sk = SwordSkills.All[tier];
            if (_stats.Energy.Value < sk.Energy) return false;
            Vector3 aim = MeleeAim(sk.MaxLunge, sk.BaseLunge, out Vector3 lunge, out bool assisted);
            if (!assisted) _motor.FaceAimFor(sk.Duration);
            _motor.QueueLunge(lunge);
            float busy = sk.Duration + sk.PostDelay;
            _busyUntilLocal = now + busy;
            _motor.OwnerFrozenUntil = now + busy;   // the system moves you, then the post-motion delay holds you
            _heavyReadyLocal = now + sk.Cooldown;
            _heavyCdDurLocal = sk.Cooldown;
            _nextLightLocal = Mathf.Max(_nextLightLocal, now + busy);
            _localComboIndex = 0;
            PlaySkill(tier);
            CmdSwordSkill(aim, charge, lunge, (byte)tier);
            return true;
        }

        /// <summary>All clients: start showing a sword skill (glow, strikes on schedule; the owner gets the callout).</summary>
        private void PlaySkill(int tier)
        {
            var sk = SwordSkills.All[tier];
            _playTier = tier;
            _playStart = Time.time;
            _playNext = 0;
            if (_anim != null && sk.Moves[0] >= 0) _anim.CancelHeavy();   // the pre-motion flows into the first strike
            SwordVis.SetGlow(sk.Glow, 3f, sk.Duration + sk.PostDelay * 0.5f);
            if (!IsOwner) return;
            AnimeScreenFx.Callout(sk.Name, sk.Glow);
            AnimeScreenFx.SpeedBurst(0.8f, sk.Duration);
            ThirdPersonCamera.Punch(6f, 0.25f);
        }

        /// <summary>All clients: the skill's strikes on schedule, and the readied skill's glow while the pre-motion is held.</summary>
        private void UpdateSwordVisuals()
        {
            if (_playTier >= 0)
            {
                var sk = SwordSkills.All[_playTier];
                float t = Time.time - _playStart;
                while (_playNext < sk.Starts.Length && t >= sk.Starts[_playNext])
                {
                    int move = sk.Moves[_playNext++];
                    if (_anim == null) continue;
                    if (move >= 0) _anim.PlayAttack(AttackKind.Light, move);
                    else _anim.PlayHeavySwing(1f);
                }
                if (_playNext >= sk.Starts.Length) _playTier = -1;
            }
            if (_swordVis == null || !_swordVis.Drawn) return;
            if (_anim == null || !_anim.IsWindingUpHeavy || _playTier >= 0) { _lastPreTier = 0; return; }
            float held = Time.time - (IsOwner ? _heavyChargeStart : _windupSeenAt);
            int tier = SwordSkills.TierFor(held);
            var ready = SwordSkills.All[tier];
            _swordVis.SetGlow(ready.Glow, 0.6f + 1.4f * Mathf.Clamp01(held / SwordSkills.All[SwordSkills.All.Length - 1].MinCharge));
            if (tier > _lastPreTier)
            {
                _swordVis.SetGlow(ready.Glow, 3f, 0.08f);   // flare: the next skill is ready
                if (IsOwner) AnimeScreenFx.SpeedBurst(0.3f, 0.2f);
            }
            _lastPreTier = tier;
        }

        // ---------------- server RPCs ----------------

        private double Now => Time.timeAsDouble;
        private Vector3 ServerChest => transform.position + Vector3.up * 1.3f;

        private static Vector3 SafeDir(Vector3 d, Vector3 fallback)
        {
            if (float.IsNaN(d.x) || d.sqrMagnitude < 1e-4f) return fallback;
            return d.normalized;
        }

        [ServerRpc] private void CmdLight(Vector3 aim, Vector3 lunge) => ServerLight(aim, lunge);
        [ServerRpc] private void CmdHeavyCharge(bool on) => ServerHeavyCharge(on);
        [ServerRpc] private void CmdHeavyRelease(Vector3 aim, float claimedCharge, Vector3 lunge) => ServerHeavyRelease(aim, claimedCharge, lunge);
        [ServerRpc] private void CmdSwordSkill(Vector3 aim, float claimedCharge, Vector3 lunge, byte tier) => ServerSwordSkill(aim, claimedCharge, lunge, tier);
        [ServerRpc] private void CmdSetSword(bool on) => _sword.Value = on;

        /// <summary>A client-claimed attack dash, capped for the move (and NaN-safe).</summary>
        private Vector3 ClampLunge(Vector3 l, float max) =>
            float.IsNaN(l.x) || float.IsNaN(l.y) || float.IsNaN(l.z) ? Vector3.zero : Vector3.ClampMagnitude(l, max * DesignScale + 0.5f);
        [ServerRpc] private void CmdBlast(Vector3 aim) => ServerBlast(aim);
        [ServerRpc] private void CmdBeamCharge(bool on) => ServerBeamCharge(on);
        [ServerRpc] private void CmdBeamFire(Vector3 aim, float claimedCharge) => ServerBeamFire(aim, claimedCharge);
        [ServerRpc] private void CmdSurge() => ServerSurge();

        /// <summary>
        /// Server: the next light combo hit towards aim. The blow lands when its fist / foot / blade arrives (the move's
        /// <see cref="AttackMove.Hit"/> after the swing starts, like the heavy), sweeping the dash from where it started; struck
        /// before then, it is lost (<see cref="ServerLightHits"/>). False when it was refused (down, stunned, too soon).
        /// </summary>
        [Server]
        public bool ServerLight(Vector3 aim, Vector3 lunge = default)
        {
            if (!_stats.ServerCanAct() || Now < _nextLightServer || Now < _serverBusyUntil) return false;
            _nextLightServer = Now + CombatMath.LightAttackInterval * 0.8; // small slack for jitter
            aim = SafeDir(aim, _motor.Forward);
            ComboHit hit = _combo.RegisterHit(Now);
            QueueLightHit(aim, hit, ClampLunge(lunge, LightLunge));
            RpcSwing((byte)AttackKind.Light, (byte)hit.Index);
            return true;
        }

        /// <summary>
        /// Server: start winding up a heavy (true) or call it off (false: hit, guarding, powering up). The swing comes with
        /// <see cref="ServerHeavyRelease"/>; meanwhile everyone else sees the fist pulled back (<see cref="RpcHeavyCharge"/>).
        /// </summary>
        [Server]
        public void ServerHeavyCharge(bool on)
        {
            if (on && (!_stats.ServerCanAct() || Now < _serverBusyUntil)) on = false;
            if (!on && !_serverHeavyCharging) return;
            _serverHeavyCharging = on;
            _serverHeavyStart = Now;
            RpcHeavyCharge(on);
        }

        /// <summary>Server: whether a heavy is being wound up (server-driven fighters).</summary>
        public bool ServerHeavyCharging => _serverHeavyCharging;

        /// <summary>
        /// Server: let go of a wound-up heavy. The charge is the client's claim clamped to what the server measured since
        /// <see cref="ServerHeavyCharge"/> (+ slack) and to <c>heavyMaxChargeTime</c>; damage, knockback and hitstun grow with it.
        /// The hit is resolved when the swing's fist lands (the moveset's <c>Heavy.Swing.Hit</c> after the release), so every
        /// client sees the impact at contact. False when refused (not wound up, can't act, cooldown).
        /// </summary>
        [Server]
        public bool ServerHeavyRelease(Vector3 aim, float claimedCharge, Vector3 lunge = default)
        {
            bool wasCharging = _serverHeavyCharging;
            _serverHeavyCharging = false;
            if (!wasCharging || !_stats.ServerCanAct() || !_heavyCd.TryUse(Now))
            {
                if (wasCharging) RpcHeavyCharge(false);
                return false;
            }
            _combo.Reset();
            _heavyAim = SafeDir(aim, _motor.Forward);
            _heavyFrom = ServerChest;
            _heavySweep = ClampLunge(lunge, HeavyLunge);
            _heavyChargeTime = CombatMath.ValidateCharge(claimedCharge, (float)(Now - _serverHeavyStart), _config.heavyMaxChargeTime);
            var swing = Moveset.Heavy != null ? Moveset.Heavy.Swing : null;
            _heavyHitAt = Now + (swing != null ? swing.Hit : 0f);
            _heavyPending = true;
            float charge01 = CombatMath.Charge01(_heavyChargeTime, _config.heavyMaxChargeTime);
            RpcSwing((byte)AttackKind.Heavy, (byte)Mathf.RoundToInt(charge01 * 255f));
            return true;
        }

        /// <summary>Server: the released heavy's fist lands now.</summary>
        private void ServerHeavyHit()
        {
            _heavyPending = false;
            if (!_stats.ServerCanAct()) return;   // hit or knocked out before the fist got there: the punch is lost
            float charge01 = CombatMath.Charge01(_heavyChargeTime, _config.heavyMaxChargeTime);
            float damage = DamageCalculator.HeavyDamage(_heavyChargeTime, _config.heavyDamage, _config.heavyMaxDamage, _config.heavyMaxChargeTime);
            float stun = Mathf.Lerp(_config.heavyHitstun, _config.heavyMaxHitstun, charge01);
            var swing = Moveset.Heavy != null ? Moveset.Heavy.Swing : null;
            Vector3 strike = swing != null ? StrikePoint(_heavyAim, swing) : ToWorld(_heavyAim, new Vector3(0.02f, 1.32f, 1.30f));
            MeleeHits(_heavyAim, damage, stun, CombatMath.HeavyKnockback(_heavyAim, charge01), HitKind.Heavy, strike, charge01, _heavySweep, _heavyFrom);
        }

        private AttackMove SkillMove(SwordSkill sk, int i) => sk.Moves[i] >= 0 ? Moveset.Move(sk.Moves[i]) : Moveset.Heavy?.Swing;

        /// <summary>
        /// Server: a sword skill fired from the held pre-motion (sword stance). The tier is the client's pick capped by the
        /// pre-motion the server measured; each strike resolves when its blade lands; no attacks until the post-motion delay
        /// is over. False when refused (not winding up, sheathed, can't act, cooldown, energy).
        /// </summary>
        [Server]
        public bool ServerSwordSkill(Vector3 aim, float claimedCharge, Vector3 lunge, int tier)
        {
            bool wasCharging = _serverHeavyCharging;
            _serverHeavyCharging = false;
            float charge = CombatMath.ValidateCharge(claimedCharge, (float)(Now - _serverHeavyStart), _config.heavyMaxChargeTime);
            tier = Mathf.Clamp(Mathf.Min(tier, SwordSkills.TierFor(charge + 0.1f)), 0, SwordSkills.All.Length - 1);
            var sk = SwordSkills.All[tier];
            if (!wasCharging || !_sword.Value || !_stats.ServerCanAct() || Now < _skillReadyServer || Now < _serverBusyUntil
                || !_stats.ServerHasEnergy(sk.Energy))
            {
                if (wasCharging) RpcHeavyCharge(false);
                return false;
            }
            _stats.ServerTrySpendEnergy(sk.Energy);
            _combo.Reset();
            _skillTier = tier;
            _skillAim = SafeDir(aim, _motor.Forward);
            _skillFrom = ServerChest;
            _skillLunge = ClampLunge(lunge, sk.MaxLunge);
            _skillReadyServer = Now + sk.Cooldown - 0.15;
            _serverBusyUntil = Now + sk.Duration + sk.PostDelay * 0.8f;
            _skillHits.Clear();
            for (int i = 0; i < sk.Starts.Length; i++)
            {
                AttackMove m = SkillMove(sk, i);
                _skillHits.Add(new SkillHit { At = Now + sk.Starts[i] + (m != null ? m.Hit : 0.1f), Index = i });
            }
            RpcSwordSkill((byte)tier);
            return true;
        }

        /// <summary>Server: strike <paramref name="i"/> of the running sword skill lands now.</summary>
        private void ServerSkillHit(int i)
        {
            if (!_stats.ServerCanAct()) { _skillHits.Clear(); return; }   // struck out of the skill: the rest is lost
            var sk = SwordSkills.All[_skillTier];
            Vector3 kb;
            HitKind kind;
            float stun, impact = 0f;
            if (i < sk.Starts.Length - 1)
            {
                kb = CombatMath.Knockback(_skillAim, CombatMath.LightKnockbackSpeed, 0f);
                kind = HitKind.Light;
                stun = _config.lightHitstun + 0.2f;   // held in the string until the last strike
            }
            else if (sk.FinalLaunch > 0f)
            {
                impact = sk.FinalLaunch;
                kb = CombatMath.HeavyKnockback(_skillAim, impact);
                kind = HitKind.Heavy;
                stun = Mathf.Lerp(_config.heavyHitstun, _config.heavyMaxHitstun, impact);
            }
            else
            {
                kb = CombatMath.Knockback(_skillAim, CombatMath.FinisherKnockbackSpeed, CombatMath.FinisherLift);
                kind = HitKind.Finisher;
                stun = _config.heavyHitstun;
            }
            AttackMove m = SkillMove(sk, i);
            Vector3 strike = m != null ? StrikePoint(_skillAim, m) : ServerChest + _skillAim;
            // the first strike sweeps the whole dash from where the skill started; the rest land where the fighter is now
            if (i == 0) MeleeHits(_skillAim, sk.Damage[i], stun, kb, kind, strike, impact, _skillLunge, _skillFrom);
            else MeleeHits(_skillAim, sk.Damage[i], stun, kb, kind, strike, impact, _skillLunge * 0.25f);
        }

        /// <summary>Design height scale of this fighter (2.3 m = 1), from the capsule (set from the hero's height everywhere).</summary>
        private float DesignScale => _cc != null ? _cc.height / 2.3f : 1f;

        private Vector3 ToWorld(Vector3 aim, Vector3 designLocal)
        {
            Vector3 flat = new Vector3(aim.x, 0f, aim.z);
            Quaternion yaw = flat.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(flat) : transform.rotation;
            return transform.position + yaw * (designLocal * DesignScale);
        }

        /// <summary>
        /// Where the striking fist / foot of combo hit <paramref name="comboIndex"/> is at the move's impact frame (world), from
        /// this fighter's moveset: a jab lands on the head, a body blow on the belly, a kick on the legs.
        /// </summary>
        public Vector3 StrikePoint(Vector3 aim, int comboIndex) => StrikePoint(aim, Moveset.Move(comboIndex));

        /// <summary>Where the striking fist / foot of a move is at its impact frame (<see cref="AttackMove.Hit"/>), in the world.</summary>
        public Vector3 StrikePoint(Vector3 aim, AttackMove m)
        {
            m.Sample(m.Hit, _strikePose);
            Vector3 limb = m.Limb switch
            {
                StrikeLimb.HandL => _strikePose.ArmL,
                StrikeLimb.HandR => _strikePose.ArmR,
                StrikeLimb.FootL => _strikePose.LegL,
                _ => _strikePose.LegR,
            };
            return ToWorld(aim, _strikePose.RootRotation * limb + _strikePose.RootPos);
        }

        /// <summary>
        /// Resolve a melee blow on everyone in the strike sphere, swept along <paramref name="sweep"/> (the attack's dash, so a
        /// lunging blow lands whether or not the server has moved the attacker yet) from <paramref name="from"/> (default:
        /// the chest now). The sword reaches further.
        /// </summary>
        private void MeleeHits(Vector3 aim, float damage, float stun, Vector3 knockback, HitKind kind, Vector3 strike, float impact = 0f,
                               Vector3 sweep = default, Vector3? from = null)
        {
            bool sword = _sword.Value;
            float reach = _config.meleeHitForwardOffset + (sword ? 0.7f * DesignScale : 0f);
            float radius = _config.meleeHitRadius + (sword ? 0.25f : 0f);
            Vector3 center = CombatMath.MeleeHitCenter(from ?? ServerChest, aim, reach, _motor.IsFlying);
            int n = Physics.OverlapCapsuleNonAlloc(center, center + sweep, radius, _overlap, ~0, QueryTriggerInteraction.Ignore);
            _hitSet.Clear();
            for (int i = 0; i < n; i++)
            {
                var target = _overlap[i].GetComponentInParent<PlayerStats>();
                if (target == null || target == _stats || !_hitSet.Add(target)) continue;
                // the spot on the victim nearest the striking fist / foot (so the reaction matches where it landed)
                Vector3 point = _overlap[i].ClosestPoint(strike);
                target.ServerApplyHit(_stats, damage, stun, knockback, point, kind, impact);
            }
        }

        [Server]
        public bool ServerBlast(Vector3 aim)
        {
            var d = Def(AttackKind.Blast);
            if (d == null || !_stats.ServerCanAct() || !_stats.ServerHasEnergy(d.energyCost) || !_stats.ServerHasAura(d.auraCost) || !_blastCd.TryUse(Now)) return false;
            _stats.ServerTrySpendEnergy(d.energyCost);
            _stats.ServerTrySpendAura(d.auraCost);
            aim = SafeDir(aim, _motor.Forward);
            Vector3 origin = ServerChest + aim * 0.8f;
            _projectiles.Add(new Projectile { Pos = origin, Dir = aim, Speed = d.speed, Life = d.duration, Radius = d.radius, Damage = d.damageMax });
            RpcBlast(origin, aim);
            return true;
        }

        [Server]
        public void ServerBeamCharge(bool on)
        {
            _serverBeamCharging = on;
            _serverBeamStart = Now;
            RpcBeamCharge(on);
        }

        /// <summary>Server: whether a beam is being charged (server-driven fighters).</summary>
        public bool ServerBeamCharging => _serverBeamCharging;

        [Server]
        public void ServerBeamFire(Vector3 aim, float claimedCharge)
        {
            var d = Def(AttackKind.Beam);
            bool wasCharging = _serverBeamCharging;
            _serverBeamCharging = false;
            if (d == null || !wasCharging || !_stats.ServerCanAct() || !_stats.ServerHasEnergy(d.energyCost) || !_stats.ServerHasAura(d.auraCost) || !_beamCd.TryUse(Now))
            {
                RpcBeamCharge(false);
                return;
            }
            _stats.ServerTrySpendEnergy(d.energyCost);
            _stats.ServerTrySpendAura(d.auraCost);
            aim = SafeDir(aim, _motor.Forward);
            float charge = CombatMath.ValidateCharge(claimedCharge, (float)(Now - _serverBeamStart), d.maxChargeTime);
            float damage = d.DamageForCharge(charge);
            Vector3 origin = ServerChest + aim * 0.6f;
            float len = BeamLength(origin, aim, d.radius, d.range, out RaycastHit[] hits);
            _hitSet.Clear();
            foreach (var h in hits)
            {
                if (h.distance > len) continue;
                var target = h.collider.GetComponentInParent<PlayerStats>();
                if (target == null || target == _stats || !_hitSet.Add(target)) continue;
                Vector3 point = h.distance > 0f ? h.point : target.transform.position + Vector3.up * 1.3f;
                target.ServerApplyHit(_stats, damage, _config.heavyHitstun,
                    CombatMath.Knockback(aim, CombatMath.BeamKnockbackSpeed, 3f), point, HitKind.Beam);
            }
            Debug.Log("[Combat] " + _stats.DisplayName + " fired beam charge=" + charge.ToString("F2") + "s dmg=" + damage.ToString("F0") + " len=" + len.ToString("F1"));
            RpcBeam(origin, aim, len, d.maxChargeTime > 0f ? charge / d.maxChargeTime : 1f);
        }

        /// <summary>Beam length = distance to the first non-player collider (or range). Returns all hits.</summary>
        private float BeamLength(Vector3 origin, Vector3 dir, float radius, float range, out RaycastHit[] hits)
        {
            hits = Physics.SphereCastAll(origin, radius, dir, range, ~0, QueryTriggerInteraction.Ignore);
            float len = range;
            foreach (var h in hits)
            {
                if (h.collider.GetComponentInParent<PlayerStats>() != null) continue;
                if (h.distance > 0f && h.distance < len) len = h.distance;
            }
            return len;
        }

        [Server]
        public bool ServerSurge()
        {
            var d = Def(AttackKind.Surge);
            if (d == null || !_stats.ServerCanAct() || _stats.SurgeActive || !_stats.ServerHasEnergy(d.energyCost) || !_stats.ServerHasAura(d.auraCost) || !_surgeCd.IsReadyAt(Now)) return false;
            _stats.ServerTrySpendEnergy(d.energyCost);
            _stats.ServerTrySpendAura(d.auraCost);
            if (_stats.ServerActivateSurge(d)) _surgeCd.TryUse(Now);
            RpcSwing((byte)AttackKind.Surge, 0);
            return true;
        }

        // ---------------- server projectile simulation ----------------

        private void ServerTick()
        {
            if (_lightHits.Count > 0) ServerLightHits();
            if (_heavyPending && Now >= _heavyHitAt) ServerHeavyHit();
            while (_skillHits.Count > 0 && Now >= _skillHits[0].At)
            {
                int strikeIndex = _skillHits[0].Index;
                _skillHits.RemoveAt(0);
                ServerSkillHit(strikeIndex);
            }
            if (_projectiles.Count == 0) return;
            float dt = (float)TimeManager.TickDelta;
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                Projectile p = _projectiles[i];
                float step = p.Speed * dt;
                bool done = false;
                var hits = Physics.SphereCastAll(p.Pos, p.Radius, p.Dir, step, ~0, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                RaycastHit bestHit = default;
                foreach (var h in hits)
                {
                    var ps = h.collider.GetComponentInParent<PlayerStats>();
                    if (ps == _stats) continue;
                    if (ps != null && ps.ServerIsEvading)
                    {
                        // dodged: the blast flies on through (ServerApplyHit only plays the dodge)
                        ps.ServerApplyHit(_stats, 0f, 0f, Vector3.zero, h.distance > 0f ? h.point : p.Pos, HitKind.Blast);
                        continue;
                    }
                    if (h.distance < best) { best = h.distance; bestHit = h; }
                }
                if (best < float.MaxValue)
                {
                    var target = bestHit.collider.GetComponentInParent<PlayerStats>();
                    Vector3 point = best > 0f ? bestHit.point : p.Pos;
                    if (target != null)
                        target.ServerApplyHit(_stats, p.Damage, _config.lightHitstun,
                            CombatMath.Knockback(p.Dir, CombatMath.BlastKnockbackSpeed, 0f), point, HitKind.Blast);
                    done = true;
                }
                p.Pos += p.Dir * step;
                p.Life -= dt;
                if (done || p.Life <= 0f) _projectiles.RemoveAt(i);
                else _projectiles[i] = p;
            }
        }

        // ---------------- observers (other clients) ----------------

        /// <summary>Everyone else: play the attack. For the heavy, index = its charge (0..255): the swing from the wind-up.</summary>
        [ObserversRpc(ExcludeOwner = true)]
        private void RpcSwing(byte kind, byte index)
        {
            if (_anim != null) _anim.PlayAttack((AttackKind)kind, index);
        }

        /// <summary>Everyone else: this fighter started (true) or called off (false) winding up a heavy.</summary>
        [ObserversRpc(ExcludeOwner = true)]
        private void RpcHeavyCharge(bool on)
        {
            if (IsOwner || _anim == null) return;
            if (on)
            {
                _anim.PlayHeavyWindUp();
                _windupSeenAt = Time.time;
            }
            else _anim.CancelHeavy();
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void RpcBlast(Vector3 origin, Vector3 dir)
        {
            var d = Def(AttackKind.Blast);
            if (_anim != null) _anim.PlayAttack(AttackKind.Blast, 0);
            CombatFx.SpawnBlastVisual(this, origin, dir, d.speed, d.duration, d.radius);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void RpcBeamCharge(bool on)
        {
            if (IsOwner) return;
            _beamChargeStart = Time.time;
            _beamCharging = on;
            SetChargeVisual(on);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void RpcBeam(Vector3 origin, Vector3 dir, float length, float charge01)
        {
            if (IsOwner) return;
            _beamCharging = false;
            SetChargeVisual(false);
            if (_anim != null) _anim.PlayAttack(AttackKind.Beam, 0);
            CombatFx.SpawnBeamVisual(origin, dir, length, charge01);
        }

        /// <summary>Everyone else: this fighter fired sword skill <paramref name="tier"/>.</summary>
        [ObserversRpc(ExcludeOwner = true)]
        private void RpcSwordSkill(byte tier)
        {
            if (IsOwner || tier >= SwordSkills.All.Length) return;
            PlaySkill(tier);
        }
    }
}
