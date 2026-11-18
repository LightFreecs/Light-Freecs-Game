using UnityEngine;

namespace LightMMORPG.Characters
{
    /// <summary>
    /// Paints the anime face texture (eyes with iris/pupil/highlights/lashes, brows, nose and mouth lines) and the hair
    /// gradient in code. Face UVs are a front planar projection of head-local XY (see <see cref="FaceUv"/>).
    /// </summary>
    public static class FacePainter
    {
        /// <summary>Head-local square covered by the face texture (meters, before head scaling).</summary>
        public const float Span = 0.24f;
        public const float CenterY = 0.06f;
        public const float EyeX = 0.046f;
        public const float EyeY = 0.082f;

        public static Vector2 FaceUv(Vector3 headLocal) => new(0.5f + headLocal.x / Span, 0.5f + (headLocal.y - CenterY) / Span);

        private struct EyeParams { public float Ew, Eh, Slope, Irx, Iry, Lash, X, Y; }

        private static EyeParams Params(EyeShape s) => s switch
        {
            EyeShape.Big => new EyeParams { Ew = 0.0275f, Eh = 0.0255f, Slope = 0.14f, Irx = 0.0148f, Iry = 0.0195f, Lash = 1.25f, X = 0.05f, Y = 0.074f },
            EyeShape.Round => new EyeParams { Ew = 0.026f, Eh = 0.021f, Slope = 0.12f, Irx = 0.0128f, Iry = 0.0165f, Lash = 1.15f, X = 0.049f, Y = 0.076f },
            EyeShape.Narrow => new EyeParams { Ew = 0.0275f, Eh = 0.0155f, Slope = 0.38f, Irx = 0.0112f, Iry = 0.0142f, Lash = 1.2f, X = 0.049f, Y = 0.078f },
            EyeShape.Fierce => new EyeParams { Ew = 0.027f, Eh = 0.0165f, Slope = 0.48f, Irx = 0.0105f, Iry = 0.0135f, Lash = 1.4f, X = 0.049f, Y = 0.078f },
            _ => new EyeParams { Ew = 0.027f, Eh = 0.019f, Slope = 0.3f, Irx = 0.012f, Iry = 0.0155f, Lash = 1.25f, X = 0.049f, Y = 0.077f },
        };

        private sealed class Canvas
        {
            public readonly int Size;
            public readonly Color[] Px;
            public readonly float MetersPerPx;
            public Canvas(int size, Color fill)
            {
                Size = size;
                Px = new Color[size * size];
                for (int i = 0; i < Px.Length; i++) Px[i] = fill;
                MetersPerPx = Span / size;
            }
            public Vector2 ToLocal(int x, int y) => new((x + 0.5f) * MetersPerPx - Span * 0.5f, (y + 0.5f) * MetersPerPx - Span * 0.5f + CenterY);
            public void Blend(int x, int y, Color c, float a)
            {
                if (a <= 0f) return;
                int i = x + y * Size;
                Px[i] = Color.Lerp(Px[i], c, Mathf.Clamp01(a));
            }
            public void Rect(Vector2 min, Vector2 max, out int x0, out int y0, out int x1, out int y1)
            {
                x0 = Mathf.Clamp(Mathf.FloorToInt((min.x + Span * 0.5f) / MetersPerPx), 0, Size - 1);
                x1 = Mathf.Clamp(Mathf.CeilToInt((max.x + Span * 0.5f) / MetersPerPx), 0, Size - 1);
                y0 = Mathf.Clamp(Mathf.FloorToInt((min.y - CenterY + Span * 0.5f) / MetersPerPx), 0, Size - 1);
                y1 = Mathf.Clamp(Mathf.CeilToInt((max.y - CenterY + Span * 0.5f) / MetersPerPx), 0, Size - 1);
            }
        }

        public static Texture2D PaintFace(CharacterRecipe r, int size = 512) => ToTexture(PaintFacePixels(r, size), size, "Face_" + r.displayName);

        public static Texture2D ToTexture(Color[] px, int size, string name)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            tex.SetPixels(px);
            tex.Apply(true, false);
            return tex;
        }

