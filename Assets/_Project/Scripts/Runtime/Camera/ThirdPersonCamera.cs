using System.Collections.Generic;
using LightMMORPG.Config;
using LightMMORPG.Input;
using LightMMORPG.Player;
using UnityEngine;

namespace LightMMORPG.Cameras
{
    /// <summary>
    /// Third-person orbit camera for the local owner. Follows the player's FishNet graphical object ("Graphics", smoothed),
    /// orbits with mouse / right stick / touch drag, pulls in on collision. Lock-on (Tab / R3): picks the best enemy player
    /// in front, Tab again cycles, camera frames both, a glowing marker floats over the target.
    /// <see cref="CurrentTarget"/> / <see cref="AimDirection"/> are what combat should aim with.
    /// Lives on the scene's Main Camera.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        public static ThirdPersonCamera Instance { get; private set; }

        [Header("Orbit")]
        public float distance = 6f;
        public float pivotHeight = 1.9f;
        public float shoulderOffset = 0.55f;
        public float minPitch = -35f, maxPitch = 70f;
        public float followSharpness = 18f;
        [Header("Collision")]
        public float collisionRadius = 0.3f;
        public float minDistance = 0.8f;
        [Header("Lock-on")]
        public float lockOnRange = 45f;
        public float lockOnMaxAngle = 75f;
        public float lockOnBreakRange = 60f;
        public float lockOnTurnSharpness = 10f;

        /// <summary>Locked-on enemy player root, or null.</summary>
        public PlayerIdentity CurrentTarget { get; private set; }
        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public bool IsLockedOn => CurrentTarget != null;

        private PlayerMotor _player;
        private Transform _follow;
        private float _yaw, _pitch = 12f;
        private Vector3 _pivot;
        private float _currentDistance;
        private Transform _marker;
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private readonly List<PlayerIdentity> _candidates = new();

        /// <summary>Unit vector the local player should aim along (chest → target, else camera forward).</summary>
        public Vector3 AimDirection
        {
            get
            {
                if (CurrentTarget != null && _player != null)
                {
                    Vector3 d = TargetPoint(CurrentTarget.transform) - (_player.transform.position + Vector3.up * 1.3f);
                    if (d.sqrMagnitude > 0.01f) return d.normalized;
                }
                return transform.forward;
            }
        }

        private void Awake()
        {
            Instance = this;
            _currentDistance = distance;
            PlayerMotor.CameraYawProvider = () => _yaw;
            PlayerMotor.LockedOnProvider = () => CurrentTarget != null;
            PlayerMotor.OnLocalPlayerStarted += Attach;
            if (PlayerMotor.Local != null) Attach(PlayerMotor.Local);
        }

        private void OnDestroy()
        {
            PlayerMotor.OnLocalPlayerStarted -= Attach;
            if (Instance == this) Instance = null;
        }

        public void Attach(PlayerMotor player)
        {
            _player = player;
            var gfx = player.transform.Find("Graphics");
            _follow = gfx != null ? gfx : player.transform;
            _yaw = player.transform.eulerAngles.y;
            _pivot = _follow.position + Vector3.up * pivotHeight;
            CurrentTarget = null;
        }

        private void LateUpdate()
        {
            if (_player == null || _follow == null)
            {
                SetMarker(null);
                return;
            }
            var input = _player.Input;
            float dt = Time.deltaTime;

            if (input != null && input.Pressed(GameAction.LockOn)) CycleTarget();
            ValidateTarget();

            Vector2 look = input != null ? input.Look : Vector2.zero;
            Vector3 self = _follow.position;
            Vector3 desiredPivot = self + Vector3.up * pivotHeight;
            float desiredDistance = distance;

            if (CurrentTarget != null)
            {
                Vector3 tp = TargetPoint(CurrentTarget.transform);
                Vector3 to = tp - desiredPivot;
                Vector3 flat = new Vector3(to.x, 0f, to.z);
                if (flat.sqrMagnitude > 0.01f)
                {
                    float wantYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                    _yaw = Mathf.LerpAngle(_yaw, wantYaw, 1f - Mathf.Exp(-lockOnTurnSharpness * dt));
                }
                // Look slightly down at the pair, a bit more when the target is above/below.
                float sep = to.magnitude;
                float wantPitch = Mathf.Clamp(10f + Mathf.Atan2(-to.y, Mathf.Max(flat.magnitude, 1f)) * Mathf.Rad2Deg * 0.5f, minPitch, maxPitch);
                _pitch = Mathf.Lerp(_pitch, wantPitch + look.y * -0.5f, 1f - Mathf.Exp(-6f * dt));
                desiredDistance = distance + Mathf.Clamp(sep * 0.25f, 0f, 6f);
                desiredPivot = Vector3.Lerp(desiredPivot, tp, 0.15f);
            }
            else
            {
                _yaw += look.x;
                _pitch = Mathf.Clamp(_pitch - look.y, minPitch, maxPitch);
            }

            _pivot = Vector3.Lerp(_pivot, desiredPivot, 1f - Mathf.Exp(-followSharpness * dt));
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 origin = _pivot + rot * new Vector3(shoulderOffset, 0f, 0f);
            Vector3 back = rot * Vector3.back;

            float allowed = CollisionDistance(origin, back, desiredDistance);
            // pull in instantly, ease back out
            _currentDistance = allowed < _currentDistance ? allowed : Mathf.Lerp(_currentDistance, allowed, 1f - Mathf.Exp(-5f * dt));

            transform.SetPositionAndRotation(origin + back * _currentDistance, rot);
            SetMarker(CurrentTarget);
        }

