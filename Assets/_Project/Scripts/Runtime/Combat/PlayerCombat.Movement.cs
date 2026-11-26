using System.Collections.Generic;
using LightMMORPG.Cameras;
using LightMMORPG.Characters;
using LightMMORPG.Core;
using LightMMORPG.VFX;
using UnityEngine;

namespace LightMMORPG.Combat
{
    /// <summary>
    /// Fight timing and movement (the rest of PlayerCombat is in PlayerCombat.cs):
    /// <list type="bullet">
    /// <item>Light blows land on their impact frame: <see cref="ServerLight"/> queues them, <see cref="ServerLightHits"/>
    /// resolves each one when the move's <see cref="AttackMove.Hit"/> time comes, so every client sees the reaction when the
    /// fist arrives. Struck or knocked out before then, the blow is lost (trades go to the faster blow).</item>
    /// <item>Rush (owner, Dash): locked on, a dash with no stick or the stick towards the opponent rushes into striking range
    /// of them instead (up to 16 m). Pursuit: right after a blow of yours sends someone flying (a finisher, a heavy, a skill's
    /// launch, a beam), Dash chases them the same way, locked on or not. Back / sideways dashes stay plain dashes, to get
    /// away. Both use the dash (its cooldown and dodge window).</item>
    /// <item>Afterimages on dashes, rushes, flash steps (<see cref="AfterimageTrail"/>) and dodges (<see cref="CombatFx"/>).</item>
    /// </list>
    /// </summary>
    public sealed partial class PlayerCombat
    {
        /// <summary>Seconds after a launching blow lands in which Dash chases the victim (they fly ~0.6-1 s).</summary>
        private const float PursuitWindow = 1.1f;
        /// <summary>No rush when the target is already this close to striking range (a plain dash instead), metres.</summary>
        private const float RushMinGap = 1.6f;
        /// <summary>A dash with the stick pushed within this many degrees of the target rushes at them.</summary>
        private const float RushAimAngle = 60f;
        /// <summary>Roughly the motor's rush speed (m/s), for leading a target that is moving.</summary>
        private const float RushSpeedEstimate = 46f;

        // ---- light blows waiting for their impact frame (server) ----
        private struct PendingLight
        {
            public double At;
            public Vector3 Aim, From, Sweep;
            public int Index;
            public float Damage;
            public bool Finisher;
        }
        private readonly List<PendingLight> _lightHits = new();

        // ---- pursuit (owner): the fighter this one just sent flying ----
        private PlayerStats _pursuit;
        private float _pursuitUntil;

        /// <summary>Server: a blow of this fighter is on its way and hasn't landed yet (a light, a heavy or a sword skill strike).</summary>
        public bool ServerStrikeIncoming => _lightHits.Count > 0 || _heavyPending || _skillHits.Count > 0;

        /// <summary>Server: a heavy or a sword skill of this fighter is on its way.</summary>
        public bool ServerBigStrikeIncoming => _heavyPending || _skillHits.Count > 0;

        private void InitFightMovement()
        {
            if (_motor != null) _motor.OwnerDashOverride = TryRush;
            PlayerStats.OnHitFeedback += OnHitForPursuit;
            AfterimageTrail.Attach(gameObject);
        }

        private void OnDestroy() => PlayerStats.OnHitFeedback -= OnHitForPursuit;

        // ---------------- light blows on their impact frame (server) ----------------

        /// <summary>Server: a light combo blow, resolved when its fist / foot / blade arrives (<see cref="ServerLightHits"/>).</summary>
        private void QueueLightHit(Vector3 aim, ComboHit hit, Vector3 sweep, bool tired)
        {
            AttackMove m = Moveset.Move(hit.Index);
            float delay = m != null ? Mathf.Clamp(m.Hit, 0f, 0.3f) : 0f;
            _lightHits.Add(new PendingLight
            {
                At = Now + delay,
                Aim = aim,
                From = ServerChest,
                Sweep = sweep,
                Index = hit.Index,
                // a tired swing (thrown without the stamina for it): weaker, and a finisher doesn't send anyone flying
                Damage = hit.Damage * (_sword.Value ? SwordDamageMul : 1f) * (tired ? CombatConstants.TiredDamageMultiplier : 1f),
                Finisher = hit.Knockback && !tired,
            });
        }

        /// <summary>
        /// Server tick: the light blows whose fist / foot / blade has arrived, each sweeping its dash from where the blow
        /// started (so it lands whether or not the server has moved the attacker yet). Struck or knocked out before then: lost.
        /// </summary>
        private void ServerLightHits()
        {
            for (int i = 0; i < _lightHits.Count;)
            {
                if (Now < _lightHits[i].At) { i++; continue; }
                PendingLight h = _lightHits[i];
                _lightHits.RemoveAt(i);
                if (!_stats.ServerCanAct()) continue;
                HitKind kind = h.Finisher ? HitKind.Finisher : HitKind.Light;
                Vector3 kb = h.Finisher
                    ? CombatMath.Knockback(h.Aim, CombatMath.FinisherKnockbackSpeed, CombatMath.FinisherLift)
                    : CombatMath.Knockback(h.Aim, CombatMath.LightKnockbackSpeed, 0f);
                float stun = h.Finisher ? _config.heavyHitstun : _config.lightHitstun;
                MeleeHits(h.Aim, h.Damage, stun, kb, kind, StrikePoint(h.Aim, h.Index), 0f, h.Sweep, h.From);
            }
        }

