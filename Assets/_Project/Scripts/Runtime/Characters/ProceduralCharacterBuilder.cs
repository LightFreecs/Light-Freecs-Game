using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using B = LightMMORPG.Characters.CharacterBone;

namespace LightMMORPG.Characters
{
    /// <summary>
    /// Turns a <see cref="CharacterRecipe"/> into a rigged, skinned anime fighter: an implicit-surface body (smooth-union
    /// of anatomical primitives → surface nets), separate finer head/face and hand meshes, outfit shells built from the
    /// same body primitives grown outwards (so they skin identically), spiky hair clumps, a painted face texture. One
    /// <see cref="SkinnedMeshRenderer"/>, one submesh per material slot, all <c>LightMMORPG/ToonLit</c>.
    /// Meshes are cached per recipe, so repeated spawns of the same hero are free.
    /// </summary>
    public static class ProceduralCharacterBuilder
    {
        public const float RefHeight = 2.3f;
        public const int VertexBudget = 90000;

        public enum SlotKind { Skin, Face, Hair, Cloth, Armor, Dark }

        public struct SlotDef
        {
            public string Key;
            public Color Color;
            public Color Color2;
            public SlotKind Kind;
        }

        /// <summary>Generated, shareable data for one recipe.</summary>
        public sealed class GeneratedCharacter
        {
            /// <summary>Created on the main thread by <see cref="Upload"/>.</summary>
            public Mesh Mesh;
            // thread-safe raw data
            public string Name;
            public Vector3[] Positions, Normals;
            public Vector2[] Uvs;
            public BoneWeight[] Weights;
            public List<int>[] Triangles;
            public Color[] FacePixels;
            public int FaceSize;
            public double UploadMilliseconds;
            public int VertexCount => Positions.Length;
            public List<SlotDef> Slots;
            public Vector3[] BonePositions;
            public float Height;
            public Vector3 ArmDirL, ArmDirR, PalmL, PalmR;
            public Texture2D Face;
            public double Milliseconds;
            public Color Aura;
        }

        // body region tags (outfits copy body primitives by tag)
        private const int TPelvis = 1 << 0, TAbdomen = 1 << 1, TChest = 1 << 2, TNeck = 1 << 3, TTrap = 1 << 4, TDeltoid = 1 << 5,
            TUpperArm = 1 << 6, TElbow = 1 << 7, TForearm = 1 << 8, TThigh = 1 << 9, TKnee = 1 << 10, TShin = 1 << 11, TAnkle = 1 << 12,
            TFoot = 1 << 13, TGlute = 1 << 14, TBack = 1 << 15, TAbs = 1 << 16, TPec = 1 << 17, TLat = 1 << 18;
        private const int TTorso = TPelvis | TAbdomen | TChest | TTrap | TBack | TPec | TLat | TGlute;
        private const int GTorso = 0;
        private static int GArm(int side) => side < 0 ? 1 : 2;
        private static int GLeg(int side) => side < 0 ? 3 : 4;

        private const float BootTop = 0.42f;
        private const float BodyGroupK = 0.035f;

        private static readonly Dictionary<string, GeneratedCharacter> Cache = new();
        private static readonly Dictionary<string, Material> MaterialCache = new();

        // ------------------------------------------------------------------ public API

        /// <summary>Builds the GameObject (bones + SkinnedMeshRenderer + <see cref="CharacterRig"/>), feet at the origin, facing +Z.</summary>
        public static GameObject Build(CharacterRecipe recipe)
        {
            var gen = GetOrGenerate(recipe);
            var root = new GameObject("Character_" + recipe.displayName);
            var bones = new Transform[CharacterBones.Count];
            for (int i = 0; i < bones.Length; i++)
            {
                var t = new GameObject(((CharacterBone)i).ToString()).transform;
                int parent = CharacterBones.Parent(i);
                t.SetParent(parent < 0 ? root.transform : bones[parent], false);
                t.localPosition = gen.BonePositions[i] - (parent < 0 ? Vector3.zero : gen.BonePositions[parent]);
                bones[i] = t;
            }
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(root.transform, false);
            var smr = bodyGo.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = gen.Mesh;
            smr.bones = bones;
            smr.rootBone = bones[(int)B.Hips];
            var mats = new Material[gen.Slots.Count];
            for (int i = 0; i < mats.Length; i++) mats[i] = GetMaterial(gen.Slots[i], gen.Face);
            smr.sharedMaterials = mats;
            var b = gen.Mesh.bounds;
            smr.localBounds = new Bounds(b.center - gen.BonePositions[(int)B.Hips], b.size * 1.6f);
            smr.updateWhenOffscreen = false;
            smr.skinnedMotionVectors = false;

            var rig = root.AddComponent<CharacterRig>();
            rig.Init(bones, gen.Height, gen.ArmDirL, gen.ArmDirR, gen.PalmL, gen.PalmR, gen.Aura);
            rig.ApplyRestPose();
            return root;
        }

        public static GeneratedCharacter GetOrGenerate(CharacterRecipe recipe)
        {
            string key = recipe.CacheKey();
            if (Cache.TryGetValue(key, out var g) && g.Mesh != null && g.Face != null) return g;
            System.Threading.Tasks.Task<GeneratedCharacter> pending;
            lock (Pending) Pending.TryGetValue(key, out pending);
            g = pending != null ? pending.Result : GenerateData(recipe);
            lock (Pending) Pending.Remove(key);
            Upload(g);
            Cache[key] = g;
            return g;
        }

        private static readonly Dictionary<string, System.Threading.Tasks.Task<GeneratedCharacter>> Pending = new();

        /// <summary>Starts generating the mesh data of these recipes on worker threads (call early, e.g. at game start).</summary>
        public static void Prewarm(IEnumerable<CharacterRecipe> recipes)
        {
            foreach (var r in recipes)
            {
                string key = r.CacheKey();
                if (Cache.ContainsKey(key)) continue;
                var copy = r.Clone();
                lock (Pending)
                {
                    if (Pending.ContainsKey(key)) continue;
                    Pending[key] = System.Threading.Tasks.Task.Run(() => GenerateData(copy));
                }
            }
        }

        public static void ClearCache()
        {
            foreach (var g in Cache.Values)
            {
                if (g.Mesh != null) UnityEngine.Object.DestroyImmediate(g.Mesh);
                if (g.Face != null) UnityEngine.Object.DestroyImmediate(g.Face);
            }
            Cache.Clear();
            MaterialCache.Clear();
        }

        /// <summary>Pure mesh generation (no GameObjects, no materials) — used by tests and the cache.</summary>
        public static GeneratedCharacter Generate(CharacterRecipe recipe)
        {
            var g = GenerateData(recipe);
            Upload(g);
            return g;
        }

        /// <summary>Thread-safe part: all geometry + face pixels, no Unity objects.</summary>
        public static GeneratedCharacter GenerateData(CharacterRecipe recipe)
        {
            var sw = Stopwatch.StartNew();
            var c = new Ctx(recipe);
            c.Bones();
            c.Body();
            c.Head();
            c.Hands();
            c.Outfit();
            c.Hair();
            var gen = c.Finish();
            gen.FaceSize = 512;
            gen.FacePixels = FacePainter.PaintFacePixels(recipe, gen.FaceSize);
            gen.Milliseconds = sw.Elapsed.TotalMilliseconds;
            return gen;
        }

