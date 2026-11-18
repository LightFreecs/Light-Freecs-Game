using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightMMORPG.Characters
{
    /// <summary>
    /// Anime hair for the Toon Head, ported from the Hair Bench (<c>docs/bench/hair-bench.html</c>,
    /// https://claude.ai/artifact/GEvJjo9jguhqtMwN4sXUxy): a handful of big flat blades whose roots sit inside the skull,
    /// plus a thin base layer over the skull down to the hairline and under the back of the head.
    /// Styles: <see cref="HairStyle.Light"/> (the owner's Blockbench hair, mirrored, plus fill blades) and
    /// <see cref="HairStyle.Messy"/> (rounded shaggy cut, bangs to the eyes, ears hidden).
    /// <para>The bench head is proportioned differently (eyes at 0.5 instead of ~0.38), so every bench height is moved
    /// onto this head by <see cref="Fit"/>: landmarks (mouth, eyes, brows, crown) at the front, nape / ear / crown on the
    /// sides and back. Positions are head units (chin = 0, crown ≈ 1), same index order as the bench (three.js and Unity
    /// mirror each other, so the triangles already face outward in Unity).</para>
    /// </summary>
    public static class ToonHair
    {
        public sealed class MeshData
        {
            public readonly List<Vector3> Positions = new();
            public readonly List<Vector3> Normals = new();
            /// <summary>0 at a blade's root … 1 at its tip (the hair gradient's v).</summary>
            public readonly List<float> Along = new();
            public readonly List<int> Triangles = new();
            public int Blades;

            public int Push(Vector3 p, float along) { Positions.Add(p); Along.Add(along); return Positions.Count - 1; }
        }

        public static bool IsToonHair(HairStyle s) => s == HairStyle.Light || s == HairStyle.Messy;

        /// <summary>Messy layers hang over the ears, so the head is built without them.</summary>
        public static bool HidesEars(HairStyle s) => s == HairStyle.Messy;

        /// <summary>Bench colours: [lit, shade] of each style.</summary>
        public static Color LitColor(HairStyle s) => s == HairStyle.Messy ? new Color32(0x2d, 0x34, 0x48, 0xff) : new Color32(0x33, 0x50, 0x3b, 0xff);

        public static MeshData Build(ToonHeadShape H, ToonHead.MeshData head, HairStyle style)
        {
            var b = new Builder(H, head);
            if (style == HairStyle.Messy) b.Messy(); else b.Light();
            b.ComputeNormals();
            return b.G;
        }

        // ================================================================== builder
        private sealed class Builder
        {
            public readonly MeshData G = new();
            private readonly ToonHeadShape H;
            private readonly ToonHead.MeshData _head;
            private readonly float[] _skull;
            private const int NA = 180, NY = 101;

            public Builder(ToonHeadShape h, ToonHead.MeshData head)
            {
                H = h; _head = head;
                _skull = SkullTable(h);
            }

            // ---------------------------------------------------------------- helpers
            private static float Clamp01(float t) => t < 0f ? 0f : t > 1f ? 1f : t;
            private static float Smooth(float t) { t = Clamp01(t); return t * t * (3f - 2f * t); }
            private static float Lerp(float a, float b, float t) => a + (b - a) * t;
            private static float Rad(float d) => d * Mathf.PI / 180f;
            private static float Sgn(float v) => v > 0f ? 1f : v < 0f ? -1f : 0f;
            private static Vector3 Norm(Vector3 a) { float l = a.magnitude; return l > 0f ? a / l : a; }

            private static float Piecewise(float y, float[] xs, float[] ys)
            {
                if (y <= xs[0]) return ys[0] + (y - xs[0]) * (ys[1] - ys[0]) / (xs[1] - xs[0]);
                for (int i = 1; i < xs.Length; i++)
                    if (y <= xs[i]) return Lerp(ys[i - 1], ys[i], (y - xs[i - 1]) / (xs[i] - xs[i - 1]));
                int n = xs.Length;
                return ys[n - 1] + (y - xs[n - 1]) * (ys[n - 1] - ys[n - 2]) / (xs[n - 1] - xs[n - 2]);
            }

            /// <summary>Bench height (bench head) → height on this head, at <paramref name="deg"/> degrees around from the front.</summary>
            private float Fit(float deg, float y)
            {
                float front = Piecewise(y, new[] { 0f, 0.2f, 0.5f, 0.615f, 1f }, new[] { 0f, H.MouthY, H.EyeY, H.BrowY, H.Top });
                float side = Piecewise(y, new[] { 0f, 0.26f, 0.352f, 1f }, new[] { 0f, H.NapeY, H.EarY, H.Top });
                float wf = 1f - Smooth((Mathf.Abs(deg) - 35f) / 45f);
                return Lerp(side, front, wf);
            }

            /// <summary>Outward from the skull centre (above eye level), horizontally from the head's axis below it.</summary>
            private Vector3 Outward(Vector3 p)
            {
                float c = Fit(Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg, 0.5f);
                return Norm(new Vector3(p.x, p.y - Mathf.Min(p.y, c), p.z));
            }

            /// <summary>Point on the head: th = degrees around (0 front, + toward +x), y = bench height, off = pushed out.</summary>
            private Vector3 S(float th, float y, float off = 0f)
            {
                var p = ToonHead.Surface(H, Rad(th), Fit(th, Mathf.Min(y, 1f)));
                return p + Outward(p) * off;
            }

            private static Vector3 Around(float th) => new(Mathf.Cos(Rad(th)), 0f, -Mathf.Sin(Rad(th)));

            private static Vector3 Bez(Vector3[] P, float t)
            {
                float u = 1f - t;
                return P[0] * (u * u * u) + P[1] * (3f * u * u * t) + P[2] * (3f * u * t * t) + P[3] * (t * t * t);
            }

            private static Vector3 BezD(Vector3[] P, float t)
            {
                float u = 1f - t;
                return P[0] * (-3f * u * u) + P[1] * (3f * u * u - 6f * u * t) + P[2] * (6f * u * t - 3f * t * t) + P[3] * (3f * t * t);
            }

            // ---------------------------------------------------------------- skull radius table
            private static float[] SkullTable(ToonHeadShape H)
            {
                const int K = 360;
                var T = new float[NA * NY];
                var ang = new float[K];
                var rad = new float[K];
                var order = new int[K];
                for (int iy = 0; iy < NY; iy++)
                {
                    float y = Mathf.Min(H.Top - 0.001f, iy / (float)(NY - 1) * H.Top);
                    for (int k = 0; k < K; k++)
                    {
                        var p = ToonHead.Surface(H, -Mathf.PI + 2f * Mathf.PI * k / K, y);
                        ang[k] = Mathf.Atan2(p.x, p.z);
                        rad[k] = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                        order[k] = k;
                    }
                    Array.Sort(order, (a, b) => ang[a].CompareTo(ang[b]));
                    int m = 0;
                    for (int ia = 0; ia < NA; ia++)
                    {
                        float a = -Mathf.PI + 2f * Mathf.PI * ia / NA;
                        while (m < K - 2 && ang[order[m + 1]] < a) m++;
                        float a0 = ang[order[m]], r0 = rad[order[m]], a1 = ang[order[m + 1]], r1 = rad[order[m + 1]];
                        T[iy * NA + ia] = a1 > a0 ? Lerp(r0, r1, Clamp01((a - a0) / (a1 - a0))) : r0;
                    }
                }
                return T;
            }

            /// <summary>This head's horizontal radius in direction phi (radians, atan2(x, z)) at height y (this head's units).</summary>
            private float SkullR(float phi, float y)
            {
                float fa = (phi + Mathf.PI) / (2f * Mathf.PI) * NA, fy = Clamp01(y / H.Top) * (NY - 1);
                int a0 = ((int)Mathf.Floor(fa) % NA + NA) % NA, a1 = (a0 + 1) % NA;
                float ta = fa - Mathf.Floor(fa);
                int y0 = Mathf.Min(NY - 2, (int)Mathf.Floor(fy));
                float ty = fy - y0;
                float V(int ia, int iy) => _skull[iy * NA + ia];
                return Lerp(Lerp(V(a0, y0), V(a1, y0), ta), Lerp(V(a0, y0 + 1), V(a1, y0 + 1), ta), ty);
            }

            // ---------------------------------------------------------------- hairlines (bench heights)
            private static readonly float[][] HairlineFull =
            {
                new[] { 0f, 0.80f }, new[] { 30f, 0.79f }, new[] { 50f, 0.74f }, new[] { 64f, 0.64f }, new[] { 74f, 0.45f }, new[] { 86f, 0.42f },
                new[] { 94f, 0.52f }, new[] { 110f, 0.53f }, new[] { 118f, 0.42f }, new[] { 125f, 0.21f }, new[] { 150f, 0.235f }, new[] { 180f, 0.265f },
            };
            private static readonly float[][] HairlineMessy =
            {
                new[] { 0f, 0.67f }, new[] { 30f, 0.66f }, new[] { 45f, 0.62f }, new[] { 58f, 0.52f }, new[] { 68f, 0.42f }, new[] { 78f, 0.38f },
                new[] { 88f, 0.45f }, new[] { 94f, 0.45f }, new[] { 110f, 0.45f }, new[] { 118f, 0.38f }, new[] { 125f, 0.21f }, new[] { 150f, 0.235f }, new[] { 180f, 0.265f },
            };

            private static float Hairline(float[][] T, float deg)
            {
                deg = Mathf.Min(180f, Mathf.Abs(deg));
                int i = 0;
                while (i < T.Length - 2 && deg > T[i + 1][0]) i++;
                return Lerp(T[i][1], T[i + 1][1], Smooth((deg - T[i][0]) / (T[i + 1][0] - T[i][0])));
            }

            // ---------------------------------------------------------------- base layer
            private void Shell(float[][] hairline, float capOff, float capTop, float tuck)
            {
                const int N = 64, R = 20;
                int crown = G.Push(new Vector3(0f, H.Top + capOff, 0f), 0.04f);
                var rows = new List<int[]>();
                for (int i = 1; i <= R; i++)
                {
                    float s = i / (float)R;
                    var row = new int[N];
                    for (int j = 0; j < N; j++)
                    {
                        float th = -Mathf.PI + 2f * Mathf.PI * j / N, deg = th * Mathf.Rad2Deg, h = Hairline(hairline, deg);
                        float phiEnd = h >= 0.5f ? Mathf.Acos(Mathf.Min(1f, (h - 0.5f) / 0.5f)) : Mathf.PI / 2f, dome = 0.5f * phiEnd;
                        float L = dome + Mathf.Max(0f, 0.5f - h), d = s * L;
                        float y = d <= dome ? 0.5f + 0.5f * Mathf.Cos(d / 0.5f) : 0.5f - (d - dome);
                        var p = ToonHead.Surface(H, th, Fit(deg, y));
                        float tk = tuck > 0f ? Lerp(tuck, 1f, Smooth((y - 0.3f) / 0.3f)) : 1f;
                        float off = (capOff * (1f - 0.6f * Smooth((s - 0.75f) / 0.25f)) + capTop * (1f - s) * (1f - s)) * tk;
                        row[j] = G.Push(p + Outward(p) * off, 0.04f + 0.08f * s);
                    }
                    rows.Add(row);
                }
                var t = G.Triangles;
                for (int j = 0; j < N; j++) t.AddRange(new[] { crown, rows[0][j], rows[0][(j + 1) % N] });
                for (int r = 0; r < rows.Count - 1; r++)
                    for (int j = 0; j < N; j++)
                    {
                        int a = rows[r][j], b = rows[r][(j + 1) % N], c = rows[r + 1][(j + 1) % N], d = rows[r + 1][j];
                        t.AddRange(new[] { a, d, c, a, c, b });
                    }
                Underside();
            }

            /// <summary>
            /// One layer of hair all round the dome: from <paramref name="y0"/> (hidden under the layer above) down to a row
            /// of pointed locks at <paramref name="edge"/>, each lock leaning back a little and lifting off the head toward its
            /// tip (by <paramref name="lip"/>) so the layer below shows from under it. <paramref name="phase"/> 2 puts the points
            /// between the points of a phase-0 layer. Where the hairline comes down past the edge the layer lies flat.
            /// <para>The layer is a closed slab (a top side and an underside meeting at the pointed edge), like the blades:
            /// the inverted-hull outline only draws round closed shapes, so an open sheet would melt into the hair under it.</para>
            /// </summary>
            private void Tier(float[][] hairline, float y0, float edge, float depth, float lip, int phase, float off, float lean = 12f)
            {
                const int N = 80, R = 12;   // 20 locks round the head
                const float under = 0.012f; // the underside starts this far below the top side
                static float Hash(float key)
                {
                    double v = Math.Sin(key * 12.9898) * 43758.5453;
                    return (float)(v - Math.Floor(v));
                }
                var top = new List<int[]>();
                var bot = new List<int[]>();
                for (int i = 0; i <= R; i++)
                {
                    float s = i / (float)R;
                    var rowT = new int[N];
                    var rowB = new int[N];
                    for (int j = 0; j < N; j++)
                    {
                        float deg0 = -180f + 360f * j / N, deg = deg0 + lean * s * Mathf.Sin(Rad(deg0));
                        float hl = Hairline(hairline, deg);
                        int k = (j + phase) % 4;
                        float tooth = 1f - Mathf.Abs(k - 2) / 2f;
                        float centre = Mathf.Abs(deg0 + 360f / N * (2 - k));
                        if (centre > 180f) centre = 360f - centre;
                        float tip = depth * (0.7f + 0.6f * Hash(Mathf.Round(centre) + edge * 100f));
                        float h = Mathf.Max(hl, edge - tip * tooth), yt = Mathf.Max(y0, h + 0.03f), y = Lerp(yt, h, s);
                        float room = Smooth((edge - hl) / (depth + 0.04f)), rise = lip * room;
                        float along = 0.1f + 0.85f * s * room;   // where the layer lies flat it keeps the base colour
                        rowT[j] = G.Push(S(deg, y, off + rise * s * s), along);
                        // underside: shares the pointed edge (last row), elsewhere sits a little lower and rises late
                        rowB[j] = i == R ? rowT[j] : G.Push(S(deg, y, off - under + (rise + under) * s * s * s * s), along);
                    }
                    top.Add(rowT);
                    bot.Add(rowB);
                }
                var t = G.Triangles;
                for (int r = 0; r < R; r++)
                    for (int j = 0; j < N; j++)
                    {
                        int jn = (j + 1) % N;
                        int a = top[r][j], b = top[r][jn], c = top[r + 1][jn], d = top[r + 1][j];
                        t.AddRange(new[] { a, d, c, a, c, b });
                        a = bot[r][j]; b = bot[r][jn]; c = bot[r + 1][jn]; d = bot[r + 1][j];
                        t.AddRange(new[] { a, c, d, a, b, c });   // facing the head
                    }
                // close the top: the top side and the underside joined along the first row
                for (int j = 0; j < N; j++)
                {
                    int jn = (j + 1) % N, a = top[0][j], b = top[0][jn], c = bot[0][jn], d = bot[0][j];
                    t.AddRange(new[] { a, b, c, a, c, d });
                }
            }

            /// <summary>
            /// The base layer carries on under the back of the skull (built from the head mesh's own rows) and ends in a
            /// straight line across, just behind the back of the neck.
            /// </summary>
            private void Underside(float off = 0.024f)
            {
                int N = ToonHead.Cols, jaw = ToonHead.CraniumRows + ToonHead.FaceRows - 1;
                int r0 = jaw - 3, r1 = jaw + 4;
                float cut = H.NeckZ - H.NeckB - 0.04f;
                int Vid(int r, int j) => 1 + r * N + j;
                Vector3 At(int i) => _head.Positions[i];
                Vector3 Nrm(int i) => _head.Normals[i];
                var ids = new int[r1 - r0 + 1][];
                for (int r = 0; r < ids.Length; r++) { ids[r] = new int[N]; for (int j = 0; j < N; j++) ids[r][j] = -1; }
                for (int j = 0; j < N; j++)
                {
                    if (At(Vid(r0, j)).z >= cut) continue;
                    int end = -1;
                    for (int r = r0; r <= r1; r++)
                    {
                        int i = Vid(r, j);
                        var p = At(i);
                        if (p.z < cut && end < 0)
                        {
                            ids[r - r0][j] = G.Push(p + Nrm(i) * off, 0.1f);
                            continue;
                        }
                        if (end < 0)
                        {
                            int k = Vid(r - 1, j);
                            var q = At(k);
                            float tt = (cut - q.z) / (p.z - q.z);
                            var e = q + (p - q) * tt;
                            var n = Norm(Nrm(k) + Nrm(i));
                            end = G.Push(e + n * (off * 0.35f), 0.12f);
                        }
                        ids[r - r0][j] = end;
                    }
                }
                for (int r = 0; r < ids.Length - 1; r++)
                    for (int j = 0; j < N; j++)
                    {
                        int a = ids[r][j], b = ids[r][(j + 1) % N], c = ids[r + 1][(j + 1) % N], d = ids[r + 1][j];
                        if (a < 0 || b < 0 || c < 0 || d < 0) continue;
                        G.Triangles.AddRange(new[] { a, d, c, a, c, b });
                    }
            }

            /// <summary>
            /// Nothing may cut into the skin below the hairline: blade vertices there that are inside the head move out to
            /// just above the skin (a blade reaching below the hairline lies on the head like a sideburn).
            /// </summary>
            private void KeepOffSkin(int from, float[][] hairline)
            {
                var P = G.Positions;
                for (int i = from; i < P.Count; i++)
                {
                    Vector3 v = P[i];
                    float phi = Mathf.Atan2(v.x, v.z), deg = phi * Mathf.Rad2Deg;
                    if (v.y > Fit(deg, 0.97f) || v.y < ToonHead.JawY(H, Mathf.Abs(phi) / Mathf.PI) + 0.01f) continue;
                    if (v.y > Fit(deg, Hairline(hairline, deg)) + 0.12f) continue;
                    float r = Mathf.Sqrt(v.x * v.x + v.z * v.z), R = SkullR(phi, v.y), want = R + 0.012f;
                    if (r >= want) continue;
                    P[i] = new Vector3(Mathf.Sin(phi) * want, v.y, Mathf.Cos(phi) * want);
                }
            }

            // ---------------------------------------------------------------- blades
            /// <summary>One blade: a cubic curve swept with a flat oval (8 sides, 10 rings) tapering to a point; wide side faces out.</summary>
            private void Clump(Vector3[] P, float w0, float flat, float taper, float swell, Vector3 hint)
            {
                const int segs = 10, sides = 8;
                int rootC = G.Push(P[0], 0f), first = rootC + 1;
                for (int r = 0; r < segs; r++)
                {
                    float t = r / (float)segs;
                    Vector3 c = Bez(P, t), T = Norm(BezD(P, t));
                    Vector3 sd = hint - T * Vector3.Dot(hint, T);
                    if (sd.magnitude < 1e-3f) sd = Vector3.Cross(T, Vector3.up);
                    sd = Norm(sd);
                    Vector3 nn = Vector3.Cross(T, sd);
                    float w = w0 * Mathf.Pow(1f - t, taper) * (1f + swell * Mathf.Sin(Mathf.PI * t)), d = w * flat;
                    for (int k = 0; k < sides; k++)
                    {
                        float a = k / (float)sides * Mathf.PI * 2f;
                        G.Push(c + sd * (Mathf.Cos(a) * w * 0.5f) + nn * (Mathf.Sin(a) * d * 0.5f), t);
                    }
                }
                int tip = G.Push(Bez(P, 1f), 1f);
                int At(int r, int k) => first + r * sides + (k % sides);
                var tr = G.Triangles;
                for (int k = 0; k < sides; k++) tr.AddRange(new[] { rootC, At(0, k + 1), At(0, k) });
                for (int r = 0; r < segs - 1; r++)
                    for (int k = 0; k < sides; k++)
                        tr.AddRange(new[] { At(r, k), At(r, k + 1), At(r + 1, k), At(r, k + 1), At(r + 1, k + 1), At(r + 1, k) });
                for (int k = 0; k < sides; k++) tr.AddRange(new[] { At(segs - 1, k), At(segs - 1, k + 1), tip });
                G.Blades++;
            }

            private struct SpikeOpts
            {
                public float Len, W, Up, Back, Out, Side, Depth, Curl, Taper, Flat;
                public Vector3 Bend;
                public static SpikeOpts Default => new() { W = 0.42f, Out = 0.3f, Depth = 0.12f, Curl = 0.1f, Taper = 1f, Flat = 0.57f, Bend = new Vector3(0f, -0.4f, -1f) };
            }

            /// <summary>Spike: root inside the skull, leaves the head outward, then follows out / up / back / sideways.</summary>
            private void Spike(float th, float y, SpikeOpts o)
            {
                Vector3 R = S(th, y, -o.Depth), n = Outward(S(th, y));
                float L = o.Len;
                Vector3 d = Norm(n * o.Out + new Vector3(0f, o.Up, -o.Back) + new Vector3((Mathf.Abs(R.x) < 1e-4f ? 0f : Sgn(R.x)) * o.Side, 0f, 0f));
                Vector3 bend = Norm(o.Bend);
                var P = new[] { R, R + Norm(n * 0.7f + d * 0.3f) * (L * 0.3f), R + d * (L * 0.65f), R + d * L + bend * (L * o.Curl) };
                Clump(P, o.W, o.Flat, o.Taper, 0f, Around(th));
            }

            /// <summary>Blade lying on the head: four points (th, bench y, off), the first one inside the skull.</summary>
            private void Strand(float[][] pts, float w, float flat = 0.5f, float taper = 1f, float swell = 0f)
            {
                var P = new Vector3[4];
                for (int i = 0; i < 4; i++) P[i] = S(pts[i][0], pts[i][1], pts[i][2]);
                Clump(P, w, flat, taper, swell, Around(pts[0][0]));
            }

            private static SpikeOpts Sp(float len, float w, float up, float back, float outv, float side = 0f, float depth = 0.12f, float curl = 0.1f, Vector3? bend = null)
            {
                var o = SpikeOpts.Default;
                o.Len = len; o.W = w; o.Up = up; o.Back = back; o.Out = outv; o.Side = side; o.Depth = depth; o.Curl = curl;
                if (bend.HasValue) o.Bend = bend.Value;
                return o;
            }

            // ---------------------------------------------------------------- shared pieces
            /// <summary>Sides, mirrored: a sideburn in front of the ear and a blade over the ear, then the spiky back.</summary>
            private void SideFill(float w = 0.3f)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    Strand(new[] { new[] { 72f * s, 0.9f, -0.1f }, new[] { 73f * s, 0.78f, 0.05f }, new[] { 74f * s, 0.6f, 0.03f }, new[] { 75f * s, 0.46f, 0.022f } }, w);
                    Strand(new[] { new[] { 95f * s, 0.92f, -0.1f }, new[] { 97f * s, 0.8f, 0.06f }, new[] { 101f * s, 0.66f, 0.05f }, new[] { 107f * s, 0.53f, 0.035f } }, w * 1.1f);
                }
                SpikyBack();
            }

            /// <summary>Back of the head as spikes down to the neck, aimed like the Light model's: back and a little up.</summary>
            private void SpikyBack()
            {
                var bend = new Vector3(0f, 0.25f, -1f);
                SpikeOpts B(float len, float w, float up, float outv, float side = 0f, float depth = 0.12f) => Sp(len, w, up, 1f, outv, side, depth, 0.08f, bend);
                for (int s = -1; s <= 1; s += 2)
                {
                    Spike(137f * s, 0.5f, B(0.36f, 0.32f, 0.25f, 0.5f));
                    Spike(138f * s, 0.34f, B(0.3f, 0.3f, 0.15f, 0.45f));
                    Spike(148f * s, 0.62f, B(0.44f, 0.4f, 0.4f, 0.4f, 0.2f));
                    Spike(152f * s, 0.42f, B(0.38f, 0.36f, 0.25f, 0.35f, 0.15f));
                }
                Spike(180f, 0.62f, B(0.46f, 0.42f, 0.4f, 0.4f));
                Spike(180f, 0.42f, B(0.4f, 0.38f, 0.25f, 0.35f));
                foreach (float th in new[] { -162f, -146f, 146f, 162f, 180f }) Spike(th, 0.3f, B(0.26f, 0.28f, 0.1f, 0.2f, 0f, 0.08f));
            }

            // ---------------------------------------------------------------- imported Light model
            // Skull the Light hair was modelled on (Lighthair.bbmodel, bench head units): y, half width, front, back.
            private static readonly float[][] LightSkull =
            {
                new[] { 0.2f, 0.30f, 0.36f, 0.26f }, new[] { 0.3f, 0.36f, 0.38f, 0.30f }, new[] { 0.4f, 0.40f, 0.38f, 0.37f }, new[] { 0.5f, 0.40f, 0.37f, 0.40f },
                new[] { 0.6f, 0.39f, 0.37f, 0.43f }, new[] { 0.7f, 0.355f, 0.35f, 0.43f }, new[] { 0.8f, 0.33f, 0.33f, 0.38f }, new[] { 0.9f, 0.265f, 0.24f, 0.30f },
                new[] { 0.95f, 0.19f, 0.18f, 0.23f }, new[] { 1.0f, 0.05f, 0.05f, 0.05f },
            };

            private static float LightSkullR(float phi, float y)
            {
                var T = LightSkull;
                int i = 0;
                while (i < T.Length - 2 && y > T[i + 1][0]) i++;
                float t = Clamp01((y - T[i][0]) / (T[i + 1][0] - T[i][0]));
                float w = Lerp(T[i][1], T[i + 1][1], t), d = Mathf.Cos(phi) >= 0f ? Lerp(T[i][2], T[i + 1][2], t) : Lerp(T[i][3], T[i + 1][3], t);
                float sx = Mathf.Sin(phi) / w, cz = Mathf.Cos(phi) / d;
                return 1f / Mathf.Sqrt(sx * sx + cz * cz);
            }

            /// <summary>
            /// The 8 spikes from the model's right half, mirrored onto the left; each spike moves as one piece onto this head
            /// (height by <see cref="Fit"/> at its root, outward by the skull difference there). The model's back shell is
            /// replaced by the base layer.
            /// </summary>
            private void Imported()
            {
                float[] pos = ToonHairLightModel.Pos, roots = ToonHairLightModel.Roots;
                byte[] part = ToonHairLightModel.Part;
                int[] tris = ToonHairLightModel.Tris;
                int n = part.Length, parts = roots.Length / 3;
                var shift = new Vector3[parts];
                var keep = new bool[parts];
                var reach = new float[parts];
                for (int k = 1; k < parts; k++)
                {
                    float rx = roots[k * 3], ry = roots[k * 3 + 1], rz = roots[k * 3 + 2];
                    keep[k] = rx >= 0f;
                    float phi = Mathf.Atan2(rx, rz), deg = phi * Mathf.Rad2Deg;
                    float yb = Mathf.Min(0.97f, Mathf.Max(0.2f, ry));
                    float yg = Fit(deg, ry), dr = SkullR(phi, Fit(deg, yb)) - LightSkullR(phi, yb);
                    shift[k] = new Vector3(Mathf.Sin(phi) * dr, yg - ry, Mathf.Cos(phi) * dr);
                }
                for (int i = 0; i < n; i++)
                {
                    int k = part[i];
                    if (k == 0) continue;
                    var d = new Vector3(pos[i * 3] - roots[k * 3], pos[i * 3 + 1] - roots[k * 3 + 1], pos[i * 3 + 2] - roots[k * 3 + 2]);
                    reach[k] = Mathf.Max(reach[k], d.magnitude);
                }
                for (int side = 1; side >= -1; side -= 2)
                {
                    var map = new int[n];
                    for (int i = 0; i < n; i++)
                    {
                        int k = part[i];
                        map[i] = -1;
                        if (k == 0 || !keep[k]) continue;
                        var p = new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
                        var r = new Vector3(roots[k * 3], roots[k * 3 + 1], roots[k * 3 + 2]);
                        float along = reach[k] > 0f ? Clamp01((p - r).magnitude / reach[k]) : 0f;
                        p += shift[k];
                        map[i] = G.Push(new Vector3(p.x * side, p.y, p.z), along);
                    }
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        int k = part[tris[t]];
                        if (k == 0 || !keep[k]) continue;
                        int a = map[tris[t]], b = map[tris[t + 1]], c = map[tris[t + 2]];
                        if (side > 0) G.Triangles.AddRange(new[] { a, b, c });
                        else G.Triangles.AddRange(new[] { a, c, b });   // mirrored copy: flip the winding
                    }
                }
                for (int k = 1; k < parts; k++) if (keep[k]) G.Blades += 2;
            }

            // ---------------------------------------------------------------- styles
            /// <summary>Light: the owner's Blockbench hair (mirrored) on a clean hairline, fill blades over the top front and sides.</summary>
            public void Light()
            {
                Shell(HairlineFull, 0.025f, 0f, 0f);
                // three layers of pointed locks over the dome, each tucked under the one above
                // (each layer starts a little further out than the one under it, so they never sit at the same height)
                Tier(HairlineFull, 1f, 0.82f, 0.09f, 0.05f, 0, 0.048f);      // from the crown
                Tier(HairlineFull, 0.88f, 0.64f, 0.1f, 0.05f, 2, 0.04f);     // middle, points between the ones above
                Tier(HairlineFull, 0.72f, 0.44f, 0.1f, 0.055f, 0, 0.034f);   // the back, behind the ears
                int from = G.Positions.Count;
                Imported();
                foreach (var (th, w) in new[] { (-26f, 0.4f), (0f, 0.44f), (26f, 0.4f) })
                    Strand(new[] { new[] { th, 0.86f, -0.1f }, new[] { th, 0.9f, 0.06f }, new[] { th * 0.8f, 0.97f, 0.1f }, new[] { th * 1.4f, 1f, 0.42f } }, w);
                foreach (float th in new[] { -25f, 25f }) Spike(th, 0.99f, Sp(0.55f, 0.46f, 1f, 0.35f, 0f, 0f, 0.1f));
                SideFill();
                KeepOffSkin(from, HairlineFull);
            }

            /// <summary>Messy layers: smooth full dome, locks falling from the crown, bangs to the eyes, ears covered, long at the back.</summary>
            public void Messy()
            {
                static float J(float a, float key)
                {
                    double v = Math.Sin(Math.Abs(key) * 12.9898 + a * 78.233) * 43758.5453;
                    return (float)((v - Math.Floor(v)) * 2.0 - 1.0) * a;
                }
                Shell(HairlineMessy, 0.055f, 0.035f, 0.35f);
                int from = G.Positions.Count;
                void Lock(float th, float tipTh, float tipY, float w, float tipOff = 0.035f, float taper = 0.85f) =>
                    Strand(new[] { new[] { th, 0.99f, -0.03f }, new[] { th, 0.9f, 0.11f }, new[] { Lerp(th, tipTh, 0.5f), Lerp(0.9f, tipY, 0.5f), 0.085f }, new[] { tipTh, tipY, tipOff } },
                        w, 0.3f, taper);
                void Pair(float th, float tipTh, float tipY, float w, float tipOff = 0.035f, float taper = 0.85f)
                {
                    Lock(th, tipTh, tipY, w, tipOff, taper);
                    Lock(-th, -tipTh, tipY, w, tipOff, taper);
                }
                // bangs: one pair down between the eyes, the next crossing the eyes, outer ones framing the face
                Pair(6f, 3f, 0.43f, 0.2f, 0.035f, 0.45f);
                Pair(18f, 16f, 0.53f, 0.24f, 0.035f, 0.45f);
                Pair(31f, 33f, 0.55f, 0.24f, 0.035f, 0.45f);
                Pair(44f, 48f, 0.5f, 0.24f, 0.035f, 0.5f);
                Pair(56f, 62f, 0.4f, 0.22f, 0.03f, 0.55f);
                Pair(70f, 72f, 0.33f, 0.2f, 0.03f);
                // round the ear and down the back: a dense row of pointed locks with alternating lengths
                float[][] rim =
                {
                    new[] { 80f, 0.38f, 0.035f }, new[] { 88f, 0.3f, 0.045f }, new[] { 96f, 0.27f, 0.045f }, new[] { 104f, 0.31f, 0.045f }, new[] { 112f, 0.26f, 0.045f },
                    new[] { 120f, 0.22f, 0.045f }, new[] { 128f, 0.15f, 0.045f }, new[] { 136f, 0.11f, 0.05f }, new[] { 144f, 0.15f, 0.05f }, new[] { 152f, 0.1f, 0.05f },
                    new[] { 160f, 0.14f, 0.05f }, new[] { 168f, 0.1f, 0.05f },
                };
                foreach (var e in rim) Pair(e[0], e[0] + 3f, e[1], 0.21f + 0.03f * Mathf.Sin(e[0]), e[2], 0.45f);
                Lock(180f, 180f, 0.12f, 0.22f, 0.05f, 0.45f);
                // shorter locks on the dome for the clump lines
                foreach (float th in new[] { 24f, 52f, 80f, 108f, 136f, 164f }) Pair(th, th + J(8f, th), 0.66f + J(0.04f, th), 0.24f, 0.1f);
                // a small cowlick at the back of the crown and a little flick on each side
                Spike(180f, 0.95f, Sp(0.2f, 0.15f, 0.75f, 0.8f, 0.3f));
                for (int s = -1; s <= 1; s += 2) Spike(74f * s, 0.64f, Sp(0.13f, 0.1f, 0.15f, 0.3f, 1f, 0f, 0.02f));
                KeepOffSkin(from, HairlineFull);   // the bench clears the skin below the full hairline for every style
            }

            /// <summary>Smooth area-weighted normals (shared vertices within each blade), like three.js computeVertexNormals.</summary>
            public void ComputeNormals()
            {
                var P = G.Positions;
                var nrm = new Vector3[P.Count];
                var t = G.Triangles;
                for (int i = 0; i < t.Count; i += 3)
                {
                    Vector3 a = P[t[i]], b = P[t[i + 1]], c = P[t[i + 2]];
                    Vector3 fn = Vector3.Cross(b - a, c - a);
                    nrm[t[i]] += fn; nrm[t[i + 1]] += fn; nrm[t[i + 2]] += fn;
                }
                for (int i = 0; i < nrm.Length; i++) G.Normals.Add(nrm[i].sqrMagnitude > 1e-20f ? nrm[i].normalized : Vector3.up);
            }
        }
    }
}
