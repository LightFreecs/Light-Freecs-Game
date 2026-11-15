using System;
using System.Collections.Generic;
using System.Globalization;
using FishNet.Component.Spawning;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using LightMMORPG.Config;
using LightMMORPG.Core;
using LightMMORPG.Player;
using UnityEngine;

namespace LightMMORPG.Combat
{
    public enum HitKind : byte { Light, Finisher, Heavy, Blast, Beam }

    /// <summary>
    /// Server-authoritative fighter stats. The server owns the Core pools (<see cref="HealthPool"/>, <see cref="EnergyPool"/>,
    /// <see cref="GuardMeter"/>, <see cref="BuffTimer"/>) and mirrors them into SyncVars every tick; clients only read.
    /// Energy regen / hold-to-charge (the motor's replicated Charge input), guard recovery, Power Surge, KO → down →
    /// respawn and the KO/death score all run here. Name and palette live on <see cref="PlayerIdentity"/>.
    /// </summary>
    public sealed class PlayerStats : NetworkBehaviour
    {
        public static readonly List<PlayerStats> All = new();
        /// <summary>Client side, every machine: (victim, attacker or null, point, health damage, blocked, guard broken, kind).</summary>
        public static event Action<PlayerStats, PlayerStats, Vector3, float, bool, bool, HitKind> OnHitFeedback;
        /// <summary>Client side, every machine: (victim, attacker or null).</summary>
        public static event Action<PlayerStats, PlayerStats> OnKnockout;

        public readonly SyncVar<float> Health = new(CombatConstants.MaxHealth, new SyncTypeSettings(0f));
        public readonly SyncVar<float> Energy = new(CombatConstants.MaxEnergy, new SyncTypeSettings(0.1f));
        public readonly SyncVar<float> Guard = new(CombatConstants.MaxGuard, new SyncTypeSettings(0.1f));
        public readonly SyncVar<bool> GuardBroken = new(false, new SyncTypeSettings(0f));
        public readonly SyncVar<float> SurgeRemaining = new(0f, new SyncTypeSettings(0.25f));
        public readonly SyncVar<bool> IsDown = new(false, new SyncTypeSettings(0f));
        public readonly SyncVar<float> DownRemaining = new(0f, new SyncTypeSettings(0.2f));
        public readonly SyncVar<int> Kos = new(0, new SyncTypeSettings(0f));
        public readonly SyncVar<int> Deaths = new(0, new SyncTypeSettings(0f));

        public static PlayerStats Local { get; private set; }

        public PlayerIdentity Identity { get; private set; }
        public PlayerMotor Motor { get; private set; }
        public string DisplayName => Identity != null ? Identity.DisplayName.Value : name;
        public bool SurgeActive => SurgeRemaining.Value > 0f;
        public float MaxHealth => _config.maxHealth;
        public float MaxEnergy => _config.maxEnergy;
        public float MaxGuard => _config.maxGuard;
        public float SurgeDuration => _config.powerSurge != null ? _config.powerSurge.duration : CombatConstants.SurgeDuration;

        private GameConfig _config;
        private HealthPool _health;
        private EnergyPool _energy;
        private GuardMeter _guard;
        private readonly BuffTimer _surge = new();
        private float _surgeDamageMultiplier = CombatConstants.SurgeDamageMultiplier;
        private bool _surgePausesRegen = true;
        private PlayerStats _lastAttacker;
        private int _lastSpawn = -1;
        private static bool _verbose;

        private void Awake()
        {
            _config = GameConfig.Load();
            Identity = GetComponent<PlayerIdentity>();
            Motor = GetComponent<PlayerMotor>();
            _health = new HealthPool(_config.maxHealth, _config.knockedOutDuration);
            _energy = new EnergyPool(_config.maxEnergy, _config.energyRegenPerSecond, _config.energyChargePerSecond);
            _guard = new GuardMeter(_config.maxGuard, CombatConstants.GuardRecoverPerSecond, _config.guardBreakStun);
            _verbose = Array.IndexOf(Environment.GetCommandLineArgs(), "-netlog") >= 0;
            Health.OnChange += OnHealthChanged;
        }