        /// <summary>Main thread: creates the Mesh + face texture from the generated data.</summary>
        public static void Upload(GeneratedCharacter g)
        {
            if (g.Mesh != null && g.Face != null) return;
            var sw = Stopwatch.StartNew();
            var mesh = new Mesh { name = "Fighter_" + g.Name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(g.Positions);
            mesh.SetNormals(g.Normals);
            mesh.SetUVs(0, g.Uvs);
            mesh.boneWeights = g.Weights;
            mesh.subMeshCount = g.Triangles.Length;
            for (int i = 0; i < g.Triangles.Length; i++) mesh.SetTriangles(g.Triangles[i], i, false);
            var bind = new Matrix4x4[g.BonePositions.Length];
            for (int i = 0; i < bind.Length; i++) bind[i] = Matrix4x4.Translate(-g.BonePositions[i]);
            mesh.bindposes = bind;
            mesh.RecalculateBounds();
            g.Mesh = mesh;
            g.Face = FacePainter.ToTexture(g.FacePixels, g.FaceSize, "Face_" + g.Name);
            g.UploadMilliseconds = sw.Elapsed.TotalMilliseconds;
        }

        // ------------------------------------------------------------------ materials

        public static Material GetMaterial(SlotDef slot, Texture2D face)
        {
            string key = slot.Kind + "|" + ColorUtility.ToHtmlStringRGBA(slot.Color) + "|" + ColorUtility.ToHtmlStringRGBA(slot.Color2) + (slot.Kind == SlotKind.Face && face != null ? "|" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(face) : "");
            if (MaterialCache.TryGetValue(key, out var m) && m != null) return m;
            m = CreateToonMaterial();
            m.name = "Toon_" + slot.Key;
            Color baseC = slot.Color;
            Color shade = new(0.66f, 0.58f, 0.8f), band2 = new(0.8f, 0.76f, 0.9f);
            float rim = 0.25f, outline = 3.0f, ramp = 0.05f;
            Color outlineC = new(baseC.r * 0.12f + 0.03f, baseC.g * 0.1f + 0.025f, baseC.b * 0.12f + 0.035f);
            switch (slot.Kind)
            {
                case SlotKind.Skin:
                case SlotKind.Face:
                    shade = new Color(0.93f, 0.77f, 0.72f); band2 = new Color(0.9f, 0.82f, 0.86f); rim = 0.2f; ramp = -0.05f;
                    outlineC = new Color(0.2f, 0.09f, 0.08f);
                    if (slot.Kind == SlotKind.Face) { m.SetTexture("_BaseMap", face); baseC = Color.white; }
                    break;
                case SlotKind.Hair:
                    m.SetTexture("_BaseMap", HairTexture(slot.Color, slot.Color2));
                    baseC = Color.white;
                    shade = new Color(0.6f, 0.55f, 0.75f); rim = 0.35f; ramp = 0.1f;
                    outlineC = new Color(slot.Color.r * 0.1f + 0.02f, slot.Color.g * 0.1f + 0.02f, slot.Color.b * 0.1f + 0.03f);
                    break;
                case SlotKind.Armor: rim = 0.6f; shade = new Color(0.62f, 0.62f, 0.82f); break;
                case SlotKind.Dark: rim = 0.25f; break;
            }
            m.SetColor("_BaseColor", baseC);
            m.SetColor("_ShadeColor", shade);
            m.SetFloat("_RampThreshold", ramp);
            m.SetFloat("_RampSmooth", 0.02f);
            m.SetFloat("_Band2Threshold", -0.45f);
            m.SetColor("_Band2Color", band2);
            m.SetFloat("_AmbientStrength", 0.4f);
            m.SetColor("_RimColor", new Color(1f, 0.97f, 0.9f));
            m.SetFloat("_RimPower", 4.5f);
            m.SetFloat("_RimStrength", rim);
            m.SetColor("_EmissionColor", Color.black);
            m.SetColor("_OutlineColor", outlineC);
            m.SetFloat("_OutlineWidth", outline);
            MaterialCache[key] = m;
            return m;
        }

        private static Material CreateToonMaterial()
        {
            var shader = Shader.Find("LightMMORPG/ToonLit");
            Material template = null;
            try { template = Config.GameConfig.Load().fighterSkinMaterial; } catch { /* no config (tests) */ }
            if (template != null && (shader == null || template.shader == shader)) return new Material(template);
            if (shader != null) return new Material(shader);
            return new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        }

        private static readonly Dictionary<string, Texture2D> HairTextures = new();

        private static Texture2D HairTexture(Color root, Color tip)
        {
            string key = ColorUtility.ToHtmlStringRGB(root) + ColorUtility.ToHtmlStringRGB(tip);
            if (HairTextures.TryGetValue(key, out var t) && t != null) return t;
            t = FacePainter.PaintHairGradient(root, tip);
            HairTextures[key] = t;
            return t;
        }

        // ------------------------------------------------------------------ generation context

        private sealed class Ctx
        {
            private readonly CharacterRecipe _r;
            private readonly float _m, _w, _sw, _hs;
            private readonly Vector3[] _p = new Vector3[CharacterBones.Count];
            private readonly List<SdfPrim> _body = new();
            private readonly List<SdfPrim> _headPrims = new();
            private readonly MeshBuffers _buf = new();
            private readonly List<SlotDef> _slots = new();
            private readonly Dictionary<string, int> _slotIndex = new();
            private readonly System.Random _rnd;
            private SkinWeightSource _bodyWeights;
            private Vector3 _armDirL, _armDirR, _palmL, _palmR;

            public Ctx(CharacterRecipe r)
            {
                _r = r;
                _m = r.muscle;
                _w = r.bulk;
                _sw = r.shoulderWidth;
                _hs = r.headSize;
                _rnd = new System.Random(r.seed * 7919 + 17);
            }

            private float Rand() => (float)_rnd.NextDouble();
            private float Rand(float a, float b) => a + (b - a) * Rand();
            private Vector3 RandDir() { Vector3 v; do { v = new Vector3(Rand(-1, 1), Rand(-1, 1), Rand(-1, 1)); } while (v.sqrMagnitude > 1f || v.sqrMagnitude < 1e-3f); return v.normalized; }

            private int Slot(string key, Color color, SlotKind kind, Color color2 = default)
            {
                if (_slotIndex.TryGetValue(key, out int i)) return i;
                color.a = 1f;
                color2.a = 1f;
                _slots.Add(new SlotDef { Key = key, Color = color, Kind = kind, Color2 = color2 });
                _slotIndex[key] = _slots.Count - 1;
                return _slots.Count - 1;
            }

            private Vector3 P(B b) => _p[(int)b];
            private Vector3 P(B leftBone, int side) => _p[CharacterBones.Side(leftBone, side)];
            private int Bn(B leftBone, int side) => CharacterBones.Side(leftBone, side);
            private Vector3 ArmDir(int side) => side < 0 ? _armDirL : _armDirR;
            private Vector3 Palm(int side) => side < 0 ? _palmL : _palmR;

            // ---------------------------------------------------------- skeleton
            public void Bones()
            {
                Set(B.Hips, 0f, 1.13f, 0f);
                Set(B.Spine, 0f, 1.25f, 0f);
                Set(B.Chest, 0f, 1.40f, -0.01f);
                Set(B.UpperChest, 0f, 1.56f, -0.015f);
                Set(B.Neck, 0f, 1.755f, -0.025f);
                Set(B.Head, 0f, 1.865f, -0.005f);
                float a = 40f * Mathf.Deg2Rad;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 dir = new(side * Mathf.Sin(a), -Mathf.Cos(a), 0f);
                    Vector3 palm = new(-side * Mathf.Cos(a), -Mathf.Sin(a), 0f);
                    if (side < 0) { _armDirL = dir; _palmL = palm; } else { _armDirR = dir; _palmR = palm; }
                    _p[Bn(B.LeftShoulder, side)] = new Vector3(side * 0.06f, 1.72f, -0.02f);
                    Vector3 ua = new(side * 0.262f * _sw * (0.5f + 0.5f * _w), 1.70f, -0.025f);
                    _p[Bn(B.LeftUpperArm, side)] = ua;
                    _p[Bn(B.LeftLowerArm, side)] = ua + dir * 0.40f;
                    Vector3 wrist = ua + dir * 0.755f;
                    _p[Bn(B.LeftHand, side)] = wrist;
                    _p[Bn(B.LeftUpperLeg, side)] = new Vector3(side * 0.112f * _w, 1.06f, 0f);
                    _p[Bn(B.LeftLowerLeg, side)] = new Vector3(side * 0.128f * _w, 0.585f, 0.025f);
                    _p[Bn(B.LeftFoot, side)] = new Vector3(side * 0.142f * _w, 0.12f, -0.015f);
                    _p[Bn(B.LeftToes, side)] = new Vector3(side * 0.152f * _w, 0.05f, 0.135f);

                    // fingers: index at the front (+Z = thumb side), palm towards the thigh
                    Vector3 across = Vector3.forward;
                    float[] off = { 0.0255f, 0.0085f, -0.0085f, -0.0255f };
                    float[] along = { 0.087f, 0.09f, 0.087f, 0.079f };
                    float[] prox = { 0.041f, 0.045f, 0.043f, 0.034f };
                    for (int f = 0; f < 4; f++)
                    {
                        Vector3 baseP = wrist + dir * along[f] + across * off[f];
                        int pb = Bn(B.LeftIndexProximal + f * 2, side);
                        _p[pb] = baseP;
                        _p[pb + 1] = baseP + FingerDir(dir, across, f) * prox[f];
                    }
                    Vector3 tb = wrist + dir * 0.022f + across * 0.026f + palm * 0.012f;
                    int tp = Bn(B.LeftThumbProximal, side);
                    _p[tp] = tb;
                    _p[tp + 1] = tb + ThumbDir(dir, across, palm) * 0.036f;
                }
            }

            private static Vector3 FingerDir(Vector3 dir, Vector3 across, int f) => (dir + across * (0.06f - 0.04f * f)).normalized;
            private static Vector3 ThumbDir(Vector3 dir, Vector3 across, Vector3 palm) => (dir * 0.72f + across * 0.58f + palm * 0.3f).normalized;

            private void Set(B b, float x, float y, float z) => _p[(int)b] = new Vector3(x, y, z);

            // ---------------------------------------------------------- body
            private void AddBody(SdfPrim p) => _body.Add(p);

            private static Quaternion RotZ(float deg) => Quaternion.Euler(0f, 0f, deg);

            public void Body()
            {
                float m = _m, w = _w, sw = _sw, wa = _r.waist;
                // torso
                AddBody(SdfPrim.Ellipsoid(new Vector3(0f, 1.07f, -0.012f), new Vector3(0.158f * w, 0.11f, 0.115f), (int)B.Hips, 0.04f, GTorso, TPelvis));
                AddBody(SdfPrim.Limb(new Vector3(0f, 1.10f, -0.005f), new Vector3(0f, 1.38f, 0f), 0.124f, 0.138f, (int)B.Spine, 0.05f, GTorso, TAbdomen, sx: 1.08f * w * wa, sz: 0.72f));
                AddBody(SdfPrim.Ellipsoid(new Vector3(0f, 1.50f, -0.012f), new Vector3(0.182f * w, 0.165f, 0.124f), (int)B.Chest, 0.05f, GTorso, TChest));
                AddBody(SdfPrim.Ellipsoid(new Vector3(0f, 1.62f, -0.028f), new Vector3(0.212f * w * sw, 0.11f, 0.112f), (int)B.UpperChest, 0.05f, GTorso, TChest));
                AddBody(SdfPrim.Ellipsoid(new Vector3(0f, 1.55f, -0.07f), new Vector3(0.165f * w, 0.14f, 0.07f), (int)B.Chest, 0.04f, GTorso, TBack));
                for (int side = -1; side <= 1; side += 2)
                {
                    // lats: wide under the armpit, narrowing to the waist → V taper
                    AddBody(SdfPrim.Ellipsoid(new Vector3(side * 0.128f * w, 1.47f, -0.035f), new Vector3(0.07f * m, 0.15f, 0.08f), RotZ(-side * 20f), (int)B.Chest, 0.04f, GTorso, TLat));
                    // traps slope from neck to shoulder
                    AddBody(SdfPrim.Limb(new Vector3(side * 0.035f, 1.80f, -0.05f), new Vector3(side * 0.19f * sw, 1.715f, -0.035f), 0.05f * Mathf.Sqrt(m), 0.032f, (int)B.UpperChest, 0.04f, GTorso, TTrap));
                    // deltoid caps (weighted to the upper arm)
                    Vector3 ua = P(B.LeftUpperArm, side);
                    AddBody(SdfPrim.Ellipsoid(ua + ArmDir(side) * 0.035f + new Vector3(side * 0.012f, 0.006f, 0f), new Vector3(0.078f * m, 0.1f, 0.088f * m), RotZ(side * 40f), Bn(B.LeftUpperArm, side), 0.035f, GTorso, TDeltoid));
                    // glutes
                    AddBody(SdfPrim.Ellipsoid(new Vector3(side * 0.07f * w, 1.045f, -0.078f), new Vector3(0.08f * w, 0.085f, 0.068f), (int)B.Hips, 0.03f, GTorso, TGlute));
                    // obliques
                    AddBody(SdfPrim.Ellipsoid(new Vector3(side * 0.108f * w * wa, 1.2f, 0.012f), new Vector3(0.05f, 0.085f, 0.06f), (int)B.Spine, 0.04f, GTorso, TAbdomen));
                }
                // pecs (placed on the current chest surface)
                for (int side = -1; side <= 1; side += 2)
                {
                    float z = FrontZ(side * 0.085f * w, 1.575f) - 0.024f * m;
                    var rot = Quaternion.Euler(0f, side * 22f, side * -8f);
                    AddBody(SdfPrim.Ellipsoid(new Vector3(side * 0.082f * w, 1.578f, z), new Vector3(0.094f * m * w, 0.066f * m, 0.05f * m), rot, (int)B.UpperChest, 0.02f, GTorso, TPec));
                }
                // abs: 3 rows x 2, small blends so the grooves stay visible
                float[] rows = m >= 0.85f ? new[] { 1.39f, 1.315f, 1.24f } : new float[0];
                foreach (float y in rows)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float z = FrontZ(side * 0.036f, y) - 0.006f;
                        AddBody(SdfPrim.Ellipsoid(new Vector3(side * 0.034f, y, z), new Vector3(0.031f * m, 0.03f, 0.018f * m), (int)B.Spine, 0.012f, GTorso, TAbs));
                    }
                // neck
                AddBody(SdfPrim.Limb(new Vector3(0f, 1.68f, -0.025f), new Vector3(0f, 1.93f, -0.008f), 0.058f * Mathf.Sqrt(m), 0.05f, (int)B.Neck, 0.03f, GTorso, TNeck));

                for (int side = -1; side <= 1; side += 2)
                {
                    int g = GArm(side);
                    Vector3 dir = ArmDir(side);
                    Vector3 ua = P(B.LeftUpperArm, side), la = P(B.LeftLowerArm, side), wr = P(B.LeftHand, side);
                    int bu = Bn(B.LeftUpperArm, side), bl = Bn(B.LeftLowerArm, side);
                    Quaternion armRot = RotZ(side * 40f);
                    Vector3 perp = Vector3.Cross(dir, Vector3.forward).normalized;
                    AddBody(SdfPrim.Limb(ua - dir * 0.02f, la, 0.064f * Mathf.Sqrt(m), 0.049f, bu, 0.03f, g, TUpperArm));
                    AddBody(SdfPrim.Ellipsoid(ua + dir * 0.2f + new Vector3(0f, 0f, 0.022f), new Vector3(0.047f * m, 0.105f, 0.05f * m), armRot, bu, 0.02f, g, TUpperArm));
                    AddBody(SdfPrim.Ellipsoid(ua + dir * 0.17f + new Vector3(0f, 0f, -0.026f) + perp * (side * 0.008f), new Vector3(0.05f * m, 0.12f, 0.047f * m), armRot, bu, 0.02f, g, TUpperArm));
                    AddBody(SdfPrim.Ellipsoid(la, Vector3.one * 0.045f, bl, 0.02f, g, TElbow));
                    AddBody(SdfPrim.Limb(la, wr, 0.054f, 0.035f, bl, 0.03f, g, TForearm, sx: 0.9f, sz: 1.12f, hint: perp));
                    AddBody(SdfPrim.Ellipsoid(la + dir * 0.09f + new Vector3(0f, 0f, 0.01f) - perp * (side * 0.008f), new Vector3(0.05f * m, 0.1f, 0.055f * m), armRot, bl, 0.03f, g, TForearm));

                    int gl = GLeg(side);
                    Vector3 ul = P(B.LeftUpperLeg, side), ll = P(B.LeftLowerLeg, side), ft = P(B.LeftFoot, side), to = P(B.LeftToes, side);
                    int bt = Bn(B.LeftUpperLeg, side), bs = Bn(B.LeftLowerLeg, side), bf = Bn(B.LeftFoot, side), btoe = Bn(B.LeftToes, side);
                    Vector3 thigh = ll - ul;
                    Quaternion thighRot = Quaternion.FromToRotation(Vector3.up, thigh.normalized);
                    AddBody(SdfPrim.Limb(ul + new Vector3(0f, 0.03f, 0f), ll, 0.104f * w, 0.062f, bt, 0.04f, gl, TThigh));
                    AddBody(SdfPrim.Ellipsoid(ul + thigh * 0.45f + new Vector3(0f, 0f, 0.03f), new Vector3(0.068f * m, 0.17f, 0.064f * m), thighRot, bt, 0.03f, gl, TThigh));
                    AddBody(SdfPrim.Ellipsoid(ul + thigh * 0.52f + new Vector3(side * 0.035f, 0f, 0.005f), new Vector3(0.05f * m, 0.15f, 0.055f * m), thighRot, bt, 0.03f, gl, TThigh));
                    AddBody(SdfPrim.Ellipsoid(ul + thigh * 0.8f + new Vector3(-side * 0.03f, 0f, 0.024f), new Vector3(0.04f * m, 0.065f, 0.04f * m), thighRot, bt, 0.03f, gl, TThigh));
                    AddBody(SdfPrim.Ellipsoid(ll, Vector3.one * 0.055f, bs, 0.025f, gl, TKnee));
                    Vector3 shin = ft - ll;
                    Quaternion shinRot = Quaternion.FromToRotation(Vector3.up, shin.normalized);
                    AddBody(SdfPrim.Limb(ll, ft, 0.057f, 0.036f, bs, 0.03f, gl, TShin));
                    AddBody(SdfPrim.Ellipsoid(ll + shin * 0.27f + new Vector3(0f, 0f, -0.034f), new Vector3(0.06f * m, 0.12f, 0.054f * m), shinRot, bs, 0.03f, gl, TShin));
                    AddBody(SdfPrim.Ellipsoid(ft, Vector3.one * 0.04f, bf, 0.02f, gl, TAnkle));
                    Vector3 heel = new(ft.x, 0.058f, -0.05f), ball = new(to.x, 0.045f, to.z - 0.005f);
                    AddBody(SdfPrim.Limb(heel, ball, 0.05f, 0.042f, bf, 0.03f, gl, TFoot, sx: 1.05f, sz: 0.8f, hint: Vector3.right));
                    AddBody(SdfPrim.Limb(ball, new Vector3(to.x + side * 0.004f, 0.04f, to.z + 0.065f), 0.04f, 0.03f, btoe, 0.02f, gl, TFoot, sx: 1.1f, sz: 0.72f, hint: Vector3.right));
                }
                _bodyWeights = new SkinWeightSource(_body, CharacterBones.Count);

                int skin = Slot("skin", _r.skinTone, SlotKind.Skin);
                var part = new SdfPart("Body", 0.0165f, skin) { GroupK = BodyGroupK, Weights = _bodyWeights };
                int hidden = HiddenSkinTags();
                foreach (var p in _body)
                    if ((p.Tag & hidden) == 0) part.Add(p);
                SdfMesher.Polygonize(part, _buf);
            }

