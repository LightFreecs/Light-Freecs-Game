using UnityEngine;

namespace LightMMORPG.Player
{
    /// <summary>
    /// Placeholder procedural animation on Graphics/Model (Graphics itself is FishNet's smoothed graphical object, so we
    /// never touch it). Driven only by synced/predicted state (motor flags + the smoothed Graphics movement), so it looks
    /// the same on every client: idle bob, lean into movement, flight pose, dash stretch, block crouch, charge tremble,
    /// stun wobble. Session 4 hooks: <see cref="PlayLunge"/> (attack swing) and <see cref="PlayRecoil"/> (got hit).
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class PlayerProceduralAnimator : MonoBehaviour
    {
        [SerializeField] private Transform _graphics;
        [SerializeField] private Transform _model;
        [SerializeField] private float _maxLean = 14f;

        private PlayerMotor _motor;
        private Vector3 _lastPos;
        private Vector3 _smoothedVelocity;
        private float _stretch;
        private float _lunge;
        private Vector3 _recoil;
        private float _flightBlend;
        private float _phase;
        private Vector3 _baseScale = Vector3.one;

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            if (_graphics == null) _graphics = transform.Find("Graphics");
            if (_model == null && _graphics != null) _model = _graphics.Find("Model");
            if (_model != null) _baseScale = _model.localScale;
            if (_graphics != null) _lastPos = _graphics.position;
        }

        /// <summary>Forward lunge/punch pose (0..1 strength), decays quickly.</summary>
        public void PlayLunge(float strength = 1f) => _lunge = Mathf.Max(_lunge, strength);

        /// <summary>Knocked back along a world direction (strength ~1).</summary>
        public void PlayRecoil(Vector3 worldDirection, float strength = 1f)
        {
            worldDirection.y = 0f;
            _recoil = worldDirection.normalized * strength;
        }

        private void LateUpdate()
        {
            if (_graphics == null || _model == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            Vector3 vel = (_graphics.position - _lastPos) / dt;
            _lastPos = _graphics.position;
            if (vel.sqrMagnitude > 900f) vel = Vector3.zero; // teleport / flash step
            _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, vel, 1f - Mathf.Exp(-10f * dt));

            Vector3 local = _graphics.InverseTransformDirection(_smoothedVelocity);
            float planar = new Vector2(local.x, local.z).magnitude;

            bool flying = _motor != null && _motor.IsFlying;
            bool dashing = _motor != null && _motor.IsDashing;
            bool blocking = _motor != null && _motor.IsBlocking;
            bool charging = _motor != null && _motor.IsCharging;
            bool stunned = _motor != null && _motor.IsStunned;

            _flightBlend = Mathf.MoveTowards(_flightBlend, flying ? 1f : 0f, dt * 3f);
            _stretch = Mathf.Lerp(_stretch, dashing ? 1f : 0f, 1f - Mathf.Exp(-(dashing ? 30f : 8f) * dt));
            _lunge = Mathf.MoveTowards(_lunge, 0f, dt * 5f);
            _recoil = Vector3.MoveTowards(_recoil, Vector3.zero, dt * 3f);

            _phase += dt * Mathf.Lerp(2.2f, 9f, Mathf.Clamp01(planar / 13f));

            // Lean: forward with speed, sideways with strafing; flight leans more (superhero pose).
            float speed01 = Mathf.Clamp01(planar / 13f);
            float leanMax = Mathf.Lerp(_maxLean, 40f, _flightBlend);
            float pitch = Mathf.Clamp(local.z / 13f, -1f, 1f) * leanMax;
            float roll = -Mathf.Clamp(local.x / 13f, -1f, 1f) * leanMax * 0.6f;
            pitch += _lunge * 18f;
            Vector3 recoilLocal = _graphics.InverseTransformDirection(_recoil);
            pitch -= recoilLocal.z * 25f;
            roll += recoilLocal.x * 25f;

            // Bob: idle breathing on ground, slow hover float in the air, step bounce while running.
            float bob = Mathf.Lerp(Mathf.Sin(_phase) * 0.04f, Mathf.Abs(Mathf.Sin(_phase)) * 0.12f, speed01 * (1f - _flightBlend));
            bob = Mathf.Lerp(bob, Mathf.Sin(Time.time * 2f) * 0.15f + 0.2f, _flightBlend);
            float crouch = blocking ? -0.12f : 0f;

            Vector3 offset = new Vector3(0f, bob + crouch, _lunge * 0.35f);
            if (charging) offset += new Vector3(Mathf.Sin(Time.time * 61f), 0f, Mathf.Cos(Time.time * 53f)) * 0.025f;
            if (stunned) roll += Mathf.Sin(Time.time * 18f) * 8f;

            // Dash stretch: longer along travel, thinner across. Block squashes slightly.
            float s = _stretch;
            Vector3 scale = new Vector3(1f - 0.25f * s, 1f - 0.12f * s, 1f + 0.6f * s);
            if (blocking) scale = Vector3.Scale(scale, new Vector3(1.05f, 0.92f, 1.05f));

            _model.localPosition = offset;
            _model.localRotation = Quaternion.Euler(pitch, 0f, roll);
            _model.localScale = Vector3.Scale(_baseScale, scale);
        }
    }
}
