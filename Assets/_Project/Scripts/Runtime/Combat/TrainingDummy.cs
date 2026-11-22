using LightMMORPG.Core;
using LightMMORPG.Player;
using UnityEngine;

namespace LightMMORPG.Combat
{
    /// <summary>
    /// Server-side brain of the Training mode dummy: an ownerless fighter (same prefab as players), driven through
    /// <see cref="PlayerMotor.ServerBotInput"/> for movement and <see cref="PlayerCombat"/>'s <c>Server*</c> attacks, so it
    /// plays by exactly the same rules (power level, aura reserve, energy, cooldowns).
    /// <list type="bullet">
    /// <item><b>Spar</b> (default): fights back. Closes in, throws 2–4 hit combos (sometimes ending in a charged heavy: a short
    /// wind-up, now and then a full charge that sends you flying), guards or
    /// backs off after taking hits, fires energy blasts from range, and powers up (Charge) when it has room, so it starts
    /// weak like everyone and gets stronger. Reaction times are human-ish so it's beatable.</item>
    /// <item><b>Stand</b> / <b>Block</b>: the old punching bag (faces you, optionally holds block).</item>
    /// </list>
    /// It refills after a few calm seconds and walks back to its spot (teleport) after a knockout or when knocked far away.
    /// </summary>
    public sealed class TrainingDummy : MonoBehaviour
    {
        public enum Behaviour { Spar, Stand, Block }

        /// <summary>The dummy of the running training session (server / host only), else null.</summary>
        public static TrainingDummy Instance { get; private set; }

        public const float RecoverAfter = 3f;
        private const float WanderLimit = 7f, SparLeash = 16f;

        public Behaviour Mode { get; set; } = Behaviour.Spar;
        public PlayerStats Stats => _stats;

        /// <summary>Label for the pause menu toggle.</summary>
        public string ModeLabel => Mode switch
        {
            Behaviour.Spar => "Dummy: Sparring",
            Behaviour.Block => "Dummy: Blocking",
            _ => "Dummy: Standing",
        };

        /// <summary>Next mode for the pause menu toggle: Sparring → Standing → Blocking → Sparring.</summary>
        public void CycleMode() => Mode = Mode switch { Behaviour.Spar => Behaviour.Stand, Behaviour.Stand => Behaviour.Block, _ => Behaviour.Spar };

        private PlayerStats _stats;
        private PlayerMotor _motor;
        private PlayerCombat _combat;
        private Vector3 _home;
        private float _lastHealth;
        private float _calm;
        private bool _wasDown;

        // spar brain
        private PlayerMotor.ReplicateData _rd;
        private float _thinkAt, _blockUntil, _chargeUntil, _backOffUntil, _nextSwing, _nextCombo, _nextBlast;
        private float _heavyStart, _heavyReleaseAt;
        private int _comboLeft;
        private bool _finishHeavy;
        private float _strafe = 1f, _strafeFlip;

        public static TrainingDummy Attach(GameObject fighter, Vector3 home)
        {
            var d = fighter.AddComponent<TrainingDummy>();
            d._stats = fighter.GetComponent<PlayerStats>();
            d._motor = fighter.GetComponent<PlayerMotor>();
            d._combat = fighter.GetComponent<PlayerCombat>();
            d._home = home;
            d._lastHealth = d._stats != null ? d._stats.Health.Value : 0f;
            if (d._motor != null) d._motor.ServerBotInput = d.BotInput;
            Instance = d;
            return d;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_motor != null) _motor.ServerBotInput = null;
        }

        private PlayerMotor.ReplicateData BotInput() => _rd;

