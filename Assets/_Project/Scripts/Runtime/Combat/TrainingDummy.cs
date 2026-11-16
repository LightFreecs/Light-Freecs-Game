using LightMMORPG.Player;
using UnityEngine;

namespace LightMMORPG.Combat
{
    /// <summary>
    /// Server-side brain of the Training mode dummy: an ownerless fighter (same prefab as players) that faces the nearest
    /// player, stands still or holds block (<see cref="Mode"/>), refills after a few calm seconds, and walks back to its
    /// spot (teleport) after a knockout or when knocked far away. Drives the motor through <see cref="PlayerMotor.ServerBotInput"/>.
    /// </summary>
    public sealed class TrainingDummy : MonoBehaviour
    {
        public enum Behaviour { Stand, Block }

        /// <summary>The dummy of the running training session (server / host only), else null.</summary>
        public static TrainingDummy Instance { get; private set; }

        public const float RecoverAfter = 3f;
        private const float WanderLimit = 7f;

        public Behaviour Mode { get; set; } = Behaviour.Stand;
        public PlayerStats Stats => _stats;

        private PlayerStats _stats;
        private PlayerMotor _motor;
        private Vector3 _home;
        private float _lastHealth;
        private float _calm;
        private bool _wasDown;

        public static TrainingDummy Attach(GameObject fighter, Vector3 home)
        {
            var d = fighter.AddComponent<TrainingDummy>();
            d._stats = fighter.GetComponent<PlayerStats>();
            d._motor = fighter.GetComponent<PlayerMotor>();
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

        private PlayerMotor.ReplicateData BotInput()
        {
            var rd = new PlayerMotor.ReplicateData { Block = Mode == Behaviour.Block };
            var target = NearestPlayer();
            if (target != null)
            {
                Vector3 to = target.transform.position - transform.position;
                if (new Vector2(to.x, to.z).sqrMagnitude > 0.01f)
                {
                    rd.CameraYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    rd.FaceAim = true;
                }
            }
            return rd;
        }

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
            if (hp < _lastHealth - 0.01f) _calm = 0f;
            else _calm += Time.deltaTime;
            _lastHealth = hp;

            if (_stats.IsDown.Value) { _wasDown = true; return; }
            if (_wasDown)
            {
                // the normal respawn picks a random spawn point: bring the dummy back to its spot instead
                _wasDown = false;
                _motor.ServerTeleport(_home);
                _calm = 0f;
                return;
            }
            if (_calm < RecoverAfter) return;
            if (hp < _stats.MaxHealth - 0.01f) _stats.ServerRestore();
            Vector3 off = transform.position - _home;
            off.y = 0f;
            if (off.sqrMagnitude > WanderLimit * WanderLimit && !_motor.IsStunned) _motor.ServerTeleport(_home);
        }
    }
}
