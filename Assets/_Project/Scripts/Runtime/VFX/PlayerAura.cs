using LightMMORPG.Characters;
using LightMMORPG.Combat;
using LightMMORPG.Player;
using UnityEngine;

namespace LightMMORPG.VFX
{
    /// <summary>
    /// The fighter's anime flame aura (<see cref="AnimeAura"/>) in the player's chosen aura colour (Customize → Body,
    /// synced with the face choices, so every client shows the same colour): shown while charging energy (hold Charge),
    /// bigger during Power Surge, and flaring up as a held heavy charges (from a quarter charge, growing to the full flame).
    /// Reads synced / predicted state only, so every client shows the same thing.
    /// Lives under Graphics/Model so it follows the smoothed model.
    /// </summary>
    [DefaultExecutionOrder(210)]
    public sealed class PlayerAura : MonoBehaviour
    {
        private PlayerStats _stats;
        private PlayerMotor _motor;
        private PlayerProceduralAnimator _anim;
        private Transform _model;
        private CharacterRig _rig;
        private AnimeAura _aura;

        private void Awake()
        {
            _stats = GetComponent<PlayerStats>();
            _motor = GetComponent<PlayerMotor>();
            _anim = GetComponent<PlayerProceduralAnimator>();
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
            // a heavy held past a quarter charge: the aura flares up with the charge (tapped heavies show nothing)
            float heavy = _anim != null && !down ? Mathf.InverseLerp(0.25f, 1f, _anim.HeavyCharge01) : 0f;
            _aura.Visible = surge || charging || heavy > 0f;
            if (!_aura.Visible) return;   // keep the last size while it shrinks away
            // charging: the aura grows with the power level (10 % → 100 %); Power Surge: full size
            float power = _stats != null ? Mathf.InverseLerp(Core.CombatConstants.StartPower, 1f, _stats.Power.Value) : 1f;
            float size = surge ? 1f : charging ? Mathf.Lerp(0.72f, 0.92f, power) : 0f;
            if (heavy > 0f) size = Mathf.Max(size, Mathf.Lerp(0.62f, 0.95f, heavy));
            _aura.Size = size;
        }
    }
}
