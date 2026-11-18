using System.Collections.Generic;
using LightMMORPG.Characters;
using UnityEngine;

namespace LightMMORPG.Menu
{
    /// <summary>
    /// Three-quarter portraits of a hairstyle on the player's Toon Head for the Customize hair tiles, drawn on the CPU
    /// (no camera or render texture): the real head and hair meshes, the painted face, two-band toon shading and an ink line.
    /// </summary>
    public static class HairPreview
    {
        public const int Width = 340, Height = 250;
        private const int SS = 2; // supersampling
        private const float Yaw = 30f, Pitch = 8f;

        private static readonly Color Ink = new(0.13f, 0.09f, 0.10f, 1f);

        /// <summary>Draws the recipe's hairstyle into <paramref name="tex"/> (made or resized as needed) and returns it.</summary>
        public static Texture2D Render(CharacterRecipe recipe, Texture2D tex)
        {
            var settings = recipe.toonHead?.Clone() ?? new ToonHeadSettings();
            var shape = ToonHead.Apply(settings);
            var head = ToonHead.BuildMesh(shape);
            var hair = ToonHair.Build(shape, head, recipe.hairStyle);
            var face = ToonFacePainter.Paint(shape, settings.eyeStyle, recipe.eyeColor, recipe.hairColor, recipe.skinTone);
            int faceN = ToonFacePainter.Size;

            var rot = Quaternion.Euler(Pitch, Yaw, 0f);
            Vector3 light = new Vector3(0.45f, 0.65f, 0.62f).normalized; // view space: +x = screen left, +z = toward the viewer

            // view-space positions (x mirrored: the camera looks down −z at the face)
            var headV = View(head.Positions, rot);
            var hairV = View(hair.Positions, rot);

            // frame the head (from the chin up) and all the hair
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            void Grow(Vector3 v) { minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x); minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y); }
            for (int i = 0; i < headV.Length; i++) if (head.Positions[i].y >= -0.02f) Grow(headV[i]);
            foreach (var v in hairV) Grow(v);
            int RW = Width * SS, RH = Height * SS;
            float pad = 0.06f, bw = (maxX - minX) * (1f + pad * 2f), bh = (maxY - minY) * (1f + pad * 2f);
            float scale = Mathf.Min(RW / bw, RH / bh), cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
            Vector3 ToScreen(Vector3 v) => new(RW * 0.5f + (v.x - cx) * scale, RH * 0.5f + (v.y - cy) * scale, v.z);

            var depth = new float[RW * RH];
            var col = new Color[RW * RH];
            for (int i = 0; i < depth.Length; i++) depth[i] = float.NegativeInfinity;

            // ---- head (without the ears when the hair covers them)
            var hs = new Vector3[headV.Length];
            for (int i = 0; i < hs.Length; i++) hs[i] = ToScreen(headV[i]);
            var headN = Normals(head.Normals, head.Positions.Count, rot);
            Color skin = recipe.skinTone; skin.a = 1f;
            Color skinShade = new(skin.r * 0.86f, skin.g * 0.72f, skin.b * 0.72f, 1f);
            int headCount = ToonHair.HidesEars(recipe.hairStyle) ? head.HeadTriangleCount * 3 : head.Triangles.Count;
            for (int t = 0; t + 2 < headCount; t += 3)
            {
                int a = head.Triangles[t], b = head.Triangles[t + 1], c = head.Triangles[t + 2];
                Raster(hs[a], hs[b], hs[c], RW, RH, depth, col, (u, v, w) =>
                {
                    Vector3 n = Lit(headN, a, b, c, u, v, w, hs);
                    Color px = Vector3.Dot(n, light) > 0.08f ? skin : skinShade;
                    Vector3 p = head.Positions[a] * u + head.Positions[b] * v + head.Positions[c] * w;
                    if (p.z > 0f)
                    {
                        Vector2 uv = ToonFacePainter.Uv(p);
                        int fx = (int)(uv.x * faceN), fy = (int)(uv.y * faceN);
                        if (fx >= 0 && fy >= 0 && fx < faceN && fy < faceN)
                        {
                            var f = face[fy * faceN + fx];
                            px = Color.Lerp(px, new Color(f.r, f.g, f.b, 1f), f.a);
                        }
                    }
                    return px;
                });
            }