        public override void OnStartNetwork()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        public override void OnStopNetwork()
        {
            All.Remove(this);
            if (Local == this) Local = null;
        }

        public override void OnStartClient()
        {
            if (IsOwner) Local = this;
        }

        public override void OnStartServer()
        {
            TimeManager.OnTick += ServerTick;
            PushAll();
        }

        public override void OnStopServer()
        {
            if (TimeManager != null) TimeManager.OnTick -= ServerTick;
        }

        private void OnHealthChanged(float prev, float next, bool asServer)
        {
            // Client-side sync log for the smoke test (non-host client only, so it proves replication).
            if (_verbose && !asServer && !IsServerStarted && Mathf.Abs(prev - next) > 0.01f)
                Debug.Log("[Sync] " + DisplayName + " hp " + prev.ToString("F0", CultureInfo.InvariantCulture) + " -> " + next.ToString("F0", CultureInfo.InvariantCulture));
        }

        // ---------------- server ----------------

        private void ServerTick()
        {
            float dt = (float)TimeManager.TickDelta;
            if (_health.IsKnockedOut)
            {
                if (_health.Tick(dt)) ServerRespawn();
                else
                {
                    Motor.ServerApplyKnockback(Vector3.zero, _health.DownRemaining + 0.2f); // keep them on the floor
                    DownRemaining.Value = _health.DownRemaining;
                }
                return;
            }

            if (_surge.Tick(dt)) EndSurge();
            _energy.RegenPaused = _surge.IsActive && _surgePausesRegen;
            _energy.Tick(dt, Motor != null && Motor.IsCharging);
            _guard.Tick(dt, Motor != null && Motor.IsBlocking);
            PushAll();
        }

        private void PushAll()
        {
            Health.Value = _health.Current;
            Energy.Value = _energy.Current;
            Guard.Value = _guard.Current;
            GuardBroken.Value = _guard.IsBroken;
            SurgeRemaining.Value = _surge.Remaining;
            IsDown.Value = _health.IsKnockedOut;
            DownRemaining.Value = _health.DownRemaining;
        }

        [Server] public bool ServerCanAct() => !_health.IsKnockedOut && (Motor == null || !Motor.IsStunned);
        [Server] public bool ServerHasEnergy(float amount) => _energy.CanSpend(amount);

        [Server]
        public bool ServerTrySpendEnergy(float amount)
        {
            if (!_energy.TrySpend(amount)) return false;
            Energy.Value = _energy.Current;
            return true;
        }

        [Server]
        public bool ServerActivateSurge(AbilityDefinition def)
        {
            if (_surge.IsActive || _health.IsKnockedOut) return false;
            _surge.Activate(def.duration);
            _surgeDamageMultiplier = def.damageMultiplier;
            _surgePausesRegen = def.pausesEnergyRegen;
            Motor.ServerSetSpeedMultiplier(def.speedMultiplier);
            SurgeRemaining.Value = _surge.Remaining;
            Debug.Log("[Combat] " + DisplayName + " Power Surge ON (" + def.duration + " s)");
            return true;
        }

        private void EndSurge()
        {
            _surge.Clear();
            if (Motor != null) Motor.ServerSetSpeedMultiplier(1f);
            SurgeRemaining.Value = 0f;
        }

