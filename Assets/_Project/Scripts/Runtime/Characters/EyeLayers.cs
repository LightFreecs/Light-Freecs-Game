using UnityEngine;

namespace LightMMORPG.Characters
{
    /// <summary>
    /// The hand-made anime eye, stored as three layers (Resources/Eyes/*.bytes, sources in Art/Eyes/Layers):
    /// base (white of the eye + black outline), iris tone map (0.5 = exact eye colour, lower darkens, higher lightens) and highlight.
    /// <see cref="EnsureLoaded"/> must run once on the main thread; after that sampling is thread-safe (FacePainter runs on workers).
    /// </summary>
    public static class EyeLayers
    {
        /// <summary>Physical size of the cropped eye image on the face (meters, before head scaling).</summary>
        public const float WidthM = 0.056f, HeightM = 0.0732f;

        private const int CropX = 180, CropY = 2, CropW = 780, CropH = 1020, Down = 2;

        public static int W { get; private set; }
        public static int H { get; private set; }
        public static bool Loaded { get; private set; }

        private static float[] baseP, toneP, hiP; // premultiplied: base rgba, tone (L*a, a), highlight a

        public static void EnsureLoaded()
        {
            if (Loaded) return;
            var b = Decode("Eyes/eye_base");
            var t = Decode("Eyes/eye_iris_tone");
            var h = Decode("Eyes/eye_highlight");
            if (b == null || t == null || h == null) { Debug.LogWarning("EyeLayers: Resources/Eyes/*.bytes missing, using procedural eyes."); return; }
            W = CropW / Down; H = CropH / Down;
            baseP = new float[W * H * 4]; toneP = new float[W * H * 2]; hiP = new float[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float r = 0, g = 0, bl = 0, a = 0, lt = 0, ta = 0, ha = 0;
                    for (int sy = 0; sy < Down; sy++)
                        for (int sx = 0; sx < Down; sx++)
                        {
                            int i = (CropX + x * Down + sx) + (CropY + y * Down + sy) * 1024;
                            var cb = b[i]; float ab = cb.a / 255f; r += cb.r / 255f * ab; g += cb.g / 255f * ab; bl += cb.b / 255f * ab; a += ab;
                            var ct = t[i]; float at = ct.a / 255f; lt += ct.r / 255f * at; ta += at;
                            ha += h[i].a / 255f;
                        }
                    float n = 1f / (Down * Down);
                    int k = (x + y * W) * 4;
                    baseP[k] = r * n; baseP[k + 1] = g * n; baseP[k + 2] = bl * n; baseP[k + 3] = a * n;
                    toneP[(x + y * W) * 2] = lt * n; toneP[(x + y * W) * 2 + 1] = ta * n;
                    hiP[x + y * W] = ha * n;
                }
            Loaded = true;
        }

        private static Color32[] Decode(string path)
        {
            var asset = Resources.Load<TextAsset>(path);
            if (asset == null) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            if (!tex.LoadImage(asset.bytes, false) || tex.width != 1024 || tex.height != 1024) { Object.DestroyImmediate(tex); return null; }
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return px;
        }

        /// <summary>Samples the composed eye at (u, v) in [0,1] (v up), tinted with the eye colour. Premultiplied RGB + alpha out.</summary>
        public static void Sample(float u, float v, Color eye, out float r, out float g, out float b, out float a)
        {
            r = g = b = a = 0f;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return;
            float fx = u * W - 0.5f, fy = v * H - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            float br = 0, bg = 0, bb = 0, ba = 0, lp = 0, ia = 0, ha = 0;
            for (int j = 0; j < 4; j++)
            {
                int xx = x0 + (j & 1), yy = y0 + (j >> 1);
                if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
                float w = ((j & 1) == 0 ? 1f - tx : tx) * ((j >> 1) == 0 ? 1f - ty : ty);
                int i = xx + yy * W, k = i * 4;
                br += baseP[k] * w; bg += baseP[k + 1] * w; bb += baseP[k + 2] * w; ba += baseP[k + 3] * w;
                lp += toneP[i * 2] * w; ia += toneP[i * 2 + 1] * w;
                ha += hiP[i] * w;
            }
            // base
            r = br; g = bg; b = bb; a = ba;
            // tinted iris over it
            if (ia > 0.0001f)
            {
                float L = Mathf.Clamp01(lp / ia), ir, ig, ib;
                if (L < 0.5f) { float s = L * 2f; ir = eye.r * s; ig = eye.g * s; ib = eye.b * s; }
                else { float s = (L - 0.5f) * 2f; ir = Mathf.Lerp(eye.r, 1f, s); ig = Mathf.Lerp(eye.g, 1f, s); ib = Mathf.Lerp(eye.b, 1f, s); }
                r = r * (1f - ia) + ir * ia; g = g * (1f - ia) + ig * ia; b = b * (1f - ia) + ib * ia; a = a * (1f - ia) + ia;
            }
            // highlight
            r = r * (1f - ha) + ha; g = g * (1f - ha) + ha; b = b * (1f - ha) + ha; a = a * (1f - ha) + ha;
        }
    }
}