            /// <summary>Body regions fully covered by the outfit (not meshed as skin).</summary>
            private int HiddenSkinTags()
            {
                var r = _r;
                bool suit = r.outfit == OutfitType.ArmoredSuit;
                int h = 0;
                bool longLegs = suit || r.legwear == LegWear.Pants;
                if (longLegs && r.boots) h |= TThigh | TKnee | TShin | TAnkle | TFoot | TGlute;
                else if (r.boots) h |= TAnkle | TFoot;
                if (suit || r.undershirt) h |= TTorso | TAbs;
                if (suit || r.sleeveLength >= 0.95f) h |= TUpperArm | TElbow | TForearm | TDeltoid;
                return h;
            }

            /// <summary>Front surface z of the current torso at (x, y).</summary>
            private float FrontZ(float x, float y)
            {
                float z = 0.35f;
                for (int i = 0; i < 80; i++)
                {
                    float d = SdfMesher.Evaluate(_body, new Vector3(x, y, z), BodyGroupK);
                    if (d < 0.0005f) break;
                    z -= Mathf.Max(d, 0.001f);
                }
                return z;
            }

            // ---------------------------------------------------------- head + face
            private Vector3 HL(float x, float y, float z) => P(B.Head) + new Vector3(x, y, z) * _hs;