            // ---- hair
            var ss = new Vector3[hairV.Length];
            for (int i = 0; i < ss.Length; i++) ss[i] = ToScreen(hairV[i]);
            var hairN = Normals(hair.Normals, hair.Positions.Count, rot);
            Color baseC = recipe.hairColor; baseC.a = 1f;
            Color shadeC = new(baseC.r * 0.58f, baseC.g * 0.58f, baseC.b * 0.66f, 1f);
            Color shineC = Color.Lerp(baseC, Color.white, 0.22f);
            for (int t = 0; t + 2 < hair.Triangles.Count; t += 3)
            {
                int a = hair.Triangles[t], b = hair.Triangles[t + 1], c = hair.Triangles[t + 2];
                Raster(ss[a], ss[b], ss[c], RW, RH, depth, col, (u, v, w) =>
                {
                    Vector3 n = Lit(hairN, a, b, c, u, v, w, ss);
                    float ndl = Vector3.Dot(n, light);
                    Color px = ndl > 0.72f ? shineC : ndl > 0.12f ? baseC : shadeC;
                    float along = hair.Along[a] * u + hair.Along[b] * v + hair.Along[c] * w;
                    var o = Color.Lerp(px * 0.82f, px, Mathf.Clamp01(along * 2.5f)); // a little darker at the roots
                    o.a = 1f;
                    return o;
                });
            }

            // ---- ink: a pixel next to something clearly nearer (or next to the figure, for background) turns to line
            var outPx = new Color[Width * Height];
            const float jump = 0.035f;
            var ink = new bool[RW * RH];
            for (int y = 0; y < RH; y++)
                for (int x = 0; x < RW; x++)
                {
                    int i = y * RW + x;
                    float d = depth[i];
                    for (int k = 0; k < 4 && !ink[i]; k++)
                    {
                        int nx = x + (k == 0 ? SS : k == 1 ? -SS : 0), ny = y + (k == 2 ? SS : k == 3 ? -SS : 0);
                        if (nx < 0 || ny < 0 || nx >= RW || ny >= RH) continue;
                        float nd = depth[ny * RW + nx];
                        if (!float.IsNegativeInfinity(nd) && (float.IsNegativeInfinity(d) || nd - d > jump)) ink[i] = true;
                    }
                }

            // ---- downsample, fading the neck out at the bottom
            for (int y = 0; y < Height; y++)
            {
                float fade = Mathf.SmoothStep(0f, 1f, y / (Height * 0.14f));
                for (int x = 0; x < Width; x++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int dy = 0; dy < SS; dy++)
                        for (int dx = 0; dx < SS; dx++)
                        {
                            int i = (y * SS + dy) * RW + x * SS + dx;
                            Color c = ink[i] ? Ink : float.IsNegativeInfinity(depth[i]) ? Color.clear : col[i];
                            r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                        }
                    outPx[y * Width + x] = a > 0.0001f ? new Color(r / a, g / a, b / a, a / (SS * SS) * fade) : Color.clear;
                }
            }

            if (tex == null || tex.width != Width || tex.height != Height)
                tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { name = "HairPreview", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(outPx);
            tex.Apply(false, false);
            return tex;
        }

        private static Vector3[] View(List<Vector3> pts, Quaternion rot)
        {
            var o = new Vector3[pts.Count];
            for (int i = 0; i < o.Length; i++) { var v = rot * pts[i]; o[i] = new Vector3(-v.x, v.y, v.z); }
            return o;
        }

        private static Vector3[] Normals(List<Vector3> ns, int count, Quaternion rot)
        {
            if (ns == null || ns.Count != count) return null;
            var o = new Vector3[count];
            for (int i = 0; i < count; i++) { var v = rot * ns[i]; o[i] = new Vector3(-v.x, v.y, v.z); }
            return o;
        }

        /// <summary>Interpolated view-space normal, turned toward the viewer (hair blades are seen from both sides).</summary>
        private static Vector3 Lit(Vector3[] ns, int a, int b, int c, float u, float v, float w, Vector3[] s)
        {
            Vector3 n;
            if (ns != null) n = (ns[a] * u + ns[b] * v + ns[c] * w).normalized;
            else
            {
                // face normal from the screen triangle (depth = view z)
                n = Vector3.Cross(s[b] - s[a], s[c] - s[a]).normalized;
            }
            return n.z < 0f ? -n : n;
        }

        /// <summary>Fills a screen triangle with depth test; shade gets the barycentric weights of a, b, c.</summary>
        private static void Raster(Vector3 p0, Vector3 p1, Vector3 p2, int W, int H, float[] depth, Color[] col, System.Func<float, float, float, Color> shade)
        {
            float area = (p1.x - p0.x) * (p2.y - p0.y) - (p1.y - p0.y) * (p2.x - p0.x);
            if (Mathf.Abs(area) < 1e-6f) return;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))));
            int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))));
            int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))));
            float inv = 1f / area;
            for (int y = y0; y <= y1; y++)
            {
                float py = y + 0.5f;
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f;
                    float u = ((p1.x - px) * (p2.y - py) - (p1.y - py) * (p2.x - px)) * inv;
                    float v = ((p2.x - px) * (p0.y - py) - (p2.y - py) * (p0.x - px)) * inv;
                    float w = 1f - u - v;
                    if (u < 0f || v < 0f || w < 0f) continue;
                    float z = p0.z * u + p1.z * v + p2.z * w;
                    int i = y * W + x;
                    if (z <= depth[i]) continue;
                    depth[i] = z;
                    col[i] = shade(u, v, w);
                }
            }
        }
    }
}