        /// <summary>Thread-safe: paints the face into a pixel array.</summary>
        public static Color[] PaintFacePixels(CharacterRecipe r, int size = 512)
        {
            var skin = r.skinTone; skin.a = 1f;
            var cv = new Canvas(size, skin);
            var ink = new Color(0.09f, 0.05f, 0.06f);
            var skinLine = new Color(skin.r * 0.55f, skin.g * 0.38f, skin.b * 0.36f);
            var brow = Color.Lerp(r.hairColor, ink, 0.55f);
            var ep = Params(r.eyeShape);
            for (int side = -1; side <= 1; side += 2)
            {
                PaintEye(cv, side, ep, r.eyeColor, ink, skinLine);
                PaintBrow(cv, side, ep, brow, r.browAngle, r.browThickness);
            }
            // nose: small shadow stroke on one side + tip line
            Stroke(cv, new Vector2(0.006f, 0.052f), new Vector2(0.0015f, 0.038f), 0.0009f, 0.0006f, skinLine, 0.75f);
            Stroke(cv, new Vector2(-0.004f, 0.0365f), new Vector2(0.004f, 0.0365f), 0.0006f, 0.0006f, skinLine, 0.45f);
            PaintMouth(cv, r.expression, ink, skinLine);

            return cv.Px;
        }

