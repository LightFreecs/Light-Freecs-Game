using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightMMORPG.Menu
{
    /// <summary>
    /// Procedural floating rock island for the main menu (runtime only, deterministic per seed): grass cap with a
    /// drippy overhanging lip, faceted rock body tapering to a point (flat-shaded ToonLit + a smooth-normal outline
    /// shell so the outline does not crack), a stone arena disc with a glowing ring, two broken pillars, a few boulders
    /// and small rocks hanging underneath that bob on their own. <see cref="TopY"/> is where the fighter stands.
    /// </summary>
    public sealed class FloatingIsland : MonoBehaviour
    {
        public float Radius { get; private set; }
        public float TopY { get; private set; }

        private readonly List<(Transform t, Vector3 basePos, float phase, float amp)> _bobbers = new();
        private float[] _phase;

        public static FloatingIsland Create(Transform parent, MenuMaterials m, int seed = 7, float radius = 3.8f, float depth = 6.5f)
        {
            var go = new GameObject("FloatingIsland");
            go.transform.SetParent(parent, false);
            var island = go.AddComponent<FloatingIsland>();
            island.Build(m, seed, radius, depth);
            return island;
        }

        private float EdgeR(float a)
        {
            return Radius * (1f + 0.07f * Mathf.Sin(3f * a + _phase[0]) + 0.05f * Mathf.Sin(5f * a + _phase[1]) + 0.03f * Mathf.Sin(9f * a + _phase[2]));
        }

        private void Build(MenuMaterials m, int seed, float radius, float depth)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            Radius = radius;
            _phase = new[] { R() * 6.28f, R() * 6.28f, R() * 6.28f };
            const float dome = 0.16f;

            // ---- grass cap + drippy lip (smooth)
            {
                var mb = new MeshBuilder();
                const int seg = 56, rings = 5;
                int center = mb.V(new Vector3(0f, dome, 0f));
                var ringIdx = new int[rings + 2][];
                for (int k = 1; k <= rings + 1; k++)
                {
                    ringIdx[k] = new int[seg];
                    for (int i = 0; i < seg; i++)
                    {
                        float a = i * Mathf.PI * 2f / seg;
                        float f, y;
                        if (k <= rings) { f = (float)k / rings; y = dome * (1f - f * f); }
                        else { f = 1.05f; y = -0.06f; }
                        float r = EdgeR(a) * f;
                        ringIdx[k][i] = mb.V(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                    }
                }
                // drip ring: grass hanging over the rock in uneven tufts
                var drip = new int[seg];
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    float d = R() < 0.4f ? 0.18f + R() * 0.32f : 0.02f + R() * 0.08f;
                    float r = EdgeR(a) * 1.02f;
                    drip[i] = mb.V(new Vector3(Mathf.Cos(a) * r, -0.2f - d, Mathf.Sin(a) * r));
                }
                for (int i = 0; i < seg; i++)
                {
                    int j = (i + 1) % seg;
                    mb.Tri(center, ringIdx[1][i], ringIdx[1][j], Vector3.up);
                    for (int k = 1; k <= rings; k++)
                        mb.Quad(ringIdx[k][i], ringIdx[k][j], ringIdx[k + 1][j], ringIdx[k + 1][i], Vector3.up);
                    Vector3 outDir = mb.Pos(ringIdx[rings + 1][i]).normalized;
                    mb.Quad(ringIdx[rings + 1][i], ringIdx[rings + 1][j], drip[j], drip[i], outDir + Vector3.down * 0.3f);
                }
                var mesh = mb.ToMesh("IslandGrass", flat: false);
                Part("Grass", mesh, m.grass, true);
            }

            // ---- rock body (faceted) + outline shell
            {
                var mb = new MeshBuilder();
                const int seg = 18, layers = 7;
                var ring = new int[layers + 1][];
                var axis = new Vector3[layers + 1];
                for (int k = 0; k <= layers; k++)
                {
                    float t = (float)k / (layers + 1);
                    ring[k] = new int[seg];
                    axis[k] = new Vector3(0.35f * t * t, 0f, -0.15f * t * t);
                    float y = -0.08f - depth * Mathf.Pow(t, 0.9f);
                    for (int i = 0; i < seg; i++)
                    {
                        float a = (i + (k > 0 ? (R() - 0.5f) * 0.35f : 0f)) * Mathf.PI * 2f / seg;
                        float f = k == 0 ? 0.985f : Mathf.Pow(1f - t, 1.25f) * (0.86f + 0.3f * R());
                        float r = EdgeR(a) * f;
                        float yy = y + (k == 0 ? 0f : (R() - 0.5f) * depth * 0.07f);
                        axis[k].y = y;
                        mb.V(new Vector3(axis[k].x + Mathf.Cos(a) * r, yy, axis[k].z + Mathf.Sin(a) * r));
                        ring[k][i] = mb.Count - 1;
                    }
                }
                int tip = mb.V(new Vector3(0.5f, -depth - 0.7f, -0.2f));
                for (int k = 0; k < layers; k++)
                    for (int i = 0; i < seg; i++)
                    {
                        int j = (i + 1) % seg;
                        Vector3 c = (mb.Pos(ring[k][i]) + mb.Pos(ring[k + 1][j])) * 0.5f;
                        Vector3 outDir = c - new Vector3(axis[k].x, c.y, axis[k].z) + Vector3.down * 0.2f;
                        mb.Quad(ring[k][i], ring[k][j], ring[k + 1][j], ring[k + 1][i], outDir);
                    }
                for (int i = 0; i < seg; i++)
                {
                    int j = (i + 1) % seg;
                    Vector3 c = (mb.Pos(ring[layers][i]) + mb.Pos(ring[layers][j])) * 0.5f;
                    mb.Tri(ring[layers][i], ring[layers][j], tip, c - new Vector3(0.5f, c.y, -0.2f) + Vector3.down * 0.5f);
                }
                Part("Rock", mb.ToMesh("IslandRock", flat: true), m.rock, true);
                if (m.outline != null) Part("RockOutline", mb.ToMesh("IslandRockOutline", flat: false), m.outline, false);
            }

            // ---- stone arena disc + glowing ring
            float discR = radius * 0.6f, discH = 0.22f;
            TopY = dome + 0.06f + discH * 0.5f + 0.02f;
            {
                var disc = Prism("ArenaDisc", 40, discR, discH);
                var t = Part("ArenaDisc", disc, m.stone, true).transform;
                t.localPosition = new Vector3(0f, dome + 0.06f, 0f);
                var ringMesh = Torus(64, 8, discR * 0.8f, 0.045f);
                var rt = Part("ArenaRing", ringMesh, m.ring, false).transform;
                rt.localPosition = new Vector3(0f, TopY + 0.005f, 0f);
                rt.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            // ---- broken pillars at the left/right edge, a few boulders
            {
                float[] side = { -1f, 1f };
                float[] heights = { 1.9f, 1.25f };
                for (int s = 0; s < 2; s++)
                {
                    float a = Mathf.PI * (s == 0 ? 0.93f : 0.06f);
                    float r = EdgeR(a) * 0.82f;
                    var p = Part("Pillar_" + s, Prism("Pillar", 10, 0.26f, heights[s]), m.stone, true).transform;
                    p.localPosition = new Vector3(Mathf.Cos(a) * r, dome + heights[s] * 0.5f - 0.05f, Mathf.Sin(a) * r + 0.6f);
                    p.localRotation = Quaternion.Euler(side[s] * 3f, R() * 90f, side[s] * -4f);
                    var cap = Part("PillarCap_" + s, Prism("PillarCap", 10, 0.34f, 0.14f), m.stone, true).transform;
                    cap.SetParent(p, false);
                    cap.localPosition = new Vector3(0f, -heights[s] * 0.5f + 0.07f, 0f);
                }
                for (int b = 0; b < 5; b++)
                {
                    float a = Mathf.PI * (0.15f + R() * 0.7f) + (b % 2 == 0 ? Mathf.PI : 0f);
                    float r = EdgeR(a) * (0.75f + R() * 0.15f);
                    float size = 0.18f + R() * 0.22f;
                    var bt = Part("Boulder_" + b, Boulder(rng, size), m.rock, true).transform;
                    bt.localPosition = new Vector3(Mathf.Cos(a) * r, dome * 0.4f + size * 0.3f, Mathf.Sin(a) * r);
                }
            }

            // ---- small rocks hanging underneath
            for (int h = 0; h < 5; h++)
            {
                float a = h * 1.3f + 0.4f;
                float r = radius * (0.95f + R() * 0.7f);
                var chunk = new GameObject("HangingRock_" + h).transform;
                chunk.SetParent(transform, false);
                var basePos = new Vector3(Mathf.Cos(a) * r, -1.6f - R() * 3.2f, Mathf.Sin(a) * r * 0.8f + 0.8f);
                chunk.localPosition = basePos;
                float s = 0.35f + R() * 0.45f;
                var mesh = MiniRock(rng, s);
                var body = Part("Body", mesh.flat, m.rock, true);
                body.transform.SetParent(chunk, false);
                if (m.outline != null) { var o = Part("Outline", mesh.smooth, m.outline, false); o.transform.SetParent(chunk, false); }
                var grass = Part("Grass", Prism("Tuft", 9, s * 0.95f, 0.12f), m.grass, true);
                grass.transform.SetParent(chunk, false);
                grass.transform.localPosition = new Vector3(0f, 0.04f, 0f);
                _bobbers.Add((chunk, basePos, R() * 6.28f, 0.12f + R() * 0.12f));
            }
        }

        private void Update()
        {
            float t = Time.time;
            foreach (var b in _bobbers)
            {
                b.t.localPosition = b.basePos + Vector3.up * (Mathf.Sin(t * 0.9f + b.phase) * b.amp);
                b.t.localRotation = Quaternion.Euler(0f, t * 6f + b.phase * 57f, Mathf.Sin(t * 0.7f + b.phase) * 4f);
            }
        }

        private GameObject Part(string name, Mesh mesh, Material mat, bool shadows)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = shadows;
            return go;
        }

        // ---------------- small mesh helpers ----------------

        /// <summary>Upright n-gon prism centred on the origin (height along Y).</summary>
        public static Mesh Prism(string name, int seg, float radius, float height)
        {
            var mb = new MeshBuilder();
            float h = height * 0.5f;
            int top = mb.V(new Vector3(0, h, 0)), bot = mb.V(new Vector3(0, -h, 0));
            var up = new int[seg];
            var dn = new int[seg];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var p = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                up[i] = mb.V(p + Vector3.up * h);
                dn[i] = mb.V(p - Vector3.up * h);
            }
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                mb.Tri(top, up[i], up[j], Vector3.up);
                mb.Tri(bot, dn[j], dn[i], Vector3.down);
                Vector3 o = (mb.Pos(up[i]) + mb.Pos(up[j])) * 0.5f;
                o.y = 0f;
                mb.Quad(up[i], up[j], dn[j], dn[i], o);
            }
            return mb.ToMesh(name, flat: true);
        }

        public static Mesh Torus(int seg, int tubeSeg, float radius, float tube)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var c = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int j = 0; j <= tubeSeg; j++)
                {
                    float b = j * Mathf.PI * 2f / tubeSeg;
                    var n = c * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    verts.Add(c * radius + n * tube);
                    norms.Add(n);
                }
            }
            int row = tubeSeg + 1;
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < tubeSeg; j++)
                {
                    int a = i * row + j, b = (i + 1) * row + j;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(b); tris.Add(a + 1); tris.Add(b + 1);
                }
            var mesh = new Mesh { name = "Torus" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            // make sure faces point outwards
            var tr = mesh.triangles;
            if (Vector3.Dot(Vector3.Cross(verts[tr[1]] - verts[tr[0]], verts[tr[2]] - verts[tr[0]]), norms[tr[0]]) < 0f)
            {
                for (int i = 0; i < tr.Length; i += 3) (tr[i + 1], tr[i + 2]) = (tr[i + 2], tr[i + 1]);
                mesh.triangles = tr;
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh Boulder(System.Random rng, float size)
        {
            var mb = new MeshBuilder();
            // squashed, jittered octahedron-ish blob: 2 rings of 6 + poles
            int top = mb.V(new Vector3(0, size * 0.8f, 0)), bot = mb.V(new Vector3(0, -size * 0.35f, 0));
            var r1 = new int[6];
            var r2 = new int[6];
            for (int i = 0; i < 6; i++)
            {
                float a = (i + (float)rng.NextDouble() * 0.4f) * Mathf.PI / 3f;
                float k = size * (0.85f + (float)rng.NextDouble() * 0.35f);
                r1[i] = mb.V(new Vector3(Mathf.Cos(a) * k, size * 0.35f, Mathf.Sin(a) * k));
                float a2 = a + 0.5f;
                r2[i] = mb.V(new Vector3(Mathf.Cos(a2) * k * 0.9f, -size * 0.1f, Mathf.Sin(a2) * k * 0.9f));
            }
            for (int i = 0; i < 6; i++)
            {
                int j = (i + 1) % 6;
                mb.Tri(top, r1[i], r1[j], Vector3.up);
                Vector3 c = mb.Pos(r1[i]) + mb.Pos(r2[i]);
                c.y = 0f;
                mb.Tri(r1[i], r2[i], r1[j], c);
                mb.Tri(r1[j], r2[i], r2[j], c);
                mb.Tri(bot, r2[j], r2[i], Vector3.down);
            }
            return mb.ToMesh("Boulder", flat: true);
        }

        private static (Mesh flat, Mesh smooth) MiniRock(System.Random rng, float size)
        {
            var mb = new MeshBuilder();
            const int seg = 7;
            int top = mb.V(new Vector3(0, 0.05f, 0));
            var a0 = new int[seg];
            var a1 = new int[seg];
            for (int i = 0; i < seg; i++)
            {
                float a = (i + (float)rng.NextDouble() * 0.3f) * Mathf.PI * 2f / seg;
                float k = size * (0.85f + (float)rng.NextDouble() * 0.3f);
                a0[i] = mb.V(new Vector3(Mathf.Cos(a) * k, 0f, Mathf.Sin(a) * k));
                a1[i] = mb.V(new Vector3(Mathf.Cos(a + 0.4f) * k * 0.55f, -size * (0.6f + (float)rng.NextDouble() * 0.3f), Mathf.Sin(a + 0.4f) * k * 0.55f));
            }
            int tip = mb.V(new Vector3(size * 0.1f, -size * 1.7f, 0f));
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                mb.Tri(top, a0[i], a0[j], Vector3.up);
                Vector3 c = mb.Pos(a0[i]) + mb.Pos(a1[i]);
                c.y = 0f;
                mb.Quad(a0[i], a0[j], a1[j], a1[i], c);
                Vector3 c2 = mb.Pos(a1[i]);
                c2.y = 0f;
                mb.Tri(a1[i], a1[j], tip, c2 + Vector3.down);
            }
            return (mb.ToMesh("MiniRock", flat: true), mb.ToMesh("MiniRockOutline", flat: false));
        }
    }

    /// <summary>Tiny indexed mesh builder that fixes triangle winding from an expected outward direction.</summary>
    public sealed class MeshBuilder
    {
        private readonly List<Vector3> _v = new();
        private readonly List<int> _t = new();
        public int Count => _v.Count;
        public int V(Vector3 p) { _v.Add(p); return _v.Count - 1; }
        public Vector3 Pos(int i) => _v[i];

        public void Tri(int a, int b, int c, Vector3 outward)
        {
            // Unity: clockwise = front; RecalculateNormals uses cross(b-a, c-a)
            if (Vector3.Dot(Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]), outward) < 0f) (b, c) = (c, b);
            _t.Add(a); _t.Add(b); _t.Add(c);
        }

        public void Quad(int a, int b, int c, int d, Vector3 outward)
        {
            Tri(a, b, c, outward);
            Tri(a, c, d, outward);
        }

        /// <param name="flat">Duplicate vertices per triangle (faceted look).</param>
        public Mesh ToMesh(string name, bool flat)
        {
            var mesh = new Mesh { name = name };
            if (flat)
            {
                var v = new Vector3[_t.Count];
                var t = new int[_t.Count];
                for (int i = 0; i < _t.Count; i++) { v[i] = _v[_t[i]]; t[i] = i; }
                if (v.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.vertices = v;
                mesh.triangles = t;
            }
            else
            {
                mesh.SetVertices(_v);
                mesh.SetTriangles(_t, 0);
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
