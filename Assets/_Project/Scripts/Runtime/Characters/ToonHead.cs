using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightMMORPG.Characters
{
    /// <summary>Which painted eye the toon head wears (the traced sketches + the hand-made layered eye). Append only: saves store the index.</summary>
    public enum ToonEyeStyle { Drawn, Tall, Wide, HandMade, Sharp, Arch }

    /// <summary>Which brow the toon head wears (Brow Bench). Classic = the original painted brow. Append only.</summary>
    public enum ToonBrowStyle { Classic, Sharp, Peak, Block }

    /// <summary>Face expressions of the Brow Bench, played by <see cref="ToonFaceAnimator"/> on top of the player's sliders.</summary>
    public enum ToonExpression { Neutral, Determined, Angry, Sad, Surprised, Happy, Joy }

    /// <summary>
    /// Player-facing head settings: the 22 sliders of the Toon Head bench (all 1 / 0 = the measured base head) and the eye.
    /// A character creator writes these; <see cref="ToonHead.Apply"/> turns them into a <see cref="ToonHeadShape"/>.
    /// </summary>
    [Serializable]
    public sealed class ToonHeadSettings
    {
        public ToonEyeStyle eyeStyle = ToonEyeStyle.Drawn;
        public ToonBrowStyle browStyle = ToonBrowStyle.Classic;

        [Header("Skull")]
        [Range(0.85f, 1.15f)] public float headWidth = 1f;
        [Tooltip("Skull height above the eyes.")]
        [Range(0.85f, 1.15f)] public float skullHeight = 1f;
        [Range(0.8f, 1.2f)] public float backOfHead = 1f;
        [Range(0.85f, 1.2f)] public float faceDepth = 1f;

        [Header("Cheeks & jaw")]
        [Range(0.85f, 1.15f)] public float cheekWidth = 1f;
        [Range(-1f, 1f)] public float cheekFull = 0f;
        [Range(0.7f, 1.4f)] public float jawWidth = 1f;

        [Header("Chin")]
        [Tooltip("Face length from the eyes to the chin.")]
        [Range(0.8f, 1.25f)] public float faceLength = 1f;
        [Range(0.4f, 2.5f)] public float chinWidth = 1f;
        [Tooltip("-1 = pointed, 1 = square.")]
        [Range(-1f, 1f)] public float chinShape = 0f;
        [Range(-0.03f, 0.04f)] public float chinForward = 0f;

        [Header("Nose & mouth")]
        [Range(0f, 2f)] public float noseSize = 1f;
        [Range(-0.04f, 0.04f)] public float noseHeight = 0f;
        [Range(-0.04f, 0.04f)] public float mouthHeight = 0f;

        [Header("Eyes")]
        [Range(0.75f, 1.25f)] public float eyeSize = 1f;
        [Range(-0.05f, 0.05f)] public float eyeHeight = 0f;
        [Range(-0.03f, 0.03f)] public float eyeSpacing = 0f;
        [Range(-0.03f, 0.04f)] public float browHeight = 0f;

        [Header("Brows (Brow Bench)")]
        [Range(0.7f, 1.4f)] public float browSize = 1f;
        [Tooltip("Degrees; positive turns the outer end up and the nose end down.")]
        [Range(-25f, 25f)] public float browAngle = 0f;
        [Tooltip("Moves both brows outward (head units).")]
        [Range(-0.03f, 0.03f)] public float browSpacing = 0f;

        [Header("Ears & neck")]
        [Range(0.7f, 1.4f)] public float earSize = 1f;
        [Range(-0.06f, 0.06f)] public float earHeight = 0f;
        [Tooltip("Degrees added to the ear angle (negative = flatter to the head).")]
        [Range(-25f, 30f)] public float earFlare = 0f;
        [Range(0.75f, 1.35f)] public float neckWidth = 1f;

        public ToonHeadSettings Clone() => (ToonHeadSettings)MemberwiseClone();
    }

    /// <summary>The head shapes from the Face Shape Bench, rebuilt on the measured base head (same numbers as the Toon Head bench).</summary>
    public static class ToonHeadPresets
    {
        public static readonly string[] Names = { "Your model", "Soft-V", "Round", "Sharp", "Square-Jaw Hero", "Long / Mature", "Heart", "Young" };

        public static ToonHeadSettings Get(int index, ToonEyeStyle eyes = ToonEyeStyle.Drawn)
        {
            var s = new ToonHeadSettings { eyeStyle = eyes };
            switch (index)
            {
                case 1: s.cheekFull = 0.35f; s.jawWidth = 0.96f; s.chinWidth = 1.35f; s.chinShape = 0.15f; break;
                case 2: s.cheekWidth = 1.06f; s.cheekFull = 0.85f; s.jawWidth = 1.18f; s.chinWidth = 1.8f; s.chinShape = 0.55f; s.faceLength = 0.92f; s.noseSize = 0.8f; break;
                case 3: s.headWidth = 0.98f; s.cheekWidth = 0.97f; s.cheekFull = -0.35f; s.jawWidth = 0.93f; s.chinWidth = 0.7f; s.chinShape = -0.7f; s.faceLength = 1.08f; s.chinForward = 0.012f; s.browHeight = -0.012f; break;
                case 4: s.headWidth = 1.03f; s.cheekWidth = 1.05f; s.cheekFull = 0.2f; s.jawWidth = 1.3f; s.chinWidth = 2.2f; s.chinShape = 1f; s.faceLength = 1.08f; s.chinForward = 0.012f; s.neckWidth = 1.25f; s.eyeSize = 0.92f; s.browHeight = -0.012f; break;
                case 5: s.headWidth = 0.95f; s.skullHeight = 0.96f; s.faceLength = 1.2f; s.cheekWidth = 0.97f; s.jawWidth = 1.1f; s.chinWidth = 1.3f; s.chinShape = 0.3f; s.chinForward = 0.018f; s.eyeSize = 0.86f; s.noseSize = 1.3f; s.neckWidth = 1.12f; s.earSize = 0.95f; break;
                case 6: s.headWidth = 1.05f; s.skullHeight = 1.04f; s.cheekWidth = 0.92f; s.cheekFull = -0.3f; s.jawWidth = 0.85f; s.chinWidth = 0.7f; s.chinShape = -0.45f; break;
                case 7: s.headWidth = 1.04f; s.skullHeight = 1.06f; s.faceLength = 0.88f; s.cheekFull = 0.6f; s.chinWidth = 1.2f; s.eyeSize = 1.08f; s.noseSize = 0.6f; s.neckWidth = 0.85f; s.earSize = 1.05f; break;
            }
            return s;
        }
    }

    /// <summary>A resolved head shape in head units (chin = 0, crown = <see cref="Top"/>, ~1). Face looks along +Z, +X = the character's right.</summary>
    public sealed class ToonHeadShape
    {
        public float CrY, CrR, CrW, CrF, CrB, CrP;
        public float[][] Face;            // rows: y, half width, front depth, back depth, front squareness
        public float JawA, JawY, ChinRound, NapeY;
        public float NeckTop, NeckNape, NeckBottom, NeckBottomBack, NeckW, NeckF, NeckB, NeckZ;
        public float CheekY, CheekH, CheekA, CheekSpread, CheekAmount;
        public float NoseY, NoseH, NoseW, NoseLen;
        public float EyeX, EyeY, EyeSize;
        /// <summary>Front depth of the unshaped head at the eye centre (the forehead's lean over the eye is measured from here).</summary>
        public float EyeZ;
        public float BrowY, BrowW, BrowH;
        /// <summary>Brow line at brow height 0 (the measured brows are fitted to the eye here, then the slider moves them).</summary>
        public float BrowY0;
        public float MouthY, MouthW, MouthH;
        public float EarY, EarZ, EarSu, EarSv, EarFlare;
        public float Top;
    }

    /// <summary>
    /// The Toon Head (from the design bench): a smooth anime head measured from the user's Blockbench model, reshaped by
    /// <see cref="ToonHeadSettings"/>, with ears, a neck and the data the <c>LightMMORPG/ToonFace</c> shader needs
    /// (face coordinates for the painted shadow map + a face mask). Thread-safe, no Unity objects.
    /// Port of <c>applyShape / buildHead / buildEars / faceShadowMap</c> in the bench page.
    /// </summary>
    public static class ToonHead
    {
        // measured from the Blockbench model (head straightened, chin = 0, crown = 1)
        private static readonly float[][] BaseFace =
        {
            new[] { 0.63f, 0.379f, 0.441f, 0.394f, 2.20f },
            new[] { 0.56f, 0.373f, 0.442f, 0.389f, 2.25f },
            new[] { 0.50f, 0.361f, 0.440f, 0.381f, 2.35f },
            new[] { 0.45f, 0.345f, 0.427f, 0.366f, 2.40f },
            new[] { 0.40f, 0.329f, 0.405f, 0.346f, 2.40f },
            new[] { 0.35f, 0.321f, 0.397f, 0.318f, 2.25f },
            new[] { 0.30f, 0.316f, 0.405f, 0.286f, 2.00f },
            new[] { 0.25f, 0.308f, 0.413f, 0.253f, 1.75f },
            new[] { 0.20f, 0.291f, 0.410f, 0.231f, 1.70f },
            new[] { 0.15f, 0.254f, 0.391f, 0.232f, 1.75f },
            new[] { 0.10f, 0.193f, 0.338f, 0.240f, 2.10f },
            new[] { 0.05f, 0.118f, 0.302f, 0.262f, 2.30f },
            new[] { 0.00f, 0.034f, 0.282f, 0.270f, 2.30f },
        };
        private const float BaseEyeY = 0.38f;

        public const int Cols = 72, CraniumRows = 16, FaceRows = 40, NeckRows = 4;
        public const float FrontBias = 1.35f;

        private static float Clamp01(float t) => t < 0f ? 0f : t > 1f ? 1f : t;
        private static float Smooth(float t) { t = Clamp01(t); return t * t * (3f - 2f * t); }
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static float Sq(float v) => v * v;

        /// <summary>Base head reshaped by the sliders (port of <c>applyShape</c>).</summary>
        public static ToonHeadShape Apply(ToonHeadSettings s)
        {
            s ??= new ToonHeadSettings();
            float eY = BaseEyeY, fl = s.faceLength, sh = s.skullHeight;
            float Y(float y) => y < eY ? y * fl : eY * fl + (y - eY) * sh;
            var o = new ToonHeadShape { Face = new float[BaseFace.Length][] };
            for (int i = 0; i < BaseFace.Length; i++)
            {
                float y = BaseFace[i][0], w = BaseFace[i][1], f = BaseFace[i][2], b = BaseFace[i][3], k = BaseFace[i][4];
                float lo = 1f - Smooth((y - 0.30f) / 0.25f);
                float cheek = Mathf.Exp(-Sq((y - 0.22f) / 0.09f));
                float jaw = Mathf.Exp(-Sq((y - 0.10f) / 0.06f));
                float chin = 1f - Smooth(y / 0.12f);
                float W = w * s.headWidth * (1f + (s.cheekWidth - 1f) * lo) * (1f + (s.jawWidth - 1f) * jaw) + 0.03f * s.cheekFull * cheek;
                W *= 1f + (s.chinWidth - 1f) * chin;
                float F = f * (1f + (s.faceDepth - 1f) * (0.4f + 0.6f * lo)) + s.chinForward * chin;
                float Bk = b * (1f + (s.backOfHead - 1f) * Smooth((y - 0.2f) / 0.3f));
                float K = Mathf.Max(1.3f, k + s.chinShape * 0.9f * (1f - Smooth((y - 0.03f) / 0.25f)));
                o.Face[i] = new[] { Y(y), W, F, Bk, K };
            }
            o.CrY = Y(0.63f); o.CrR = 0.37f * sh; o.CrW = 0.379f * s.headWidth; o.CrF = 0.441f * (1f + (s.faceDepth - 1f) * 0.4f); o.CrB = 0.394f * s.backOfHead; o.CrP = 2.1f;
            o.JawA = 0.5f; o.JawY = Y(0.15f); o.ChinRound = 0.9f; o.NapeY = Y(0.31f);
            o.NeckTop = 0f; o.NeckNape = Y(0.29f); o.NeckBottom = -0.17f; o.NeckBottomBack = -0.03f;
            o.NeckW = 0.148f * s.neckWidth; o.NeckF = 0.155f * s.neckWidth; o.NeckB = 0.160f * s.neckWidth; o.NeckZ = -0.10f;
            o.CheekY = Y(0.20f); o.CheekH = 0.06f; o.CheekA = 0.24f; o.CheekSpread = 0.11f; o.CheekAmount = 0.004f;
            o.NoseY = Y(0.272f) + s.noseHeight; o.NoseH = 0.048f * s.noseSize; o.NoseW = 0.022f; o.NoseLen = 0.045f;
            o.EyeX = 0.150f + s.eyeSpacing; o.EyeY = Y(0.380f) + s.eyeHeight; o.EyeSize = s.eyeSize;
            o.BrowY0 = Y(0.522f) + s.eyeHeight + (s.eyeSize - 1f) * 0.06f;
            o.BrowY = o.BrowY0 + s.browHeight; o.BrowW = 0.215f; o.BrowH = 0.066f;
            o.MouthY = Y(0.165f) + s.mouthHeight; o.MouthW = 0.09f; o.MouthH = 0.034f;
            o.EarY = Y(0.39f) + s.earHeight; o.EarZ = -0.11f; o.EarSu = 1.31f * s.earSize; o.EarSv = 1.02f * s.earSize; o.EarFlare = 40f + s.earFlare;
            o.Top = Y(1f);
            o.EyeZ = BaseFrontZ(o, o.EyeX, o.EyeY);
            return o;
        }

        // ------------------------------------------------------------------ surface

        /// <summary>Half width, front depth, back depth and front squareness of the head at height y.</summary>
        public static void FaceAt(ToonHeadShape H, float y, out float w, out float f, out float b, out float k)
        {
            if (y > H.CrY)
            {
                float q = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(Mathf.Abs((y - H.CrY) / H.CrR), H.CrP)), 1f / H.CrP);
                w = H.CrW * q; f = H.CrF * q; b = H.CrB * q; k = 2f;
                return;
            }
            var T = H.Face;
            int n = T.Length;
            if (y <= T[n - 1][0]) { w = T[n - 1][1]; f = T[n - 1][2]; b = T[n - 1][3]; k = T[n - 1][4]; return; }
            int i = 0;
            while (i < n - 2 && y < T[i + 1][0]) i++;
            float[] p0 = T[Math.Max(0, i - 1)], p1 = T[i], p2 = T[i + 1], p3 = T[Math.Min(n - 1, i + 2)];
            float t = (p1[0] - y) / (p1[0] - p2[0]);
            float Cr(int c) { float a = p0[c], bb = p1[c], cc = p2[c], d = p3[c]; return 0.5f * ((2f * bb) + (-a + cc) * t + (2f * a - 5f * bb + 4f * cc - d) * t * t + (-a + 3f * bb - 3f * cc + d) * t * t * t); }
            w = Cr(1); f = Cr(2); b = Cr(3); k = Cr(4);
        }

        private static Vector2 Section(float theta, float w, float f, float b, float k)
        {
            float s = Mathf.Sin(theta), c = Mathf.Cos(theta);
            if (c >= 0f) { float e = 2f / k; return new Vector2(w * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), e), f * Mathf.Pow(c, e)); }
            return new Vector2(w * s, b * c);
        }

        public static float JawY(ToonHeadShape H, float a)
        {
            if (a <= H.JawA)
            {
                float t = a / H.JawA, r = H.ChinRound;
                return H.JawY * (Mathf.Sqrt(t * t + r * r) - r) / (Mathf.Sqrt(1f + r * r) - r);
            }
            return Lerp(H.JawY, H.NapeY, Smooth((a - H.JawA) / (1f - H.JawA)));
        }

        private static float NeckTopY(ToonHeadShape H, float a) => Lerp(H.NeckTop, H.NeckNape, Smooth(a));

        /// <summary>Height (head units) where the neck meets the jaw / nape, a = 0 at the front to 1 at the back.</summary>
        public static float NeckTopHeight(ToonHeadShape H, float a) => NeckTopY(H, a);
        private static float NeckBotY(ToonHeadShape H, float a) => Lerp(H.NeckBottom, H.NeckBottomBack, Smooth(a));

        private static Vector3 NeckPoint(ToonHeadShape H, float theta, float y)
        {
            float s = Mathf.Sin(theta), c = Mathf.Cos(theta);
            return new Vector3(H.NeckW * s, y, H.NeckZ + (c >= 0f ? H.NeckF * c : H.NeckB * c));
        }

        /// <summary>Point on the head; <paramref name="eyePlates"/> false = the head without the upright eye areas (what the hair is built on).</summary>
        public static Vector3 Surface(ToonHeadShape H, float theta, float y, bool eyePlates = true)
        {
            FaceAt(H, y, out float w, out float f, out float b, out float k);
            Vector2 xz = Section(theta, w, f, b, k);
            float x = xz.x, z = xz.y, a = Mathf.Abs(theta) / Mathf.PI;
            float bulge = H.CheekAmount * Mathf.Exp(-Sq((y - H.CheekY) / H.CheekH)) * Mathf.Exp(-Sq((a - H.CheekA) / H.CheekSpread));
            float len = Mathf.Sqrt(x * x + z * z);
            if (len < 1e-6f) len = 1f;
            x += x / len * bulge; z += z / len * bulge;
            // the nose is only a soft bump: the painted shadow does the rest
            if (z > 0f) z += H.NoseH * Mathf.Exp(-Sq(x / H.NoseW)) * Mathf.Exp(-Sq((y - H.NoseY) / H.NoseLen)) * (y < H.NoseY ? 0.7f : 1f);
            if (z > 0f && eyePlates) z = EyeSocket(H, x, y, z);
            return new Vector3(x, y, z);
        }

        // Upright eyes (side view). The eyes are painted by a front projection, so on the round face they used to lean forward
        // with the forehead and sit on the profile line (flat and crooked). Around each eye the forehead's lean (measured down
        // the eye centre) is taken out and the eye is set EyeSetBack back, under the brow and behind a slightly raised nose
        // bridge. Across the eye the face keeps its own smooth curve, so painted lines stay smooth from the side (a flat plate
        // here made a crease the outer lid line bent over), and the change fades out with a long smootherstep past the outer
        // corner. Only depth changes: the front view and the painted face stay the same.
        public const float EyeSetBack = 0.012f, NoseBridge = 0.010f;
        private const float EyeHalfWidth = 0.09f;   // half the painted eye's width at eye size 1 (the styles are 0.165-0.19 wide)

        private static float EyeSocket(ToonHeadShape H, float x, float y, float z)
        {
            float ax = Mathf.Abs(x), s = H.EyeSize, hw = EyeHalfWidth * s;
            // the middle of the face (nose bridge) never sinks, even with big eyes set close together
            float in0 = Mathf.Max(0.008f, H.EyeX - hw - 0.045f), in1 = Mathf.Max(in0 + 0.025f, H.EyeX - hw - 0.005f);
            float r = Smooth((ax - in0) / (in1 - in0)) * (1f - Smoother((ax - (H.EyeX + 0.8f * hw)) / 0.2f))
                    * Smooth((y - (H.EyeY - 0.155f * s)) / (0.06f * s)) * (1f - Smooth((y - (H.EyeY + 0.075f * s)) / (0.08f * s)))
                    * Smooth(z / 0.08f);
            if (r > 0f)
            {
                float lean = BaseFrontZ(H, H.EyeX, y) - H.EyeZ;
                // below the eye centre the cheek turns back: never pull it forward past where it was (soft cap, smooth at 0)
                if (lean < 0f) lean = -EyeSetBack * (float)Math.Tanh(-lean / EyeSetBack);
                z -= r * (lean + EyeSetBack);
            }
            float bridge = Smooth((y - (H.NoseY - 0.02f)) / 0.05f) * (1f - Smooth((y - (H.EyeY + 0.02f)) / 0.08f));
            return z + NoseBridge * Mathf.Exp(-Sq(x / 0.03f)) * bridge;
        }

        private static float Smoother(float t) { t = Clamp01(t); return t * t * t * (t * (6f * t - 15f) + 10f); }

        /// <summary>Front depth of the unshaped head (superellipse section, no cheek, nose or eye shaping) at (x, y).</summary>
        private static float BaseFrontZ(ToonHeadShape H, float x, float y)
        {
            FaceAt(H, y, out float w, out float f, out _, out float k);
            float e = 2f / Mathf.Max(1e-3f, k);
            float sn = Mathf.Pow(Clamp01(Mathf.Abs(x) / Mathf.Max(1e-4f, w)), 1f / e);
            return f * Mathf.Pow(Mathf.Sqrt(Mathf.Max(0f, 1f - sn * sn)), e);
        }

        /// <summary>Front depth of the face at (x, y) with the upright eye areas (where the painted eye sits).</summary>
        public static float FrontZ(ToonHeadShape H, float x, float y) => EyeSocket(H, x, y, BaseFrontZ(H, x, y));

        // ------------------------------------------------------------------ mesh

        /// <summary>Head + neck + ears in head units. Triangles face outward in Unity; normals point out.</summary>
        public sealed class MeshData
        {
            public readonly List<Vector3> Positions = new();
            public readonly List<Vector3> Normals = new();
            /// <summary>(sin θ around the head, height, face mask 0..1) for the painted face shadow.</summary>
            public readonly List<Vector3> FaceCoords = new();
            /// <summary>0 = head, 1 = bottom of the neck (for skin weights).</summary>
            public readonly List<float> NeckBlend = new();
            public readonly List<int> Triangles = new();
            public int HeadTriangleCount, EarTriangleCount;
            /// <summary>Triangles of the head down to the first neck row only (a short collar): bodies with their own neck use these.</summary>
            public int CollarTriangleCount;
            /// <summary>First vertex of the collar's bottom edge (the first neck row below the neck top, <see cref="Cols"/> vertices).</summary>
            public int CollarRimStart;
        }

        public static MeshData BuildMesh(ToonHeadShape H)
        {
            var m = new MeshData();
            // Same index order as the bench: with identical coordinates, three.js (right-handed, CCW front faces) and Unity
            // (left-handed, CW front faces) see each other mirrored, so these triangles already face outward in Unity
            // (Unity's face normal Cross(b − a, c − a) = three.js's).
            var tris = m.Triangles;
            float maskTop = H.BrowY + 0.11f;
            int Add(Vector3 p, float? theta, float neck = 0f)
            {
                m.Positions.Add(p);
                float a = Mathf.Abs(theta ?? 0f);
                float mask = theta.HasValue ? (1f - Smooth((a - 0.75f) / 0.45f)) * Smooth((p.y + 0.02f) / 0.08f) * (1f - Smooth((p.y - maskTop) / 0.10f)) : 0f;
                m.FaceCoords.Add(new Vector3(Mathf.Sin(theta ?? 0f), p.y, mask));
                m.NeckBlend.Add(neck);
                return m.Positions.Count - 1;
            }

            int N = Cols;
            var thetas = new float[N];
            for (int j = 0; j < N; j++)
            {
                float u = -1f + 2f * j / N;
                thetas[j] = Mathf.PI * Mathf.Sign(u) * Mathf.Pow(Mathf.Abs(u), FrontBias);
            }
            var rows = new List<int[]>();
            int crown = Add(new Vector3(0f, H.CrY + H.CrR, 0f), 0f);
            for (int i = 1; i <= CraniumRows; i++)
            {
                float y = H.CrY + H.CrR * Mathf.Cos(i / (float)CraniumRows * Mathf.PI / 2f);
                var row = new int[N];
                for (int j = 0; j < N; j++) row[j] = Add(Surface(H, thetas[j], y), thetas[j]);
                rows.Add(row);
            }
            for (int i = 1; i <= FaceRows; i++)
            {
                float s = i / (float)FaceRows;
                var row = new int[N];
                for (int j = 0; j < N; j++) row[j] = Add(Surface(H, thetas[j], Lerp(H.CrY, JawY(H, Mathf.Abs(thetas[j]) / Mathf.PI), s)), thetas[j]);
                rows.Add(row);
            }
            var jaw = rows[rows.Count - 1];
            var jawP = new Vector3[N];
            for (int j = 0; j < N; j++) jawP[j] = m.Positions[jaw[j]];
            foreach (float t in new[] { 0.25f, 0.5f, 0.75f })
            {
                var row = new int[N];
                for (int j = 0; j < N; j++)
                {
                    Vector3 n = NeckPoint(H, thetas[j], NeckTopY(H, Mathf.Abs(thetas[j]) / Mathf.PI)), J = jawP[j];
                    row[j] = Add(new Vector3(Lerp(J.x, n.x, t), Lerp(J.y, n.y, t) - 0.010f * Mathf.Sin(Mathf.PI * t), Lerp(J.z, n.z, t)), null);
                }
                rows.Add(row);
            }
            int neckRow0 = rows.Count;
            for (int i = 0; i <= NeckRows; i++)
            {
                float t = i / (float)NeckRows;
                var row = new int[N];
                if (i == 1) m.CollarRimStart = m.Positions.Count;
                for (int j = 0; j < N; j++)
                {
                    float a = Mathf.Abs(thetas[j]) / Mathf.PI;
                    row[j] = Add(NeckPoint(H, thetas[j], Lerp(NeckTopY(H, a), NeckBotY(H, a), t)), null, t);
                }
                rows.Add(row);
            }
            int bottom = Add(new Vector3(0f, (H.NeckBottom + H.NeckBottomBack) / 2f, H.NeckZ), null, 1f);
            for (int j = 0; j < N; j++) tris.AddRange(new[] { crown, rows[0][j], rows[0][(j + 1) % N] });
            for (int r = 0; r < rows.Count - 1; r++)
            {
                for (int j = 0; j < N; j++)
                {
                    int a = rows[r][j], b = rows[r][(j + 1) % N], c = rows[r + 1][(j + 1) % N], d = rows[r + 1][j];
                    tris.AddRange(new[] { a, d, c, a, c, b });
                }
                if (r == neckRow0) m.CollarTriangleCount = tris.Count / 3;
            }
            var last = rows[rows.Count - 1];
            for (int j = 0; j < N; j++) tris.AddRange(new[] { bottom, last[(j + 1) % N], last[j] });
            m.HeadTriangleCount = tris.Count / 3;

            BuildEars(H, m, tris);
            m.EarTriangleCount = tris.Count / 3 - m.HeadTriangleCount;

            // smooth normals (area-weighted, outward)
            var nrm = new Vector3[m.Positions.Count];
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = m.Positions[tris[i]], b = m.Positions[tris[i + 1]], c = m.Positions[tris[i + 2]];
                Vector3 fn = Vector3.Cross(b - a, c - a);
                nrm[tris[i]] += fn; nrm[tris[i + 1]] += fn; nrm[tris[i + 2]] += fn;
            }
            for (int i = 0; i < nrm.Length; i++)
            {
                Vector3 n = nrm[i];
                m.Normals.Add(n.sqrMagnitude > 1e-20f ? n.normalized : Vector3.up);
            }
            m.Normals[crown] = Vector3.up;
            return m;
        }

        private static readonly float[,] EarOutline =
        {
            { 0.085f, 0.000f }, { 0.104f, 0.030f }, { 0.100f, 0.064f }, { 0.076f, 0.091f }, { 0.036f, 0.103f }, { -0.008f, 0.096f }, { -0.048f, 0.074f },
            { -0.078f, 0.046f }, { -0.094f, 0.018f }, { -0.086f, -0.003f }, { -0.046f, -0.009f }, { 0.000f, -0.011f }, { 0.046f, -0.009f },
        };
        private static readonly float[,] EarRings = { { 1.0f, -0.015f }, { 1.0f, 0.035f }, { 0.96f, 0.048f }, { 0.80f, 0.044f }, { 0.62f, 0.032f }, { 0.40f, 0.020f } };

        // smooth ears: a lofted, rounded shell on each side
        private static void BuildEars(ToonHeadShape H, MeshData m, List<int> tris)
        {
            int L = EarOutline.GetLength(0);
            var dense = new List<Vector2>();
            for (int i = 0; i < L; i++)
            {
                int i0 = (i - 1 + L) % L, i2 = (i + 1) % L, i3 = (i + 2) % L;
                for (int k = 0; k < 3; k++)
                {
                    float t = k / 3f;
                    float Cr(int c) { float a = EarOutline[i0, c], b = EarOutline[i, c], cc = EarOutline[i2, c], d = EarOutline[i3, c]; return 0.5f * ((2f * b) + (-a + cc) * t + (2f * a - 5f * b + 4f * cc - d) * t * t + (-a + 3f * b - 3f * cc + d) * t * t * t); }
                    dense.Add(new Vector2(Cr(0), Cr(1)));
                }
            }
            float SU = H.EarSu, SV = H.EarSv, cU = 0.004f * SU, cV = 0.046f * SV, tau = -12f * Mathf.Deg2Rad, flare = Mathf.Tan(H.EarFlare * Mathf.Deg2Rad);
            Vector2 up = new(Mathf.Cos(tau), Mathf.Sin(tau)), back = new(Mathf.Sin(tau), -Mathf.Cos(tau));   // (y, z) components
            int R = EarRings.GetLength(0), M = dense.Count;
            for (int side = -1; side <= 1; side += 2)
            {
                int baseIdx = m.Positions.Count;
                Vector3 Place(float u, float v, float d)
                {
                    float y = H.EarY + u * up.x + v * back.x, z = H.EarZ + u * up.y + v * back.y;
                    FaceAt(H, y, out float w, out _, out float b, out _);
                    float hx = w * Mathf.Sqrt(Mathf.Max(0f, 1f - Sq(Mathf.Min(0f, z) / b)));
                    float lift = 0.3f + 0.7f * Smooth((v + 0.01f) / (0.07f * SV));
                    return new Vector3(side * (hx - 0.004f + d * lift * 1.4f + Mathf.Max(0f, v) * flare), y, z);
                }
                for (int r = 0; r < R; r++)
                {
                    float sc = EarRings[r, 0], d = EarRings[r, 1];
                    foreach (var p in dense) AddEar(m, Place(cU + (p.x * SU - cU) * sc, cV + (p.y * SV - cV) * sc, d));
                }
                AddEar(m, Place(cU, cV, 0.014f));
                int centre = baseIdx + R * M;
                for (int r = 0; r < R - 1; r++)
                    for (int k = 0; k < M; k++)
                    {
                        int n = (k + 1) % M, a = baseIdx + r * M + k, b = baseIdx + r * M + n, c = baseIdx + (r + 1) * M + n, d = baseIdx + (r + 1) * M + k;
                        if (side > 0) tris.AddRange(new[] { a, c, b, a, d, c }); else tris.AddRange(new[] { a, b, c, a, c, d });
                    }
                for (int k = 0; k < M; k++)
                {
                    int a = baseIdx + (R - 1) * M + k, b = baseIdx + (R - 1) * M + (k + 1) % M;
                    if (side > 0) tris.AddRange(new[] { a, centre, b }); else tris.AddRange(new[] { a, b, centre });
                }
            }
        }

        private static void AddEar(MeshData m, Vector3 p)
        {
            m.Positions.Add(p);
            m.FaceCoords.Add(new Vector3(0f, p.y, 0f));
            m.NeckBlend.Add(0f);
        }

        // ------------------------------------------------------------------ painted face shadow

        public const int ShadowMapSize = 256;
        /// <summary>Face shadow map covers face heights ShadowV0 … ShadowV0 + ShadowVSpan (head units).</summary>
        public const float ShadowV0 = -0.05f, ShadowVSpan = 0.8f;

        /// <summary>
        /// Value at each face point = how far the light must swing to the far side before that point goes dark
        /// (u: 0 = far edge of the face … 1 = near edge; the shader mirrors it to follow the light).
        /// </summary>
        public static byte[] ShadowMap(ToonHeadShape H)
        {
            const int S = ShadowMapSize;
            var data = new byte[S * S];
            for (int j = 0; j < S; j++)
                for (int i = 0; i < S; i++)
                {
                    float u = i / (float)(S - 1), v = j / (float)(S - 1) * ShadowVSpan + ShadowV0;
                    float t = u;
                    // the shadow edge wraps the cheek: it bows towards the lit side under the eye and back again at the jaw
                    t -= 0.10f * Mathf.Exp(-Sq((v - 0.30f) / 0.10f)) * Mathf.Sin(Mathf.PI * u);
                    // forehead and eye line stay lit a little longer
                    t += 0.06f * Mathf.Exp(-Sq((v - (H.EyeY + 0.06f)) / 0.08f)) * Mathf.Sin(Mathf.PI * u);
                    // nose shadow: a small wedge just on the far side of the nose that darkens first
                    float nu = (u - 0.47f) / (0.010f + 0.012f * Clamp01((v - (H.NoseY - 0.065f)) / 0.06f)), nv = (v - (H.NoseY - 0.025f)) / 0.035f;
                    if (u < 0.5f) t -= 0.22f * Mathf.Exp(-(nu * nu) - (nv * nv)) * (v < H.NoseY - 0.005f ? 1f : Mathf.Exp(-Sq((v - (H.NoseY - 0.005f)) / 0.03f)));
                    // under the chin and the jaw edge tip into shadow early
                    t -= 0.12f * (1f - Smooth((v + 0.01f) / 0.08f));
                    data[j * S + i] = (byte)Mathf.RoundToInt(Clamp01(t) * 255f);
                }
            return data;
        }
    }
}
