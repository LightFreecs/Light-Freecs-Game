using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LightMMORPG.Menu
{
    /// <summary>
    /// Runtime-generated sprites (rounded panels, soft glows, fading lines, line icons) and small uGUI helpers for the
    /// main menu, so it needs no imported textures.
    /// </summary>
    public static class MenuUi
    {
        private static readonly Dictionary<string, Sprite> Cache = new();

        private static Sprite Make(string key, int w, int h, Func<float, float, float> alpha, Vector4 border = default, Func<float, float, Color> color = null)
        {
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = key, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    Color c = color != null ? color(fx, fy) : Color.white;
                    c.a *= Mathf.Clamp01(alpha(fx, fy));
                    px[y * w + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            s.name = key;
            Cache[key] = s;
            return s;
        }

        private static float RoundRectSdf(float x, float y, float w, float h, float r)
        {
            float qx = Mathf.Abs(x - w * 0.5f) - (w * 0.5f - r);
            float qy = Mathf.Abs(y - h * 0.5f) - (h * 0.5f - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        /// <summary>9-sliced filled rounded rectangle (radius in reference pixels at pixelsPerUnitMultiplier 1).</summary>
        public static Sprite RoundedRect(int radius)
        {
            int size = radius * 2 + 4;
            return Make("rr" + radius, size, size, (x, y) => 0.5f - RoundRectSdf(x, y, size, size, radius), new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
        }

        /// <summary>9-sliced rounded rectangle outline.</summary>
        public static Sprite RoundedRectOutline(int radius, float stroke)
        {
            int size = radius * 2 + 4;
            return Make("rro" + radius + "_" + stroke, size, size, (x, y) =>
            {
                float d = RoundRectSdf(x, y, size, size, radius);
                return Mathf.Min(0.5f - d, 0.5f + d + stroke);
            }, new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
        }

        /// <summary>Radial soft glow (white, alpha falls off smoothly to the edge).</summary>
        public static Sprite SoftCircle()
        {
            const int s = 128;
            return Make("soft", s, s, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(s * 0.5f, s * 0.5f)) / (s * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                return a * a * (3f - 2f * a);
            });
        }

        /// <summary>Solid disc with antialiased edge.</summary>
        public static Sprite Disc()
        {
            const int s = 64;
            return Make("disc", s, s, (x, y) => s * 0.5f - 1f - Vector2.Distance(new Vector2(x, y), new Vector2(s * 0.5f, s * 0.5f)) + 0.5f);
        }

        /// <summary>Horizontal line texture that fades out at both ends (stretch it).</summary>
        public static Sprite FadeLine()
        {
            return Make("fadeline", 256, 4, (x, y) =>
            {
                float t = x / 256f;
                return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.18f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - t) / 0.18f));
            });
        }

        /// <summary>Vertical fade: opaque at the bottom, transparent at the top (stretch it).</summary>
        public static Sprite VerticalFade()
        {
            return Make("vfade", 4, 128, (x, y) => { float t = Mathf.Clamp01((1f - y / 128f) * 1.5f); return t * t * (3f - 2f * t); });
        }

        /// <summary>Rounded square avatar with a diagonal two-colour gradient.</summary>
        public static Sprite GradientAvatar(Color a, Color b, int radius = 24)
        {
            const int s = 76;
            return Make("avatar", s, s, (x, y) => 0.5f - RoundRectSdf(x, y, s, s, radius), default,
                (x, y) => Color.Lerp(a, b, Mathf.Clamp01((x + (s - y)) / (2f * s))));
        }

        // ---------------- bevel (Customize buttons) ----------------

        /// <summary>Signed distance to a box with the top-left and bottom-right corners cut at 45° (texture y up).</summary>
        private static float BevelSdf(float x, float y, float size, float cut)
        {
            const float pad = 1f;
            float lo = pad, hi = size - pad;
            float d = Mathf.Max(Mathf.Max(lo - x, x - hi), Mathf.Max(lo - y, y - hi));
            const float k = 0.70710678f;
            d = Mathf.Max(d, (cut - ((x - lo) + (hi - y))) * k); // top-left
            d = Mathf.Max(d, (cut - ((hi - x) + (y - lo))) * k); // bottom-right
            return d;
        }

        private static int BevelSize(int cut) => cut * 2 + 6;
        private static Vector4 BevelBorder(int cut) => new(cut + 3, cut + 3, cut + 3, cut + 3);

        /// <summary>9-sliced fill of a box with two cut corners (top-left, bottom-right).</summary>
        public static Sprite BevelFill(int cut)
        {
            int s = BevelSize(cut);
            return Make("bvf" + cut, s, s, (x, y) => 0.5f - BevelSdf(x, y, s, cut), BevelBorder(cut));
        }

        /// <summary>9-sliced outline of the bevel box.</summary>
        public static Sprite BevelEdge(int cut, float stroke)
        {
            int s = BevelSize(cut);
            return Make("bve" + cut + "_" + stroke, s, s, (x, y) =>
            {
                float d = BevelSdf(x, y, s, cut);
                return Mathf.Min(0.5f - d, 0.5f + d + stroke);
            }, BevelBorder(cut));
        }

        /// <summary>The bevel outline's top border only (top edge, cut corner, top of the sides): a light catching the top.</summary>
        public static Sprite BevelTop(int cut, float stroke)
        {
            int s = BevelSize(cut);
            float from = s - (cut + 3);
            return Make("bvt" + cut + "_" + stroke, s, s, (x, y) =>
            {
                float d = BevelSdf(x, y, s, cut);
                float a = Mathf.Min(0.5f - d, 0.5f + d + stroke);
                return y >= from ? a * Mathf.Clamp01((y - from) / (s - from) * 1.6f) : 0f;
            }, BevelBorder(cut));
        }

        // ---------------- icons (SDF strokes) ----------------

        private static float Seg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude;
        }

        private static float Ring(Vector2 p, Vector2 c, float r) => Mathf.Abs((p - c).magnitude - r);

        /// <summary>Arc from angle a0 to a1 (degrees, counter-clockwise, 0 = +x).</summary>
        private static float Arc(Vector2 p, Vector2 c, float r, float a0, float a1)
        {
            Vector2 d = p - c;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float rel = Mathf.Repeat(ang - a0, 360f);
            if (rel <= Mathf.Repeat(a1 - a0, 360f)) return Mathf.Abs(d.magnitude - r);
            Vector2 e0 = c + new Vector2(Mathf.Cos(a0 * Mathf.Deg2Rad), Mathf.Sin(a0 * Mathf.Deg2Rad)) * r;
            Vector2 e1 = c + new Vector2(Mathf.Cos(a1 * Mathf.Deg2Rad), Mathf.Sin(a1 * Mathf.Deg2Rad)) * r;
            return Mathf.Min((p - e0).magnitude, (p - e1).magnitude);
        }

        public enum IconKind { Friends, Settings, Quit, Diamond, Crystal, Eye, Head, Jaw, Nose, Ear, Dice, Undo, Check, Plus, Minus, Hair, Body }

        private static float Poly(Vector2 p, params Vector2[] pts)
        {
            float d = float.MaxValue;
            for (int i = 0; i + 1 < pts.Length; i++) d = Mathf.Min(d, Seg(p, pts[i], pts[i + 1]));
            return d;
        }

        private static float Disc(Vector2 p, Vector2 c, float r) => r - (p - c).magnitude + 0.5f;

        /// <summary>Line icons for the Customize screen (thicker stroke, drawn at ~24-34 px).</summary>
        private static Func<float, float, float> CustomizeIcon(IconKind kind)
        {
            const float half = 4.6f;
            float Stroke(float d) => half - d + 0.5f;
            return kind switch
            {
                IconKind.Eye => (x, y) =>
                {
                    var p = new Vector2(x, y);
                    float d = Mathf.Min(Arc(p, new Vector2(48, 22), 38f, 43f, 137f), Arc(p, new Vector2(48, 74), 38f, 223f, 317f));
                    return Mathf.Max(Stroke(d), Disc(p, new Vector2(48, 48), 9f));
                },
                IconKind.Head => (x, y) =>
                {
                    var p = new Vector2(x, y);
                    return Stroke(Mathf.Min(Ring(p, new Vector2(48, 60), 19f), Arc(p, new Vector2(48, 4), 34f, 28f, 152f)));
                },
                IconKind.Jaw => (x, y) =>
                {
                    var p = new Vector2(x, y);
                    float d = Poly(p, new Vector2(22, 80), new Vector2(26, 48), new Vector2(48, 20), new Vector2(70, 48), new Vector2(74, 80));
                    return Stroke(Mathf.Min(d, Seg(p, new Vector2(40, 40), new Vector2(56, 40))));
                },
                IconKind.Nose => (x, y) =>
                {
                    var p = new Vector2(x, y);
                    float d = Poly(p, new Vector2(52, 82), new Vector2(39, 46), new Vector2(57, 42));
                    return Stroke(Mathf.Min(d, Arc(p, new Vector2(48, 42), 22f, 232f, 308f)));
                },
                IconKind.Ear => (x, y) =>
                {
                    var p = new Vector2(x, y);
                    return Stroke(Mathf.Min(Arc(p, new Vector2(42, 52), 26f, -110f, 110f), Arc(p, new Vector2(45, 54), 10f, -70f, 120f)));
                },
                IconKind.Dice => (x, y) =>
                {
                    var p = new Vector2(x, y);
                    float qx = Mathf.Abs(x - 48) - 24f, qy = Mathf.Abs(y - 48) - 24f;
                    float box = Mathf.Abs(new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - 8f);
                    float dots = Mathf.Max(Mathf.Max(Disc(p, new Vector2(34, 62), 5.5f), Disc(p, new Vector2(62, 34), 5.5f)),
                        Mathf.Max(Disc(p, new Vector2(48, 48), 5.5f), Mathf.Max(Disc(p, new Vector2(62, 62), 5.5f), Disc(p, new Vector2(34, 34), 5.5f))));
                    return Mathf.Max(Stroke(box), dots);
                },
                IconKind.Undo => (x, y) =>
                {
                    var p = new Vector2(x, y);
                    var c = new Vector2(48, 46);
                    const float r = 25f, a1 = 70f;
                    var e = c + new Vector2(Mathf.Cos(a1 * Mathf.Deg2Rad), Mathf.Sin(a1 * Mathf.Deg2Rad)) * r;
                    var t = new Vector2(-Mathf.Sin(a1 * Mathf.Deg2Rad), Mathf.Cos(a1 * Mathf.Deg2Rad)); // counter-clockwise travel
                    var n = new Vector2(-t.y, t.x);
                    float d = Arc(p, c, r, 110f, a1);
                    d = Mathf.Min(d, Seg(p, e, e - t * 13f + n * 11f));
                    d = Mathf.Min(d, Seg(p, e, e - t * 13f - n * 11f));
                    return Stroke(d);
                },
                IconKind.Check => (x, y) => Stroke(Poly(new Vector2(x, y), new Vector2(22, 48), new Vector2(40, 30), new Vector2(74, 66))),
                IconKind.Hair => (x, y) =>
                {
                    // head outline with three spikes rising off the top
                    var p = new Vector2(x, y);
                    float d = Arc(p, new Vector2(48, 40), 24f, 200f, 340f);
                    d = Mathf.Min(d, Poly(p, new Vector2(24, 44), new Vector2(26, 78), new Vector2(40, 60), new Vector2(50, 86), new Vector2(58, 62), new Vector2(74, 76), new Vector2(72, 44)));
                    return Stroke(d);
                },
                IconKind.Body => (x, y) =>
                {
                    // standing figure: head, shoulders tapering to the waist, two legs
                    var p = new Vector2(x, y);
                    float d = Ring(p, new Vector2(48, 79), 9f);
                    d = Mathf.Min(d, Poly(p, new Vector2(30, 40), new Vector2(26, 62), new Vector2(70, 62), new Vector2(66, 40)));
                    d = Mathf.Min(d, Poly(p, new Vector2(38, 40), new Vector2(40, 34), new Vector2(56, 34), new Vector2(58, 40)));
                    d = Mathf.Min(d, Mathf.Min(Seg(p, new Vector2(42, 34), new Vector2(40, 10)), Seg(p, new Vector2(54, 34), new Vector2(56, 10))));
                    return Stroke(d);
                },
                IconKind.Plus => (x, y) => Stroke(Mathf.Min(Seg(new Vector2(x, y), new Vector2(24, 48), new Vector2(72, 48)), Seg(new Vector2(x, y), new Vector2(48, 24), new Vector2(48, 72)))),
                _ => (x, y) => Stroke(Seg(new Vector2(x, y), new Vector2(24, 48), new Vector2(72, 48))),
            };
        }

        public static Sprite Icon(IconKind kind)
        {
            const int s = 96;
            const float half = 3.6f; // stroke half width
            if (kind >= IconKind.Eye) return Make("icon" + kind, s, s, CustomizeIcon(kind));
            Func<float, float, float> f = kind switch
            {
                IconKind.Friends => (x, y) =>
                {
                    var p = new Vector2(x, y);
                    float d = Ring(p, new Vector2(38, 62), 13f);
                    d = Mathf.Min(d, Arc(p, new Vector2(38, 18), 26f, 22f, 158f));
                    d = Mathf.Min(d, Ring(p, new Vector2(68, 58), 9.5f));
                    d = Mathf.Min(d, Arc(p, new Vector2(70, 24), 19f, 30f, 92f));
                    return half - d + 0.5f;
                },
                IconKind.Settings => (x, y) =>
                {
                    var p = new Vector2(x, y);
                    var c = new Vector2(48, 48);
                    float d = Ring(p, c, 12f);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4f;
                        var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        d = Mathf.Min(d, Seg(p, c + dir * 24f, c + dir * 34f));
                    }
                    return half - d + 0.5f;
                },
                IconKind.Quit => (x, y) =>
                {
                    var p = new Vector2(x, y);
                    float d = Arc(p, new Vector2(48, 44), 28f, 122f, 58f);
                    d = Mathf.Min(d, Seg(p, new Vector2(48, 48), new Vector2(48, 80)));
                    return half - d + 0.5f;
                },
                IconKind.Diamond => (x, y) => 30f - (Mathf.Abs(x - 48) + Mathf.Abs(y - 48)) + 0.5f,
                _ => (x, y) =>
                {
                    float ax = Mathf.Abs(x - 48) / 38f, ay = Mathf.Abs(y - 48) / 38f;
                    float v = Mathf.Sqrt(ax) + Mathf.Sqrt(ay); // astroid = 4-point star
                    return (1f - v) * 30f + 0.5f;
                },
            };
            return Make("icon" + kind, s, s, f);
        }

        // ---------------- uGUI helpers ----------------

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Place(this RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Fill(this RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static Image Img(Transform parent, string name, Sprite sprite, Color color, bool sliced = false, float ppuMul = 1f)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            if (sliced) { img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = ppuMul; }
            return img;
        }

        public static Text Label(Transform parent, string name, string text, Font font, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter, float spacingEm = 0f)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.supportRichText = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            if (spacingEm != 0f) rt.gameObject.AddComponent<LetterSpacing>().em = spacingEm;
            return t;
        }

        /// <summary>Width of a single-line label including its letter spacing.</summary>
        public static float Width(Text t)
        {
            var ls = t.GetComponent<LetterSpacing>();
            float extra = ls != null ? ls.em * t.fontSize * Mathf.Max(0, t.text.Length - 1) : 0f;
            return t.preferredWidth + extra;
        }

        public static Shadow AddShadow(Graphic g, Color c, Vector2 d)
        {
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = c;
            s.effectDistance = d;
            s.useGraphicAlpha = true;
            return s;
        }
    }

    /// <summary>CSS-like letter spacing for single-line legacy <see cref="Text"/> (in em, keeps the alignment centre).</summary>
    [RequireComponent(typeof(Text))]
    public sealed class LetterSpacing : BaseMeshEffect
    {
        public float em;
        private readonly List<UIVertex> _stream = new();

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Mathf.Approximately(em, 0f)) return;
            var text = GetComponent<Text>();
            string s = text.text;
            if (string.IsNullOrEmpty(s)) return;
            float spacing = em * text.fontSize;
            vh.GetUIVertexStream(_stream); // 6 vertices per glyph quad, in character order
            int glyphs = _stream.Count / 6;
            int visible = 0;
            foreach (char c in s) if (!char.IsWhiteSpace(c)) visible++;
            bool stripped = glyphs % Mathf.Max(1, visible) == 0 && glyphs != s.Length && visible > 0;
            int perBlock = stripped ? visible : s.Length;
            int blocks = Mathf.Max(1, glyphs / Mathf.Max(1, perBlock)); // shadow/outline copies come first
            float total = spacing * (s.Length - 1);
            float shift = text.alignment switch
            {
                TextAnchor.UpperLeft or TextAnchor.MiddleLeft or TextAnchor.LowerLeft => 0f,
                TextAnchor.UpperRight or TextAnchor.MiddleRight or TextAnchor.LowerRight => -total,
                _ => -total * 0.5f,
            };
            for (int b = 0; b < blocks; b++)
            {
                int q = 0;
                for (int ci = 0; ci < s.Length && q < perBlock; ci++)
                {
                    if (stripped && char.IsWhiteSpace(s[ci])) continue;
                    float off = ci * spacing + shift;
                    int baseIdx = (b * perBlock + q) * 6;
                    if (baseIdx + 6 > _stream.Count) break;
                    for (int k = 0; k < 6; k++)
                    {
                        var v = _stream[baseIdx + k];
                        v.position.x += off;
                        _stream[baseIdx + k] = v;
                    }
                    q++;
                }
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(_stream);
        }
    }

    /// <summary>
    /// Selectable used for every menu control: selection (keyboard/gamepad focus or mouse hover) drives a 0..1 glow
    /// value that the owner renders through <see cref="OnGlow"/>; Submit/click invokes <see cref="OnClick"/>.
    /// </summary>
    public sealed class GlowSelectable : Selectable, ISubmitHandler, IPointerClickHandler
    {
        public Action OnClick;
        public Action<float> OnGlow;
        public float speed = 10f;
        public float Glow { get; private set; }
        public bool Focused => EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;

        public override void OnPointerEnter(PointerEventData eventData)
        {
            base.OnPointerEnter(eventData);
            if (IsInteractable() && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(gameObject);
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (IsInteractable()) OnClick?.Invoke();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && IsInteractable()) OnClick?.Invoke();
        }

        /// <summary>Snap the glow to its target (screenshots).</summary>
        public void Snap()
        {
            Glow = Focused ? 1f : 0f;
            OnGlow?.Invoke(Glow);
        }

        private void Update()
        {
            float target = Focused && IsInteractable() ? 1f : 0f;
            if (Mathf.Approximately(Glow, target)) return;
            Glow = Mathf.MoveTowards(Glow, target, Time.unscaledDeltaTime * speed);
            OnGlow?.Invoke(Glow);
        }
    }
}