        private static void PaintMouth(Canvas cv, Expression ex, Color ink, Color skinLine)
        {
            var lip = new Color(0.32f, 0.12f, 0.12f);
            if (ex == Expression.Grin)
            {
                // open "D" grin: flat-ish upper lip, deep lower arc, teeth strip, tongue
                const float w = 0.019f, top = 0.009f, depth = 0.017f;
                cv.Rect(new Vector2(-w - 0.003f, top - depth - 0.004f), new Vector2(w + 0.003f, top + 0.004f), out int x0, out int y0, out int x1, out int y1);
                float px = cv.MetersPerPx;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        Vector2 p = cv.ToLocal(x, y);
                        float nx = p.x / w;
                        if (Mathf.Abs(nx) > 1.15f) continue;
                        float yTop = top + 0.0025f * nx * nx;
                        float yBot = top - depth * Mathf.Pow(Mathf.Max(0f, 1f - nx * nx), 0.65f);
                        float inside = Mathf.Min(yTop - p.y, p.y - yBot);
                        float a = Mathf.Clamp01(inside / px + 0.5f);
                        if (a > 0f)
                        {
                            Color c = new(0.42f, 0.07f, 0.1f);
                            float tongue = 1f - Mathf.Sqrt((p.x / (w * 0.6f)) * (p.x / (w * 0.6f)) + Mathf.Pow((p.y - (yBot + 0.0005f)) / (depth * 0.45f), 2f));
                            c = Color.Lerp(c, new Color(0.95f, 0.45f, 0.45f), Mathf.Clamp01(tongue * 0.006f / px + 0.5f));
                            c = Color.Lerp(c, Color.white, Mathf.Clamp01((p.y - (yTop - 0.0032f)) / px + 0.5f) * (Mathf.Abs(nx) < 0.8f ? 1f : 0f));
                            cv.Blend(x, y, c, a);
                        }
                        float edge = Mathf.Min(Mathf.Abs(yTop - p.y), Mathf.Abs(p.y - yBot));
                        if (inside > -0.002f) cv.Blend(x, y, ink, Mathf.Clamp01((0.0006f - edge) / px + 0.5f));
                    }
                return;
            }
            float tilt = ex == Expression.Smirk ? 0.0035f : 0f;
            Curve(cv, -0.015f, 0.015f, x => 0.006f - 0.0022f * (x / 0.015f) * (x / 0.015f) + 0.0006f + tilt * Mathf.Max(0f, x / 0.015f) * (x / 0.015f) * 1.6f,
                x => 0.0012f * (1f - 0.55f * Mathf.Abs(x / 0.015f)), lip, 1f);
            Curve(cv, -0.006f, 0.006f, x => -0.0035f, x => 0.0009f * (1f - Mathf.Abs(x / 0.006f)), skinLine, 0.45f);
        }

        private static float UpperLid(in EyeParams e, float nx)
        {
            if (nx > 1f) return e.Eh * (0.12f + e.Slope) + (nx - 1f) * e.Eh * (0.35f + e.Slope * 0.6f);
            float s = Mathf.Sqrt(Mathf.Max(0f, 1f - nx * nx));
            return e.Eh * (0.86f * Mathf.Pow(s, 0.8f) + 0.12f) + e.Eh * e.Slope * nx;
        }

        private static float LowerLid(in EyeParams e, float nx)
        {
            float s = Mathf.Sqrt(Mathf.Max(0f, 1f - nx * nx));
            return -e.Eh * 0.62f * Mathf.Pow(s, 1.2f) + e.Eh * (0.08f + e.Slope * 0.55f * nx);
        }

        private static void PaintEye(Canvas cv, int side, EyeParams e, Color eyeColor, Color ink, Color skinLine)
        {
            float cx = side * e.X, cy = e.Y;
            cv.Rect(new Vector2(cx - e.Ew * 1.6f, cy - e.Eh * 1.6f), new Vector2(cx + e.Ew * 1.6f, cy + e.Eh * 2.4f), out int x0, out int y0, out int x1, out int y1);
            float px = cv.MetersPerPx;
            float icx = -0.07f * e.Ew, icy = 0.08f * e.Eh;
            Color irisTop = eyeColor * 0.3f, irisBot = Color.Lerp(eyeColor, Color.white, 0.35f) * 1.05f;
            Color sclera = new(0.98f, 0.98f, 1f), scleraShade = new(0.74f, 0.76f, 0.88f);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 l = cv.ToLocal(x, y);
                    float ex = (l.x - cx) * side, ey = l.y - cy;
                    float nx = ex / e.Ew;
                    float yu = UpperLid(e, nx), yl = LowerLid(e, nx);
                    float inside = Mathf.Min(Mathf.Min(yu - ey, ey - yl), (1f - Mathf.Abs(nx)) * e.Ew * 0.5f);
                    float a = Mathf.Clamp01(inside / px + 0.5f);
                    if (a > 0f)
                    {
                        float shadeT = Mathf.Clamp01((yu - ey) / (e.Eh * 0.55f));
                        Color c = Color.Lerp(scleraShade, sclera, shadeT * shadeT);
                        // iris
                        float dx = (ex - icx) / e.Irx, dy = (ey - icy) / e.Iry;
                        float ir = Mathf.Sqrt(dx * dx + dy * dy);
                        float irisA = Mathf.Clamp01((1f - ir) * e.Irx / px + 0.5f);
                        if (irisA > 0f)
                        {
                            float t = Mathf.Clamp01(0.5f - dy * 0.5f);
                            Color ic = Color.Lerp(irisTop, irisBot, Mathf.SmoothStep(0f, 1f, t));
                            ic = Color.Lerp(ic, irisTop * 0.6f, Mathf.SmoothStep(0.72f, 1f, ir)); // dark rim
                            // pupil
                            float pr = Mathf.Sqrt((dx / 0.42f) * (dx / 0.42f) + (dy / 0.5f) * (dy / 0.5f));
                            ic = Color.Lerp(ic, eyeColor * 0.08f, Mathf.Clamp01((1f - pr) * e.Irx * 0.42f / px + 0.5f));
                            // shadow cast by the upper lid
                            ic = Color.Lerp(ic, ic * 0.45f, Mathf.Clamp01(1f - (yu - ey) / (e.Eh * 0.45f)));
                            // highlights
                            float h1 = Ellipse(ex, ey, icx - 0.38f * e.Irx, icy + 0.42f * e.Iry, 0.34f * e.Irx, 0.26f * e.Iry);
                            float h2 = Ellipse(ex, ey, icx + 0.36f * e.Irx, icy - 0.48f * e.Iry, 0.16f * e.Irx, 0.13f * e.Iry);
                            ic = Color.Lerp(ic, Color.white, Mathf.Clamp01(h1 * 0.34f * e.Irx / px + 0.5f));
                            ic = Color.Lerp(ic, Color.white, 0.85f * Mathf.Clamp01(h2 * 0.16f * e.Irx / px + 0.5f));
                            c = Color.Lerp(c, ic, irisA);
                        }
                        cv.Blend(x, y, c, a);
                    }
                    // upper lash line (thick, heavier at the outer corner, with a flick past it)
                    if (nx > -1.04f && nx < 1.32f)
                    {
                        float taper = Mathf.SmoothStep(-1.04f, -0.55f, nx) * (nx > 1f ? 1f - (nx - 1f) / 0.32f : 1f);
                        float th = Mathf.Lerp(0.0011f, 0.0031f, Mathf.SmoothStep(-0.8f, 0.7f, nx)) * e.Lash * Mathf.Max(taper, 0.25f);
                        if (nx > 1f) th *= Mathf.Clamp01(1f - (nx - 1f) / 0.32f);
                        float centre = yu + th * 0.25f;
                        float la = Mathf.Clamp01((th * 0.5f - Mathf.Abs(ey - centre)) / px + 0.5f);
                        cv.Blend(x, y, ink, la);
                    }
                    // lower lash (thin, outer two thirds)
                    if (nx > 0.05f && nx < 1.0f)
                    {
                        float th = 0.0007f * Mathf.SmoothStep(0.05f, 0.5f, nx);
                        float la = Mathf.Clamp01((th * 0.5f - Mathf.Abs(ey - yl)) / px + 0.5f);
                        cv.Blend(x, y, ink, la * 0.9f);
                    }
                    // eyelid crease
                    if (nx > -0.45f && nx < 0.95f)
                    {
                        float crease = UpperLid(e, nx) + e.Eh * 0.5f + 0.0012f;
                        float th = 0.0005f * Mathf.Sin(Mathf.InverseLerp(-0.45f, 0.95f, nx) * Mathf.PI);
                        float la = Mathf.Clamp01((th * 0.5f - Mathf.Abs(ey - crease)) / px + 0.5f);
                        cv.Blend(x, y, skinLine, la * 0.8f);
                    }
                }
        }

        // > 0 inside (normalized), for highlight ellipses
        private static float Ellipse(float x, float y, float cx, float cy, float rx, float ry)
        {
            float dx = (x - cx) / rx, dy = (y - cy) / ry;
            return 1f - Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static void PaintBrow(Canvas cv, int side, EyeParams e, Color col, float angle, float thick)
        {
            float cx = side * e.X;
            float innerX = cx - side * e.Ew * 0.95f, outerX = cx + side * e.Ew * 1.15f;
            float innerY = e.Y + e.Eh + 0.0115f - angle * 0.0055f;
            float outerY = e.Y + e.Eh + 0.0135f + angle * 0.0028f;
            float lo = Mathf.Min(innerX, outerX), hi = Mathf.Max(innerX, outerX);
            cv.Rect(new Vector2(lo - 0.004f, Mathf.Min(innerY, outerY) - 0.008f), new Vector2(hi + 0.004f, Mathf.Max(innerY, outerY) + 0.008f),
                out int x0, out int y0, out int x1, out int y1);
            float px = cv.MetersPerPx;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 l = cv.ToLocal(x, y);
                    float t = (l.x - innerX) / (outerX - innerX);
                    if (t < -0.05f || t > 1.05f) continue;
                    float tc = Mathf.Clamp01(t);
                    float yc = Mathf.Lerp(innerY, outerY, tc) + 0.0028f * Mathf.Sin(tc * Mathf.PI) * (1f - angle * 0.3f);
                    float th = Mathf.Lerp(0.0052f, 0.0014f, Mathf.Pow(tc, 1.3f)) * thick;
                    float endFade = t < 0f ? 1f + t / 0.05f : (t > 1f ? 1f - (t - 1f) / 0.05f : 1f);
                    float a = Mathf.Clamp01((th * 0.5f - Mathf.Abs(l.y - yc)) / px + 0.5f) * Mathf.Clamp01(endFade);
                    cv.Blend(x, y, col, a);
                }
        }

        private static void Stroke(Canvas cv, Vector2 a, Vector2 b, float thA, float thB, Color col, float alpha)
        {
            float pad = Mathf.Max(thA, thB) + 0.002f;
            cv.Rect(Vector2.Min(a, b) - Vector2.one * pad, Vector2.Max(a, b) + Vector2.one * pad, out int x0, out int y0, out int x1, out int y1);
            Vector2 ab = b - a;
            float l2 = Mathf.Max(ab.sqrMagnitude, 1e-12f);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = cv.ToLocal(x, y);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
                    float d = (p - (a + ab * t)).magnitude;
                    float th = Mathf.Lerp(thA, thB, t);
                    cv.Blend(x, y, col, alpha * Mathf.Clamp01((th * 0.5f - d) / cv.MetersPerPx + 0.5f));
                }
        }

        private static void Curve(Canvas cv, float xMin, float xMax, System.Func<float, float> yOf, System.Func<float, float> thOf, Color col, float alpha)
        {
            cv.Rect(new Vector2(xMin - 0.002f, yOf(0f) - 0.006f), new Vector2(xMax + 0.002f, yOf(0f) + 0.006f), out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = cv.ToLocal(x, y);
                    if (p.x < xMin || p.x > xMax) continue;
                    float th = Mathf.Max(thOf(p.x), 0f);
                    cv.Blend(x, y, col, alpha * Mathf.Clamp01((th * 0.5f - Mathf.Abs(p.y - yOf(p.x))) / cv.MetersPerPx + 0.5f));
                }
        }

        /// <summary>Grayscale gradient along a hair clump (v = 0 root → 1 tip): dark roots, a glossy band, lighter tips.</summary>
        public static Texture2D PaintHairGradient(Color root, Color tip)
        {
            const int h = 128;
            var tex = new Texture2D(4, h, TextureFormat.RGBA32, true, false) { name = "HairGradient", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[4 * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                float g = Mathf.Lerp(0.62f, 0.86f, Mathf.SmoothStep(0f, 0.22f, v));
                float band = Mathf.Exp(-Mathf.Pow((v - 0.34f) / 0.045f, 2f));
                g = Mathf.Lerp(g, 1f, band * 0.9f);
                g = Mathf.Lerp(g, 0.95f, Mathf.SmoothStep(0.7f, 1f, v));
                Color c = Color.Lerp(root, tip, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.95f, v))) * g;
                c.a = 1f;
                for (int x = 0; x < 4; x++) px[x + y * 4] = c;
            }
            tex.SetPixels(px);
            tex.Apply(true, false);
            return tex;
        }
    }
}
