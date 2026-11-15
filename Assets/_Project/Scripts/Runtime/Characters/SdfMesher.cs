using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightMMORPG.Characters
{
    public enum SdfOp : byte { Union, Subtract, Intersect }
    public enum SdfShape : byte { Limb, Ellipsoid, Cylinder, Box, Plane, Custom }

    /// <summary>
    /// One signed-distance primitive in model space (bind pose). Limbs are round cones (tapered capsules) with an
    /// optional elliptical cross-section; ellipsoids are scaled spheres. <see cref="Group"/>: primitives smooth-union
    /// inside their group (with <see cref="K"/>), groups join with the part's GroupK; Group &lt; 0 = applied to the whole
    /// part last (clip planes, cut-outs). <see cref="Tag"/> lets outfits copy body regions (see <see cref="Inflated"/>).
    /// </summary>
    public struct SdfPrim
    {
        public SdfShape Shape;
        public SdfOp Op;
        public int Group;
        public float K;
        public int Bone;
        public int Tag;

        // construction params (kept so the prim can be re-built inflated)
        public Vector3 A, B, Size;   // limb: A,B ends, Size=(ra, rb, 0); ellipsoid: A=centre, Size=radii; box: A=centre, Size=half; cylinder: A=centre, B=axis, Size=(r, halfH)
        public Quaternion Rot;       // ellipsoid / box frame
        public float Sx, Sz;         // limb cross-section scale
        public Vector3 Hint;         // limb frame x hint
        public Func<Vector3, float> Fn; // custom distance (always evaluated, like planes)

        // evaluation data
        internal Vector3 Origin, LocalB, InvScale;
        internal Quaternion InvRot;
        internal float Ra, Rb, MinScale, Round;
        internal Vector3 BMin, BMax;
        internal bool Infinite;

        public static SdfPrim Limb(Vector3 a, Vector3 b, float ra, float rb, int bone, float k, int group, int tag = 0,
            float sx = 1f, float sz = 1f, Vector3 hint = default)
        {
            var p = new SdfPrim { Shape = SdfShape.Limb, A = a, B = b, Size = new Vector3(ra, rb, 0f), Sx = sx, Sz = sz, Hint = hint, Bone = bone, K = k, Group = group, Tag = tag };
            p.Prepare();
            return p;
        }

        public static SdfPrim Ellipsoid(Vector3 c, Vector3 radii, Quaternion rot, int bone, float k, int group, int tag = 0)
        {
            var p = new SdfPrim { Shape = SdfShape.Ellipsoid, A = c, Size = radii, Rot = rot, Bone = bone, K = k, Group = group, Tag = tag };
            p.Prepare();
            return p;
        }

        public static SdfPrim Ellipsoid(Vector3 c, Vector3 radii, int bone, float k, int group, int tag = 0)
            => Ellipsoid(c, radii, Quaternion.identity, bone, k, group, tag);

        /// <summary>Capped cylinder around <paramref name="axis"/> through <paramref name="c"/>.</summary>
        public static SdfPrim Cylinder(Vector3 c, Vector3 axis, float radius, float halfHeight, SdfOp op, float k, int group = -1)
        {
            var p = new SdfPrim { Shape = SdfShape.Cylinder, A = c, B = axis.normalized, Size = new Vector3(radius, halfHeight, 0f), Op = op, K = k, Group = group, Bone = -1 };
            p.Prepare();
            return p;
        }

        public static SdfPrim Box(Vector3 c, Quaternion rot, Vector3 half, float round, SdfOp op, float k, int group = -1, int bone = -1)
        {
            var p = new SdfPrim { Shape = SdfShape.Box, A = c, Rot = rot, Size = half, Sx = round, Op = op, K = k, Group = group, Bone = bone };
            p.Prepare();
            return p;
        }

        /// <summary>Half-space clip: keeps the side the normal points away from (n·(p - point) &lt; 0).</summary>
        public static SdfPrim Keep(Vector3 point, Vector3 outwardNormal, float k = 0f)
        {
            var p = new SdfPrim { Shape = SdfShape.Plane, A = point, B = outwardNormal.normalized, Op = SdfOp.Intersect, K = k, Group = -1, Bone = -1 };
            p.Prepare();
            return p;
        }

        /// <summary>Arbitrary (approximate) distance function, applied to the whole part (Group -1).</summary>
        public static SdfPrim Custom(Func<Vector3, float> fn, SdfOp op, float k = 0f)
        {
            var p = new SdfPrim { Shape = SdfShape.Custom, Fn = fn, Op = op, K = k, Group = -1, Bone = -1 };
            p.Prepare();
            return p;
        }

        public SdfPrim WithOp(SdfOp op) { var c = this; c.Op = op; return c; }
        public SdfPrim WithGroup(int g) { var c = this; c.Group = g; return c; }

        /// <summary>Same primitive grown by <paramref name="offset"/> (outfit shells), blend widened by <paramref name="kAdd"/>.</summary>
        public SdfPrim Inflated(float offset, float kAdd)
        {
            var c = this;
            c.K += kAdd;
            switch (Shape)
            {
                case SdfShape.Limb: c.Size = new Vector3(Size.x + offset, Size.y + offset, 0f); break;
                case SdfShape.Ellipsoid: c.Size = Size + Vector3.one * offset; break;
                case SdfShape.Box: c.Size = Size + Vector3.one * offset; break;
                case SdfShape.Cylinder: c.Size = new Vector3(Size.x + offset, Size.y + offset, 0f); break;
            }
            c.Prepare();
            return c;
        }

        private void Prepare()
        {
            Infinite = false;
            float pad;
            switch (Shape)
            {
                case SdfShape.Limb:
                {
                    Vector3 axis = B - A;
                    float len = axis.magnitude;
                    Vector3 dir = len > 1e-6f ? axis / len : Vector3.up;
                    Vector3 hx = Hint.sqrMagnitude > 1e-6f ? Hint : (Mathf.Abs(Vector3.Dot(dir, Vector3.right)) > 0.9f ? Vector3.up : Vector3.right);
                    Vector3 x = (hx - dir * Vector3.Dot(hx, dir));
                    if (x.sqrMagnitude < 1e-8f) x = Vector3.Cross(dir, Vector3.forward);
                    x.Normalize();
                    Vector3 z = Vector3.Cross(x, dir);
                    var rot = Quaternion.LookRotation(z, dir);
                    InvRot = Quaternion.Inverse(rot);
                    Origin = A;
                    LocalB = new Vector3(0f, len, 0f);
                    float sx = Sx <= 0f ? 1f : Sx, sz = Sz <= 0f ? 1f : Sz;
                    InvScale = new Vector3(1f / sx, 1f, 1f / sz);
                    MinScale = Mathf.Min(1f, Mathf.Min(sx, sz));
                    Ra = Size.x; Rb = Size.y;
                    pad = Mathf.Max(Ra, Rb) * Mathf.Max(1f, Mathf.Max(sx, sz)) + K;
                    BMin = Vector3.Min(A, B) - Vector3.one * pad;
                    BMax = Vector3.Max(A, B) + Vector3.one * pad;
                    break;
                }
                case SdfShape.Ellipsoid:
                    InvRot = Quaternion.Inverse(Rot);
                    Origin = A;
                    LocalB = Vector3.zero;
                    InvScale = new Vector3(1f / Size.x, 1f / Size.y, 1f / Size.z);
                    MinScale = Mathf.Min(Size.x, Mathf.Min(Size.y, Size.z));
                    Ra = Rb = 1f;
                    pad = Mathf.Max(Size.x, Mathf.Max(Size.y, Size.z)) + K;
                    BMin = A - Vector3.one * pad;
                    BMax = A + Vector3.one * pad;
                    break;
                case SdfShape.Cylinder:
                    InvRot = Quaternion.Inverse(Quaternion.FromToRotation(Vector3.up, B));
                    Origin = A;
                    InvScale = Vector3.one;
                    MinScale = 1f;
                    Ra = Size.x; Rb = Size.y;
                    pad = Mathf.Sqrt(Ra * Ra + Rb * Rb) + K;
                    BMin = A - Vector3.one * pad;
                    BMax = A + Vector3.one * pad;
                    break;
                case SdfShape.Box:
                    InvRot = Quaternion.Inverse(Rot);
                    Origin = A;
                    InvScale = Vector3.one;
                    MinScale = 1f;
                    Round = Sx;
                    pad = Size.magnitude + K;
                    BMin = A - Vector3.one * pad;
                    BMax = A + Vector3.one * pad;
                    break;
                default:
                    Infinite = true;
                    Origin = A;
                    break;
            }
        }

        internal readonly float Distance(Vector3 p)
        {
            if (Shape == SdfShape.Custom) return Fn(p);
            if (Shape == SdfShape.Plane)
                return (p.x - Origin.x) * B.x + (p.y - Origin.y) * B.y + (p.z - Origin.z) * B.z;
            Vector3 q = InvRot * (p - Origin);
            switch (Shape)
            {
                case SdfShape.Limb:
                case SdfShape.Ellipsoid:
                    q.x *= InvScale.x; q.y *= InvScale.y; q.z *= InvScale.z;
                    return RoundCone(q, LocalB, Ra, Rb) * MinScale;
                case SdfShape.Cylinder:
                {
                    float dx = Mathf.Sqrt(q.x * q.x + q.z * q.z) - Ra;
                    float dy = Mathf.Abs(q.y) - Rb;
                    float ox = Mathf.Max(dx, 0f), oy = Mathf.Max(dy, 0f);
                    return Mathf.Min(Mathf.Max(dx, dy), 0f) + Mathf.Sqrt(ox * ox + oy * oy);
                }
                default:
                {
                    float qx = Mathf.Abs(q.x) - Size.x + Round, qy = Mathf.Abs(q.y) - Size.y + Round, qz = Mathf.Abs(q.z) - Size.z + Round;
                    float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f), oz = Mathf.Max(qz, 0f);
                    return Mathf.Sqrt(ox * ox + oy * oy + oz * oz) + Mathf.Min(Mathf.Max(qx, Mathf.Max(qy, qz)), 0f) - Round;
                }
            }
        }

        // Inigo Quilez' round cone from the origin to b.
        private static float RoundCone(Vector3 p, Vector3 b, float r1, float r2)
        {
            float l2 = b.x * b.x + b.y * b.y + b.z * b.z;
            if (l2 < 1e-10f) return p.magnitude - r1;
            float rr = r1 - r2;
            float a2 = l2 - rr * rr;
            if (a2 <= 1e-9f) return r1 >= r2 ? p.magnitude - r1 : (p - b).magnitude - r2;
            float il2 = 1f / l2;
            float y = p.x * b.x + p.y * b.y + p.z * b.z;
            float z = y - l2;
            float xx = p.x * l2 - b.x * y, xy = p.y * l2 - b.y * y, xz = p.z * l2 - b.z * y;
            float x2 = xx * xx + xy * xy + xz * xz;
            float y2 = y * y * l2;
            float z2 = z * z * l2;
            float k = (rr >= 0f ? 1f : -1f) * rr * rr * x2;
            if ((z >= 0f ? 1f : -1f) * a2 * z2 > k) return Mathf.Sqrt(x2 + z2) * il2 - r2;
            if ((y >= 0f ? 1f : -1f) * a2 * y2 < k) return Mathf.Sqrt(x2 + y2) * il2 - r1;
            return (Mathf.Sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
        }

        /// <summary>Cheap capsule approximation for skin weights: endpoints + radius (model space).</summary>
        internal readonly void WeightCapsule(out Vector3 a, out Vector3 b, out float r)
        {
            switch (Shape)
            {
                case SdfShape.Limb: a = A; b = B; r = 0.5f * (Size.x + Size.y) * Mathf.Sqrt((Sx <= 0 ? 1 : Sx) * (Sz <= 0 ? 1 : Sz)); return;
                case SdfShape.Ellipsoid:
                {
                    // long axis as a segment
                    Vector3 s = Size;
                    Vector3 axis; float half, rad;
                    if (s.x >= s.y && s.x >= s.z) { axis = Rot * Vector3.right; half = s.x; rad = 0.5f * (s.y + s.z); }
                    else if (s.y >= s.z) { axis = Rot * Vector3.up; half = s.y; rad = 0.5f * (s.x + s.z); }
                    else { axis = Rot * Vector3.forward; half = s.z; rad = 0.5f * (s.x + s.y); }
                    float seg = Mathf.Max(0f, half - rad);
                    a = A - axis * seg; b = A + axis * seg; r = rad; return;
                }
                default: a = A; b = A; r = Size.magnitude; return;
            }
        }
    }

    /// <summary>A mesh piece generated from SDF primitives with naive surface nets (one material slot by default).</summary>
    public sealed class SdfPart
    {
        public string Name;
        public float Cell = 0.015f;
        public float GroupK = 0.03f;
        public int Slot;
        public readonly List<SdfPrim> Prims = new();
        /// <summary>Optional surface displacement (folds); must stay within ±DisplaceMax.</summary>
        public Func<Vector3, float> Displace;
        public float DisplaceMax;
        /// <summary>Optional per-triangle slot from (centroid, normal); return -1 to keep <see cref="Slot"/>.</summary>
        public Func<Vector3, Vector3, int> Classify;
        public Func<Vector3, Vector3, Vector2> Uv;
        public SkinWeightSource Weights;
        public Bounds? ClipBounds;

        public SdfPart(string name, float cell, int slot) { Name = name; Cell = cell; Slot = slot; }
        public SdfPart Add(SdfPrim p) { Prims.Add(p); return this; }
    }

    /// <summary>Vertex/triangle buffers shared by all parts of one character (one submesh per material slot).</summary>
    public sealed class MeshBuffers
    {
        public readonly List<Vector3> Positions = new(32768);
        public readonly List<Vector3> Normals = new(32768);
        public readonly List<Vector2> Uvs = new(32768);
        public readonly List<BoneWeight> Weights = new(32768);
        public readonly List<List<int>> Triangles = new();

        public List<int> Slot(int slot)
        {
            while (Triangles.Count <= slot) Triangles.Add(new List<int>());
            return Triangles[slot];
        }
    }

    /// <summary>Distance-based skin weights: each bone's distance = min over its capsules; weights fall off exponentially.</summary>
    public sealed class SkinWeightSource
    {
        private readonly Vector3[] _a, _ab;
        private readonly float[] _abLen2, _r;
        private readonly int[] _bone;
        private readonly float[] _boneDist;
        private readonly int _fixedBone = -1;
        private readonly float _sigma;

        public SkinWeightSource(IEnumerable<SdfPrim> prims, int boneCount, float sigma = 0.022f)
        {
            var a = new List<Vector3>(); var ab = new List<Vector3>(); var l2 = new List<float>(); var r = new List<float>(); var bone = new List<int>();
            foreach (var p in prims)
            {
                if (p.Bone < 0 || p.Op != SdfOp.Union) continue;
                p.WeightCapsule(out var pa, out var pb, out var pr);
                a.Add(pa); ab.Add(pb - pa); l2.Add(Mathf.Max((pb - pa).sqrMagnitude, 1e-10f)); r.Add(pr); bone.Add(p.Bone);
            }
            _a = a.ToArray(); _ab = ab.ToArray(); _abLen2 = l2.ToArray(); _r = r.ToArray(); _bone = bone.ToArray();
            _boneDist = new float[boneCount];
            _sigma = sigma;
        }

        /// <summary>Everything rigidly on one bone (hair, pads).</summary>
        public SkinWeightSource(int fixedBone) { _fixedBone = fixedBone; }

        public float[] NewScratch() => new float[_boneDist?.Length ?? 0];

        public BoneWeight Compute(Vector3 p) => Compute(p, _boneDist);

        public BoneWeight Compute(Vector3 p, float[] _boneDist)
        {
            if (_fixedBone >= 0) return new BoneWeight { boneIndex0 = _fixedBone, weight0 = 1f };
            for (int i = 0; i < _boneDist.Length; i++) _boneDist[i] = float.MaxValue;
            float best = float.MaxValue;
            for (int i = 0; i < _a.Length; i++)
            {
                Vector3 pa = p - _a[i];
                float t = Mathf.Clamp01(Vector3.Dot(pa, _ab[i]) / _abLen2[i]);
                float d = (pa - _ab[i] * t).magnitude - _r[i];
                if (d < _boneDist[_bone[i]]) _boneDist[_bone[i]] = d;
                if (d < best) best = d;
            }
            // top 4
            int b0 = -1, b1 = -1, b2 = -1, b3 = -1;
            float w0 = 0, w1 = 0, w2 = 0, w3 = 0;
            float cut = best + 5f * _sigma;
            for (int b = 0; b < _boneDist.Length; b++)
            {
                float d = _boneDist[b];
                if (d > cut) continue;
                float w = Mathf.Exp(-(d - best) / _sigma);
                if (w > w0) { b3 = b2; w3 = w2; b2 = b1; w2 = w1; b1 = b0; w1 = w0; b0 = b; w0 = w; }
                else if (w > w1) { b3 = b2; w3 = w2; b2 = b1; w2 = w1; b1 = b; w1 = w; }
                else if (w > w2) { b3 = b2; w3 = w2; b2 = b; w2 = w; }
                else if (w > w3) { b3 = b; w3 = w; }
            }
            if (b0 < 0) return new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            float sum = w0 + w1 + w2 + w3;
            return new BoneWeight
            {
                boneIndex0 = b0, weight0 = w0 / sum,
                boneIndex1 = Mathf.Max(b1, 0), weight1 = w1 / sum,
                boneIndex2 = Mathf.Max(b2, 0), weight2 = w2 / sum,
                boneIndex3 = Mathf.Max(b3, 0), weight3 = w3 / sum,
            };
        }
    }

    /// <summary>Naive surface nets over a block-culled sample grid. Fast enough for spawn-time generation.</summary>
    public static class SdfMesher
    {
        private const float Big = 1e4f;
        private const int BlockSize = 4;

        private static readonly int[] EdgeA = new int[12];
        private static readonly int[] EdgeB = new int[12];

        static SdfMesher()
        {
            int e = 0;
            for (int c = 0; c < 8; c++)
                for (int bit = 1; bit < 8; bit <<= 1)
                    if ((c & bit) == 0) { EdgeA[e] = c; EdgeB[e] = c | bit; e++; }
        }

        private static float SMin(float a, float b, float k)
        {
            if (k <= 0f) return a < b ? a : b;
            float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
            return (a < b ? a : b) - h * h * k * 0.25f;
        }

        private static float SMax(float a, float b, float k) => -SMin(-a, -b, k);

        private static float Eval(SdfPrim[] prims, int[] list, int start, int count, Vector3 p, float groupK)
        {
            float total = Big, g = Big;
            int cur = int.MinValue;
            bool global = false;
            for (int i = start; i < start + count; i++)
            {
                ref readonly SdfPrim pr = ref prims[list[i]];
                if (pr.Group < 0)
                {
                    if (!global)
                    {
                        if (cur != int.MinValue) total = SMin(total, g, groupK);
                        global = true;
                    }
                    float dg = pr.Distance(p);
                    total = pr.Op switch
                    {
                        SdfOp.Union => SMin(total, dg, pr.K),
                        SdfOp.Subtract => SMax(total, -dg, pr.K),
                        _ => SMax(total, dg, pr.K),
                    };
                    continue;
                }
                if (pr.Group != cur)
                {
                    if (cur != int.MinValue) total = SMin(total, g, groupK);
                    g = Big;
                    cur = pr.Group;
                }
                float d = pr.Distance(p);
                g = pr.Op switch
                {
                    SdfOp.Union => SMin(g, d, pr.K),
                    SdfOp.Subtract => SMax(g, -d, pr.K),
                    _ => SMax(g, d, pr.K),
                };
            }
            if (!global && cur != int.MinValue) total = SMin(total, g, groupK);
            return total;
        }

        /// <summary>Unculled field value (for placement queries while building a recipe).</summary>
        public static float Evaluate(IReadOnlyList<SdfPrim> prims, Vector3 p, float groupK)
        {
            var arr = new SdfPrim[prims.Count];
            var idx = new int[prims.Count];
            for (int i = 0; i < arr.Length; i++) { arr[i] = prims[i]; idx[i] = i; }
            Array.Sort(idx, (x, y) =>
            {
                int gx = arr[x].Group < 0 ? int.MaxValue : arr[x].Group, gy = arr[y].Group < 0 ? int.MaxValue : arr[y].Group;
                return gx != gy ? gx.CompareTo(gy) : x.CompareTo(y);
            });
            return Eval(arr, idx, 0, idx.Length, p, groupK);
        }

        /// <summary>Polygonizes <paramref name="part"/> and appends it to <paramref name="buf"/>. Returns the vertex count added.</summary>
        /// <summary>Set to log vertex count + time per part (tuning).</summary>
        public static bool LogStats;

        public static int Polygonize(SdfPart part, MeshBuffers buf)
        {
            var sw = LogStats ? System.Diagnostics.Stopwatch.StartNew() : null;
            int n = PolygonizeInner(part, buf);
            if (sw != null) Debug.Log("[SdfMesher] " + part.Name + ": " + n + " verts, " + sw.Elapsed.TotalMilliseconds.ToString("F1") + " ms");
            return n;
        }

        private static int PolygonizeInner(SdfPart part, MeshBuffers buf)
        {
            // stable sort: groups ascending, global (-1) last
            var sorted = new List<SdfPrim>(part.Prims);
            var order = new int[sorted.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (x, y) =>
            {
                int gx = sorted[x].Group < 0 ? int.MaxValue : sorted[x].Group;
                int gy = sorted[y].Group < 0 ? int.MaxValue : sorted[y].Group;
                return gx != gy ? gx.CompareTo(gy) : x.CompareTo(y);
            });
            var prims = new SdfPrim[order.Length];
            for (int i = 0; i < order.Length; i++) prims[i] = sorted[order[i]];

            // ---- bounds
            Vector3 bmin = Vector3.one * float.MaxValue, bmax = Vector3.one * float.MinValue;
            bool any = false;
            foreach (var p in prims)
                if (p.Op == SdfOp.Union && !p.Infinite) { bmin = Vector3.Min(bmin, p.BMin); bmax = Vector3.Max(bmax, p.BMax); any = true; }
            if (!any) return 0;
            foreach (var p in prims)
            {
                if (p.Op != SdfOp.Intersect) continue;
                if (!p.Infinite) { bmin = Vector3.Max(bmin, p.BMin); bmax = Vector3.Min(bmax, p.BMax); continue; }
                // axis-aligned clip planes shrink the grid
                Vector3 n = p.B;
                for (int ax = 0; ax < 3; ax++)
                {
                    if (n[ax] > 0.999f) bmax[ax] = Mathf.Min(bmax[ax], p.A[ax] + p.K);
                    else if (n[ax] < -0.999f) bmin[ax] = Mathf.Max(bmin[ax], p.A[ax] - p.K);
                }
            }
            if (part.ClipBounds.HasValue)
            {
                bmin = Vector3.Max(bmin, part.ClipBounds.Value.min);
                bmax = Vector3.Min(bmax, part.ClipBounds.Value.max);
            }
            float h = part.Cell;
            float pad = 2f * h + part.DisplaceMax;
            bmin -= Vector3.one * pad;
            bmax += Vector3.one * pad;
            if (bmax.x <= bmin.x || bmax.y <= bmin.y || bmax.z <= bmin.z) return 0;
            int nx = Mathf.CeilToInt((bmax.x - bmin.x) / h) + 1;
            int ny = Mathf.CeilToInt((bmax.y - bmin.y) / h) + 1;
            int nz = Mathf.CeilToInt((bmax.z - bmin.z) / h) + 1;
            if (nx < 3 || ny < 3 || nz < 3) return 0;

            // ---- block prim lists
            int bx = (nx + BlockSize - 1) / BlockSize, by = (ny + BlockSize - 1) / BlockSize, bz = (nz + BlockSize - 1) / BlockSize;
            var blockStart = new int[bx * by * bz];
            var blockCount = new int[bx * by * bz];
            var flat = new List<int>(prims.Length * 8);
            float margin = 2.5f * h + part.DisplaceMax;
            for (int k = 0; k < bz; k++)
                for (int j = 0; j < by; j++)
                    for (int i = 0; i < bx; i++)
                    {
                        Vector3 lo = bmin + new Vector3(i, j, k) * (BlockSize * h) - Vector3.one * margin;
                        Vector3 hi = bmin + new Vector3(i + 1, j + 1, k + 1) * (BlockSize * h) + Vector3.one * margin;
                        int start = flat.Count;
                        bool union = false;
                        for (int q = 0; q < prims.Length; q++)
                        {
                            ref readonly SdfPrim p = ref prims[q];
                            if (p.Infinite || p.Op == SdfOp.Intersect) { flat.Add(q); continue; }
                            if (p.BMax.x < lo.x || p.BMin.x > hi.x || p.BMax.y < lo.y || p.BMin.y > hi.y || p.BMax.z < lo.z || p.BMin.z > hi.z) continue;
                            flat.Add(q);
                            if (p.Op == SdfOp.Union) union = true;
                        }
                        int bi = i + bx * (j + by * k);
                        if (!union) { flat.RemoveRange(start, flat.Count - start); blockCount[bi] = 0; continue; }
                        blockStart[bi] = start;
                        blockCount[bi] = flat.Count - start;
                    }
            int[] list = flat.ToArray();

            // ---- sample (per block; blocks whose centre is far from the surface are filled with its sign)
            var v = new float[nx * ny * nz];
            float far = BlockSize * h * 2f;
            float dispBand = part.DisplaceMax + 3f * h;
            var disp = part.Displace;
            float skip = 0.87f * BlockSize * h + 2f * h + part.DisplaceMax;
            float groupK = part.GroupK;
            System.Threading.Tasks.Parallel.For(0, bz, k =>
            {
                for (int j = 0; j < by; j++)
                    for (int i = 0; i < bx; i++)
                    {
                        int bi = i + bx * (j + by * k);
                        int c = blockCount[bi], st = blockStart[bi];
                        int x0 = i * BlockSize, y0 = j * BlockSize, z0 = k * BlockSize;
                        int x1 = Mathf.Min(x0 + BlockSize, nx), y1 = Mathf.Min(y0 + BlockSize, ny), z1 = Mathf.Min(z0 + BlockSize, nz);
                        float fill = far;
                        bool evaluate = c > 0;
                        if (evaluate)
                        {
                            var centre = bmin + new Vector3(x0 + (x1 - x0 - 1) * 0.5f, y0 + (y1 - y0 - 1) * 0.5f, z0 + (z1 - z0 - 1) * 0.5f) * h;
                            float dc = Eval(prims, list, st, c, centre, groupK);
                            if (Mathf.Abs(dc) > skip) { evaluate = false; fill = dc > 0f ? far : -far; }
                        }
                        for (int z = z0; z < z1; z++)
                            for (int y = y0; y < y1; y++)
                            {
                                int row = nx * (y + ny * z);
                                for (int x = x0; x < x1; x++)
                                {
                                    if (!evaluate) { v[row + x] = fill; continue; }
                                    var p = new Vector3(bmin.x + x * h, bmin.y + y * h, bmin.z + z * h);
                                    float d = Eval(prims, list, st, c, p, groupK);
                                    if (disp != null && Mathf.Abs(d) < dispBand) d += disp(p);
                                    v[row + x] = d;
                                }
                            }
                    }
            });

            // ---- one vertex per sign-changing cell
            int cx = nx - 1, cy = ny - 1, cz = nz - 1;
            var cellVert = new int[cx * cy * cz];
            int baseIndex = buf.Positions.Count;
            var verts = new List<Vector3>(4096);
            var cellOf = new List<int>(4096);
            var corner = new float[8];
            for (int z = 0; z < cz; z++)
                for (int y = 0; y < cy; y++)
                    for (int x = 0; x < cx; x++)
                    {
                        int ci = x + cx * (y + cy * z);
                        int mask = 0;
                        for (int c = 0; c < 8; c++)
                        {
                            int ix = x + (c & 1), iy = y + ((c >> 1) & 1), iz = z + ((c >> 2) & 1);
                            float val = v[ix + nx * (iy + ny * iz)];
                            corner[c] = val;
                            if (val < 0f) mask |= 1 << c;
                        }
                        if (mask == 0 || mask == 255) { cellVert[ci] = -1; continue; }
                        Vector3 sum = Vector3.zero;
                        int n = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            int a = EdgeA[e], b = EdgeB[e];
                            bool sa = (mask & (1 << a)) != 0, sb = (mask & (1 << b)) != 0;
                            if (sa == sb) continue;
                            float t = corner[a] / (corner[a] - corner[b]);
                            Vector3 pa = new Vector3(a & 1, (a >> 1) & 1, (a >> 2) & 1);
                            Vector3 pb = new Vector3(b & 1, (b >> 1) & 1, (b >> 2) & 1);
                            sum += pa + (pb - pa) * t;
                            n++;
                        }
                        sum /= n;
                        cellVert[ci] = verts.Count;
                        verts.Add(bmin + new Vector3(x + sum.x, y + sum.y, z + sum.z) * h);
                        cellOf.Add(ci);
                    }
            if (verts.Count == 0) return 0;

            // ---- project onto the surface + gradient normals
            var vArr = verts.ToArray();
            var cellArr = cellOf.ToArray();
            var normals = new Vector3[vArr.Length];
            float eps = h * 0.3f;
            System.Threading.Tasks.Parallel.For(0, vArr.Length, i =>
            {
                int ci = cellArr[i];
                int x = ci % cx, y = (ci / cx) % cy, z = ci / (cx * cy);
                int bi = Mathf.Min(x / BlockSize, bx - 1) + bx * (Mathf.Min(y / BlockSize, by - 1) + by * Mathf.Min(z / BlockSize, bz - 1));
                int s0 = blockStart[bi], c = blockCount[bi];
                Vector3 p = vArr[i];
                if (c == 0) { normals[i] = Vector3.up; return; }
                float F(Vector3 q)
                {
                    float d = Eval(prims, list, s0, c, q, groupK);
                    if (disp != null && Mathf.Abs(d) < dispBand) d += disp(q);
                    return d;
                }
                float d0 = F(p);
                Vector3 g = new Vector3(
                    F(p + new Vector3(eps, 0, 0)) - F(p - new Vector3(eps, 0, 0)),
                    F(p + new Vector3(0, eps, 0)) - F(p - new Vector3(0, eps, 0)),
                    F(p + new Vector3(0, 0, eps)) - F(p - new Vector3(0, 0, eps))) / (2f * eps);
                float gl = g.magnitude;
                if (gl > 1e-4f)
                {
                    Vector3 move = -g * (d0 / (gl * gl));
                    float ml = move.magnitude;
                    if (ml > 0.5f * h) move *= 0.5f * h / ml;
                    vArr[i] = p + move;
                    normals[i] = g / gl;
                }
                else normals[i] = Vector3.up;
            });
            verts.Clear();
            verts.AddRange(vArr);

            // ---- skin weights (parallel, per-thread scratch)
            var bw = new BoneWeight[vArr.Length];
            var wsrc = part.Weights;
            if (wsrc != null) System.Threading.Tasks.Parallel.For(0, vArr.Length, () => wsrc.NewScratch(), (i, _, scratch) => { bw[i] = wsrc.Compute(vArr[i], scratch); return scratch; }, _ => { });
            else for (int i = 0; i < bw.Length; i++) bw[i] = new BoneWeight { weight0 = 1f };

            // ---- append vertices
            for (int i = 0; i < verts.Count; i++)
            {
                buf.Positions.Add(verts[i]);
                buf.Normals.Add(normals[i]);
                buf.Uvs.Add(part.Uv != null ? part.Uv(verts[i], normals[i]) : DefaultUv(verts[i]));
                buf.Weights.Add(bw[i]);
            }

            // ---- quads across sign-changing grid edges
            var defaultTris = buf.Slot(part.Slot);
            for (int z = 1; z < cz; z++)
                for (int y = 1; y < cy; y++)
                    for (int x = 0; x < cx; x++)
                        EdgeQuad(v[x + nx * (y + ny * z)], v[x + 1 + nx * (y + ny * z)],
                            Cell(x, y - 1, z - 1), Cell(x, y, z - 1), Cell(x, y, z), Cell(x, y - 1, z));
            for (int z = 1; z < cz; z++)
                for (int y = 0; y < cy; y++)
                    for (int x = 1; x < cx; x++)
                        EdgeQuad(v[x + nx * (y + ny * z)], v[x + nx * (y + 1 + ny * z)],
                            Cell(x - 1, y, z - 1), Cell(x, y, z - 1), Cell(x, y, z), Cell(x - 1, y, z));
            for (int z = 0; z < cz; z++)
                for (int y = 1; y < cy; y++)
                    for (int x = 1; x < cx; x++)
                        EdgeQuad(v[x + nx * (y + ny * z)], v[x + nx * (y + ny * (z + 1))],
                            Cell(x - 1, y - 1, z), Cell(x, y - 1, z), Cell(x, y, z), Cell(x - 1, y, z));
            return verts.Count;

            int Cell(int x, int y, int z) => cellVert[x + cx * (y + cy * z)];

            void EdgeQuad(float v0, float v1, int a, int b, int c, int d)
            {
                if ((v0 < 0f) == (v1 < 0f)) return;
                if (a < 0 || b < 0 || c < 0 || d < 0) return;
                Vector3 pa = verts[a], pb = verts[b], pc = verts[c], pd = verts[d];
                Vector3 nsum = normals[a] + normals[b] + normals[c] + normals[d];
                if (Vector3.Dot(Vector3.Cross(pb - pa, pc - pa) + Vector3.Cross(pc - pa, pd - pa), nsum) < 0f)
                {
                    (b, d) = (d, b);
                    (pb, pd) = (pd, pb);
                }
                bool diagAC = (pa - pc).sqrMagnitude <= (pb - pd).sqrMagnitude;
                if (diagAC) { Tri(a, b, c); Tri(a, c, d); }
                else { Tri(a, b, d); Tri(b, c, d); }
            }

            void Tri(int a, int b, int c)
            {
                var tris = defaultTris;
                if (part.Classify != null)
                {
                    Vector3 cen = (verts[a] + verts[b] + verts[c]) / 3f;
                    Vector3 nrm = normals[a] + normals[b] + normals[c];
                    int slot = part.Classify(cen, nrm.normalized);
                    if (slot >= 0 && slot != part.Slot) tris = buf.Slot(slot);
                }
                tris.Add(baseIndex + a); tris.Add(baseIndex + b); tris.Add(baseIndex + c);
            }
        }

        public static Vector2 DefaultUv(Vector3 p) => new(Mathf.Atan2(p.x, p.z) / (2f * Mathf.PI) + 0.5f, p.y / 2.5f);
    }
}