        private float CollisionDistance(Vector3 origin, Vector3 dir, float maxDist)
        {
            int n = Physics.SphereCastNonAlloc(origin, collisionRadius, dir, _hits, maxDist, ~0, QueryTriggerInteraction.Ignore);
            float best = maxDist;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.collider.GetComponentInParent<PlayerMotor>() != null) continue; // ignore players
                if (h.distance <= 0f) continue; // started inside
                best = Mathf.Min(best, h.distance);
            }
            return Mathf.Max(minDistance, best);
        }

        // ---------------- lock-on ----------------

        /// <summary>Tab: lock the best candidate; again → next candidate; if none other → release.</summary>
        public void CycleTarget()
        {
            _candidates.Clear();
            Vector3 camPos = transform.position;
            Vector3 camFwd = transform.forward;
            foreach (var p in PlayerIdentity.All)
            {
                if (p == null || p.Motor == _player) continue;
                Vector3 to = TargetPoint(p.transform) - _player.transform.position;
                if (to.magnitude > lockOnRange) continue;
                Vector3 fromCam = TargetPoint(p.transform) - camPos;
                if (Vector3.Angle(camFwd, fromCam) > lockOnMaxAngle) continue;
                _candidates.Add(p);
            }
            Vector3 me = _player.transform.position;
            _candidates.Sort((a, b) => Score(a).CompareTo(Score(b)));

            float Score(PlayerIdentity p)
            {
                Vector3 to = TargetPoint(p.transform) - me;
                return to.magnitude + Vector3.Angle(camFwd, TargetPoint(p.transform) - camPos) * 0.3f;
            }

            if (_candidates.Count == 0) { CurrentTarget = null; return; }
            if (CurrentTarget == null) { CurrentTarget = _candidates[0]; return; }
            int idx = _candidates.IndexOf(CurrentTarget);
            if (idx < 0) CurrentTarget = _candidates[0];
            else if (_candidates.Count == 1) CurrentTarget = null;
            else CurrentTarget = _candidates[(idx + 1) % _candidates.Count];
        }

        public void ClearTarget() => CurrentTarget = null;

        private void ValidateTarget()
        {
            if (CurrentTarget == null) return;
            if (!CurrentTarget.isActiveAndEnabled || !PlayerIdentity.All.Contains(CurrentTarget) ||
                Vector3.Distance(CurrentTarget.transform.position, _player.transform.position) > lockOnBreakRange)
                CurrentTarget = null;
        }

        private static Vector3 TargetPoint(Transform t)
        {
            var gfx = t.Find("Graphics");
            return (gfx != null ? gfx.position : t.position) + Vector3.up * 1.3f;
        }

        private void SetMarker(PlayerIdentity target)
        {
            if (target == null)
            {
                if (_marker != null) _marker.gameObject.SetActive(false);
                return;
            }
            if (_marker == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "LockOnMarker";
                Destroy(go.GetComponent<Collider>());
                var r = go.GetComponent<Renderer>();
                var cfg = GameConfig.Load();
                r.sharedMaterial = cfg.impactMaterial != null ? cfg.impactMaterial : cfg.auraMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                go.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
                _marker = go.transform;
            }
            _marker.gameObject.SetActive(true);
            var gfx = target.transform.Find("Graphics");
            Vector3 top = (gfx != null ? gfx.position : target.transform.position) + Vector3.up * (3.1f + Mathf.Sin(Time.time * 4f) * 0.12f);
            _marker.SetPositionAndRotation(top, Quaternion.Euler(45f, Time.time * 180f, 45f));
        }
    }
}
