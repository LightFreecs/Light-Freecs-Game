using LightMMORPG.Cameras;
using LightMMORPG.Combat;
using LightMMORPG.Config;
using LightMMORPG.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightMMORPG.VFX
{
    /// <summary>
    /// Visual-only combat effects built from primitives + the GameConfig glow materials (nothing here affects gameplay).
    /// Also hooks <see cref="PlayerStats.OnHitFeedback"/> once to play impact flash, hit-stop, recoil and camera shake.
    /// </summary>
    public static class CombatFx
    {
        private static bool _hooked;
        private static bool Headless => Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            PlayerStats.OnHitFeedback += OnHit;
        }

        private static void OnHit(PlayerStats victim, PlayerStats attacker, Vector3 point, float damage, bool blocked, bool guardBreak, HitKind kind, float impact)
        {
            bool big = kind == HitKind.Finisher || kind == HitKind.Heavy || kind == HitKind.Beam || guardBreak;
            // a charged heavy (impact = its charge) hits harder everywhere: longer freeze, bigger shake, a shockwave, and it
            // sends the victim flying
            float charged = kind == HitKind.Heavy ? Mathf.Clamp01(impact) : 0f;
            float stop = (big ? 0.11f : 0.06f) + 0.12f * charged;
            var victimAnim = victim != null ? victim.GetComponent<PlayerProceduralAnimator>() : null;
            var attackerAnim = attacker != null ? attacker.GetComponent<PlayerProceduralAnimator>() : null;
            if (victimAnim != null)
            {
                victimAnim.HitStop(stop);
                Vector3 dir = attacker != null ? victim.transform.position - attacker.transform.position : -victim.transform.forward;
                victimAnim.PlayRecoil(dir, blocked ? 0.4f + 0.4f * charged : (big ? 1.4f + 0.6f * charged : 0.8f));
                // the body reacts where the fist / foot / blast landed (the server reports the struck spot)
                float react = blocked ? 0.3f : kind == HitKind.Beam ? 1.8f : big ? 1.6f + 0.4f * charged : kind == HitKind.Blast ? 1.2f : 1f;
                victimAnim.PlayHitReaction(point, dir, react, big && !blocked);
                if (kind == HitKind.Heavy && !blocked) victimAnim.PlayLaunch(dir, charged);
            }
            if (attackerAnim != null && kind != HitKind.Blast && kind != HitKind.Beam) attackerAnim.HitStop(stop);

            bool localVictim = victim != null && victim.IsOwner;
            bool localAttacker = attacker != null && attacker.IsOwner;
            if (localVictim) ThirdPersonCamera.Shake(big ? 0.35f + 0.35f * charged : 0.16f, big ? 0.3f + 0.25f * charged : 0.15f);
            else if (localAttacker) ThirdPersonCamera.Shake(big ? 0.18f + 0.22f * charged : 0.07f, 0.12f + 0.12f * charged);
            // anime impact: the camera punches in, a white impact frame, speed lines burst out from the blow
            if ((localVictim || localAttacker) && !blocked)
            {
                if (big)
                {
                    ThirdPersonCamera.Punch(-(4f + 6f * charged), 0.22f + 0.15f * charged);
                    AnimeScreenFx.ImpactFrame(0.22f + 0.28f * charged);
                    AnimeScreenFx.SpeedBurst(0.55f + 0.45f * charged, 0.3f + 0.2f * charged);
                }
                else ThirdPersonCamera.Punch(-1.2f, 0.1f);
            }

            var cfg = GameConfig.Load();
            bool melee = kind == HitKind.Light || kind == HitKind.Finisher || kind == HitKind.Heavy;
            if (melee && !blocked) AttackFx.SpawnSpark(point, big);   // yellow spark burst (Attack Bench)
            else SpawnFlash(point, big ? 1.6f : (blocked ? 0.6f : 0.9f), blocked ? cfg.auraMaterial : cfg.impactMaterial, big ? 0.22f : 0.14f);
            // the shockwave of a charged heavy: a bright burst that grows with the charge
            if (charged > 0.2f) SpawnFlash(point, 1.2f + 2.4f * charged, blocked ? cfg.auraMaterial : cfg.impactMaterial, 0.18f + 0.14f * charged);
        }

        /// <summary>Expanding/fading sphere flash.</summary>
        public static void SpawnFlash(Vector3 position, float size, Material mat, float life)
        {
            if (Headless) return;
            var go = MakeGlow(PrimitiveType.Sphere, "ImpactFlash", mat != null ? mat : GameConfig.Load().impactMaterial);
            go.transform.position = position;
            go.AddComponent<FxFlash>().Init(size, life);
        }

        public static void SpawnBlastVisual(PlayerCombat shooter, Vector3 origin, Vector3 dir, float speed, float life, float radius)
        {
            if (Headless) return;
            var go = MakeGlow(PrimitiveType.Sphere, "BlastVisual", GameConfig.Load().projectileMaterial);
            go.transform.position = origin;
            go.transform.localScale = Vector3.one * radius * 2.4f;
            go.AddComponent<ProjectileVisual>().Init(shooter != null ? shooter.transform : null, dir, speed, life, radius);
        }

        public static void SpawnBeamVisual(Vector3 origin, Vector3 dir, float length, float charge01)
        {
            if (Headless) return;
            var cfg = GameConfig.Load();
            var go = MakeGlow(PrimitiveType.Cylinder, "BeamVisual", cfg.beamMaterial);
            var core = MakeGlow(PrimitiveType.Cylinder, "Core", cfg.impactMaterial);
            core.transform.SetParent(go.transform, false);
            core.transform.localScale = new Vector3(0.45f, 1f, 0.45f);
            float width = Mathf.Lerp(0.7f, 1.8f, charge01);
            go.transform.SetPositionAndRotation(origin + dir * (length * 0.5f), Quaternion.FromToRotation(Vector3.up, dir));
            go.transform.localScale = new Vector3(width, length * 0.5f, width);
            go.AddComponent<BeamVisual>().Init(width, length, 0.4f);
            SpawnFlash(origin + dir * length, width * 1.6f, cfg.impactMaterial, 0.35f);
            ThirdPersonCamera.ShakeNear(origin, 0.12f + 0.15f * charge01, 0.3f);
        }

        public static GameObject MakeGlow(PrimitiveType type, string name, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }
    }

    public sealed class FxFlash : MonoBehaviour
    {
        private float _size, _life, _t;

        public void Init(float size, float life)
        {
            _size = size;
            _life = Mathf.Max(0.01f, life);
            transform.localScale = Vector3.one * size * 0.3f;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float k = _t / _life;
            if (k >= 1f) { Destroy(gameObject); return; }
            // pop out fast, then shrink away
            float s = k < 0.3f ? Mathf.Lerp(0.3f, 1f, k / 0.3f) : Mathf.Lerp(1f, 0f, (k - 0.3f) / 0.7f);
            transform.localScale = Vector3.one * _size * s;
        }
    }

    /// <summary>Client visual copy of an Energy Blast. Stops on the first world/player collider (not the shooter).</summary>
    public sealed class ProjectileVisual : MonoBehaviour
    {
        private Transform _shooter;
        private Vector3 _dir;
        private float _speed, _life, _radius;

        public void Init(Transform shooter, Vector3 dir, float speed, float life, float radius)
        {
            _shooter = shooter;
            _dir = dir.normalized;
            _speed = speed;
            _life = life;
            _radius = radius;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            float step = _speed * dt;
            var hits = Physics.SphereCastAll(transform.position, _radius, _dir, step, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 bestPoint = default;
            foreach (var h in hits)
            {
                if (_shooter != null && h.collider.transform.IsChildOf(_shooter)) continue;
                if (h.distance < best) { best = h.distance; bestPoint = h.distance > 0f ? h.point : transform.position; }
            }
            if (best < float.MaxValue)
            {
                CombatFx.SpawnFlash(bestPoint, 0.9f, GameConfig.Load().projectileMaterial, 0.15f);
                Destroy(gameObject);
                return;
            }
            transform.position += _dir * step;
            _life -= dt;
            if (_life <= 0f) Destroy(gameObject);
        }
    }

    /// <summary>Beam cylinder that flares then thins out over its lifetime.</summary>
    public sealed class BeamVisual : MonoBehaviour
    {
        private float _width, _halfLength, _life, _t;

        public void Init(float width, float length, float life)
        {
            _width = width;
            _halfLength = length * 0.5f;
            _life = life;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float k = _t / _life;
            if (k >= 1f) { Destroy(gameObject); return; }
            float w = _width * (k < 0.15f ? Mathf.Lerp(0.4f, 1.15f, k / 0.15f) : Mathf.Lerp(1.15f, 0.05f, (k - 0.15f) / 0.85f));
            w *= 1f + Mathf.Sin(Time.time * 70f) * 0.06f;
            transform.localScale = new Vector3(w, _halfLength, w);
        }
    }

    /// <summary>Growing glow ball in front of the chest while the Charged Beam is held.</summary>
    public sealed class BeamChargeVisual : MonoBehaviour
    {
        private PlayerCombat _owner;
        private Transform _follow;
        private bool _active;
        private float _start;
        private float _maxCharge = 1.5f;

        public static BeamChargeVisual Create(PlayerCombat owner)
        {
            var cfg = GameConfig.Load();
            var go = CombatFx.MakeGlow(PrimitiveType.Sphere, "BeamCharge", cfg.beamMaterial);
            var v = go.AddComponent<BeamChargeVisual>();
            v._owner = owner;
            var gfx = owner.transform.Find("Graphics");
            v._follow = gfx != null ? gfx : owner.transform;
            if (cfg.chargedBeam != null && cfg.chargedBeam.maxChargeTime > 0f) v._maxCharge = cfg.chargedBeam.maxChargeTime;
            go.SetActive(false);
            return v;
        }

        public void Begin()
        {
            _active = true;
            _start = Time.time;
            gameObject.SetActive(!Application.isBatchMode);
        }

        public void End()
        {
            _active = false;
            gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!_active || _owner == null || _follow == null) { End(); if (_owner == null) Destroy(gameObject); return; }
            float c = Mathf.Clamp01((Time.time - _start) / _maxCharge);
            float pulse = 1f + Mathf.Sin(Time.time * (c >= 1f ? 40f : 18f)) * 0.12f;
            transform.position = _follow.position + Vector3.up * 1.35f + _follow.forward * 0.85f;
            transform.localScale = Vector3.one * Mathf.Lerp(0.25f, 0.95f, c) * pulse;
        }
    }
}
