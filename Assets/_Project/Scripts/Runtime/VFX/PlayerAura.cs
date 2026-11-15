using LightMMORPG.Combat;
using LightMMORPG.Config;
using LightMMORPG.Player;
using UnityEngine;

namespace LightMMORPG.VFX
{
    /// <summary>
    /// Aura shell around the fighter (aura material): big and steady during Power Surge, smaller flickering one while
    /// charging energy (hold Charge). Reads synced/predicted state only, so every client shows the same thing.
    /// Lives under Graphics/Model so it follows the procedural animation.
    /// </summary>
    [DefaultExecutionOrder(210)]
    public sealed class PlayerAura : MonoBehaviour
    {
        private PlayerStats _stats;
        private PlayerMotor _motor;
        private Transform _shell;
        private float _blend;

        private void Awake()
        {
            _stats = GetComponent<PlayerStats>();
            _motor = GetComponent<PlayerMotor>();
            if (Application.isBatchMode) { enabled = false; return; }
            var model = transform.Find("Graphics/Model");
            var go = CombatFx.MakeGlow(PrimitiveType.Capsule, "Aura", GameConfig.Load().auraMaterial);
            go.transform.SetParent(model != null ? model : transform, false);
            go.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            _shell = go.transform;
            go.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_shell == null) return;
            bool surge = _stats != null && _stats.SurgeActive && !_stats.IsDown.Value;
            bool charging = _motor != null && _motor.IsCharging && (_stats == null || !_stats.IsDown.Value);
            float want = surge ? 1f : (charging ? 0.7f : 0f);
            _blend = Mathf.MoveTowards(_blend, want, Time.deltaTime * 4f);
            _shell.gameObject.SetActive(_blend > 0.01f);
            if (_blend <= 0.01f) return;
            float flicker = charging && !surge ? 1f + Mathf.Sin(Time.time * 25f) * 0.06f + Mathf.Sin(Time.time * 41f) * 0.04f
                                               : 1f + Mathf.Sin(Time.time * 6f) * 0.03f;
            float s = Mathf.Lerp(0.9f, surge ? 1.55f : 1.35f, _blend) * flicker;
            _shell.localScale = new Vector3(1f * s, 1.05f * s, 1f * s);
        }
    }
}