            public void Head()
            {
                float hs = _hs;
                int head = (int)B.Head;
                _headPrims.Add(SdfPrim.Ellipsoid(HL(0f, 0.13f, -0.014f), new Vector3(0.116f, 0.13f, 0.13f) * hs, head, 0.04f, 0));
                _headPrims.Add(SdfPrim.Ellipsoid(HL(0f, 0.068f, 0.034f), new Vector3(0.088f, 0.085f, 0.088f) * hs, head, 0.045f, 0));
                for (int side = -1; side <= 1; side += 2)
                {
                    _headPrims.Add(SdfPrim.Limb(HL(side * 0.074f, 0.085f, -0.012f), HL(side * 0.014f, -0.006f, 0.077f), 0.024f * hs, 0.017f * hs, head, 0.03f, 0));
                    _headPrims.Add(SdfPrim.Ellipsoid(HL(side * 0.11f, 0.068f, -0.014f), new Vector3(0.014f, 0.032f, 0.022f) * hs, Quaternion.Euler(0f, -side * 20f, 0f), head, 0.01f, 0));
                }
                _headPrims.Add(SdfPrim.Ellipsoid(HL(0f, -0.008f, 0.077f), new Vector3(0.026f, 0.019f, 0.02f) * hs, head, 0.025f, 0));
                _headPrims.Add(SdfPrim.Limb(HL(0f, 0.064f, 0.112f), HL(0f, 0.038f, 0.124f), 0.006f * hs, 0.007f * hs, head, 0.012f, 0));

                int skin = Slot("skin", _r.skinTone, SlotKind.Skin);
                int face = Slot("face", Color.white, SlotKind.Face);
                Vector3 hp = P(B.Head);
                var part = new SdfPart("Head", 0.0088f, skin) { GroupK = 0.03f, Weights = new SkinWeightSource(head) };
                part.Prims.AddRange(_headPrims);
                part.Uv = (p, n) => FacePainter.FaceUv((p - hp) / hs);
                part.Classify = (cen, nrm) =>
                {
                    Vector3 l = (cen - hp) / hs;
                    return l.z > 0.015f && nrm.z > 0.1f && Mathf.Abs(l.x) < 0.112f && l.y > -0.058f && l.y < 0.178f ? face : -1;
                };
                SdfMesher.Polygonize(part, _buf);
            }

            // ---------------------------------------------------------- hands
            public void Hands()
            {
                int skin = Slot("skin", _r.skinTone, SlotKind.Skin);
                for (int side = -1; side <= 1; side += 2)
                {
                    var prims = new List<SdfPrim>();
                    Vector3 dir = ArmDir(side), palm = Palm(side), across = Vector3.forward, w = P(B.LeftHand, side);
                    int hand = Bn(B.LeftHand, side);
                    Quaternion frame = Quaternion.LookRotation(palm, dir);
                    prims.Add(SdfPrim.Box(w + dir * 0.048f + palm * 0.002f, frame, new Vector3(0.038f, 0.046f, 0.0165f), 0.012f, SdfOp.Union, 0.012f, 0, hand));
                    prims.Add(SdfPrim.Limb(w - dir * 0.05f, w + dir * 0.02f, 0.034f, 0.031f, hand, 0.015f, 0, 0, sx: 0.78f, sz: 1.15f, hint: palm));
                    prims.Add(SdfPrim.Ellipsoid(w + dir * 0.035f + across * 0.021f + palm * 0.009f, new Vector3(0.017f, 0.028f, 0.014f), frame, hand, 0.014f, 0));
                    float[] inter = { 0.036f, 0.04f, 0.037f, 0.029f };
                    for (int f = 0; f < 4; f++)
                    {
                        int pb = Bn(B.LeftIndexProximal + f * 2, side);
                        Vector3 a = _p[pb], b = _p[pb + 1];
                        Vector3 tip = b + FingerDir(dir, across, f) * inter[f];
                        prims.Add(SdfPrim.Limb(a - dir * 0.01f, b, 0.0098f, 0.0088f, pb, 0.004f, 1 + f));
                        prims.Add(SdfPrim.Limb(b, tip, 0.0088f, 0.0074f, pb + 1, 0.004f, 1 + f));
                    }
                    int tp = Bn(B.LeftThumbProximal, side);
                    Vector3 td = ThumbDir(dir, across, palm);
                    prims.Add(SdfPrim.Limb(_p[tp] - td * 0.01f, _p[tp + 1], 0.0118f, 0.0104f, tp, 0.005f, 5));
                    prims.Add(SdfPrim.Limb(_p[tp + 1], _p[tp + 1] + (td + palm * 0.25f).normalized * 0.031f, 0.0104f, 0.0086f, tp + 1, 0.005f, 5));
                    var part = new SdfPart("Hand", 0.006f, skin) { GroupK = 0.0045f, Weights = new SkinWeightSource(prims, CharacterBones.Count, 0.006f) };
                    part.Prims.AddRange(prims);
                    SdfMesher.Polygonize(part, _buf);
                }
            }

            // ---------------------------------------------------------- outfits
            private SdfPart Shell(string name, float cell, int slot, int tags, float offset, float kAdd, int exclude = 0, int onlyGroup = int.MinValue)
            {
                var part = new SdfPart(name, cell, slot) { GroupK = BodyGroupK + kAdd, Weights = _bodyWeights };
                foreach (var p in _body)
                {
                    if ((p.Tag & tags) == 0 || (p.Tag & exclude) != 0) continue;
                    if (onlyGroup != int.MinValue && p.Group != onlyGroup) continue;
                    part.Add(p.Inflated(offset, kAdd));
                }
                return part;
            }

            private void Emit(SdfPart part) => SdfMesher.Polygonize(part, _buf);

            private static float Smooth01(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

            private int ArmTags(float sleeve) => sleeve < 0.02f ? 0 : (TDeltoid | TUpperArm | (sleeve > 0.5f ? TElbow : 0) | (sleeve > 0.55f ? TForearm : 0));

            private void SleeveCuts(SdfPart part, float sleeve, float baggy)
            {
                if (sleeve < 0.02f) return;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 dir = ArmDir(side), ua = P(B.LeftUpperArm, side);
                    float start = 0.08f + sleeve * 0.64f;
                    if (baggy > 0f && start < 0.42f)
                        part.Add(SdfPrim.Limb(ua + dir * 0.05f, ua + dir * start, 0.07f * Mathf.Sqrt(_m), 0.07f + 0.012f * baggy, Bn(B.LeftUpperArm, side), 0.03f, GArm(side)));
                    part.Add(SdfPrim.Cylinder(ua + dir * (start + 0.6f), dir, 0.21f, 0.6f, SdfOp.Subtract, 0.006f));
                }
            }

            /// <summary>Band of width <paramref name="w"/> just outside the opening's side edges (front only).</summary>
            private static Func<Vector3, float> OpeningEdge(float halfW, float vY, float vSlope, float w)
                => p =>
                {
                    float ox = Mathf.Abs(p.x) - (halfW + Mathf.Max(0f, p.y - vY) * vSlope);
                    return Mathf.Max(Mathf.Max(ox - w, -ox - 0.004f), 0.01f - p.z);
                };

            /// <summary>Front opening: |x| &lt; half width (widening above vY into a V), front side only.</summary>
            private static Func<Vector3, float> Opening(float halfW, float vY, float vSlope)
                => p => Mathf.Max(Mathf.Abs(p.x) - (halfW + Mathf.Max(0f, p.y - vY) * vSlope), 0.015f - p.z);

            public void Outfit()
            {
                var r = _r;
                switch (r.outfit)
                {
                    case OutfitType.Gi: Gi(); break;
                    case OutfitType.ArmoredSuit: Armor(); break;
                    case OutfitType.CroppedJacket: CroppedJacket(); break;
                    default: Jacket(); break;
                }
                if (r.outfit != OutfitType.ArmoredSuit) Pants(r.outfit == OutfitType.Gi ? r.outfitPrimary : r.outfitSecondary);
                if (r.belt) Belt();
                if (r.wristbands) Wristbands();
                if (r.boots) Boots();
                if (r.accessory == Accessory.ShoulderPads || r.outfit == OutfitType.ArmoredSuit) ShoulderPads(r.outfit == OutfitType.ArmoredSuit ? r.outfitSecondary : r.accessoryColor);
                if (r.accessory == Accessory.Scarf) Scarf();
            }

            private void Gi()
            {
                var r = _r;
                int top = Slot("top", r.outfitPrimary, SlotKind.Cloth);
                int lapel = Slot("lapel", r.outfitSecondary, SlotKind.Cloth);
                int tags = TTorso | ArmTags(r.sleeveLength);
                var open = Opening(0f, 1.43f, 1.05f);
                var p = Shell("GiTop", 0.016f, top, tags, 0.022f, 0.03f, TAbs);
                p.Add(SdfPrim.Ellipsoid(new Vector3(0f, 1.03f, -0.012f), new Vector3(0.2f * _w, 0.085f, 0.15f), (int)B.Hips, 0.05f, GTorso));
                p.Add(SdfPrim.Keep(new Vector3(0f, 0.985f, 0f), Vector3.down));
                SleeveCuts(p, r.sleeveLength, 1f);
                p.Add(SdfPrim.Custom(open, SdfOp.Subtract, 0.006f));
                p.Displace = q => 0.0035f * Smooth01(1.32f, 1.05f, q.y) * Mathf.Sin(q.x * 95f + Mathf.Sin(q.z * 30f) * 2f);
                p.DisplaceMax = 0.0036f;
                Emit(p);
                if (r.undershirt) Undershirt();

                // lapel band along the V (a slightly bigger shell, intersected with the band around the opening)
                var l = Shell("GiLapel", 0.0095f, lapel, TChest | TTrap | TBack | TPec | TLat, 0.03f, 0.03f, TAbs);
                l.Add(SdfPrim.Custom(OpeningEdge(0f, 1.43f, 1.05f, 0.026f), SdfOp.Intersect, 0.004f));
                l.Add(SdfPrim.Keep(new Vector3(0f, 1.36f, 0f), Vector3.down));
                Emit(l);
            }

            private void Undershirt(float maxY = 9f)
            {
                int under = Slot("undershirt", Color.Lerp(_r.outfitSecondary, Color.black, 0.25f), SlotKind.Cloth);
                var u = Shell("Undershirt", 0.016f, under, TTorso | TAbs, 0.006f, 0.004f);
                u.Add(SdfPrim.Limb(new Vector3(0f, 1.69f, -0.025f), new Vector3(0f, 1.8f, -0.02f), 0.066f, 0.064f, (int)B.Neck, 0.02f, GTorso));
                u.Add(SdfPrim.Keep(new Vector3(0f, 1.0f, 0f), Vector3.down));
                if (maxY < 9f) u.Add(SdfPrim.Keep(new Vector3(0f, maxY, 0f), Vector3.up));
                Emit(u);
            }