        // ---------------- rush / pursuit (owner) ----------------

        /// <summary>Owner: a blow of ours sent someone flying (finisher, heavy, beam, a skill's launch): Dash chases them for a moment.</summary>
        private void OnHitForPursuit(PlayerStats victim, PlayerStats attacker, Vector3 point, float damage, bool blocked, bool guardBreak,
                                     HitKind kind, float impact, HitReaction reaction)
        {
            if (!IsOwner || attacker != _stats || victim == null || victim == _stats) return;
            if (_motor != null) _motor.QueueConnected();   // our blow landed (hit or blocked): Dash / Block / Jump may cut its recovery
            if (blocked) return;
            if (kind != HitKind.Finisher && kind != HitKind.Heavy && kind != HitKind.Beam) return;
            _pursuit = victim;
            _pursuitUntil = Time.time + PursuitWindow;
        }

        /// <summary>
        /// Owner, Dash pressed (<see cref="Player.PlayerMotor.OwnerDashOverride"/>): rush at the opponent instead of the plain
        /// dash. The target is the one this fighter just sent flying (pursuit, for <see cref="PursuitWindow"/>), else the
        /// lock-on target. Only with no stick or the stick towards them (within <see cref="RushAimAngle"/>), only when they
        /// are out of reach, and only when the dash is ready. Aims where they will be on arrival. True = a rush was queued.
        /// </summary>
        private bool TryRush()
        {
            if (_stats == null || _input == null || _motor == null || _stats.IsDown.Value || _motor.IsStunned || !_motor.DashReady
                || _motor.IsCharging || _motor.RecoveryLocked) return false;
            // stamina: no rush when short or winded (the press falls back to the plain dash, which the motor gates on its own cost)
            if (!_motor.CanAfford(CombatConstants.StaminaRush + (_motor.InRecovery ? CombatConstants.StaminaDashCancel : 0f))) return false;
            float now = Time.time;
            if (now < _busyUntilLocal || now < _motor.OwnerFrozenUntil) return false;
            bool pursuit = _pursuit != null && now < _pursuitUntil && _pursuit.isActiveAndEnabled && !_pursuit.IsDown.Value;
            PlayerStats target = pursuit ? _pursuit : LockedTarget();
            if (target == null || target == _stats) return false;

            Vector3 to = target.transform.position - transform.position;
            if (!_motor.IsFlying) to.y = 0f;
            float dist = to.magnitude;
            float contact = (_swordLocal ? 1.9f : 1.15f) * DesignScale;
            if (dist < 1e-3f || dist - contact < RushMinGap * DesignScale) return false;   // already in reach: a plain dash

            // the stick decides: none or towards them = rush; away or aside = the plain dash (to get away)
            Vector2 mv = _input.Move;
            if (mv.sqrMagnitude > 0.04f)
            {
                var cam = ThirdPersonCamera.Instance;
                float yaw = cam != null ? cam.Yaw : _motor.FacingYaw;
                Vector3 wish = Quaternion.Euler(0f, yaw, 0f) * new Vector3(mv.x, 0f, mv.y);
                Vector3 flatTo = new Vector3(to.x, 0f, to.z);
                if (flatTo.sqrMagnitude > 1e-4f && Vector3.Angle(wish, flatTo) > RushAimAngle) return false;
            }

            // where they'll be on arrival: someone sent flying keeps sliding away while the knockback slows down
            float t = Mathf.Clamp(dist / RushSpeedEstimate, 0.08f, 0.36f);
            Vector3 v = target.Motor != null ? Vector3.ClampMagnitude(target.Motor.Velocity, 40f) : Vector3.zero;
            if (!_motor.IsFlying) v.y = 0f;
            float vm = v.magnitude;
            Vector3 aimAt = to;
            if (vm > 0.1f)
            {
                float decel = target.Motor.IsStunned ? target.Motor.KnockbackDeceleration : 0f;
                float travel = decel > 0f && vm < decel * t ? vm * vm / (2f * decel) : vm * t - 0.5f * decel * t * t;
                aimAt += v / vm * travel;
            }
            float d = aimAt.magnitude;
            if (d < 1e-3f) return false;
            Vector3 rush = aimAt / d * Mathf.Max(0f, d - contact);
            if (rush.sqrMagnitude < 0.25f) return false;

            _motor.QueueRush(rush);
            if (pursuit) _pursuitUntil = 0f;   // one chase per launch
            AnimeScreenFx.SpeedBurst(pursuit ? 0.9f : 0.7f, 0.35f);
            ThirdPersonCamera.Punch(pursuit ? 4f : 3f, 0.3f);   // on top of the dash's own widening
            return true;
        }

        private static PlayerStats LockedTarget()
        {
            var cam = ThirdPersonCamera.Instance;
            if (cam == null || cam.CurrentTarget == null) return null;
            var s = cam.CurrentTarget.GetComponent<PlayerStats>();
            return s != null && !s.IsDown.Value ? s : null;
        }
    }
}
