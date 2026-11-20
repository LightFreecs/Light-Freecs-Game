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
    /// Energy regen / hold-to-charge (the motor's replicated Charge input; it also raises the power level, <see cref="AuraReserve"/>),
    /// the aura reserve, guard recovery, Power Surge, KO → down →
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
        /// <summary>Power level (share of full strength, 0.1 at the start of a battle; hold Charge to raise it). See <see cref="AuraReserve"/>.</summary>
        public readonly SyncVar<float> Power = new(CombatConstants.StartPower, new SyncTypeSettings(0.1f));
        /// <summary>Aura reserve left (never refills until the next battle / respawn).</summary>
        public readonly SyncVar<float> Aura = new(CombatConstants.AuraReserve, new SyncTypeSettings(0.1f));
        /// <summary>0 while there is aura left, then rising to 1: strength and defence sink.</summary>
        public readonly SyncVar<float> Exhaustion = new(0f, new SyncTypeSettings(0.25f));
        public readonly SyncVar<bool> IsDown = new(false, new SyncTypeSettings(0f));
        public readonly SyncVar<float> DownRemaining = new(0f, new SyncTypeSettings(0.2f));
        public readonly SyncVar<int> Kos = new(0, new SyncTypeSettings(0f));
        public readonly SyncVar<int> Deaths = new(0, new SyncTypeSettings(0f));
        /// <summary>Account level shown next to the name (0 = none, e.g. the training dummy; guests are 1).</summary>
        public readonly SyncVar<int> Level = new(0);
        /// <summary>The fighter's validated stat points (<see cref="StatAllocation.Pack"/>); everyone derives <see cref="Numbers"/> from it.</summary>
        public readonly SyncVar<int> Build = new(0);

        public static PlayerStats Local { get; private set; }

        public PlayerIdentity Identity { get; private set; }
        public PlayerMotor Motor { get; private set; }
        public string DisplayName => Identity != null ? Identity.DisplayName.Value : name;
        public bool SurgeActive => SurgeRemaining.Value > 0f;
        /// <summary>Combat numbers from the stat build (all 1 for an unallocated fighter). Same on server and clients.</summary>
        public FighterNumbers Numbers { get; private set; } = FighterNumbers.Base;
        public float MaxHealth => Numbers.MaxHealth(_config.maxHealth);
        public float MaxEnergy => Numbers.MaxEnergy(_config.maxEnergy);
        public float MaxGuard => Numbers.MaxGuard(_config.maxGuard);
        public float MaxAura => Numbers.MaxAura();
        /// <summary>Damage dealt multiplier from the power level and exhaustion (synced values, same everywhere).</summary>
        public float Strength => AuraReserve.Strength(Power.Value, Exhaustion.Value);
        public bool AuraDepleted => Aura.Value <= 0f;
        /// <summary>"Name  Lv 12" for the HUD (just the name when the level is unknown).</summary>
        public string NameWithLevel => Level.Value > 0 ? DisplayName + "  Lv " + Level.Value : DisplayName;
        public float SurgeDuration => _config.powerSurge != null ? _config.powerSurge.duration : CombatConstants.SurgeDuration;

        private GameConfig _config;
        private HealthPool _health;
        private EnergyPool _energy;
        private GuardMeter _guard;
        private readonly BuffTimer _surge = new();
        private readonly AuraReserve _aura = new();
        private float _surgeDamageMultiplier = CombatConstants.SurgeDamageMultiplier;
        private bool _surgePausesRegen = true;
        private PlayerStats _lastAttacker;
        private int _lastSpawn = -1;
        private bool _buildReceived;
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
            Build.OnChange += (_, next, _) => Numbers = FighterStats.Compute(StatAllocation.Unpack(next));
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
            Numbers = FighterStats.Compute(StatAllocation.Unpack(Build.Value)); // late joiners: value already synced
            if (!IsOwner) return;
            Local = this;
            // signed-in players bring their level and stat points; guests keep the base stats
            var account = Accounts.AccountStore.Instance.Current;
            if (account != null) CmdSetBuild(account.Level, account.stats.Pack());
        }

        /// <summary>
        /// Owner → server, once per spawn: account level and stat points. The server never trusts the totals: the level
        /// is clamped to 1–50 and an allocation that does not fit it (over-spent, over the per-stat cap) is dropped.
        /// Accounts are device-local for now, so the level itself is the client's word until there are online accounts.
        /// </summary>
        [ServerRpc]
        private void CmdSetBuild(int level, int packed)
        {
            if (_buildReceived) return; // no re-sending mid-match
            _buildReceived = true;
            level = FighterStats.ClampLevel(level);
            var claimed = StatAllocation.Unpack(packed);
            var build = FighterStats.Validated(level, claimed);
            if (build != claimed) Debug.Log("[Stats] " + DisplayName + " sent an invalid build (" + claimed + ") for level " + level + ": using base stats");
            Level.Value = level;
            ServerApplyBuild(build);
        }

        /// <summary>Server: use this stat build (max health / energy / guard keep their current fill, speed goes to the motor).</summary>
        [Server]
        public void ServerApplyBuild(StatAllocation build)
        {
            Build.Value = build.Pack();
            Numbers = FighterStats.Compute(build);
            _health.SetMax(MaxHealth);
            _energy.Configure(MaxEnergy, _config.energyRegenPerSecond * Numbers.EnergyRegenMultiplier,
                _config.energyChargePerSecond * Numbers.EnergyRegenMultiplier);
            _guard.SetMax(MaxGuard);
            _aura.SetMax(MaxAura);
            if (Motor != null) Motor.ServerSetStatSpeed(Numbers.SpeedMultiplier);
            PushAll();
            Debug.Log("[Stats] " + DisplayName + " level " + Level.Value + " build " + build + " hp=" + MaxHealth.ToString("F0", CultureInfo.InvariantCulture));
        }

        public override void OnStartServer()
        {
            TimeManager.OnTick += ServerTick;
            if (Owner.IsValid) Level.Value = 1; // until the owner reports its account level
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
            _aura.Tick(dt, Motor != null && Motor.IsCharging, CombatConstants.PowerChargePerSecond * Numbers.EnergyRegenMultiplier);
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
            Power.Value = _aura.Power;
            Aura.Value = _aura.Current;
            Exhaustion.Value = _aura.Exhaustion;
            IsDown.Value = _health.IsKnockedOut;
            DownRemaining.Value = _health.DownRemaining;
        }

        /// <summary>Server: full health, energy and guard (training dummy recovering between combos). Ignored while down.</summary>
        [Server]
        public void ServerRestore()
        {
            if (_health.IsKnockedOut) return;
            _health.Reset();
            _energy.Refill();
            _guard.Reset();
            _aura.Reset();
            PushAll();
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

        [Server] public bool ServerHasAura(float amount) => _aura.CanSpend(amount);

        /// <summary>Server: spend aura reserve (energy attacks, later specials). False (nothing spent) when there isn't enough.</summary>
        [Server]
        public bool ServerTrySpendAura(float amount)
        {
            if (!_aura.TrySpend(amount)) return false;
            Aura.Value = _aura.Current;
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
            // stat builds: attacker's Power, this player's Defense (both exactly 1 without points)
            if (attacker != null) dmgBase *= attacker.Numbers.DamageMultiplier;
            dmgBase *= Numbers.DamageTakenMultiplier;
            // power level (10 % at the start of a battle, charge to raise it) and exhaustion once the aura is spent
            if (attacker != null) dmgBase *= attacker._aura.StrengthMultiplier;
            dmgBase *= _aura.DamageTakenMultiplier;
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
            _aura.Reset(); // a new battle: back to 10 % power, full aura
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