            private void Armor()
            {
                var r = _r;
                int suit = Slot("suit", r.outfitPrimary, SlotKind.Cloth);
                int armor = Slot("armor", r.outfitSecondary, SlotKind.Armor);
                int trim = Slot("trim", r.outfitTrim, SlotKind.Armor);
                var s = Shell("Bodysuit", 0.0165f, suit, TTorso | TAbs | TDeltoid | TUpperArm | TElbow | TForearm | TThigh | TKnee | TShin, 0.006f, 0.004f);
                s.Add(SdfPrim.Limb(new Vector3(0f, 1.69f, -0.025f), new Vector3(0f, 1.84f, -0.02f), 0.069f, 0.066f, (int)B.Neck, 0.02f, GTorso));
                Emit(s);

                float hx = 0.258f * _w;
                var plate = Shell("ChestPlate", 0.014f, armor, TChest | TPec | TLat | TBack | TTrap | TAbdomen, 0.036f, 0.045f, TAbs, GTorso);
                plate.Add(SdfPrim.Keep(new Vector3(0f, 1.8f, 0f), Vector3.up));
                plate.Add(SdfPrim.Keep(new Vector3(0f, 1.33f, 0f), Vector3.down));
                plate.Add(SdfPrim.Keep(new Vector3(hx, 0f, 0f), Vector3.right));
                plate.Add(SdfPrim.Keep(new Vector3(-hx, 0f, 0f), Vector3.left));
                plate.Add(SdfPrim.Cylinder(new Vector3(0f, 1.82f, -0.02f), Vector3.up, 0.094f, 0.2f, SdfOp.Subtract, 0.01f));
                Emit(plate);

                var t = Shell("PlateTrim", 0.009f, trim, TChest | TLat | TBack | TAbdomen, 0.043f, 0.045f, TAbs, GTorso);
                t.Add(SdfPrim.Keep(new Vector3(0f, 1.37f, 0f), Vector3.up));
                t.Add(SdfPrim.Keep(new Vector3(0f, 1.325f, 0f), Vector3.down));
                t.Add(SdfPrim.Keep(new Vector3(hx + 0.006f, 0f, 0f), Vector3.right));
                t.Add(SdfPrim.Keep(new Vector3(-hx - 0.006f, 0f, 0f), Vector3.left));
                Emit(t);
                // collar ring
                var c = new SdfPart("PlateCollar", 0.008f, trim) { Weights = _bodyWeights };
                c.Add(SdfPrim.Limb(new Vector3(0f, 1.79f, -0.02f), new Vector3(0f, 1.835f, -0.022f), 0.1f, 0.096f, (int)B.UpperChest, 0.01f, GTorso));
                c.Add(SdfPrim.Cylinder(new Vector3(0f, 1.8f, -0.02f), Vector3.up, 0.082f, 0.2f, SdfOp.Subtract, 0.006f));
                Emit(c);
            }

            private void Jacket()
            {
                var r = _r;
                int jacket = Slot("jacket", r.outfitPrimary, SlotKind.Cloth);
                int trim = Slot("trim", r.outfitTrim, SlotKind.Cloth);
                if (r.undershirt) Undershirt();
                bool vest = r.sleeveLength < 0.02f;
                var open = vest ? Opening(0.085f, 1.36f, 0.6f) : Opening(0.05f, 1.48f, 0.95f);
                var edge = vest ? OpeningEdge(0.085f, 1.36f, 0.6f, 0.022f) : OpeningEdge(0.05f, 1.48f, 0.95f, 0.022f);
                float hem = vest ? 1.0f : 0.9f;
                var j = Shell("Jacket", 0.016f, jacket, TTorso | ArmTags(r.sleeveLength), 0.026f, 0.035f, TAbs);
                j.Add(SdfPrim.Ellipsoid(new Vector3(0f, hem + 0.07f, -0.012f), new Vector3(0.21f * _w, 0.12f, 0.16f), (int)B.Hips, 0.05f, GTorso));
                if (!vest) j.Add(SdfPrim.Limb(new Vector3(0f, 1.12f, -0.015f), new Vector3(0f, hem - 0.02f, -0.025f), 0.2f, 0.225f, (int)B.Hips, 0.06f, GTorso, sx: 1.05f * _w, sz: 0.74f));
                j.Add(SdfPrim.Keep(new Vector3(0f, hem, 0f), Vector3.down));
                SleeveCuts(j, r.sleeveLength, 0f);
                j.Add(SdfPrim.Custom(open, SdfOp.Subtract, 0.006f));
                j.Displace = q => 0.003f * Smooth01(1.3f, 1.08f, q.y) * Mathf.Sin(q.x * 85f + Mathf.Sin(q.z * 25f) * 2f)
                                  + (q.y < 1.08f ? 0.004f * Mathf.Sin(Mathf.Atan2(q.z, q.x) * 9f) * Smooth01(1.08f, hem, q.y) : 0f);
                j.DisplaceMax = 0.0072f;
                Emit(j);

                var t = Shell("JacketTrim", 0.0085f, trim, TTorso | (vest ? 0 : TDeltoid), 0.037f, 0.035f, TAbs);
                if (!vest) t.Add(SdfPrim.Limb(new Vector3(0f, 1.12f, -0.015f), new Vector3(0f, hem - 0.02f, -0.025f), 0.211f, 0.236f, (int)B.Hips, 0.06f, GTorso, sx: 1.05f * _w, sz: 0.74f));
                t.Add(SdfPrim.Ellipsoid(new Vector3(0f, hem + 0.07f, -0.012f), new Vector3(0.221f * _w, 0.131f, 0.171f), (int)B.Hips, 0.05f, GTorso));
                t.Add(SdfPrim.Custom(edge, SdfOp.Intersect, 0.004f));
                t.Add(SdfPrim.Keep(new Vector3(0f, hem - 0.004f, 0f), Vector3.down));
                t.Add(SdfPrim.Keep(new Vector3(0f, 1.82f, 0f), Vector3.up));
                Emit(t);
            }

            private float BootTopY => _r.bootLaces ? 0.5f : BootTop;