        private PlayerStats NearestPlayer()
        {
            PlayerStats best = null;
            float bestD = float.MaxValue;
            foreach (var p in PlayerStats.All)
            {
                if (p == null || p == _stats) continue;
                float d = (p.transform.position - transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        private void Update()
        {
            if (_stats == null || _motor == null || !_stats.IsServerStarted) return;
            float hp = _stats.Health.Value;
            bool hurt = hp < _lastHealth - 0.01f;
            if (hurt) _calm = 0f;
            else _calm += Time.deltaTime;
            _lastHealth = hp;

            var target = NearestPlayer();
            _rd = default;
            if (target != null) Face(target);

            if (_stats.IsDown.Value) { _wasDown = true; ResetBrain(); return; }
            if (_wasDown)
            {
                // the normal respawn picks a random spawn point: bring the dummy back to its spot instead
                _wasDown = false;
                _motor.ServerTeleport(_home);
                _calm = 0f;
                return;
            }

            if (Mode == Behaviour.Spar) Spar(target, hurt);
            else { CancelHeavy(); _rd.Block = Mode == Behaviour.Block; }

            if (_calm >= RecoverAfter && hp < _stats.MaxHealth - 0.01f && !InFight(target)) _stats.ServerRestore();
            Vector3 off = transform.position - _home;
            off.y = 0f;
            float leash = Mode == Behaviour.Spar ? SparLeash : WanderLimit;
            if (off.sqrMagnitude > leash * leash && !_motor.IsStunned) _motor.ServerTeleport(_home);
        }

        private void Face(PlayerStats target)
        {
            Vector3 to = target.transform.position - transform.position;
            if (new Vector2(to.x, to.z).sqrMagnitude <= 0.01f) return;
            _rd.CameraYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            _rd.FaceAim = true;
        }

        // while sparring the dummy only heals when the player has stepped away (no free refills mid-fight)
        private bool InFight(PlayerStats target) =>
            Mode == Behaviour.Spar && target != null && !target.IsDown.Value && (target.transform.position - transform.position).sqrMagnitude < 10f * 10f;

        private void ResetBrain()
        {
            _comboLeft = 0;
            _chargeUntil = _blockUntil = _backOffUntil = 0f;
            if (_combat != null && _combat.ServerBeamCharging) _combat.ServerBeamCharge(false);
            CancelHeavy();
        }

        private void CancelHeavy()
        {
            if (_combat != null && _combat.ServerHeavyCharging) _combat.ServerHeavyCharge(false);
        }

        /// <summary>Winds up a heavy: mostly short holds, now and then a full charge that sends you flying.</summary>
        private void StartHeavy(float now)
        {
            _combat.ServerHeavyCharge(true);
            if (!_combat.ServerHeavyCharging) return;
            _heavyStart = now;
            _heavyReleaseAt = now + (Random.value < 0.25f ? CombatConstants.HeavyMaxChargeTime + 0.1f : Random.Range(0.12f, 0.9f));
        }

        // ---------------- sparring ----------------

        private void Spar(PlayerStats target, bool hurt)
        {
            float now = Time.time;
            if (target == null || target.IsDown.Value || _combat == null)
            {
                // nobody to fight: power up a little while waiting
                ResetBrain();
                if (_stats.Power.Value < 0.5f && !_stats.AuraDepleted) _rd.Charge = true;
                return;
            }
            if (!_stats.ServerCanAct()) { _comboLeft = 0; CancelHeavy(); return; }

            Vector3 to = target.transform.position - transform.position;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            float dist = flat.magnitude;
            Vector3 aim = to.sqrMagnitude > 1e-4f ? to.normalized : transform.forward;

            // winding up a heavy: hold it (shuffling in while out of reach), let go once the planned charge is reached
            if (_combat.ServerHeavyCharging)
            {
                _rd.Heavy = true;
                if (dist > 2.1f) _rd.Move = new Vector2(0f, 1f);
                if (now >= _heavyReleaseAt)
                {
                    _combat.ServerHeavyRelease(aim, now - _heavyStart);
                    _nextCombo = now + Random.Range(0.9f, 1.6f);
                }
                return;
            }

            // taking hits: sometimes guard up, sometimes step out
            if (hurt && _comboLeft == 0 && now >= _blockUntil)
            {
                float r = Random.value;
                if (r < 0.3f) _blockUntil = now + Random.Range(0.5f, 1.0f);
                else if (r < 0.45f) _backOffUntil = now + Random.Range(0.4f, 0.7f);
                _chargeUntil = 0f;
            }
            if (now < _blockUntil) { _rd.Block = true; return; }
            if (now < _backOffUntil) { _rd.Move = new Vector2(_strafe * 0.4f, -1f); _rd.Sprint = true; return; }

            // powering up: only with room to breathe; the player rushing in breaks it off
            if (now < _chargeUntil)
            {
                if (dist < 4f || _stats.AuraDepleted || _stats.Power.Value >= 0.999f) _chargeUntil = 0f;
                else { _rd.Charge = true; return; }
            }

            if (now >= _thinkAt)
            {
                _thinkAt = now + Random.Range(0.18f, 0.35f);   // reaction time
                if (now >= _strafeFlip) { _strafe = Random.value < 0.5f ? -1f : 1f; _strafeFlip = now + Random.Range(1.2f, 2.8f); }
                // room to power up: charge towards ~70 % (and to full sometimes), keeping some aura in reserve
                bool room = dist > 7f && _comboLeft == 0;
                float want = Random.value < 0.25f ? 1f : 0.7f;
                if (room && !_stats.AuraDepleted && _stats.Aura.Value > _stats.MaxAura * 0.25f && _stats.Power.Value < want && Random.value < 0.6f)
                {
                    _chargeUntil = now + Random.Range(1.0f, 2.2f);
                    _rd.Charge = true;
                    return;
                }
                // from range: an energy blast now and then
                if (dist > 5f && dist < 20f && now >= _nextBlast && _combat.CanAfford(AttackKind.Blast) && Random.value < 0.5f)
                {
                    _combat.ServerBlast(aim);
                    _nextBlast = now + Random.Range(1.4f, 3.2f);
                }
            }

            if (dist > 2.1f)
            {
                // close in (run when far), weaving a little
                _rd.Move = new Vector2(dist < 4f ? _strafe * 0.25f : 0f, 1f);
                _rd.Sprint = dist > 4.5f;
                _comboLeft = 0;
                return;
            }

            // in range: combos with gaps in between
            if (_comboLeft == 0 && now >= _nextCombo)
            {
                _comboLeft = Random.Range(2, 5);
                _finishHeavy = Random.value < 0.3f;
                _nextSwing = now + Random.Range(0.05f, 0.2f);
            }
            if (_comboLeft > 0)
            {
                if (now >= _nextSwing)
                {
                    bool last = _comboLeft == 1;
                    if (last && _finishHeavy) StartHeavy(now);
                    else _combat.ServerLight(aim);
                    _comboLeft--;
                    _nextSwing = now + 0.32f;
                    if (_comboLeft == 0) _nextCombo = now + Random.Range(0.7f, 1.5f);
                }
                return;
            }
            // between combos: circle a little, sometimes hop back out
            _rd.Move = new Vector2(_strafe * 0.6f, dist < 1.3f ? -0.6f : 0f);
            if (Random.value < 0.004f) _backOffUntil = now + 0.5f;
        }
    }
}