        /// <summary>
        /// Server: resolve one hit on this player. Applies surge (attacker) and block (this player), guard drain/break,
        /// hitstun + knockback through the motor, KO. Returns false if the hit was ignored (already down).
        /// </summary>
        [Server]
        public bool ServerApplyHit(PlayerStats attacker, float baseDamage, float hitstun, Vector3 knockback, Vector3 point, HitKind kind)
        {
            if (_health.IsKnockedOut) return false;
            bool surging = attacker != null && attacker._surge.IsActive;
            float dmgBase = surging ? baseDamage * attacker._surgeDamageMultiplier / CombatConstants.SurgeDamageMultiplier : baseDamage;
            bool blocking = Motor != null && Motor.IsBlocking && !_guard.IsBroken;
            DamageResult r = DamageCalculator.Calculate(dmgBase, surging, blocking);

            bool guardBroke = false;
            Vector3 flat = new Vector3(knockback.x, 0f, knockback.z);
            if (blocking)
            {
                guardBroke = _guard.Drain(r.GuardDamage);
                if (guardBroke) Motor.ServerApplyKnockback(flat.normalized * CombatMath.BlockedPushSpeed * 1.5f, _guard.BreakStunDuration);
                else Motor.ServerApplyKnockback(flat.normalized * CombatMath.BlockedPushSpeed, 0f);
            }
            else if (Motor != null) Motor.ServerApplyKnockback(knockback, hitstun);

            if (attacker != null && attacker != this) _lastAttacker = attacker;
            bool ko = _health.ApplyDamage(r.HealthDamage);
            PushAll();

            Debug.Log("[Combat] " + (attacker != null ? attacker.DisplayName : "?") + " hit " + DisplayName + " " + kind +
                      " dmg=" + r.HealthDamage.ToString("F0", CultureInfo.InvariantCulture) + (blocking ? " (blocked)" : "") +
                      (guardBroke ? " GUARD BREAK" : "") + " hp=" + _health.Current.ToString("F0", CultureInfo.InvariantCulture));

            byte flags = (byte)((blocking ? 1 : 0) | (guardBroke ? 2 : 0));
            RpcHitFeedback(attacker != null ? attacker.NetworkObject : null, point, r.HealthDamage, flags, (byte)kind);
            if (ko) ServerKnockout();
            return true;
        }

        private void ServerKnockout()
        {
            EndSurge();
            Deaths.Value++;
            PlayerStats killer = _lastAttacker;
            if (killer != null && killer != this && killer.IsSpawned) killer.Kos.Value++;
            Motor.ServerApplyKnockback(Vector3.zero, _health.DownRemaining + 0.2f);
            PushAll();
            Debug.Log("[Combat] KO! " + DisplayName + " knocked out by " + (killer != null ? killer.DisplayName : "?") +
                      " — score " + (killer != null ? killer.DisplayName + " KOs=" + killer.Kos.Value + ", " : "") + DisplayName + " deaths=" + Deaths.Value);
            RpcKnockout(killer != null ? killer.NetworkObject : null);
            _lastAttacker = null;
        }

        private void ServerRespawn()
        {
            _health.Reset();
            _energy.Refill();
            _guard.Reset();
            EndSurge();
            Vector3 pos = transform.position + Vector3.up;
            var spawner = FindAnyObjectByType<PlayerSpawner>();
            if (spawner != null && spawner.Spawns != null && spawner.Spawns.Length > 0)
            {
                int i = CombatMath.PickSpawn(spawner.Spawns.Length, UnityEngine.Random.value, _lastSpawn);
                _lastSpawn = i;
                if (spawner.Spawns[i] != null) pos = spawner.Spawns[i].position;
            }
            Motor.ServerTeleport(pos);
            PushAll();
            Debug.Log("[Combat] Respawn " + DisplayName + " at (" + pos.x.ToString("F1", CultureInfo.InvariantCulture) + ", " +
                      pos.z.ToString("F1", CultureInfo.InvariantCulture) + ") hp=" + _health.Current);
        }

        // ---------------- observers ----------------

        [ObserversRpc]
        private void RpcHitFeedback(NetworkObject attacker, Vector3 point, float damage, byte flags, byte kind)
        {
            PlayerStats a = attacker != null ? attacker.GetComponent<PlayerStats>() : null;
            OnHitFeedback?.Invoke(this, a, point, damage, (flags & 1) != 0, (flags & 2) != 0, (HitKind)kind);
        }

        [ObserversRpc]
        private void RpcKnockout(NetworkObject attacker)
        {
            PlayerStats a = attacker != null ? attacker.GetComponent<PlayerStats>() : null;
            OnKnockout?.Invoke(this, a);
        }
    }
}