            private void Pants(Color color)
            {
                var r = _r;
                int pants = Slot("pants", color, SlotKind.Cloth);
                float bag = r.pantsBagginess;
                bool shorts = r.legwear == LegWear.Shorts;
                float bottom = shorts ? 0.69f : (r.boots ? BootTopY - 0.035f : 0.13f);
                var p = Shell("Pants", 0.016f, pants, TPelvis | TGlute | TThigh | TKnee | TShin | TAbdomen, shorts ? 0.026f : 0.022f, 0.02f);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 ul = P(B.LeftUpperLeg, side), ll = P(B.LeftLowerLeg, side), ft = P(B.LeftFoot, side);
                    if (shorts)
                    {
                        // slightly flared shorts legs
                        p.Add(SdfPrim.Limb(Vector3.Lerp(ul, ll, 0.25f), Vector3.Lerp(ul, ll, 0.9f), 0.11f, 0.1f * bag, Bn(B.LeftUpperLeg, side), 0.05f, GLeg(side)));
                        continue;
                    }
                    Vector3 top = Vector3.Lerp(ul, ll, 0.3f), low = Vector3.Lerp(ll, ft, Mathf.InverseLerp(ll.y, ft.y, BootTopY + 0.05f));
                    p.Add(SdfPrim.Limb(top, low, 0.1f + 0.022f * bag, 0.068f + 0.038f * bag, Bn(B.LeftUpperLeg, side), 0.05f, GLeg(side)));
                    p.Add(SdfPrim.Ellipsoid(low + new Vector3(0f, -0.01f, -0.005f), new Vector3(0.08f + 0.03f * bag, 0.06f, 0.08f + 0.03f * bag), Bn(B.LeftLowerLeg, side), 0.04f, GLeg(side)));
                }
                p.Add(SdfPrim.Keep(new Vector3(0f, 1.16f, 0f), Vector3.up));
                p.Add(SdfPrim.Keep(new Vector3(0f, bottom, 0f), Vector3.down));
                float legX = P(B.LeftUpperLeg, 1).x;
                float bt = BootTopY;
                if (shorts)
                {
                    p.Displace = q => q.y > 1.0f ? 0f : 0.003f * Mathf.Sin(Mathf.Atan2(q.z - 0.01f, Mathf.Abs(q.x) - legX) * 4f + q.y * 12f);
                    p.DisplaceMax = 0.0031f;
                }
                else
                {
                    p.Displace = q =>
                    {
                        if (q.y > 1.0f) return 0f;
                        float ang = Mathf.Atan2(q.z - 0.01f, Mathf.Abs(q.x) - legX);
                        float vertical = 0.0045f * bag * Mathf.Sin(ang * 5f + q.y * 9f) * Mathf.Lerp(0.4f, 1f, Smooth01(0.95f, 0.55f, q.y));
                        float bunch = 0.004f * bag * Mathf.Sin(q.y * 72f + ang * 1.5f) * Smooth01(bt + 0.16f, bt + 0.02f, q.y);
                        return vertical + bunch;
                    };
                    p.DisplaceMax = 0.0086f * bag;
                }
                Emit(p);
                if (shorts)
                {
                    // rolled cuff at the hem
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 ul = P(B.LeftUpperLeg, side), ll = P(B.LeftLowerLeg, side);
                        var c = new SdfPart("ShortsCuff", 0.009f, pants) { Weights = _bodyWeights };
                        c.Add(SdfPrim.Limb(Vector3.Lerp(ul, ll, 0.25f), Vector3.Lerp(ul, ll, 0.9f), 0.12f, 0.112f * bag, Bn(B.LeftUpperLeg, side), 0.01f, 0));
                        foreach (var bp in _body)
                            if (bp.Group == GLeg(side) && (bp.Tag & TThigh) != 0) c.Add(bp.Inflated(0.036f, 0.02f));
                        c.Add(SdfPrim.Keep(new Vector3(0f, bottom + 0.042f, 0f), Vector3.up, 0.003f));
                        c.Add(SdfPrim.Keep(new Vector3(0f, bottom - 0.004f, 0f), Vector3.down, 0.003f));
                        Emit(c);
                    }
                }
            }

            private void Belt()
            {
                int belt = Slot("belt", _r.beltColor, SlotKind.Cloth);
                var b = Shell("Belt", 0.0085f, belt, TAbdomen | TPelvis, 0.036f, 0.005f, TAbs, GTorso);
                b.Add(SdfPrim.Keep(new Vector3(0f, 1.168f, 0f), Vector3.up, 0.004f));
                b.Add(SdfPrim.Keep(new Vector3(0f, 1.098f, 0f), Vector3.down, 0.004f));
                Emit(b);
                if (_r.outfit == OutfitType.ArmoredSuit) return;
                float zf = FrontZ(0.03f, 1.133f) + 0.036f;
                if (_r.outfit != OutfitType.Gi)
                {
                    int buckle = Slot("buckle", new Color(0.78f, 0.8f, 0.84f), SlotKind.Armor);
                    var bk = new SdfPart("Buckle", 0.005f, buckle) { GroupK = 0.005f, Weights = _bodyWeights };
                    float bz = FrontZ(0f, 1.133f) + 0.036f;
                    bk.Add(SdfPrim.Box(new Vector3(0f, 1.133f, bz), Quaternion.identity, new Vector3(0.034f, 0.024f, 0.008f), 0.004f, SdfOp.Union, 0.004f, 0, (int)B.Hips));
                    bk.Add(SdfPrim.Box(new Vector3(0f, 1.133f, bz + 0.01f), Quaternion.identity, new Vector3(0.02f, 0.011f, 0.01f), 0.002f, SdfOp.Subtract, 0.002f, -1));
                    Emit(bk);
                    return;
                }
                var k = new SdfPart("BeltKnot", 0.0065f, belt) { GroupK = 0.01f, Weights = _bodyWeights };
                Vector3 knot = new(0.035f, 1.133f, zf + 0.008f);
                k.Add(SdfPrim.Ellipsoid(knot, new Vector3(0.03f, 0.026f, 0.018f), (int)B.Hips, 0.01f, 0));
                k.Add(SdfPrim.Limb(knot, new Vector3(0.062f, 0.95f, zf + 0.03f), 0.019f, 0.016f, (int)B.Hips, 0.01f, 1, sx: 1.5f, sz: 0.42f, hint: Vector3.right));
                k.Add(SdfPrim.Limb(knot, new Vector3(0.014f, 0.97f, zf + 0.026f), 0.019f, 0.016f, (int)B.Hips, 0.01f, 2, sx: 1.5f, sz: 0.42f, hint: Vector3.right));
                Emit(k);
            }

            private void Wristbands()
            {
                int band = Slot("wrist", _r.wristbandColor, SlotKind.Cloth);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 dir = ArmDir(side), la = P(B.LeftLowerArm, side);
                    var w = Shell("Wristband", 0.008f, band, TForearm, 0.014f, 0.003f, 0, GArm(side));
                    w.Add(SdfPrim.Cylinder(la + dir * (0.355f * 0.83f), dir, 0.15f, 0.355f * 0.16f, SdfOp.Intersect, 0.003f));
                    Emit(w);
                }
            }

            private void Boots()
            {
                var r = _r;
                float top = BootTopY;
                int boot = Slot("boot", r.bootColor, SlotKind.Cloth);
                int cuff = Slot("bootTrim", r.bootTrimColor, SlotKind.Cloth);
                int sole = Slot("sole", Color.Lerp(r.bootColor, Color.black, 0.72f), SlotKind.Dark);
                int lace = r.bootLaces ? Slot("laces", r.laceColor, SlotKind.Cloth) : -1;
                for (int side = -1; side <= 1; side += 2)
                {
                    var b = Shell("Boot", 0.013f, boot, TShin | TAnkle | TFoot, 0.017f, 0.012f, 0, GLeg(side));
                    b.Add(SdfPrim.Keep(new Vector3(0f, top, 0f), Vector3.up));
                    Emit(b);
                    float cuffH = r.bootLaces ? 0.075f : 0.05f;
                    var c = Shell("BootCuff", 0.009f, cuff, TShin, r.bootLaces ? 0.033f : 0.028f, 0.012f, 0, GLeg(side));
                    c.Add(SdfPrim.Keep(new Vector3(0f, top + 0.004f, 0f), Vector3.up));
                    c.Add(SdfPrim.Keep(new Vector3(0f, top - cuffH, 0f), Vector3.down));
                    Emit(c);
                    // sole + toe cap
                    float toeZ = P(B.LeftToes, side).z + 0.065f;
                    var s = Shell("Sole", 0.009f, sole, TFoot, 0.022f, 0.006f, 0, GLeg(side));
                    s.Add(SdfPrim.Custom(q => Mathf.Min(q.y - 0.03f, (toeZ - 0.045f) - q.z), SdfOp.Intersect, 0.004f));
                    Emit(s);
                    if (lace < 0) continue;
                    // laced front panel with cross-lace ridges
                    Vector3 ll = P(B.LeftLowerLeg, side), ft = P(B.LeftFoot, side), to = P(B.LeftToes, side);
                    var l = Shell("Laces", 0.006f, lace, TShin | TAnkle | TFoot, 0.022f, 0.012f, 0, GLeg(side));
                    l.ClipBounds = MinMax(Mathf.Min(ll.x, ft.x) - 0.05f, 0.05f, -0.03f, Mathf.Max(ll.x, ft.x) + 0.05f, top, 0.25f);
                    float yTop = top - cuffH - 0.004f;
                    l.Add(SdfPrim.Custom(q =>
                    {
                        float t = Mathf.InverseLerp(ll.y, ft.y, q.y);
                        float lx = Mathf.Lerp(ll.x, ft.x, t);
                        float lz = q.y > ft.y ? Mathf.Lerp(ll.z, ft.z, t) : Mathf.Lerp(ft.z, to.z, Mathf.InverseLerp(ft.y, 0.05f, q.y));
                        float w = q.y > ft.y + 0.03f ? 0.021f : 0.026f;
                        return Mathf.Max(Mathf.Max(Mathf.Abs(q.x - lx) - w, lz + 0.02f - q.z), Mathf.Max(q.y - yTop, 0.075f - q.y));
                    }, SdfOp.Intersect, 0.003f));
                    l.Displace = q => 0.0022f * Mathf.Abs(Mathf.Sin(q.y * 95f + Mathf.Abs(q.x) * 140f));
                    l.DisplaceMax = 0.0023f;
                    Emit(l);
                }
            }

            private void TrimBand(string name, int tags, Func<Vector3, float> region, int slot, Bounds clip, float offset = 0.034f)
            {
                var t = Shell(name, 0.0075f, slot, tags, offset, 0.03f, TAbs);
                t.ClipBounds = clip;
                t.Add(SdfPrim.Custom(region, SdfOp.Intersect, 0.003f));
                Emit(t);
            }

            private static Bounds MinMax(float x0, float y0, float z0, float x1, float y1, float z1)
            {
                var b = new Bounds();
                b.SetMinMax(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));
                return b;
            }

            private void CroppedJacket()
            {
                var r = _r;
                int jacket = Slot("jacket", r.outfitPrimary, SlotKind.Cloth);
                int trim = Slot("trim", r.outfitTrim, SlotKind.Cloth);
                const float hem = 1.235f;
                if (r.undershirt) Undershirt(hem + 0.05f);
                int armTags = ArmTags(r.sleeveLength);
                var j = Shell("CropJacket", 0.016f, jacket, TTorso | armTags, 0.026f, 0.03f, TAbs);
                j.Add(SdfPrim.Keep(new Vector3(0f, hem, 0f), Vector3.down));
                SleeveCuts(j, r.sleeveLength, 0f);
                j.Displace = q => 0.0025f * Mathf.Sin(q.x * 70f + q.z * 25f) * Smooth01(1.38f, hem, q.y);
                j.DisplaceMax = 0.0026f;
                Emit(j);

                // mandarin collar
                var c = new SdfPart("Collar", 0.009f, jacket) { Weights = _bodyWeights };
                c.Add(SdfPrim.Limb(new Vector3(0f, 1.69f, -0.03f), new Vector3(0f, 1.835f, -0.035f), 0.096f, 0.086f, (int)B.Neck, 0.01f, 0, sx: 1.05f));
                c.Add(SdfPrim.Cylinder(new Vector3(0f, 1.8f, -0.03f), Vector3.up, 0.07f, 0.3f, SdfOp.Subtract, 0.005f));
                c.Add(SdfPrim.Custom(q => Mathf.Max(Mathf.Abs(q.x) - 0.016f, 0.02f - q.z), SdfOp.Subtract, 0.004f));
                c.Add(SdfPrim.Keep(new Vector3(0f, 1.835f, 0f), Vector3.up, 0.004f));
                Emit(c);
                var ct = new SdfPart("CollarTrim", 0.0075f, trim) { Weights = _bodyWeights };
                ct.Add(SdfPrim.Limb(new Vector3(0f, 1.69f, -0.03f), new Vector3(0f, 1.84f, -0.035f), 0.1f, 0.09f, (int)B.Neck, 0.01f, 0, sx: 1.05f));
                ct.Add(SdfPrim.Cylinder(new Vector3(0f, 1.8f, -0.03f), Vector3.up, 0.074f, 0.3f, SdfOp.Subtract, 0.004f));
                ct.Add(SdfPrim.Custom(q => Mathf.Max(Mathf.Abs(q.x) - 0.012f, 0.02f - q.z), SdfOp.Subtract, 0.003f));
                ct.Add(SdfPrim.Custom(q => Mathf.Min(1.81f - q.y, Mathf.Abs(q.x) - 0.032f) * -1f + 0f, SdfOp.Subtract, 0.003f));
                ct.Add(SdfPrim.Keep(new Vector3(0f, 1.84f, 0f), Vector3.up, 0.004f));
                Emit(ct);

                // contrast piping: hem, front placket, slanted chest pockets
                TrimBand("HemTrim", TTorso, q => Mathf.Max(hem - q.y, q.y - (hem + 0.03f)), trim, MinMax(-0.4f, hem - 0.01f, -0.3f, 0.4f, hem + 0.04f, 0.3f));
                TrimBand("Placket", TTorso, q => Mathf.Max(Mathf.Max(Mathf.Abs(q.x) - 0.012f, 0.02f - q.z), Mathf.Max(hem - q.y, q.y - 1.72f)), trim, MinMax(-0.03f, hem - 0.01f, 0f, 0.03f, 1.73f, 0.3f));
                for (int side = -1; side <= 1; side += 2)
                {
                    float cx = side * 0.095f * _w, cy = 1.33f, ang = side * 28f * Mathf.Deg2Rad;
                    float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                    TrimBand("Pocket", TTorso, q =>
                    {
                        float dx = q.x - cx, dy = q.y - cy;
                        float u = dx * ca + dy * sa, v = -dx * sa + dy * ca;
                        return Mathf.Max(Mathf.Max(Mathf.Abs(u) - 0.045f, Mathf.Abs(v) - 0.0055f), 0.03f - q.z);
                    }, trim, MinMax(cx - 0.06f, cy - 0.04f, 0f, cx + 0.06f, cy + 0.04f, 0.3f), 0.032f);
                }
                if (r.sleeveLength >= 0.9f)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 dir = ArmDir(side), ua = P(B.LeftUpperArm, side);
                        float end = 0.08f + r.sleeveLength * 0.64f;
                        var cf = Shell("Cuff", 0.008f, trim, TForearm, 0.034f, 0.03f, 0, GArm(side));
                        cf.Add(SdfPrim.Cylinder(ua + dir * (end - 0.028f), dir, 0.15f, 0.026f, SdfOp.Intersect, 0.003f));
                        Emit(cf);
                    }
            }

            private void ShoulderPads(Color color)
            {
                int pad = Slot("pads", color, SlotKind.Armor);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 ua = P(B.LeftUpperArm, side);
                    var part = new SdfPart("ShoulderPad", 0.01f, pad) { GroupK = 0.008f, Weights = new SkinWeightSource(Bn(B.LeftShoulder, side)) };
                    Vector3 c = new(ua.x + side * 0.03f, 1.79f, -0.012f);
                    part.Add(SdfPrim.Ellipsoid(c, new Vector3(0.13f, 0.042f, 0.12f), RotZ(-side * 20f), -1, 0.01f, 0));
                    part.Add(SdfPrim.Ellipsoid(c + new Vector3(side * 0.04f, -0.05f, 0f), new Vector3(0.108f, 0.032f, 0.102f), RotZ(-side * 34f), -1, 0.01f, 1));
                    Emit(part);
                }
            }

            private void Scarf()
            {
                int scarf = Slot("scarf", _r.accessoryColor, SlotKind.Cloth);
                var s = new SdfPart("Scarf", 0.01f, scarf) { GroupK = 0.025f, Weights = _bodyWeights };
                s.Add(SdfPrim.Limb(new Vector3(0f, 1.715f, -0.03f), new Vector3(0f, 1.835f, -0.02f), 0.1f, 0.092f, (int)B.Neck, 0.03f, 0, sx: 1.06f));
                s.Add(SdfPrim.Ellipsoid(new Vector3(0.04f, 1.72f, 0.08f), new Vector3(0.055f, 0.045f, 0.04f), (int)B.UpperChest, 0.03f, 0));
                Vector3[] tail = { new(0.05f, 1.76f, -0.1f), new(0.12f, 1.56f, -0.23f), new(0.17f, 1.32f, -0.28f), new(0.21f, 1.08f, -0.3f) };
                float[] rad = { 0.05f, 0.045f, 0.037f, 0.024f };
                for (int i = 0; i < tail.Length - 1; i++)
                    s.Add(SdfPrim.Limb(tail[i], tail[i + 1], rad[i], rad[i + 1], (int)B.UpperChest, 0.02f, 1, sx: 1.55f, sz: 0.36f, hint: Vector3.right));
                Vector3[] tail2 = { new(-0.03f, 1.75f, -0.1f), new(-0.08f, 1.55f, -0.21f), new(-0.11f, 1.36f, -0.25f) };
                for (int i = 0; i < tail2.Length - 1; i++)
                    s.Add(SdfPrim.Limb(tail2[i], tail2[i + 1], rad[i] * 0.9f, rad[i + 1] * 0.9f, (int)B.UpperChest, 0.02f, 2, sx: 1.5f, sz: 0.36f, hint: Vector3.right));
                s.Displace = q => q.y < 1.7f ? 0.004f * Mathf.Sin(q.y * 40f + q.x * 30f) : 0f;
                s.DisplaceMax = 0.0042f;
                Emit(s);
            }

            // ---------------------------------------------------------- hair
            private static readonly Vector3 CraniumC = new(0f, 0.125f, -0.012f);
            private static readonly Vector3 CraniumR = new(0.112f, 0.128f, 0.128f);
            private static Vector3 Scalp(Vector3 u) => CraniumC + Vector3.Scale(u, CraniumR);
            private static bool InHairRegion(Vector3 s) => s.y > 0.16f + 0.9f * (s.z - 0.115f) && s.y > -0.03f;

            public void Hair()
            {
                var r = _r;
                int hair = Slot("hair", r.hairColor, SlotKind.Hair, r.hairTipColor);
                // scalp cap fills the gaps between clumps
                var cap = new SdfPart("HairCap", 0.012f, hair) { GroupK = 0.02f, Weights = new SkinWeightSource((int)B.Head) };
                cap.Add(SdfPrim.Ellipsoid(HL(0f, 0.127f, -0.014f), (CraniumR + Vector3.one * 0.011f) * _hs, (int)B.Head, 0.02f, 0));
                cap.Add(SdfPrim.Keep(HL(0f, 0.145f, 0.115f), new Vector3(0f, -1f, 0.9f)));
                for (int side = -1; side <= 1; side += 2)
                    cap.Add(SdfPrim.Ellipsoid(HL(side * 0.115f, 0.055f, -0.01f), new Vector3(0.035f, 0.045f, 0.04f) * _hs, -1, 0.01f, -1).WithOp(SdfOp.Subtract));
                cap.Uv = (p, n) => new Vector2(0.5f, 0.08f);
                Emit(cap);

                var hb = new HairBuilder(_buf, hair, P(B.Head), _hs);
                Vector3 up = Vector3.up, back = Vector3.back, down = Vector3.down, fwd = Vector3.forward;
                float L = r.spikeLength, curl = r.spikeCurl;
                int n = r.spikeCount;
                int candidates = n * 4;
                int placed = 0;
                for (int i = 0; i < candidates && placed < n; i++)
                {
                    float y = 1f - 2f * (i + 0.5f) / candidates;
                    float rad = Mathf.Sqrt(1f - y * y);
                    float phi = i * 2.399963f;
                    Vector3 u = new Vector3(Mathf.Cos(phi) * rad, y, Mathf.Sin(phi) * rad);
                    u = (u + RandDir() * 0.08f).normalized;
                    Vector3 s = Scalp(u);
                    if (!InHairRegion(s)) continue;
                    placed++;
                    // keep density: only every other candidate when there are plenty
                    Vector3 jit = RandDir();
                    Vector3 g; float len, width; Vector3 bend;
                    switch (r.hairStyle)
                    {
                        case HairStyle.SweptBack:
                            g = (back * 1f + up * 0.3f + u * 0.6f + jit * 0.12f).normalized;
                            len = Rand(0.11f, 0.19f) * L * (u.y < 0f ? 0.8f : 1f);
                            width = Rand(0.042f, 0.056f);
                            bend = (down * 0.6f + back * 0.4f).normalized;
                            break;
                        case HairStyle.Wild:
                            g = (u * 1f + up * 0.45f + back * 0.12f + jit * 0.45f).normalized;
                            len = Rand(0.16f, 0.32f) * L * (u.y < -0.1f ? 0.75f : 1f);
                            width = Rand(0.058f, 0.08f);
                            bend = Vector3.ProjectOnPlane(RandDir() + down * 0.6f, g).normalized;
                            break;
                        case HairStyle.LongSpiky:
                            if (u.z < 0.15f && u.y < 0.75f)
                            {
                                g = (down * 1f + back * 0.6f + u * 0.4f + jit * 0.15f).normalized;
                                len = Rand(0.32f, 0.58f) * L * Mathf.Lerp(1.15f, 0.8f, Mathf.Clamp01(u.y));
                                width = Rand(0.055f, 0.07f);
                                bend = (back * 0.5f + Vector3.right * Mathf.Sign(u.x) * 0.4f + down * 0.2f).normalized;
                            }
                            else
                            {
                                g = (back * 0.85f + up * 0.45f + u * 0.5f + jit * 0.12f).normalized;
                                len = Rand(0.15f, 0.25f) * L;
                                width = Rand(0.05f, 0.062f);
                                bend = (down * 0.7f + back * 0.3f).normalized;
                            }
                            break;
                        case HairStyle.Short:
                            g = (u + up * 0.6f + jit * 0.2f).normalized;
                            len = Rand(0.06f, 0.1f) * L;
                            width = Rand(0.04f, 0.05f);
                            bend = back;
                            break;
                        default: // SpikyUp
                            if (u.y < 0.05f)
                            {
                                g = (u * 1f + down * 0.35f + back * 0.5f + jit * 0.2f).normalized;
                                len = Rand(0.1f, 0.16f) * L;
                            }
                            else
                            {
                                g = (u * 1.0f + up * 1.1f + back * (0.25f + Mathf.Max(0f, u.z) * 0.45f) + jit * 0.22f).normalized;
                                len = Rand(0.17f, 0.3f) * L * (0.8f + 0.35f * u.y);
                            }
                            width = Rand(0.075f, 0.1f);
                            bend = (back * 0.7f + down * 0.3f).normalized;
                            break;
                    }
                    hb.Clump(s, u, g, len, width, curl * Rand(0.6f, 1.3f), bend, Rand(-0.6f, 0.6f));
                }

                // flowing layer over the whole scalp (follows the style's flow, lies close to the head)
                int layer = r.hairStyle == HairStyle.Short ? 18 : 26;
                int layerCandidates = layer * 3;
                for (int i = 0; i < layerCandidates && layer > 0; i++)
                {
                    float y = 1f - 2f * (i + 0.5f) / layerCandidates;
                    float rad = Mathf.Sqrt(1f - y * y);
                    float phi = i * 2.399963f + 1.1f;
                    Vector3 u = new Vector3(Mathf.Cos(phi) * rad, y, Mathf.Sin(phi) * rad);
                    Vector3 sp = Scalp(u);
                    if (!InHairRegion(sp) || sp.y < 0.02f) continue;
                    layer--;
                    Vector3 flow = r.hairStyle switch
                    {
                        HairStyle.SpikyUp => up * 1f + back * 0.4f,
                        HairStyle.Wild => u + up * 0.5f,
                        HairStyle.LongSpiky => back * 1f + down * 0.6f,
                        HairStyle.Short => back * 0.6f + up * 0.6f,
                        _ => back * 1f + down * 0.2f,
                    };
                    flow = Vector3.ProjectOnPlane(flow, u);
                    if (flow.sqrMagnitude < 1e-3f) flow = Vector3.ProjectOnPlane(back, u);
                    Vector3 g = (flow.normalized + u * 0.45f + RandDir() * 0.12f).normalized;
                    float len = Rand(0.1f, 0.16f) * Mathf.Lerp(1f, L, 0.5f);
                    hb.Clump(sp, u, g, len, Rand(0.07f, 0.095f), 0.35f, (-u * 0.6f + flow.normalized * 0.4f).normalized, Rand(-0.3f, 0.3f));
                }

                // bangs over the forehead + side locks framing the face
                int bangs = r.hairStyle switch { HairStyle.SweptBack => 2, HairStyle.Short => 4, HairStyle.LongSpiky => 5, HairStyle.SpikyUp => 4, _ => 4 };
                float bangLen = r.hairStyle switch { HairStyle.LongSpiky => 0.13f, HairStyle.Short => 0.075f, HairStyle.SweptBack => 0.12f, HairStyle.Wild => 0.1f, _ => 0.1f };
                for (int i = 0; i < bangs; i++)
                {
                    float t = bangs == 1 ? 0.5f : i / (float)(bangs - 1);
                    float x = r.hairStyle == HairStyle.SweptBack ? (i == 0 ? -0.06f : 0.045f) : Mathf.Lerp(-0.085f, 0.085f, t) + Rand(-0.006f, 0.006f);
                    Vector3 u = new Vector3(x / 0.112f, 0.66f, 0.74f).normalized;
                    Vector3 s = Scalp(u);
                    Vector3 g = r.hairStyle == HairStyle.SpikyUp || r.hairStyle == HairStyle.Wild
                        ? new Vector3(x * 4f + Rand(-0.2f, 0.2f), -0.55f, 0.85f).normalized
                        : new Vector3(x * 2.6f + Rand(-0.15f, 0.15f), -1f, 0.5f).normalized;
                    float len = bangLen * L * Rand(0.82f, 1.15f) * (1f - 0.25f * Mathf.Abs(x) / 0.07f);
                    hb.Clump(s, u, g, len, Rand(0.05f, 0.064f), 0.25f, (fwd * 0.6f + down * 0.4f).normalized, Rand(-0.4f, 0.4f));
                }
                float lockLen = r.hairStyle switch { HairStyle.LongSpiky => 0.24f, HairStyle.Short => 0.07f, HairStyle.Wild => 0.14f, _ => 0.12f };
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 u = new Vector3(side * 0.93f, 0.2f, 0.32f).normalized;
                    Vector3 s = Scalp(u);
                    Vector3 g = new Vector3(side * 0.22f, -1f, 0.18f).normalized;
                    hb.Clump(s, u, g, lockLen * L, 0.046f, 0.2f, (fwd * 0.5f + Vector3.right * side * 0.5f).normalized, 0f);
                    if (r.hairStyle != HairStyle.Short)
                    {
                        Vector3 u2 = new Vector3(side * 0.85f, 0.42f, -0.3f).normalized;
                        hb.Clump(Scalp(u2), u2, new Vector3(side * 0.6f, -0.5f, -0.6f).normalized, lockLen * 0.9f * L, 0.05f, 0.3f, down, 0.2f);
                    }
                }
            }

            // ---------------------------------------------------------- finish
            public GeneratedCharacter Finish()
            {
                float s = _r.height / RefHeight;
                var pos = _buf.Positions;
                for (int i = 0; i < pos.Count; i++) pos[i] *= s;
                var bones = new Vector3[_p.Length];
                for (int i = 0; i < bones.Length; i++) bones[i] = _p[i] * s;

                while (_buf.Triangles.Count < _slots.Count) _buf.Triangles.Add(new List<int>());
                return new GeneratedCharacter
                {
                    Name = _r.displayName,
                    Positions = pos.ToArray(), Normals = _buf.Normals.ToArray(), Uvs = _buf.Uvs.ToArray(), Weights = _buf.Weights.ToArray(),
                    Triangles = _buf.Triangles.GetRange(0, _slots.Count).ToArray(),
                    Slots = new List<SlotDef>(_slots), BonePositions = bones, Height = _r.height,
                    ArmDirL = _armDirL, ArmDirR = _armDirR, PalmL = _palmL, PalmR = _palmR, Aura = _r.auraColor,
                };
            }
        }

        /// <summary>Tapered, curved, twisted hair clumps (elliptical cross-section, sharp tip), rigid on the head bone.</summary>
        private sealed class HairBuilder
        {
            private const int Segments = 7, Ring = 7;
            private readonly MeshBuffers _buf;
            private readonly int _slot;
            private readonly Vector3 _head;
            private readonly float _hs;

            public HairBuilder(MeshBuffers buf, int slot, Vector3 headPos, float headScale) { _buf = buf; _slot = slot; _head = headPos; _hs = headScale; }

            public void Clump(Vector3 baseLocal, Vector3 outward, Vector3 growth, float length, float width, float curl, Vector3 bendDir, float twist)
            {
                Vector3 p0 = baseLocal - outward * 0.014f - growth * 0.01f;
                Vector3 p2 = p0 + growth * length + bendDir * (length * curl);
                Vector3 p1 = p0 + growth * (length * 0.5f);
                var tris = _buf.Slot(_slot);
                int start = _buf.Positions.Count;
                var bw = new BoneWeight { boneIndex0 = (int)B.Head, weight0 = 1f };
                for (int j = 0; j < Segments; j++)
                {
                    float t = j / (float)Segments;
                    Vector3 pos = Bez(p0, p1, p2, t);
                    Vector3 tan = BezD(p0, p1, p2, t).normalized;
                    Vector3 side = Vector3.Cross(tan, outward);
                    if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(tan, Vector3.right);
                    side.Normalize();
                    Vector3 nrm = Vector3.Cross(side, tan).normalized;
                    float wt = width * 0.5f * Mathf.Pow(1f - t, 0.85f) * (1f + 0.3f * Mathf.Sin(Mathf.PI * t));
                    float th = wt * 0.5f;
                    float rot = twist * t;
                    for (int a = 0; a < Ring; a++)
                    {
                        float ang = a * (2f * Mathf.PI / Ring) + rot;
                        float c = Mathf.Cos(ang), s = Mathf.Sin(ang);
                        Vector3 v = pos + side * (c * wt) + nrm * (s * th);
                        Vector3 n = (side * (c / wt) + nrm * (s / th)).normalized;
                        n = (n + tan * 0.25f).normalized;
                        _buf.Positions.Add(_head + v * _hs);
                        _buf.Normals.Add(n);
                        _buf.Uvs.Add(new Vector2(a / (float)Ring, t));
                        _buf.Weights.Add(bw);
                    }
                }
                int tip = _buf.Positions.Count;
                _buf.Positions.Add(_head + p2 * _hs);
                _buf.Normals.Add(BezD(p0, p1, p2, 1f).normalized);
                _buf.Uvs.Add(new Vector2(0.5f, 1f));
                _buf.Weights.Add(bw);

                // winding: first quad's geometric normal must agree with the vertex normal
                Vector3 q0 = _buf.Positions[start], q1 = _buf.Positions[start + 1], q2 = _buf.Positions[start + Ring + 1];
                bool flip = Vector3.Dot(Vector3.Cross(q1 - q0, q2 - q0), _buf.Normals[start]) < 0f;
                for (int j = 0; j < Segments - 1; j++)
                    for (int a = 0; a < Ring; a++)
                    {
                        int i00 = start + j * Ring + a, i01 = start + j * Ring + (a + 1) % Ring;
                        int i10 = i00 + Ring, i11 = i01 + Ring;
                        Add(tris, i00, i01, i11, flip);
                        Add(tris, i00, i11, i10, flip);
                    }
                int last = start + (Segments - 1) * Ring;
                for (int a = 0; a < Ring; a++) Add(tris, last + a, last + (a + 1) % Ring, tip, flip);
            }

            private static void Add(List<int> t, int a, int b, int c, bool flip)
            {
                t.Add(a);
                if (flip) { t.Add(c); t.Add(b); } else { t.Add(b); t.Add(c); }
            }

            private static Vector3 Bez(Vector3 a, Vector3 b, Vector3 c, float t) => (1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c;
            private static Vector3 BezD(Vector3 a, Vector3 b, Vector3 c, float t) => 2 * (1 - t) * (b - a) + 2 * t * (c - b);
        }
    }
}
