using System;
using LightMMORPG.Input;
using LightMMORPG.Player;
using UnityEngine;

namespace LightMMORPG.Combat
{
    /// <summary>
    /// Smoke-test bot (command line <c>-autoattack</c>): drives the local player through <see cref="TouchInputState"/>
    /// (the same path the touch overlay uses) — walk up to the nearest other player, light combo, a charged heavy (held ~1 s), back off,
    /// three Energy Blasts, a fully charged Beam, charge energy when low — and repeats. Aims via
    /// <see cref="PlayerCombat.AimOverride"/>. Runs before the input reader so its taps land the same frame.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class CombatAutoPilot : MonoBehaviour
    {
        private enum Phase { Approach1, Combo, Approach2, Heavy, BackOff, Blasts, Beam, Recharge }

        private Phase _phase;
        private float _phaseStart;
        private float _nextAction;
        private int _count;
        private PlayerStats _target;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoattack") < 0) return;
            var go = new GameObject("CombatAutoPilot");
            DontDestroyOnLoad(go);
            go.AddComponent<CombatAutoPilot>();
            Debug.Log("[Auto] -autoattack enabled");
        }

        private void OnEnable()
        {
            PlayerCombat.AimOverride = () =>
            {
                var me = PlayerMotor.Local;
                if (me == null || _target == null) return null;
                return (_target.transform.position - me.transform.position);
            };
        }

        private void OnDisable() => PlayerCombat.AimOverride = null;

        private void Go(Phase p)
        {
            _phase = p;
            _phaseStart = Time.time;
            _count = 0;
            _nextAction = Time.time + 0.2f;
            Debug.Log("[Auto] phase " + p + (_target != null ? " target=" + _target.DisplayName + " hp=" + _target.Health.Value.ToString("F0") : ""));
        }

        private void Update()
        {
            var me = PlayerMotor.Local;
            var stats = PlayerStats.Local;
            TouchInputState.Move = Vector2.zero;
            if (me == null || stats == null) return;

            _target = FindTarget(me);
            if (_target == null || _target.IsDown.Value || stats.IsDown.Value)
            {
                TouchInputState.Release(GameAction.Beam);
                TouchInputState.Release(GameAction.Charge);
                TouchInputState.Release(GameAction.Heavy);
                if (_phase != Phase.Approach1) Go(Phase.Approach1);
                return;
            }

            Vector3 to = _target.transform.position - me.transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            float t = Time.time - _phaseStart;
            bool act = Time.time >= _nextAction;

            switch (_phase)
            {
                case Phase.Approach1:
                case Phase.Approach2:
                    if (dist > 1.8f) Steer(to.normalized);
                    if (dist <= 1.8f || t > 8f) Go(_phase == Phase.Approach1 ? Phase.Combo : Phase.Heavy);
                    break;
                case Phase.Combo:
                    if (dist > 2.2f) Steer(to.normalized);
                    if (act) { TouchInputState.Tap(GameAction.Light); _count++; _nextAction = Time.time + 0.3f; }
                    if (_count >= 4) Go(Phase.Approach2);
                    break;
                case Phase.Heavy:
                    // a charged heavy: hold ~1 s (most of the charge), then let go
                    if (dist > 1.8f) Steer(to.normalized);
                    if (t < 0.25f) TouchInputState.Press(GameAction.Heavy);
                    else if (t > 1.25f) { TouchInputState.Release(GameAction.Heavy); if (t > 1.9f) Go(Phase.BackOff); }
                    break;
                case Phase.BackOff:
                    if (dist < 9f && t < 2.5f) Steer(-to.normalized);
                    else Go(Phase.Blasts);
                    break;
                case Phase.Blasts:
                    if (act) { TouchInputState.Tap(GameAction.Blast); _count++; _nextAction = Time.time + 0.45f; }
                    if (_count >= 3) Go(Phase.Beam);
                    break;
                case Phase.Beam:
                    if (t < 0.1f) TouchInputState.Press(GameAction.Beam);
                    else if (t > 1.7f) { TouchInputState.Release(GameAction.Beam); if (t > 1.9f) Go(Phase.Recharge); }
                    break;
                case Phase.Recharge:
                    bool low = stats.Energy.Value < 70f;
                    if (low && t < 3f) TouchInputState.Press(GameAction.Charge);
                    else { TouchInputState.Release(GameAction.Charge); if (t > 0.6f) Go(Phase.Approach1); }
                    break;
            }
        }

        private static void Steer(Vector3 worldDir)
        {
            float yaw = PlayerMotor.CameraYawProvider != null ? PlayerMotor.CameraYawProvider() : 0f;
            Vector3 local = Quaternion.Euler(0f, -yaw, 0f) * worldDir;
            TouchInputState.Move = new Vector2(local.x, local.z);
        }

        private static PlayerStats FindTarget(PlayerMotor me)
        {
            PlayerStats best = null;
            float bestD = float.MaxValue;
            foreach (var p in PlayerStats.All)
            {
                if (p == null || p.Motor == me) continue;
                float d = (p.transform.position - me.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }
    }
}
