using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightMMORPG.Menu
{
    /// <summary>
    /// Cel-shaded cloud field around the menu island: lumpy cumulus meshes (merged spheres with a flat bottom, small
    /// highlight bubbles on top, wispy tails) in a back layer, at the island's sides and low in front of the camera, plus
    /// a wide cloud bank below the island that reaches the horizon. Clouds drift sideways and wrap, bob gently and turn
    /// to face the camera (yaw only). Colours come from the <c>LightMMORPG/ToonCloud</c> materials set by <see cref="MenuSky"/>.
    /// </summary>
    public sealed class ToonClouds : MonoBehaviour
    {
        private sealed class Cloud
        {
            public Transform t;
            public Vector3 basePos;
            public float speed, bobPhase, bobAmp, wrap;
        }

        private readonly List<Cloud> _clouds = new();
        private Transform _cam;
        private float _speedScale = 1f;

        public static ToonClouds Create(Transform parent, MenuMaterials m, Transform cam, float speedScale, int seed = 11)
        {
            var go = new GameObject("Clouds");
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<ToonClouds>();
            c._cam = cam;
            c._speedScale = speedScale;
            c.Build(m, seed);
            return c;
        }

        private void Build(MenuMaterials m, int seed)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            float Range(float a, float b) => a + (b - a) * R();

            // back layer (behind the island, fades into fog)
            for (int i = 0; i < 6; i++)
                Add(m.cloud, rng, new Vector3(Mathf.Lerp(-50f, 50f, i / 5f) + Range(-6f, 6f), Range(-6f, 1.5f), Range(32f, 60f)), Range(10f, 18f), Range(0.25f, 0.6f), 75f);
            // flanking clouds at the island's height, left and right of it
            Add(m.cloud, rng, new Vector3(-11.5f, 0.6f, 7f), 7f, 0.35f, 32f);
            Add(m.cloud, rng, new Vector3(12.5f, 1.4f, 10f), 7.5f, 0.3f, 32f);
            Add(m.cloud, rng, new Vector3(-14f, -3.2f, 2f), 8f, 0.4f, 32f);
            Add(m.cloud, rng, new Vector3(13.5f, -2.6f, 0f), 6.5f, 0.45f, 32f);
            // low front clouds framing the bottom corners
            Add(m.cloud, rng, new Vector3(-7.2f, -4.2f, -4f), 5f, 0.5f, 16f);
            Add(m.cloud, rng, new Vector3(7.4f, -4.6f, -3f), 4.6f, 0.45f, 16f);

            // cloud bank well below the island
            for (int i = 0; i < 36; i++)
            {
                float z = Mathf.Lerp(-2f, 80f, Mathf.Pow(i / 35f, 1.2f)) + Range(-3f, 3f);
                float spread = 16f + z * 1.1f;
                var pos = new Vector3(Range(-spread, spread), -16f - Range(0f, 2f), z);
                Add(m.bank, rng, pos, Range(16f, 28f) * (1f + z / 100f), Range(0.15f, 0.35f), spread + 15f, bank: true);
            }
        }

        private void Add(Material mat, System.Random rng, Vector3 pos, float width, float speed, float wrap, bool bank = false)
        {
            var go = new GameObject(bank ? "Bank" : "Cloud", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.GetComponent<MeshFilter>().sharedMesh = CloudMesh(rng, width, bank);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            _clouds.Add(new Cloud
            {
                t = go.transform, basePos = pos, speed = speed, bobPhase = (float)rng.NextDouble() * 6.28f,
                bobAmp = bank ? 0.15f : 0.25f, wrap = wrap,
            });
        }

        private void Update()
        {
            float t = Time.time;
            Vector3 camPos = _cam != null ? _cam.position : new Vector3(0f, 2f, -17f);
            foreach (var c in _clouds)
            {
                float x = c.basePos.x + t * c.speed * _speedScale;
                x = Mathf.Repeat(x + c.wrap, c.wrap * 2f) - c.wrap;
                var p = new Vector3(x, c.basePos.y + Mathf.Sin(t * 0.35f + c.bobPhase) * c.bobAmp, c.basePos.z);
                c.t.localPosition = p;
                Vector3 to = c.t.position - camPos;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f) c.t.rotation = Quaternion.LookRotation(to);
            }
        }

        // ---------------- mesh ----------------

        private struct Lump
        {
            public Vector3 c, r; // centre, radii
            public Color col;   // r = highlight bubble, g = wisp, b = random
        }

        /// <summary>Merged-sphere cumulus, long axis = X, facing -Z. Bottom is flattened at y = 0.</summary>
        public static Mesh CloudMesh(System.Random rng, float width, bool bank)
        {
            float R() => (float)rng.NextDouble();
            var lumps = new List<Lump>();
            int n = bank ? 4 + rng.Next(3) : 5 + rng.Next(3);
            float baseR = width * (bank ? 0.14f : 0.17f);
            float flat = bank ? 0.55f : 1f;
            float maxR = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0.5f : (float)i / (n - 1);
                float r = baseR * (0.55f + 0.6f * Mathf.Sin(t * Mathf.PI)) * (0.85f + 0.3f * R());
                maxR = Mathf.Max(maxR, r);
                var c = new Vector3((t - 0.5f) * (width - baseR * 1.4f) + (R() - 0.5f) * baseR * 0.3f, r * 0.3f * flat, (R() - 0.5f) * r * 0.35f);
                lumps.Add(new Lump { c = c, r = new Vector3(r, r * flat, r * 0.85f), col = new Color(0, 0, R()) });
                // back row for depth
                if (i > 0)
                {
                    var c2 = new Vector3(c.x - baseR * 0.45f, c.y + r * 0.12f, r * 0.5f);
                    float r2 = r * 0.85f;
                    lumps.Add(new Lump { c = c2, r = new Vector3(r2, r2 * flat, r2 * 0.85f), col = new Color(0, 0, R()) });
                }
            }
            // highlight bubbles on the bigger lumps' tops
            int count = lumps.Count;
            for (int i = 0; i < count; i++)
            {
                var l = lumps[i];
                if (l.r.x < maxR * 0.72f || l.c.z > 0.01f * width) continue;
                for (int k = 0; k < 2; k++)
                {
                    float s = l.r.x * (0.32f + 0.12f * R());
                    var c = l.c + new Vector3((k == 0 ? -0.35f : 0.3f) * l.r.x, l.r.y * (0.72f + 0.1f * R()) - s * 0.25f, -l.r.z * 0.32f);
                    lumps.Add(new Lump { c = c, r = new Vector3(s, s * flat, s), col = new Color(1, 0, R()) });
                }
            }
            // wispy tail(s)
            float side = R() < 0.5f ? -1f : 1f;
            float endX = side * (width * 0.5f - baseR * 0.4f);
            lumps.Add(new Lump { c = new Vector3(endX + side * baseR * 1.1f, baseR * 0.22f * flat, -baseR * 0.1f), r = new Vector3(baseR * 1.8f, baseR * 0.3f * flat, baseR * 0.55f), col = new Color(0, 1, R()) });
            if (!bank)
                lumps.Add(new Lump { c = new Vector3(endX + side * baseR * 2.6f, baseR * 0.45f, 0f), r = new Vector3(baseR * 1.5f, baseR * 0.14f, baseR * 0.35f), col = new Color(0, 1, R()) });
            if (R() < 0.6f)
                lumps.Add(new Lump { c = new Vector3(-endX - side * baseR * 0.8f, baseR * 0.12f * flat, 0f), r = new Vector3(baseR * 1.1f, baseR * 0.22f * flat, baseR * 0.45f), col = new Color(0, 1, R()) });

            Vector3 centroid = Vector3.zero;
            foreach (var l in lumps) centroid += l.c;
            centroid /= lumps.Count;
            centroid.y = maxR * 0.2f;

            const int lat = 9, lon = 16;
            var verts = new List<Vector3>(lumps.Count * (lat + 1) * (lon + 1));
            var norms = new List<Vector3>(verts.Capacity);
            var cols = new List<Color>(verts.Capacity);
            var tris = new List<int>();
            foreach (var l in lumps)
            {
                int start = verts.Count;
                for (int a = 0; a <= lat; a++)
                {
                    float th = Mathf.PI * a / lat; // 0 top .. pi bottom
                    for (int b = 0; b <= lon; b++)
                    {
                        float ph = Mathf.PI * 2f * b / lon;
                        var sn = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                        var p = l.c + Vector3.Scale(sn, l.r);
                        var nrm = new Vector3(sn.x / l.r.x, sn.y / l.r.y, sn.z / l.r.z).normalized;
                        bool below = p.y < 0f;
                        if (below)
                        {
                            p.y *= 0.12f; // flat cumulus bottom
                            nrm = Vector3.Lerp(nrm, Vector3.down, 0.7f).normalized;
                        }
                        // blend towards the whole cloud's normal → big, readable light bands
                        var whole = (p - centroid);
                        whole.y *= 1.6f;
                        nrm = Vector3.Lerp(nrm, whole.normalized, 0.3f).normalized;
                        verts.Add(p);
                        norms.Add(nrm);
                        cols.Add(l.col);
                    }
                }
                for (int a = 0; a < lat; a++)
                    for (int b = 0; b < lon; b++)
                    {
                        int i0 = start + a * (lon + 1) + b, i1 = i0 + 1, i2 = i0 + lon + 1, i3 = i2 + 1;
                        tris.Add(i0); tris.Add(i1); tris.Add(i2);
                        tris.Add(i2); tris.Add(i1); tris.Add(i3);
                    }
            }
            var mesh = new Mesh { name = bank ? "CloudBank" : "Cloud" };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetColors(cols);
            // winding check (first triangle of the first lump must face outwards)
            Vector3 v0 = verts[tris[lon * 6 + 0]], v1 = verts[tris[lon * 6 + 1]], v2 = verts[tris[lon * 6 + 2]];
            if (Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v0), norms[tris[lon * 6]]) < 0f)
                for (int i = 0; i < tris.Count; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
