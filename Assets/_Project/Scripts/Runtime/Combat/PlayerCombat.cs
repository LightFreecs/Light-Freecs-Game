using System.Collections.Generic;
using FishNet.Object;
using LightMMORPG.Cameras;
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
    /// </summary>
    public sealed class PlayerCombat : NetworkBehaviour
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

        // ---- server state ----
        private ComboChain _combo;
        private double _nextLightServer;
        private Cooldown _heavyCd, _blastCd, _beamCd, _surgeCd;
        private bool _serverBeamCharging;
        private double _serverBeamStart;
        private struct Projectile { public Vector3 Pos, Dir; public float Speed, Life, Radius, Damage; }
        private readonly List<Projectile> _projectiles = new();
        private readonly Collider[] _overlap = new Collider[16];
        private readonly HashSet<PlayerStats> _hitSet = new();

        // ---- visuals ----
        private BeamChargeVisual _chargeVisual;

        public bool IsChargingBeam => _beamCharging;
        public float BeamCharge01 => _beamCharging && _config.chargedBeam != null && _config.chargedBeam.maxChargeTime > 0f
            ? Mathf.Clamp01((Time.time - _beamChargeStart) / _config.chargedBeam.maxChargeTime) : 0f;

        private void Awake()
        {
            _config = GameConfig.Load();
            _stats = GetComponent<PlayerStats>();
            _motor = GetComponent<PlayerMotor>();
            _input = GetComponent<PlayerInputReader>();
            _anim = GetComponent<PlayerProceduralAnimator>();
            _combo = new ComboChain(_config.lightComboDamage, _config.comboWindow);
            _heavyCd = new Cooldown(_config.heavyCooldown);
            _blastCd = new Cooldown(Def(AttackKind.Blast)?.cooldown ?? 0.35f);
            _beamCd = new Cooldown(Def(AttackKind.Beam)?.cooldown ?? 4f);
            _surgeCd = new Cooldown(Def(AttackKind.Surge)?.cooldown ?? 20f);
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
                case AttackKind.Heavy: ready = _heavyReadyLocal; dur = _config.heavyCooldown; break;
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
            if (!IsOwner || _input == null || !_input.InputEnabled) return;
            bool canAct = !_stats.IsDown.Value && !_motor.IsStunned;
            float now = Time.time;

            if (_beamCharging && !canAct) CancelBeam();

            if (!canAct) return;

            if (_input.Pressed(GameAction.Light) && now >= _nextLightLocal)
            {
                _nextLightLocal = now + CombatMath.LightAttackInterval;
                if (now - _localComboTime > _config.comboWindow) _localComboIndex = 0;
                int idx = _localComboIndex;
                _localComboIndex = (idx + 1) % Mathf.Max(1, _config.lightComboDamage.Length);
                _localComboTime = now;
                _motor.FaceAimFor(0.6f);
                if (_anim != null) _anim.PlayAttack(AttackKind.Light, idx);
                CmdLight(Aim());
            }
            else if (_input.Pressed(GameAction.Heavy) && now >= _heavyReadyLocal)
            {
                _heavyReadyLocal = now + _config.heavyCooldown;
                _motor.FaceAimFor(0.8f);
                if (_anim != null) _anim.PlayAttack(AttackKind.Heavy, 0);
                CmdHeavy(Aim());
            }

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

        private void SetChargeVisual(bool on)
        {
            if (on)
            {
                if (_chargeVisual == null) _chargeVisual = BeamChargeVisual.Create(this);
                _chargeVisual.Begin();
            }
            else if (_chargeVisual != null) _chargeVisual.End();
        }

        // ---------------- server RPCs ----------------

        private double Now => Time.timeAsDouble;
        private Vector3 ServerChest => transform.position + Vector3.up * 1.3f;

        private static Vector3 SafeDir(Vector3 d, Vector3 fallback)
        {
            if (float.IsNaN(d.x) || d.sqrMagnitude < 1e-4f) return fallback;
            return d.normalized;
        }

        [ServerRpc]
        private void CmdLight(Vector3 aim)
        {
            if (!_stats.ServerCanAct() || Now < _nextLightServer) return;
            _nextLightServer = Now + CombatMath.LightAttackInterval * 0.8; // small slack for jitter
            aim = SafeDir(aim, _motor.Forward);
            ComboHit hit = _combo.RegisterHit(Now);
            HitKind kind = hit.Knockback ? HitKind.Finisher : HitKind.Light;
            Vector3 kb = hit.Knockback
                ? CombatMath.Knockback(aim, CombatMath.FinisherKnockbackSpeed, CombatMath.FinisherLift)
                : CombatMath.Knockback(aim, CombatMath.LightKnockbackSpeed, 0f);
            float stun = hit.Knockback ? _config.heavyHitstun : _config.lightHitstun;
            MeleeHits(aim, hit.Damage, stun, kb, kind);
            RpcSwing((byte)AttackKind.Light, (byte)hit.Index);
        }

        [ServerRpc]
        private void CmdHeavy(Vector3 aim)
        {
            if (!_stats.ServerCanAct() || !_heavyCd.TryUse(Now)) return;
            aim = SafeDir(aim, _motor.Forward);
            _combo.Reset();
            MeleeHits(aim, _config.heavyDamage, _config.heavyHitstun,
                CombatMath.Knockback(aim, CombatMath.HeavyKnockbackSpeed, CombatMath.HeavyLift), HitKind.Heavy);
            RpcSwing((byte)AttackKind.Heavy, 0);
        }

        private void MeleeHits(Vector3 aim, float damage, float stun, Vector3 knockback, HitKind kind)
        {
            Vector3 center = CombatMath.MeleeHitCenter(ServerChest, aim, _config.meleeHitForwardOffset, _motor.IsFlying);
            int n = Physics.OverlapSphereNonAlloc(center, _config.meleeHitRadius, _overlap, ~0, QueryTriggerInteraction.Ignore);
            _hitSet.Clear();
            for (int i = 0; i < n; i++)
            {
                var target = _overlap[i].GetComponentInParent<PlayerStats>();
                if (target == null || target == _stats || !_hitSet.Add(target)) continue;
                Vector3 point = _overlap[i].ClosestPoint(center);
                target.ServerApplyHit(_stats, damage, stun, knockback, point, kind);
            }
        }

        [ServerRpc]
        private void CmdBlast(Vector3 aim)
        {
            var d = Def(AttackKind.Blast);
            if (d == null || !_stats.ServerCanAct() || !_stats.ServerHasEnergy(d.energyCost) || !_stats.ServerHasAura(d.auraCost) || !_blastCd.TryUse(Now)) return;
            _stats.ServerTrySpendEnergy(d.energyCost);
            _stats.ServerTrySpendAura(d.auraCost);
            aim = SafeDir(aim, _motor.Forward);
            Vector3 origin = ServerChest + aim * 0.8f;
            _projectiles.Add(new Projectile { Pos = origin, Dir = aim, Speed = d.speed, Life = d.duration, Radius = d.radius, Damage = d.damageMax });
            RpcBlast(origin, aim);
        }

        [ServerRpc]
        private void CmdBeamCharge(bool on)
        {
            _serverBeamCharging = on;
            _serverBeamStart = Now;
            RpcBeamCharge(on);
        }

        [ServerRpc]
        private void CmdBeamFire(Vector3 aim, float claimedCharge)
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

        [ServerRpc]
        private void CmdSurge()
        {
            var d = Def(AttackKind.Surge);
            if (d == null || !_stats.ServerCanAct() || _stats.SurgeActive || !_stats.ServerHasEnergy(d.energyCost) || !_stats.ServerHasAura(d.auraCost) || !_surgeCd.IsReadyAt(Now)) return;
            _stats.ServerTrySpendEnergy(d.energyCost);
            _stats.ServerTrySpendAura(d.auraCost);
            if (_stats.ServerActivateSurge(d)) _surgeCd.TryUse(Now);
            RpcSwing((byte)AttackKind.Surge, 0);
        }

        // ---------------- server projectile simulation ----------------

        private void ServerTick()
        {
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

        [ObserversRpc(ExcludeOwner = true)]
        private void RpcSwing(byte kind, byte index)
        {
            if (_anim != null) _anim.PlayAttack((AttackKind)kind, index);
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
    }
}
