using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightMMORPG.Characters
{
    /// <summary>
    /// Paints the toon head's face features (eyes, brows, mouth) into one RGBA texture with transparent skin, laid on the
    /// head by a front planar projection: texture covers head x ∈ [−0.5, 0.5], y ∈ [−0.1, 0.9] (head units, see
    /// <see cref="ToonHead"/>). Same layout and numbers as the Toon Head bench, where each feature was a flat patch over the face.
    /// Thread-safe except <see cref="ToonEyeLayers.EnsureLoaded"/>.
    /// </summary>
    public static class ToonFacePainter
    {
        public const int Size = 1024;
        public const float X0 = -0.5f, Y0 = -0.1f, Span = 1f;

        /// <summary>Face texture UV of a head-unit position.</summary>
        public static Vector2 Uv(Vector3 head) => new((head.x - X0) / Span, (head.y - Y0) / Span);

        // ------------------------------------------------------------------ the three eyes traced from the user's sketches
        // Eye space: u 0 = inner corner → 1 = outer corner, v up, both in eye widths, v = 0 at the iris centre.
        // Strokes are (u, v, thickness). Ported verbatim from the Low-Poly Head bench.

        public sealed class SketchEye
        {
            public float Width, Shift;
            public Vector3[] Upper, Lower;
            public Vector3[][] Extra;
            public Vector2[] WhiteTop, WhiteBottom, LidShade;
            public Vector2 WhiteRange = new(0f, 0.94f);
            public float IrisU, IrisV, IrisRx, IrisRy, IrisRing, DarkTop;
            public Vector3[] Shine;
            public Color White = Hex(0xfbf8f4), LidShadeColor = Hex(0xb9bcc8), Lid = Hex(0x1d1616);
        }

        private static Vector3[] S3(params float[] f) { var r = new Vector3[f.Length / 3]; for (int i = 0; i < r.Length; i++) r[i] = new Vector3(f[i * 3], f[i * 3 + 1], f[i * 3 + 2]); return r; }
        private static Vector2[] S2(params float[] f) { var r = new Vector2[f.Length / 2]; for (int i = 0; i < r.Length; i++) r[i] = new Vector2(f[i * 2], f[i * 2 + 1]); return r; }
        private static Color Hex(int rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        /// <summary>Sketch 1: thin-to-heavy upper lid hooking down at the outer corner, a separate lower lid, tall oval iris.</summary>
        public static readonly SketchEye Drawn = new()
        {
            Width = 0.17f,
            Upper = S3(0.000f, -0.002f, 0.010f, 0.076f, 0.068f, 0.016f, 0.156f, 0.158f, 0.022f, 0.256f, 0.268f, 0.028f, 0.356f, 0.344f, 0.034f,
                       0.496f, 0.388f, 0.040f, 0.636f, 0.348f, 0.050f, 0.756f, 0.248f, 0.058f, 0.856f, 0.128f, 0.064f, 0.956f, 0.010f, 0.066f,
                       0.990f, -0.020f, 0.062f, 0.966f, -0.092f, 0.040f, 0.926f, -0.192f, 0.006f),
            Lower = S3(0.056f, -0.356f, 0.006f, 0.136f, -0.422f, 0.016f, 0.256f, -0.456f, 0.018f, 0.396f, -0.492f, 0.018f, 0.536f, -0.528f, 0.018f,
                       0.636f, -0.452f, 0.017f, 0.736f, -0.382f, 0.014f, 0.806f, -0.336f, 0.005f),
            IrisU = 0.466f, IrisV = 0f, IrisRx = 0.180f, IrisRy = 0.232f, IrisRing = 0.026f,
            Shine = S3(0.40f, 0.10f, 0.055f, 0.53f, -0.12f, 0.025f),
        };

        /// <summary>Sketch 2: one big arch, heaviest across the top and inner side, no lower lid, tall iris with a dark top half.</summary>
        public static readonly SketchEye Tall = new()
        {
            Width = 0.165f, Shift = -0.10f,
            Upper = S3(0.000f, 0.191f, 0.035f, 0.027f, 0.300f, 0.050f, 0.100f, 0.427f, 0.062f, 0.200f, 0.555f, 0.066f, 0.309f, 0.627f, 0.066f,
                       0.436f, 0.660f, 0.062f, 0.564f, 0.642f, 0.056f, 0.691f, 0.600f, 0.050f, 0.800f, 0.527f, 0.050f, 0.891f, 0.409f, 0.046f,
                       0.955f, 0.264f, 0.046f, 0.982f, 0.100f, 0.041f, 0.982f, -0.064f, 0.035f, 0.955f, -0.191f, 0.025f, 0.909f, -0.300f, 0.010f),
            WhiteTop = S2(0.005f, 0.20f, 0.10f, 0.41f, 0.20f, 0.535f, 0.309f, 0.605f, 0.436f, 0.640f, 0.564f, 0.622f, 0.691f, 0.582f,
                          0.80f, 0.51f, 0.891f, 0.392f, 0.955f, 0.248f, 0.968f, 0.10f, 0.968f, -0.06f),
            WhiteBottom = S2(0.005f, 0.17f, 0.03f, -0.15f, 0.10f, -0.38f, 0.22f, -0.47f, 0.367f, -0.485f, 0.60f, -0.47f, 0.80f, -0.42f, 0.92f, -0.33f, 0.968f, -0.16f),
            WhiteRange = new Vector2(0.005f, 0.968f),
            LidShade = S2(0.0f, 0.03f, 0.30f, 0.08f, 0.60f, 0.14f, 0.85f, 0.22f, 0.97f, 0.26f),
            IrisU = 0.367f, IrisV = 0f, IrisRx = 0.269f, IrisRy = 0.464f, IrisRing = 0.030f, DarkTop = 0.48f,
            Shine = S3(0.27f, 0.24f, 0.085f, 0.47f, -0.30f, 0.030f),
            LidShadeColor = Hex(0xb9bcc8),
        };

        /// <summary>Sketch 3: long flat upper lid ending in a heavy wing with a dropping stroke, round iris, curved lower lid.</summary>
        public static readonly SketchEye Wide = new()
        {
            Width = 0.19f, Shift = 0.04f,
            Upper = S3(0.000f, 0.030f, 0.008f, 0.068f, 0.118f, 0.013f, 0.160f, 0.217f, 0.018f, 0.291f, 0.289f, 0.021f, 0.423f, 0.291f, 0.021f,
                       0.554f, 0.276f, 0.024f, 0.672f, 0.223f, 0.040f, 0.764f, 0.164f, 0.056f, 0.843f, 0.085f, 0.068f, 0.915f, 0.050f, 0.060f,
                       1.000f, 0.026f, 0.010f),
            Extra = new[] { S3(0.930f, 0.040f, 0.064f, 0.880f, 0.000f, 0.058f, 0.840f, -0.070f, 0.036f, 0.816f, -0.120f, 0.020f, 0.790f, -0.164f, 0.005f) },
            Lower = S3(0.127f, -0.295f, 0.008f, 0.200f, -0.335f, 0.016f, 0.252f, -0.360f, 0.016f, 0.383f, -0.387f, 0.014f, 0.514f, -0.361f, 0.012f,
                       0.619f, -0.325f, 0.005f),
            WhiteTop = S2(0.0f, 0.030f, 0.068f, 0.110f, 0.160f, 0.208f, 0.291f, 0.279f, 0.423f, 0.281f, 0.554f, 0.264f, 0.672f, 0.207f,
                          0.764f, 0.145f, 0.843f, 0.063f, 0.900f, 0.030f),
            WhiteBottom = S2(0.0f, 0.030f, 0.06f, -0.150f, 0.127f, -0.290f, 0.252f, -0.360f, 0.383f, -0.382f, 0.514f, -0.356f, 0.619f, -0.320f,
                             0.720f, -0.240f, 0.800f, -0.160f, 0.900f, 0.020f),
            WhiteRange = new Vector2(0.0f, 0.90f),
            LidShade = S2(0.0f, 0.02f, 0.5f, 0.06f, 0.85f, 0.08f),
            IrisU = 0.383f, IrisV = 0f, IrisRx = 0.223f, IrisRy = 0.252f, IrisRing = 0.020f, DarkTop = 0.45f,
            Shine = S3(0.252f, 0.125f, 0.075f),
            LidShadeColor = Hex(0xc4c2c8),
        };

        public static SketchEye Sketch(ToonEyeStyle s) => s switch { ToonEyeStyle.Tall => Tall, ToonEyeStyle.Wide => Wide, _ => Drawn };

        private static float SketchTop(SketchEye e)
        {
            float m = float.MinValue;
            foreach (var p in e.Upper) m = Mathf.Max(m, p.y + p.z / 2f);
            return e.Width * (m + e.Shift);
        }

        // hand-made eye from the Eye Bench (390 × 512 layers): patch size at eye size 1, dy puts the white's centre on the eye line
        private const float LayerW = 0.205f, LayerDy = -0.012f;

        // ------------------------------------------------------------------ painting

        /// <summary>Paints the face features. Pixels are bottom row first (Texture2D.SetPixels order), straight alpha.</summary>
        public static Color[] Paint(ToonHeadShape H, ToonEyeStyle style, Color eyeColor, Color hairColor, Color skin)
        {
            var cv = new Canvas(Size, new Color(skin.r, skin.g, skin.b, 0f));
            Color iris = eyeColor; iris.a = 1f;
            var irisTop = new Color(iris.r * 0.32f, iris.g * 0.32f, iris.b * 0.32f, 1f);
            var irisGlow = Color.Lerp(iris, Color.white, 0.55f);
            float sz = H.EyeSize;
            if (style == ToonEyeStyle.HandMade && !ToonEyeLayers.Ready) style = ToonEyeStyle.Drawn;   // layers load on the main thread

            float browLift = 0f;
            for (int side = -1; side <= 1; side += 2)
            {
                if (style == ToonEyeStyle.HandMade) PaintLayeredEye(cv, H, side, iris, sz);
                else
                {
                    var es = Sketch(style);
                    browLift = Mathf.Max(0f, SketchTop(es) - SketchTop(Drawn)) * sz;
                    PaintSketchEye(cv, H, es, side, sz, iris, irisTop, irisGlow);
                }
            }
            var ink = new Color(0.09f, 0.05f, 0.06f, 1f);
            var brow = Color.Lerp(hairColor, Hex(0x3a2218), 0.5f); brow = Color.Lerp(brow, ink, 0.3f); brow.a = 1f;
            for (int side = -1; side <= 1; side += 2) PaintBrow(cv, H, side, browLift, brow);
            PaintMouth(cv, H);
            return cv.Px;
        }

        private static void PaintSketchEye(Canvas cv, ToonHeadShape H, SketchEye ES, int side, float sz, Color iris, Color irisTop, Color irisGlow)
        {
            float W = ES.Width * sz;
            Vector2 E(float u, float v) => new(side * (H.EyeX + (u - 0.5f) * W), H.EyeY + (v + ES.Shift) * W);

            Func<float, float> whiteTop, whiteBot;
            Vector2 wr = ES.WhiteRange;
            if (ES.WhiteTop != null) { whiteTop = Piecewise(ES.WhiteTop); whiteBot = Piecewise(ES.WhiteBottom); }
            else
            {
                var upperLine = Piecewise(Flat(ES.Upper));
                var wbPts = new List<Vector2> { new(ES.Upper[0].x, ES.Upper[0].y) };
                foreach (var p in ES.Lower) wbPts.Add(new Vector2(p.x, p.y));
                wbPts.Add(new Vector2(0.94f, -0.17f));
                var wb = Piecewise(wbPts.ToArray());
                whiteTop = u => upperLine(u) - 0.004f;
                whiteBot = u => wb(u) + 0.008f;
            }
            const int steps = 60;
            List<Vector2> Band(Func<float, float> bot, Func<float, float> top)
            {
                var pts = new List<Vector2>(steps * 2 + 2);
                for (int i = 0; i <= steps; i++) { float u = Mathf.Lerp(wr.x, wr.y, i / (float)steps); pts.Add(E(u, bot(u))); }
                for (int i = steps; i >= 0; i--) { float u = Mathf.Lerp(wr.x, wr.y, i / (float)steps); pts.Add(E(u, Mathf.Max(bot(u), top(u)))); }
                return pts;
            }

            var white = cv.NewMask();
            white.FillPolygon(Band(whiteBot, whiteTop));
            cv.Composite(white, ES.White);
            if (ES.LidShade != null)
            {
                var shadeF = Piecewise(ES.LidShade);
                var shade = cv.NewMask();
                shade.FillPolygon(Band(u => Mathf.Max(whiteBot(u), whiteTop(u) - shadeF(u)), whiteTop));
                shade.Multiply(white);
                cv.Composite(shade, ES.LidShadeColor);
            }
            // iris: the eye colour, a darker top part where the shape has one (else a dark-to-glow gradient), a dark ring, shines
            Vector2 ic = E(ES.IrisU, ES.IrisV);
            float rx = ES.IrisRx * W, ry = ES.IrisRy * W;
            var irisM = cv.NewMask();
            irisM.FillEllipse(ic, rx, ry);
            irisM.Multiply(white);
            if (ES.DarkTop > 0f)
            {
                cv.Composite(irisM, iris);
                float cut = E(0f, ES.IrisV + ES.IrisRy * (1f - 2f * ES.DarkTop)).y;
                var dark = irisM.Copy();
                dark.KeepAbove(cut);
                cv.Composite(dark, irisTop);
            }
            else
            {
                float yTop = ic.y + ry, yBot = ic.y - ry;
                cv.Composite(irisM, y =>
                {
                    float t = Mathf.Clamp01((yTop - y) / (yTop - yBot));   // 0 at the top
                    return t < 0.5f ? Color.Lerp(irisTop, iris, t / 0.5f) : Color.Lerp(iris, irisGlow, Mathf.Clamp01((t - 0.5f) / 0.4f));
                });
            }
            var ring = cv.NewMask();
            ring.StrokeEllipse(ic, rx, ry, ES.IrisRing * W);
            ring.Multiply(white);
            cv.Composite(ring, irisTop);
            var shine = cv.NewMask();
            foreach (var s in ES.Shine) shine.FillEllipse(E(s.x, s.y), s.z * W, s.z * W);
            shine.Multiply(white);
            cv.Composite(shine, Color.white);
            // lid lines: strokes with a thickness per point
            var lids = cv.NewMask();
            void Stroke(Vector3[] pts) => lids.Stroke(Densify(pts, 6), p => E(p.x, p.y), p => p.z * W);
            Stroke(ES.Upper);
            if (ES.Extra != null) foreach (var e in ES.Extra) Stroke(e);
            if (ES.Lower != null) Stroke(ES.Lower);
            cv.Composite(lids, ES.Lid);
        }

        private static void PaintLayeredEye(Canvas cv, ToonHeadShape H, int side, Color iris, float sz)
        {
            var L = ToonEyeLayers.Composed(iris);
            float w = LayerW * sz, h = w * L.Height / L.Width;
            float cx = H.EyeX, cy = H.EyeY + LayerDy * sz;
            cv.Image(L.Pixels, L.Width, L.Height, (x, y) => new Vector2((x * side - cx) / w + 0.5f, (y - cy) / h + 0.5f),
                new Vector2(side * cx - w / 2f, cy - h / 2f), new Vector2(side * cx + w / 2f, cy + h / 2f));
        }

        // brow from the bench's 512 × 128 canvas path (canvas y down), mirrored for the other side
        private static void PaintBrow(Canvas cv, ToonHeadShape H, int side, float lift, Color color)
        {
            float cx = H.EyeX + 0.01f, cy = H.BrowY + lift, w = H.BrowW, h = H.BrowH;
            Vector2 C(float px, float py) => new(side * (cx + (px / 512f - 0.5f) * w), cy + ((1f - py / 128f) - 0.5f) * h);
            var pts = new List<Vector2> { C(22, 118) };
            Bezier(pts, C(22, 118), C(130, 44), C(330, 18), C(498, 50));
            Bezier(pts, C(498, 50), C(330, 42), C(150, 74), C(28, 128));
            var m = cv.NewMask();
            m.FillPolygon(pts);
            cv.Composite(m, color);
        }

        // mouth: a short quadratic stroke from the bench's 256 × 128 canvas
        private static void PaintMouth(Canvas cv, ToonHeadShape H)
        {
            Vector2 C(float px, float py) => new((px / 256f - 0.5f) * H.MouthW, H.MouthY + ((1f - py / 128f) - 0.5f) * H.MouthH);
            Vector2 a = C(70, 70), c = C(128, 84), b = C(186, 66);
            var pts = new List<Vector3>();
            for (int i = 0; i <= 16; i++)
            {
                float t = i / 16f;
                Vector2 p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * c + t * t * b;
                pts.Add(new Vector3(p.x, p.y, 0.0028f));
            }
            var m = cv.NewMask();
            m.Stroke(pts, p => new Vector2(p.x, p.y), p => p.z);
            cv.Composite(m, Hex(0x8a3a30));
        }

        // ------------------------------------------------------------------ curve helpers

        private static Vector2[] Flat(Vector3[] p) { var r = new Vector2[p.Length]; for (int i = 0; i < p.Length; i++) r[i] = new Vector2(p[i].x, p[i].y); return r; }

        private static Func<float, float> Piecewise(Vector2[] pts) => u =>
        {
            if (u <= pts[0].x) return pts[0].y;
            for (int i = 1; i < pts.Length; i++)
                if (u <= pts[i].x) return Mathf.Lerp(pts[i - 1].y, pts[i].y, (u - pts[i - 1].x) / (pts[i].x - pts[i - 1].x));
            return pts[pts.Length - 1].y;
        };

        private static List<Vector3> Densify(Vector3[] pts, int n)
        {
            var o = new List<Vector3>();
            int N = pts.Length;
            Vector3 At(int k) => pts[Mathf.Clamp(k, 0, N - 1)];
            for (int i = 0; i < N - 1; i++)
                for (int k = 0; k < n; k++)
                {
                    float t = k / (float)n;
                    Vector3 p0 = At(i - 1), p1 = At(i), p2 = At(i + 1), p3 = At(i + 2);
                    o.Add(0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (t * t) + (-p0 + 3f * p1 - 3f * p2 + p3) * (t * t * t)));
                }
            o.Add(pts[N - 1]);
            return o;
        }

        private static void Bezier(List<Vector2> o, Vector2 a, Vector2 b, Vector2 c, Vector2 d, int n = 24)
        {
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n, u = 1f - t;
                o.Add(u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d);
            }
        }

        // ------------------------------------------------------------------ tiny anti-aliased rasterizer (head units in, pixels out)

        private sealed class Canvas
        {
            public readonly Color[] Px;
            public readonly int N;
            private readonly float _ppu;   // pixels per head unit

            public Canvas(int n, Color fill)
            {
                N = n; _ppu = n / Span;
                Px = new Color[n * n];
                for (int i = 0; i < Px.Length; i++) Px[i] = fill;
            }

            public float ToPxX(float x) => (x - X0) * _ppu;
            public float ToPxY(float y) => (y - Y0) * _ppu;
            public float ToHeadX(float px) => px / _ppu + X0;
            public float ToHeadY(float py) => py / _ppu + Y0;
            public float PixelSize => 1f / _ppu;

            public Mask NewMask() => new(this);

            private void Over(int i, Color c, float a)
            {
                if (a <= 0f) return;
                Color d = Px[i];
                float oa = a + d.a * (1f - a);
                if (oa <= 1e-6f) return;
                float k = d.a * (1f - a);
                Px[i] = new Color((c.r * a + d.r * k) / oa, (c.g * a + d.g * k) / oa, (c.b * a + d.b * k) / oa, oa);
            }

            public void Composite(Mask m, Color c)
            {
                for (int i = 0; i < m.Cov.Length; i++)
                {
                    float a = m.Cov[i] * c.a;
                    if (a > 0f) Over(m.Index(i), c, a);
                }
            }

            public void Composite(Mask m, Func<float, Color> colorAtY)
            {
                for (int i = 0; i < m.Cov.Length; i++)
                {
                    float a = m.Cov[i];
                    if (a <= 0f) continue;
                    int idx = m.Index(i);
                    Over(idx, colorAtY(ToHeadY(idx / N + 0.5f)), a);
                }
            }

            /// <summary>Draws an RGBA image (bottom row first) over the head-space box lo..hi, uv(x, y) gives the image coordinate (0..1).</summary>
            public void Image(Color32[] img, int w, int h, Func<float, float, Vector2> uv, Vector2 lo, Vector2 hi)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt(ToPxX(Mathf.Min(lo.x, hi.x)))), x1 = Mathf.Min(N - 1, Mathf.CeilToInt(ToPxX(Mathf.Max(lo.x, hi.x))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(ToPxY(lo.y))), y1 = Mathf.Min(N - 1, Mathf.CeilToInt(ToPxY(hi.y)));
                for (int py = y0; py <= y1; py++)
                    for (int px = x0; px <= x1; px++)
                    {
                        Vector2 t = uv(ToHeadX(px + 0.5f), ToHeadY(py + 0.5f));
                        if (t.x < 0f || t.y < 0f || t.x > 1f || t.y > 1f) continue;
                        Color c = Bilinear(img, w, h, t.x * (w - 1), t.y * (h - 1));
                        Over(py * N + px, new Color(c.r, c.g, c.b, 1f), c.a);
                    }
            }

            private static Color Bilinear(Color32[] img, int w, int h, float fx, float fy)
            {
                int x0 = Mathf.Clamp((int)fx, 0, w - 1), y0 = Mathf.Clamp((int)fy, 0, h - 1);
                int x1 = Mathf.Min(x0 + 1, w - 1), y1 = Mathf.Min(y0 + 1, h - 1);
                float tx = fx - x0, ty = fy - y0;
                // premultiplied so transparent texels don't bleed their colour
                Vector4 P(Color32 c) { float a = c.a / 255f; return new Vector4(c.r / 255f * a, c.g / 255f * a, c.b / 255f * a, a); }
                Vector4 v = Vector4.Lerp(Vector4.Lerp(P(img[y0 * w + x0]), P(img[y0 * w + x1]), tx), Vector4.Lerp(P(img[y1 * w + x0]), P(img[y1 * w + x1]), tx), ty);
                return v.w > 1e-5f ? new Color(v.x / v.w, v.y / v.w, v.z / v.w, v.w) : new Color(0, 0, 0, 0);
            }
        }

        /// <summary>Coverage (0..1) over the whole canvas, stored sparsely as rows touched.</summary>
        private sealed class Mask
        {
            private readonly Canvas _c;
            public float[] Cov;
            private int _x0, _y0, _w, _h;

            public Mask(Canvas c) { _c = c; Cov = Array.Empty<float>(); }

            public int Index(int i) => (_y0 + i / _w) * _c.N + _x0 + i % _w;

            private void Ensure(int x0, int y0, int x1, int y1)
            {
                x0 = Mathf.Clamp(x0, 0, _c.N - 1); y0 = Mathf.Clamp(y0, 0, _c.N - 1); x1 = Mathf.Clamp(x1, 0, _c.N - 1); y1 = Mathf.Clamp(y1, 0, _c.N - 1);
                if (Cov.Length > 0) { x0 = Mathf.Min(x0, _x0); y0 = Mathf.Min(y0, _y0); x1 = Mathf.Max(x1, _x0 + _w - 1); y1 = Mathf.Max(y1, _y0 + _h - 1); }
                int w = x1 - x0 + 1, h = y1 - y0 + 1;
                if (Cov.Length > 0 && x0 == _x0 && y0 == _y0 && w == _w && h == _h) return;
                var n = new float[w * h];
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                        n[(y + _y0 - y0) * w + (x + _x0 - x0)] = Cov[y * _w + x];
                Cov = n; _x0 = x0; _y0 = y0; _w = w; _h = h;
            }

            private void Max(int px, int py, float a)
            {
                if (a <= 0f || px < _x0 || py < _y0 || px >= _x0 + _w || py >= _y0 + _h) return;
                int i = (py - _y0) * _w + (px - _x0);
                if (a > Cov[i]) Cov[i] = Mathf.Min(1f, a);
            }

            private float Get(int px, int py) => px < _x0 || py < _y0 || px >= _x0 + _w || py >= _y0 + _h || Cov.Length == 0 ? 0f : Cov[(py - _y0) * _w + (px - _x0)];

            public Mask Copy() { var m = new Mask(_c) { Cov = (float[])Cov.Clone(), _x0 = _x0, _y0 = _y0, _w = _w, _h = _h }; return m; }

            public void Multiply(Mask other)
            {
                for (int i = 0; i < Cov.Length; i++)
                    if (Cov[i] > 0f) Cov[i] *= other.Get(_x0 + i % _w, _y0 + i / _w);
            }

            /// <summary>Keeps only the coverage above head height y (soft over one pixel).</summary>
            public void KeepAbove(float y)
            {
                float py = _c.ToPxY(y);
                for (int i = 0; i < Cov.Length; i++)
                {
                    float cy = _y0 + i / _w + 0.5f;
                    Cov[i] *= Mathf.Clamp01(cy - py + 0.5f);
                }
            }

            /// <summary>Even-odd polygon fill (head-space points), 4 sub-scanlines per pixel, exact horizontal coverage.</summary>
            public void FillPolygon(IList<Vector2> head)
            {
                int n = head.Count;
                if (n < 3) return;
                var p = new Vector2[n];
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    p[i] = new Vector2(_c.ToPxX(head[i].x), _c.ToPxY(head[i].y));
                    minX = Mathf.Min(minX, p[i].x); maxX = Mathf.Max(maxX, p[i].x); minY = Mathf.Min(minY, p[i].y); maxY = Mathf.Max(maxY, p[i].y);
                }
                int x0 = Mathf.FloorToInt(minX), x1 = Mathf.CeilToInt(maxX), y0 = Mathf.FloorToInt(minY), y1 = Mathf.CeilToInt(maxY);
                Ensure(x0, y0, x1, y1);
                const int Sub = 4;
                var row = new float[x1 - x0 + 2];
                var xs = new List<float>();
                for (int py = Mathf.Max(0, y0); py <= Mathf.Min(_c.N - 1, y1); py++)
                {
                    Array.Clear(row, 0, row.Length);
                    for (int s = 0; s < Sub; s++)
                    {
                        float sy = py + (s + 0.5f) / Sub;
                        xs.Clear();
                        for (int i = 0, j = n - 1; i < n; j = i++)
                        {
                            Vector2 a = p[i], b = p[j];
                            if ((a.y > sy) != (b.y > sy)) xs.Add(a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x));
                        }
                        xs.Sort();
                        for (int k = 0; k + 1 < xs.Count; k += 2) AddSpan(row, x0, xs[k], xs[k + 1], 1f / Sub);
                    }
                    for (int i = 0; i < row.Length; i++) if (row[i] > 0f) Max(x0 + i, py, row[i]);
                }
            }

            private static void AddSpan(float[] row, int x0, float a, float b, float w)
            {
                a -= x0; b -= x0;
                int ia = Mathf.FloorToInt(a), ib = Mathf.FloorToInt(b);
                if (ia == ib) { if (ia >= 0 && ia < row.Length) row[ia] += (b - a) * w; return; }
                if (ia >= 0 && ia < row.Length) row[ia] += (ia + 1 - a) * w;
                for (int i = Mathf.Max(0, ia + 1); i < Mathf.Min(row.Length, ib); i++) row[i] += w;
                if (ib >= 0 && ib < row.Length) row[ib] += (b - ib) * w;
            }

            public void FillEllipse(Vector2 c, float rx, float ry)
            {
                float cx = _c.ToPxX(c.x), cy = _c.ToPxY(c.y), prx = rx / _c.PixelSize, pry = ry / _c.PixelSize;
                int x0 = Mathf.FloorToInt(cx - prx - 1), x1 = Mathf.CeilToInt(cx + prx + 1), y0 = Mathf.FloorToInt(cy - pry - 1), y1 = Mathf.CeilToInt(cy + pry + 1);
                Ensure(x0, y0, x1, y1);
                float rm = Mathf.Min(prx, pry);
                for (int py = Mathf.Max(0, y0); py <= Mathf.Min(_c.N - 1, y1); py++)
                    for (int px = Mathf.Max(0, x0); px <= Mathf.Min(_c.N - 1, x1); px++)
                    {
                        float dx = (px + 0.5f - cx) / prx, dy = (py + 0.5f - cy) / pry;
                        Max(px, py, Mathf.Clamp01((1f - Mathf.Sqrt(dx * dx + dy * dy)) * rm + 0.5f));
                    }
            }

            public void StrokeEllipse(Vector2 c, float rx, float ry, float width)
            {
                float cx = _c.ToPxX(c.x), cy = _c.ToPxY(c.y), prx = rx / _c.PixelSize, pry = ry / _c.PixelSize, hw = width / _c.PixelSize / 2f;
                int x0 = Mathf.FloorToInt(cx - prx - hw - 1), x1 = Mathf.CeilToInt(cx + prx + hw + 1), y0 = Mathf.FloorToInt(cy - pry - hw - 1), y1 = Mathf.CeilToInt(cy + pry + hw + 1);
                Ensure(x0, y0, x1, y1);
                float rm = Mathf.Min(prx, pry);
                for (int py = Mathf.Max(0, y0); py <= Mathf.Min(_c.N - 1, y1); py++)
                    for (int px = Mathf.Max(0, x0); px <= Mathf.Min(_c.N - 1, x1); px++)
                    {
                        float dx = (px + 0.5f - cx) / prx, dy = (py + 0.5f - cy) / pry;
                        float d = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - 1f) * rm;
                        Max(px, py, Mathf.Clamp01(hw - d + 0.5f));
                    }
            }

            /// <summary>Variable-width polyline: a quad per segment plus a round joint at each point.</summary>
            public void Stroke(List<Vector3> pts, Func<Vector3, Vector2> pos, Func<Vector3, float> width)
            {
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    Vector2 a = pos(pts[i]), b = pos(pts[i + 1]);
                    float ta = Mathf.Max(0f, width(pts[i])) / 2f, tb = Mathf.Max(0f, width(pts[i + 1])) / 2f;
                    Vector2 t = b - a;
                    float l = t.magnitude;
                    if (l > 1e-7f)
                    {
                        t /= l;
                        var nn = new Vector2(-t.y, t.x);
                        FillPolygon(new[] { a + nn * ta, b + nn * tb, b - nn * tb, a - nn * ta });
                    }
                    if (tb > 0f) FillEllipse(b, tb, tb);
                    if (i == 0 && ta > 0f) FillEllipse(a, ta, ta);
                }
            }
        }
    }

    /// <summary>
    /// The hand-made eye from the Eye Bench: three layers (white + lash line, iris tone map, highlight) in
    /// <c>Resources/Eyes/*.bytes</c> (PNG bytes). Tone map: 0.5 = the iris colour exactly, darker toward black, lighter toward white.
    /// </summary>
    public static class ToonEyeLayers
    {
        public sealed class Image { public Color32[] Pixels; public int Width, Height; }

        private static Image _base, _tone, _hi;
        private static readonly Dictionary<Color32, Image> Tinted = new();
        public static bool Ready => _base != null && _tone != null && _hi != null;

        /// <summary>Main thread only: decodes the three PNG layers once.</summary>
        public static void EnsureLoaded()
        {
            if (Ready) return;
            _base = Load("Eyes/eye_base"); _tone = Load("Eyes/eye_tone"); _hi = Load("Eyes/eye_highlight");
        }

        private static Image Load(string path)
        {
            var ta = Resources.Load<TextAsset>(path);
            if (ta == null) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(ta.bytes)) return null;
                return new Image { Pixels = tex.GetPixels32(), Width = tex.width, Height = tex.height };
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        /// <summary>Base + tone map tinted to <paramref name="iris"/> + highlight, cached per colour. Thread-safe once loaded.</summary>
        public static Image Composed(Color iris)
        {
            Color32 key = iris;
            lock (Tinted) if (Tinted.TryGetValue(key, out var hit)) return hit;
            int w = _base.Width, h = _base.Height;
            var o = new Color32[w * h];
            Color c = iris;
            for (int i = 0; i < o.Length; i++)
            {
                Color d = _base.Pixels[i];
                Color32 t = _tone.Pixels[i];
                float L = t.r / 255f, ta = t.a / 255f;
                Color tc = L < 0.5f ? new Color(c.r * L * 2f, c.g * L * 2f, c.b * L * 2f) : Color.Lerp(c, Color.white, (L - 0.5f) * 2f);
                d = Over(d, tc, ta);
                Color32 hi = _hi.Pixels[i];
                d = Over(d, new Color(hi.r / 255f, hi.g / 255f, hi.b / 255f), hi.a / 255f);
                o[i] = d;
            }
            var img = new Image { Pixels = o, Width = w, Height = h };
            lock (Tinted) Tinted[key] = img;
            return img;
        }

        private static Color Over(Color d, Color c, float a)
        {
            float oa = a + d.a * (1f - a);
            if (oa <= 1e-6f) return new Color(0, 0, 0, 0);
            float k = d.a * (1f - a);
            return new Color((c.r * a + d.r * k) / oa, (c.g * a + d.g * k) / oa, (c.b * a + d.b * k) / oa, oa);
        }
    }
}
