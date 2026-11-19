using System.Collections.Generic;
using LightMMORPG.Characters;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightMMORPG.VFX
{
    /// <summary>
    /// Basic-attack effects from the Attack Bench (docs/bench/attack-bench.html), after the owner's reference clip:
    /// ink speed lines over a pale stretched copy of the striking limb (<see cref="LimbSpeedLines"/>), yellow impact sparks
    /// (<see cref="SpawnSpark"/>) and the finisher's speed-line background (<see cref="SpeedLineBackdrop"/>). Visual only.
    /// </summary>
    public static class AttackFx
    {
        public static bool Headless => Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        private static int _sparkSeed;

        /// <summary>Yellow spark burst at a hit point: one bright frame, then it shrinks and fades (finisher: bigger, longer).</summary>
        public static void SpawnSpark(Vector3 position, bool big)
        {
            if (Headless) return;
            var go = new GameObject("ImpactSpark");
            go.transform.position = position;
            go.AddComponent<MeshFilter>().sharedMesh = FxMeshes.Quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = FxMaterials.Make(FxTextures.Spark(_sparkSeed++ % 3), CompareFunction.Always, 3010);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.AddComponent<FxSpark>().Init(big, (_sparkSeed * 137) % 360);
        }

        internal static float Rnd(int i, int k)
        {
            double x = System.Math.Sin(i * 127.1 + k * 311.7) * 43758.5453;
            return (float)(x - System.Math.Floor(x));
        }
    }

    /// <summary>Faces the camera, pops to full size for a frame, then shrinks and fades out.</summary>
    public sealed class FxSpark : MonoBehaviour
    {
        private bool _big;
        private float _age, _spin;
        private Material _mat;

        public void Init(bool big, float spin)
        {
            _big = big;
            _spin = spin;
            _mat = GetComponent<MeshRenderer>().sharedMaterial;
            Update();
        }

        private void Update()
        {
            float life = _big ? 0.22f : 0.14f, u = _age / life;
            if (u >= 1f) { Destroy(_mat); Destroy(gameObject); return; }
            var cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, _spin);
            float size = (_big ? 1.3f : 0.8f) * (u < 0.15f ? 1.15f : 1f - 0.35f * u);
            transform.localScale = new Vector3(size, size, size);
            _mat.color = new Color(1f, 1f, 1f, Mathf.Max(0f, 1f - u * u));
            _age += Time.deltaTime;
        }
    }

    /// <summary>
    /// Speed lines on the striking limb, like a hand-drawn smear: dense ink lines (some white) along the path of points spread
    /// over the forearm / shin, over a pale stretched copy of the limb. Samples are kept in the fighter's Graphics space every
    /// 1/120 s of animation time; the animator ticks it after posing, so it holds still during hit-stop like the pose.
    /// </summary>
    public sealed class LimbSpeedLines : MonoBehaviour
    {
        private const int Hist = 16, Streaks = 26;
        private const float Span = 0.09f, SampleDt = 1f / 120f;

        private struct Sample { public Vector3 A, B, V, W; public float R, Ext, Age; }
        private struct Def { public float S, Phi, R, Len, W; public bool White; }
        private struct Pt { public Vector3 Pos; public float A, W; }

        private static readonly Def[] Defs = MakeDefs();
        private static readonly Color GhostColor = new(1f, 0.9f, 0.85f), InkColor = new(0.13f, 0.09f, 0.1f), WhiteColor = Color.white;

        private readonly List<Sample> _hist = new();
        private readonly List<Pt> _pts = new();
        private Sample _head;
        private bool _hasHead, _limbSet;
        private float _fade, _sampleClock, _hs = 1f;
        private StrikeLimb _limb;
        private Transform _space;
        private Mesh _ghost, _ink;
        private Vector3[] _gPos, _iPos;
        private Color[] _gCol, _iCol;

        private static Def[] MakeDefs()
        {
            var d = new Def[Streaks];
            for (int i = 0; i < Streaks; i++)
                d[i] = new Def
                {
                    S = 0.2f + 1.2f * AttackFx.Rnd(i, 1), Phi = Mathf.PI * 2f * AttackFx.Rnd(i, 2), R = 0.35f + 0.75f * AttackFx.Rnd(i, 3),
                    Len = 0.4f + 0.6f * AttackFx.Rnd(i, 4), White = AttackFx.Rnd(i, 5) < 0.2f, W = 0.0035f + 0.005f * AttackFx.Rnd(i, 6),
                };
            return d;
        }

        public static LimbSpeedLines Create(Transform graphics)
        {
            var go = new GameObject("LimbSpeedLines");
            go.transform.SetParent(graphics, false);
            var s = go.AddComponent<LimbSpeedLines>();
            s._space = graphics;
            s._ghost = s.MakeLayer("Ghost", 2, CompareFunction.LessEqual, 3000, out s._gPos, out s._gCol);
            s._ink = s.MakeLayer("Ink", Streaks, CompareFunction.Always, 3001, out s._iPos, out s._iCol);
            return s;
        }

        private Mesh MakeLayer(string layerName, int strips, CompareFunction zTest, int queue, out Vector3[] pos, out Color[] col)
        {
            var go = new GameObject(layerName);
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = "SpeedLines" + layerName };
            mesh.MarkDynamic();
            pos = new Vector3[strips * Hist * 2];
            col = new Color[pos.Length];
            var tri = new int[strips * (Hist - 1) * 6];
            int t = 0;
            for (int s = 0; s < strips; s++)
                for (int i = 0; i < Hist - 1; i++)
                {
                    int a = (s * Hist + i) * 2;
                    tri[t++] = a; tri[t++] = a + 1; tri[t++] = a + 2;
                    tri[t++] = a + 1; tri[t++] = a + 3; tri[t++] = a + 2;
                }
            mesh.vertices = pos;
            mesh.colors = col;
            mesh.triangles = tri;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 20f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = FxMaterials.Make(null, zTest, queue);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return mesh;
        }

        /// <summary>A new strike started: forget the old trail.</summary>
        public void Begin()
        {
            _hist.Clear();
            _hasHead = false;
            _sampleClock = 0f;
        }

        /// <summary>Called by the animator each (non-frozen) frame after posing. dt = animation time step.</summary>
        public void Tick(CharacterRig rig, AttackMove move, float t, float dt, float hs)
        {
            _hs = hs;
            bool active = move != null && t >= move.Streak.x && t <= move.Streak.y;
            if (active) { _limb = move.Limb; _limbSet = true; }
            _fade = Mathf.Clamp01(_fade + (active ? 1f : -1f) * dt / 0.03f);
            for (int i = 0; i < _hist.Count; i++) { var h = _hist[i]; h.Age += dt; _hist[i] = h; }
            _sampleClock += dt;
            if (_limbSet && (active || _fade > 0f) && dt > 0f)
            {
                _head = Frame(rig);
                _hasHead = true;
                if (_sampleClock >= SampleDt || _hist.Count == 0) { _hist.Add(_head); _sampleClock = 0f; }
            }
            while (_hist.Count > 0 && _hist[0].Age > Span) _hist.RemoveAt(0);
            while (_hist.Count > Hist) _hist.RemoveAt(0);
            if (_fade <= 0f) _hasHead = false;
            Build();
        }

        private Sample Frame(CharacterRig rig)
        {
            bool hand = _limb == StrikeLimb.HandL || _limb == StrikeLimb.HandR;
            int side = _limb == StrikeLimb.HandL || _limb == StrikeLimb.FootL ? -1 : 1;
            var lower = (CharacterBone)CharacterBones.Side(hand ? CharacterBone.LeftLowerArm : CharacterBone.LeftLowerLeg, side);
            var end = lower + 1;
            Transform tl = rig[lower], te = rig[end];
            Vector3 d = rig.BindLocal(end).normalized;
            Vector3 p1 = Vector3.ProjectOnPlane(hand ? rig.BindPalm(side) : Vector3.right, d).normalized;
            Vector3 v = tl.rotation * p1, w = tl.rotation * Vector3.Cross(d, p1);
            return new Sample
            {
                A = _space.InverseTransformPoint(tl.position), B = _space.InverseTransformPoint(te.position),
                V = _space.InverseTransformDirection(v).normalized, W = _space.InverseTransformDirection(w).normalized,
                R = (hand ? 0.05f : 0.065f) * _hs, Ext = hand ? 1.42f : 1.12f,
            };
        }

        private void Build()
        {
            var cam = Camera.main;
            Vector3 camL = cam != null ? _space.InverseTransformPoint(cam.transform.position) : new Vector3(0f, 2f, -5f);
            int n = _hist.Count + (_hasHead ? 1 : 0);
            Sample At(int k) => k < _hist.Count ? _hist[k] : _head;   // oldest first; the live head is the newest

            for (int s = 0; s < 2; s++)
            {
                _pts.Clear();
                for (int k = n - 1; k >= 0 && _pts.Count < Hist; k--)
                {
                    var h = At(k);
                    float u = h.Age / Span;
                    if (u > 1f) break;
                    Vector3 p = s == 0 ? Vector3.LerpUnclamped(h.A, h.B, h.Ext * 0.93f) : Vector3.Lerp(h.A, h.B, 0.25f);
                    _pts.Add(new Pt { Pos = p, A = (1f - u) * 0.55f * _fade, W = h.R * (s == 0 ? 1.5f : 1.2f) * (1f - 0.6f * u) });
                }
                Strip(_gPos, _gCol, s, GhostColor, camL);
            }
            for (int s = 0; s < Streaks; s++)
            {
                var d = Defs[s];
                float maxAge = Span * d.Len;
                _pts.Clear();
                for (int k = n - 1; k >= 0 && _pts.Count < Hist; k--)
                {
                    var h = At(k);
                    float u = h.Age / maxAge;
                    if (u > 1f) break;
                    Vector3 off = h.V * (Mathf.Cos(d.Phi) * d.R * h.R) + h.W * (Mathf.Sin(d.Phi) * d.R * h.R);
                    Vector3 p = Vector3.LerpUnclamped(h.A, h.B, Mathf.Min(d.S, h.Ext)) + off;
                    _pts.Add(new Pt { Pos = p, A = (1f - u) * (d.White ? 0.9f : 0.85f) * _fade, W = d.W * _hs });
                }
                Strip(_iPos, _iCol, s, d.White ? WhiteColor : InkColor, camL);
            }
            _ghost.vertices = _gPos; _ghost.colors = _gCol;
            _ink.vertices = _iPos; _ink.colors = _iCol;
            _ghost.bounds = _ink.bounds = new Bounds(Vector3.zero, Vector3.one * 20f);
        }

        /// <summary>One camera-facing strip through the points (newest first), each with its own alpha and half width.</summary>
        private void Strip(Vector3[] pos, Color[] col, int s, Color color, Vector3 camL)
        {
            for (int i = 0; i < Hist; i++)
            {
                int k = Mathf.Min(i, _pts.Count - 1), b = (s * Hist + i) * 2;
                Vector3 p = Vector3.zero, side = Vector3.zero;
                float a = 0f;
                if (_pts.Count > 1)
                {
                    var P = _pts[k];
                    Vector3 along = _pts[Mathf.Min(_pts.Count - 1, k + 1)].Pos - _pts[Mathf.Max(0, k - 1)].Pos;
                    if (along.sqrMagnitude < 1e-10f) along = Vector3.forward;
                    side = Vector3.Cross(along, camL - P.Pos).normalized * P.W;
                    p = P.Pos;
                    a = i < _pts.Count ? P.A : 0f;
                }
                pos[b] = p + side; pos[b + 1] = p - side;
                color.a = a;
                col[b] = color; col[b + 1] = color;
            }
        }
    }

    /// <summary>
    /// The finisher's speed-line background for the local player: diagonal white / blue lines streaming past, on a
    /// camera-facing sheet far behind the fighters (depth-tested, so the fighters and nearby ground draw in front of it).
    /// </summary>
    public sealed class SpeedLineBackdrop : MonoBehaviour
    {
        private const float Distance = 22f;
        private static SpeedLineBackdrop _inst;
        private Material _mat;
        private MeshRenderer _renderer;
        private float _delay, _hold, _amount, _scroll;
        private bool _on;

        /// <summary>Show the lines after <paramref name="delay"/> seconds for <paramref name="length"/> seconds (fade in / out around it).</summary>
        public static void Play(float delay, float length)
        {
            if (AttackFx.Headless || Camera.main == null) return;
            if (_inst == null) _inst = Create();
            _inst._delay = delay;
            _inst._hold = length;
            _inst._on = true;
        }

        private static SpeedLineBackdrop Create()
        {
            var go = new GameObject("SpeedLineBackdrop");
            go.AddComponent<MeshFilter>().sharedMesh = FxMeshes.Quad;
            var b = go.AddComponent<SpeedLineBackdrop>();
            b._renderer = go.AddComponent<MeshRenderer>();
            b._mat = FxMaterials.Make(FxTextures.Lines, CompareFunction.LessEqual, 2990);
            b._mat.mainTextureScale = new Vector2(2f, 5f);
            b._renderer.sharedMaterial = b._mat;
            b._renderer.shadowCastingMode = ShadowCastingMode.Off;
            b._renderer.receiveShadows = false;
            b._renderer.enabled = false;
            return b;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            float target = 0f;
            if (_on)
            {
                if (_delay > 0f) _delay -= dt;
                else { target = 1f; _hold -= dt; if (_hold <= 0f) _on = false; }
            }
            _amount = Mathf.Clamp01(_amount + (target > _amount ? dt / 0.06f : -dt / 0.18f));
            var cam = Camera.main;
            bool show = _amount > 0.01f && cam != null;
            _renderer.enabled = show;
            if (!show) return;
            var ct = cam.transform;
            float h = 2f * Distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), w = h * cam.aspect, size = Mathf.Sqrt(w * w + h * h) * 1.05f;
            transform.SetPositionAndRotation(ct.position + ct.forward * Distance, ct.rotation * Quaternion.Euler(0f, 0f, -22f));
            transform.localScale = new Vector3(size, size, 1f);
            _scroll += dt * 2.2f;
            _mat.mainTextureOffset = new Vector2(_scroll, 0f);
            _mat.color = new Color(1f, 1f, 1f, _amount);
        }
    }

    internal static class FxMaterials
    {
        public static Material Make(Texture tex, CompareFunction zTest, int queue)
        {
            var shader = Shader.Find("LightMMORPG/FxUnlit") ?? Shader.Find("Sprites/Default");
            var m = new Material(shader) { renderQueue = queue, mainTexture = tex != null ? tex : Texture2D.whiteTexture };
            m.SetFloat("_ZTest", (float)zTest);
            return m;
        }
    }

    internal static class FxMeshes
    {
        private static Mesh _quad;

        /// <summary>Unit quad in XY with white vertex colours.</summary>
        public static Mesh Quad
        {
            get
            {
                if (_quad != null) return _quad;
                _quad = new Mesh
                {
                    name = "FxQuad",
                    vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) },
                    uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) },
                    colors = new[] { Color.white, Color.white, Color.white, Color.white },
                    triangles = new[] { 0, 2, 1, 2, 3, 1 },
                };
                return _quad;
            }
        }
    }

    /// <summary>Effect textures drawn once on the CPU (same shapes as the Attack Bench's canvas drawings).</summary>
    internal static class FxTextures
    {
        private static readonly Texture2D[] Sparks = new Texture2D[3];
        private static Texture2D _lines;

        /// <summary>Yellow shards round a white core, with a few white rays (3 variants).</summary>
        public static Texture2D Spark(int variant)
        {
            if (Sparks[variant] != null) return Sparks[variant];
            const int S = 128;
            int seed = variant == 0 ? 1 : variant == 1 ? 20 : 40;
            // shapes in the bench's 256 px space, centred
            var shards = new Vector2[11][];
            var shardPale = new bool[11];
            for (int i = 0; i < 11; i++)
            {
                float a = i / 11f * Mathf.PI * 2f + AttackFx.Rnd(i, seed) * 0.5f, r0 = 14f + 14f * AttackFx.Rnd(i, seed + 1);
                float r1 = 70f + 56f * AttackFx.Rnd(i, seed + 2), wd = 0.06f + 0.07f * AttackFx.Rnd(i, seed + 3);
                shards[i] = new[] { Polar(a - wd, r0), Polar(a, r1), Polar(a + wd, r0) };
                shardPale[i] = i % 3 == 0;
            }
            var rays = new Vector2[7][];
            for (int i = 0; i < 7; i++)
            {
                float a = AttackFx.Rnd(i, seed + 7) * Mathf.PI * 2f, r1 = 90f + 30f * AttackFx.Rnd(i, seed + 8);
                rays[i] = new[] { Polar(a, 30f), Polar(a, r1) };
            }
            var px = new Color[S * S];
            Color yellow = new(1f, 0.894f, 0.36f), pale = new(1f, 0.97f, 0.77f), rim = new(0.85f, 0.6f, 0.1f);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    // 2×2 supersampled shards (a darker rim near each shard's edge)
                    Color sum = new(0f, 0f, 0f, 0f);
                    float cov = 0f;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            Vector2 p = new((x + 0.25f + sx * 0.5f) * 2f - 128f, (y + 0.25f + sy * 0.5f) * 2f - 128f);
                            for (int i = 0; i < 11; i++)
                            {
                                var tr = shards[i];
                                if (!InTri(p, tr[0], tr[1], tr[2])) continue;
                                bool edge = !InTri(p, Shrink(tr, 0), Shrink(tr, 1), Shrink(tr, 2));
                                sum += (edge ? rim : (shardPale[i] ? pale : yellow)) * 0.25f;
                                cov += 0.25f;
                                break;
                            }
                        }
                    Color c = cov > 0f ? new Color(sum.r / cov, sum.g / cov, sum.b / cov, cov) : new Color(0f, 0f, 0f, 0f);
                    Vector2 q = new((x + 0.5f) * 2f - 128f, (y + 0.5f) * 2f - 128f);
                    foreach (var ray in rays)
                    {
                        float d = SegDist(q, ray[0], ray[1]);
                        float k = Mathf.Clamp01(1.6f - d * 0.8f);
                        if (k > 0f) { c = Color.Lerp(c, Color.white, k); c.a = Mathf.Max(c.a, k); }
                    }
                    float r = q.magnitude, core = r < 34f ? Mathf.Clamp01(1f - r / 34f) * 1.6f : 0f;
                    if (core > 0f) { float k = Mathf.Clamp01(core); c = Color.Lerp(c, Color.white, k); c.a = Mathf.Max(c.a, k); }
                    px[y * S + x] = c;
                }
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = "ImpactSpark" + variant, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.SetPixels(px);
            t.Apply(false, true);
            return Sparks[variant] = t;

            static Vector2 Polar(float a, float r) => new(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            static Vector2 Shrink(Vector2[] tr, int i) => Vector2.Lerp(tr[i], (tr[0] + tr[1] + tr[2]) / 3f, 0.18f);
        }

        /// <summary>Horizontal streaks (white and blue) on transparent, tiling sideways; the backdrop scrolls and tilts it.</summary>
        public static Texture2D Lines
        {
            get
            {
                if (_lines != null) return _lines;
                const int W = 256, H = 128;
                var px = new Color[W * H];
                for (int i = 0; i < 46; i++)
                {
                    bool white = AttackFx.Rnd(i, 16) < 0.55f;
                    int y0 = Mathf.FloorToInt(AttackFx.Rnd(i, 11) * H), th = 1 + Mathf.FloorToInt(AttackFx.Rnd(i, 15) * 2.5f);
                    int x0 = Mathf.FloorToInt(AttackFx.Rnd(i, 12) * W), len = 40 + Mathf.FloorToInt(AttackFx.Rnd(i, 13) * 120f);
                    Color c = white ? new Color(1f, 1f, 1f, 0.85f) : new Color(0.31f, 0.45f, 0.66f, 0.4f);
                    for (int dx = 0; dx < len; dx++)
                    {
                        float taper = Mathf.Sin(Mathf.PI * dx / len);
                        for (int dy = 0; dy < th; dy++)
                        {
                            int x = (x0 + dx) % W, y = Mathf.Min(H - 1, y0 + dy);
                            var cc = c; cc.a *= taper;
                            if (cc.a > px[y * W + x].a) px[y * W + x] = cc;
                        }
                    }
                }
                _lines = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "SpeedLines", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
                _lines.SetPixels(px);
                _lines.Apply(false, true);
                return _lines;
            }
        }

        private static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        private static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);

        private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }
    }
}
