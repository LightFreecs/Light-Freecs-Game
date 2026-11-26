using LightMMORPG.Characters;
using LightMMORPG.Config;
using LightMMORPG.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightMMORPG.VFX
{
    /// <summary>
    /// The fighter's sword in sword stance: a placeholder blade built from cubes in the right fist (until one is modelled in
    /// Blender), a glow shell whose colour and strength show the readied sword skill, and a light ribbon traced between the
    /// blade's base and tip while a strike is live (the animator's <see cref="PlayerProceduralAnimator.InStrike"/>) and the
    /// tip sweeps fast, so every slash leaves an anime arc and wind-ups don't smear.
    /// The blade leaves the fist on the thumb side, tipped 0.35 along the forearm; its flat faces the palm and its edges run along
    /// the forearm, like a real grip, so the sword moves (BasicMovesets.Sword) cut edge first. It is placed in the hand's own frame
    /// from the bind pose, so it rides the fist roll and the wrist bend (<see cref="AttackPose.WristR"/>) whatever pose it was drawn
    /// in. The same placement is in ArtSource/Player/player.blend (SwordProxy) and docs/bench/sword-bench.html.
    /// </summary>
    public sealed class SwordVisual : MonoBehaviour
    {
        private const int Samples = 12;
        private const float IdleGlow = 0.35f;
        private static readonly Color IdleColor = new(0.5f, 1.2f, 2.4f);
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        private Transform _root, _base, _tip;
        private Renderer _glow;
        private PlayerProceduralAnimator _anim;
        private MaterialPropertyBlock _mpb;
        private GameObject _ribbonGo;
        private Mesh _ribbon;
        private Material _ribbonMat;
        private readonly Vector3[] _b = new Vector3[Samples], _t = new Vector3[Samples];
        private readonly float[] _a = new float[Samples];
        private readonly Vector3[] _verts = new Vector3[Samples * 2];
        private readonly Color[] _cols = new Color[Samples * 2];
        private int _count;
        private Color _color = IdleColor;
        private float _glowNow = IdleGlow, _glowTarget = IdleGlow, _holdUntil;

        public bool Drawn { get; private set; }

        public static SwordVisual For(Component fighter) =>
            fighter.TryGetComponent(out SwordVisual v) ? v : fighter.gameObject.AddComponent<SwordVisual>();

        public void SetDrawn(bool on)
        {
            Drawn = on;
            if (on) Build();
            if (_root != null) _root.gameObject.SetActive(on);
            if (_ribbonGo != null) _ribbonGo.SetActive(on);
            _count = 0;
        }

        /// <summary>Blade glow: colour (HDR) and strength (0.35 idle … 3 skill), held for <paramref name="hold"/> seconds.</summary>
        public void SetGlow(Color c, float strength, float hold = 0f)
        {
            _color = c;
            _glowTarget = strength;
            _holdUntil = Time.time + hold;
            if (hold > 0f) _glowNow = Mathf.Max(_glowNow, strength);
        }

        private void Build()
        {
            if (_root != null) return;
            var rig = GetComponentInChildren<CharacterRig>();
            Transform hand = rig != null ? rig[CharacterBone.RightHand] : null;
            if (hand == null) return;
            _anim = GetComponent<PlayerProceduralAnimator>();
            // in the hand's own frame (the rig's bind rotations are identity): the forearm's line and the thumb's side, from the bind pose
            Vector3 fore = rig.BindLocal(CharacterBone.RightHand);
            float forearm = Mathf.Max(0.12f, fore.magnitude);
            fore /= forearm;
            // the blade leaves the fist on the thumb side, tipped a little along the forearm
            Vector3 up = Vector3.ProjectOnPlane(rig.BindLocal(CharacterBone.RightThumbProximal), fore);
            if (up.sqrMagnitude < 1e-8f) up = Vector3.ProjectOnPlane(Vector3.forward, fore);
            Vector3 axis = (up.normalized + fore * 0.35f).normalized;

            _root = new GameObject("Sword").transform;
            _root.SetParent(hand, false);
            _root.localPosition = fore * (forearm * 0.25f);
            _root.localRotation = Quaternion.LookRotation(axis, fore);

            float k = forearm / 0.27f;            // ~1 on the default hero
            float len = 0.95f * k, start = 0.11f * k;
            var cfg = GameConfig.Load();
            var steel = new Material(cfg.fighterBodyMaterial);
            Tint(steel, new Color(0.8f, 0.84f, 0.92f));
            var dark = new Material(cfg.fighterBodyMaterial);
            Tint(dark, new Color(0.1f, 0.1f, 0.14f));
            // local x = the flat's normal (the palm's side), y = along the edges (the forearm's line), z = the blade
            Box("Grip", new Vector3(0f, 0f, -0.02f * k), new Vector3(0.035f, 0.035f, 0.2f) * k, dark);
            Box("Guard", new Vector3(0f, 0f, 0.09f * k), new Vector3(0.035f, 0.2f, 0.04f) * k, dark);
            Box("Blade", new Vector3(0f, 0f, start + len * 0.5f), new Vector3(0.012f * k, 0.055f * k, len), steel);
            _glow = Box("Glow", new Vector3(0f, 0f, start + len * 0.5f), new Vector3(0.045f * k, 0.11f * k, len * 1.04f), new Material(cfg.auraMaterial));
            _base = Point("Base", start + len * 0.12f);
            _tip = Point("Tip", start + len);

            _ribbonGo = new GameObject("SwordTrail");
            _ribbon = new Mesh { name = "SwordTrail" };
            _ribbon.MarkDynamic();
            _ribbon.vertices = _verts;
            var tris = new int[(Samples - 1) * 12];   // both windings: seen from either side
            for (int i = 0, n = 0; i < Samples - 1; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                tris[n++] = a; tris[n++] = b; tris[n++] = c; tris[n++] = c; tris[n++] = b; tris[n++] = d;
                tris[n++] = a; tris[n++] = c; tris[n++] = b; tris[n++] = c; tris[n++] = d; tris[n++] = b;
            }
            _ribbon.triangles = tris;
            _ribbonGo.AddComponent<MeshFilter>().sharedMesh = _ribbon;
            var mr = _ribbonGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _ribbonMat = FxMaterials.Make(null, CompareFunction.LessEqual, (int)RenderQueue.Transparent + 2);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private static void Tint(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        private Renderer Box(string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return r;
        }

        private Transform Point(string name, float z)
        {
            var t = new GameObject(name).transform;
            t.SetParent(_root, false);
            t.localPosition = new Vector3(0f, 0f, z);
            return t;
        }

        private void LateUpdate()
        {
            if (!Drawn) return;
            if (_root == null) Build();   // the rig wasn't there yet, or was rebuilt (customisation)
            if (_root == null) return;
            if (_ribbonGo != null && !_ribbonGo.activeSelf) _ribbonGo.SetActive(true);
            if (_holdUntil > 0f && Time.time > _holdUntil)
            {
                _holdUntil = 0f;
                _glowTarget = IdleGlow;
                _color = IdleColor;
            }
            _glowNow = Mathf.MoveTowards(_glowNow, _glowTarget, Time.deltaTime * 8f);
            _mpb ??= new MaterialPropertyBlock();
            _glow.GetPropertyBlock(_mpb);
            _mpb.SetColor(ColorId, _color);
            _mpb.SetFloat(IntensityId, _glowNow);
            _glow.SetPropertyBlock(_mpb);

            // slash ribbon: shift the samples, add this frame's blade, fade each by age and by how fast the tip swept then; a new
            // sample only shows while a strike is live (no rig: whenever it sweeps fast)
            for (int i = Samples - 1; i > 0; i--) { _b[i] = _b[i - 1]; _t[i] = _t[i - 1]; _a[i] = _a[i - 1]; }
            Vector3 tip = _tip.position;
            float sweep = _count > 0 ? (tip - _t[1]).magnitude / Mathf.Max(Time.deltaTime, 1e-3f) : 0f;
            _b[0] = _base.position;
            _t[0] = tip;
            _a[0] = _anim == null || _anim.InStrike ? Mathf.InverseLerp(4f, 14f, sweep) : 0f;
            _count = Mathf.Min(_count + 1, Samples);
            for (int i = 0; i < Samples; i++)
            {
                int j = Mathf.Min(i, _count - 1);
                float a = _a[j] * (1f - i / (float)(Samples - 1));
                _verts[i * 2] = _b[j];
                _verts[i * 2 + 1] = _t[j];
                _cols[i * 2] = new Color(1f, 1f, 1f, a * 0.15f);
                _cols[i * 2 + 1] = new Color(1f, 1f, 1f, a);
            }
            _ribbon.vertices = _verts;
            _ribbon.colors = _cols;
            _ribbon.RecalculateBounds();
            _ribbonMat.SetColor(ColorId, _color * (0.7f + 0.3f * _glowNow));
        }

        private void OnDestroy()
        {
            if (_ribbonGo != null) Destroy(_ribbonGo);
        }
    }
}
