using UnityEngine;
using UnityEngine.Rendering;

namespace LightMMORPG.VFX
{
    /// <summary>
    /// The hand-drawn anime flame aura (shader <c>LightMMORPG/AnimeAura</c>, bench <c>docs/bench/aura-bench.html</c>): the
    /// owner's reference outline (a tiered flame "pine tree" with one peak over the head, round a see-through arch where
    /// the fighter stands), its tips licking and boiling. Two camera-facing quads at the fighter's feet, the flame just
    /// behind the fighter and the rising flicks just in front.
    /// Set <see cref="Visible"/>, <see cref="Size"/> (0.85 charging, 1 Power Surge), <see cref="Color"/> and
    /// <see cref="Height"/> (the fighter's height); it flares up when it appears and shrinks away when it stops.
    /// </summary>
    [DefaultExecutionOrder(220)]
    public sealed class AnimeAura : MonoBehaviour
    {
        /// <summary>How bright the aura colour is drawn (HDR, so the spikes catch Bloom).</summary>
        public const float Intensity = 1.35f;

        // The quad spans aura space x -0.55..0.55, y -0.04..1.21 (the fighter is 0.62 tall in it; the traced outline's
        // wings reach x ±0.5 and its peak ~1.0 at full power).
        private const float SpanX = 1.1f, SpanY = 1.25f, FeetY = 0.04f, FighterY = 0.62f;

        public bool Visible { get; set; }
        public float Size { get; set; } = 0.85f;
        public float Height { get; set; } = 2.3f;
        /// <summary>The aura colour (any brightness; it is normalised and drawn at <see cref="Intensity"/>).</summary>
        public Color Color { get; set; } = new(1f, 0.85f, 0.1f);

        private static Shader _shader;
        private Material _back, _front;
        private Transform _backQ, _frontQ;
        private float _fade, _flare;
        private MaterialPropertyBlock _mpb;
        private float _seed;

        public static AnimeAura Create(Transform parent)
        {
            var go = new GameObject("AnimeAura");
            go.transform.SetParent(parent, false);
            return go.AddComponent<AnimeAura>();
        }

        private void Awake()
        {
            if (_shader == null) _shader = Resources.Load<Shader>("VFX/AnimeAura");
            if (_shader == null) { Debug.LogWarning("[AnimeAura] shader Resources/VFX/AnimeAura missing"); enabled = false; return; }
            _back = new Material(_shader) { name = "AnimeAura (back)" };
            _front = new Material(_shader) { name = "AnimeAura (front)" };
            _front.SetFloat("_Front", 1f);
            _back.renderQueue = _front.renderQueue = (int)RenderQueue.Transparent;
            _backQ = MakeQuad("Back", _back);
            _frontQ = MakeQuad("Front", _front);
            _seed = Random.value * 50f;
            _mpb = new MaterialPropertyBlock();
            SetShown(false);
        }

        private Transform MakeQuad(string name, Material m)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Destroy(q.GetComponent<Collider>());
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            q.transform.SetParent(transform, false);
            return q.transform;
        }

        private void SetShown(bool on)
        {
            if (_backQ.gameObject.activeSelf != on) { _backQ.gameObject.SetActive(on); _frontQ.gameObject.SetActive(on); }
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            _fade = Mathf.MoveTowards(_fade, Visible ? 1f : 0f, dt / (Visible ? 0.12f : 0.25f));
            // flare: bursts out a little oversized when it starts, settles in a quarter second
            _flare = Visible && _fade < 1f ? 1f : Mathf.MoveTowards(_flare, 0f, dt / 0.25f);
            SetShown(_fade > 0.001f);
            if (_fade <= 0.001f) return;
            var cam = Camera.main;
            if (cam == null) return;

            // face the camera about the vertical (a hand-drawn aura always reads upright)
            Vector3 feet = transform.position;
            Vector3 view = feet - cam.transform.position;
            view.y = 0f;
            if (view.sqrMagnitude < 1e-6f) view = cam.transform.forward;
            view.y = 0f;
            Vector3 fwd = view.sqrMagnitude > 1e-6f ? view.normalized : Vector3.forward;
            var rot = Quaternion.LookRotation(fwd, Vector3.up);
            float h = Mathf.Max(0.5f, Height);
            float qh = h * SpanY / FighterY, qw = h * SpanX / FighterY;
            Vector3 centre = feet + Vector3.up * (qh * (0.5f - FeetY / SpanY));
            Place(_backQ, centre + fwd * (0.18f * h), rot, qw, qh);
            Place(_frontQ, centre - fwd * (0.16f * h), rot, qw, qh);

            float max = Mathf.Max(Color.r, Mathf.Max(Color.g, Color.b));
            var c = max > 1e-4f ? Color / max : Color.white;
            c.a = 1f;
            float ease = _fade * _fade * (3f - 2f * _fade);
            float size = Size * Mathf.Lerp(0.55f, 1f, ease) * (1f + 0.12f * _flare * Mathf.Sin(_fade * Mathf.PI));
            Apply(_backQ, c * Intensity, size, ease);
            Apply(_frontQ, c * Intensity, size, ease);
        }

        private static void Place(Transform q, Vector3 pos, Quaternion rot, float w, float h)
        {
            q.SetPositionAndRotation(pos, rot);
            var ps = q.parent != null ? q.parent.lossyScale : Vector3.one;
            q.localScale = new Vector3(w / Mathf.Max(1e-4f, Mathf.Abs(ps.x)), h / Mathf.Max(1e-4f, Mathf.Abs(ps.y)), 1f);
        }

        private void Apply(Transform q, Color c, float size, float fade)
        {
            var r = q.GetComponent<MeshRenderer>();
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_Color", c);
            _mpb.SetFloat("_Strength", size);
            _mpb.SetFloat("_Fade", fade);
            _mpb.SetFloat("_SpanX", SpanX);
            _mpb.SetFloat("_Seed", _seed);
            r.SetPropertyBlock(_mpb);
        }

        private void OnDestroy()
        {
            if (_back != null) Destroy(_back);
            if (_front != null) Destroy(_front);
        }
    }
}
