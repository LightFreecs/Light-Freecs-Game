using LightMMORPG.Characters;
using LightMMORPG.Combat;
using LightMMORPG.Player;
using UnityEngine;

namespace LightMMORPG.VFX
{
    /// <summary>
    /// The fighter's anime flame aura (<see cref="AnimeAura"/>) in the player's chosen aura colour (Customize → Body,
    /// synced with the face choices, so every client shows the same colour): shown while charging energy (hold Charge)
    /// and, bigger, during Power Surge. Reads synced / predicted state only, so every client shows the same thing.
    /// Lives under Graphics/Model so it follows the smoothed model.
    /// </summary>
    [DefaultExecutionOrder(210)]
    public sealed class PlayerAura : MonoBehaviour
    {
        private PlayerStats _stats;
        private PlayerMotor _motor;
        private Transform _model;
        private CharacterRig _rig;
        private AnimeAura _aura;

        private void Awake()
        {
            _stats = GetComponent<PlayerStats>();
            _motor = GetComponent<PlayerMotor>();
            if (Application.isBatchMode) { enabled = false; return; }
            _model = transform.Find("Graphics/Model");
            _aura = AnimeAura.Create(_model != null ? _model : transform);
        }

        private void LateUpdate()
        {
            if (_aura == null) return;
            bool down = _stats != null && _stats.IsDown.Value;
            bool surge = _stats != null && _stats.SurgeActive && !down;
            bool charging = _motor != null && _motor.IsCharging && !down;
            // the hero is rebuilt when its looks change (palette, face choices): pick up the new rig
            if (_rig == null) _rig = (_model != null ? _model : transform).GetComponentInChildren<CharacterRig>();
            if (_rig != null)
            {
                _aura.Color = _rig.AuraColor;
                _aura.Height = _rig.Height;
            }
            _aura.Visible = surge || charging;
            // charging: the aura grows with the power level (10 % → 100 %); Power Surge: full size
            float power = _stats != null ? Mathf.InverseLerp(Core.CombatConstants.StartPower, 1f, _stats.Power.Value) : 1f;
            _aura.Size = surge ? 1f : Mathf.Lerp(0.72f, 0.92f, power);
        }
    }
}
