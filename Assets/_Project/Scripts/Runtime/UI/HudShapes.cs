using UnityEngine;
using UnityEngine.UI;

namespace LightMMORPG.UI
{
    /// <summary>
    /// A slanted (parallelogram) piece of a HUD bar (bench <c>docs/bench/hud-bench.html</c>: CSS skewX(-20deg)): the span
    /// <see cref="From"/>..<see cref="To"/> of the rect's width and <see cref="VMin"/>..<see cref="VMax"/> of its height,
    /// with a horizontal gradient <see cref="Left"/> → <see cref="Right"/> (times <see cref="Graphic.color"/>).
    /// "/" leaning by default, "\" when <see cref="Mirror"/>. <see cref="Ticks"/> &gt; 0 draws that many − 1 thin
    /// dividers instead. <see cref="Grow"/> enlarges the whole shape (pixels) for outlines.
    /// </summary>
    public sealed class SlantShape : MaskableGraphic
    {
        public const float DefaultSlant = 20f;

        [SerializeField] private float _from, _to = 1f, _vMin, _vMax = 1f, _slant = DefaultSlant, _grow, _tickWidth = 2f;
        [SerializeField] private bool _mirror;
        [SerializeField] private int _ticks;
        [SerializeField] private Color _left = Color.white, _right = Color.white;

        public float From { get => _from; set { if (!Mathf.Approximately(_from, value)) { _from = value; SetVerticesDirty(); } } }
        public float To { get => _to; set { if (!Mathf.Approximately(_to, value)) { _to = value; SetVerticesDirty(); } } }
        public float VMin { get => _vMin; set { _vMin = value; SetVerticesDirty(); } }
        public float VMax { get => _vMax; set { _vMax = value; SetVerticesDirty(); } }
        public float Slant { get => _slant; set { _slant = value; SetVerticesDirty(); } }
        public float Grow { get => _grow; set { _grow = value; SetVerticesDirty(); } }
        public bool Mirror { get => _mirror; set { _mirror = value; SetVerticesDirty(); } }
        public int Ticks { get => _ticks; set { _ticks = value; SetVerticesDirty(); } }
        public Color Left { get => _left; set { if (_left != value) { _left = value; SetVerticesDirty(); } } }
        public Color Right { get => _right; set { if (_right != value) { _right = value; SetVerticesDirty(); } } }

        /// <summary>One colour across.</summary>
        public void SetColors(Color c) { Left = c; Right = c; }

        /// <summary>Fills from the bar's start (the left, or the right edge when mirrored) to this fraction.</summary>
        public void SetFill(float frac)
        {
            frac = Mathf.Clamp01(frac);
            if (_mirror) { From = 1f - frac; To = 1f; }
            else { From = 0f; To = frac; }
        }

        public static SlantShape Create(Transform parent, string name, bool mirror = false)
        {
            var rt = UiKit.Rect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiKit.Stretch(rt);
            var s = rt.gameObject.AddComponent<SlantShape>();
            s.raycastTarget = false;
            s._mirror = mirror;
            return s;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            float g = _grow;
            float x0 = r.xMin - g, w = r.width + 2f * g, y0 = r.yMin - g, h = r.height + 2f * g;
            float lean = h * Mathf.Tan(_slant * Mathf.Deg2Rad) * (_mirror ? -1f : 1f);
            Vector2 P(float t, float v) => new(x0 + t * w + (v - 0.5f) * lean, y0 + v * h);
            Color c = color;
            if (_ticks > 0)
            {
                float tw = _tickWidth / Mathf.Max(1f, w);
                for (int i = 1; i < _ticks; i++)
                {
                    float t = (float)i / _ticks;
                    Quad(vh, P(t - tw * 0.5f, _vMin), P(t - tw * 0.5f, _vMax), P(t + tw * 0.5f, _vMax), P(t + tw * 0.5f, _vMin), _left * c, _left * c);
                }
                return;
            }
            if (_to - _from <= 1e-4f || _vMax - _vMin <= 1e-4f) return;
            Quad(vh, P(_from, _vMin), P(_from, _vMax), P(_to, _vMax), P(_to, _vMin), _left * c, _right * c);
        }

        private static void Quad(VertexHelper vh, Vector2 bl, Vector2 tl, Vector2 tr, Vector2 br, Color left, Color right)
        {
            int i = vh.currentVertCount;
            vh.AddVert(bl, left, Vector4.zero);
            vh.AddVert(tl, left, Vector4.zero);
            vh.AddVert(tr, right, Vector4.zero);
            vh.AddVert(br, right, Vector4.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }

    /// <summary>
    /// A ring / disc / arc for the HUD power gauge: fills <see cref="Fill"/> of a full turn clockwise from the top between
    /// <see cref="Inner"/> and <see cref="Outer"/> (fractions of half the rect's smaller side). <see cref="Notches"/> &gt; 0
    /// draws that many radial marks instead (every 1/n of a turn).
    /// </summary>
    public sealed class RingShape : MaskableGraphic
    {
        [SerializeField] private float _fill = 1f, _inner = 0.7f, _outer = 1f, _notchWidth = 2f;
        [SerializeField] private int _notches, _segments = 72;

        public float Fill { get => _fill; set { if (!Mathf.Approximately(_fill, value)) { _fill = value; SetVerticesDirty(); } } }
        public float Inner { get => _inner; set { _inner = value; SetVerticesDirty(); } }
        public float Outer { get => _outer; set { _outer = value; SetVerticesDirty(); } }
        public int Notches { get => _notches; set { _notches = value; SetVerticesDirty(); } }
        public float NotchWidth { get => _notchWidth; set { _notchWidth = value; SetVerticesDirty(); } }

        public static RingShape Create(Transform parent, string name, float inner, float outer, Color c)
        {
            var rt = UiKit.Rect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiKit.Stretch(rt);
            var s = rt.gameObject.AddComponent<RingShape>();
            s.raycastTarget = false;
            s._inner = inner;
            s._outer = outer;
            s.color = c;
            return s;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Vector2 ctr = r.center;
            float half = Mathf.Min(r.width, r.height) * 0.5f, ri = _inner * half, ro = _outer * half;
            Color c = color;
            Vector2 Dir(float turn) { float a = turn * Mathf.PI * 2f; return new Vector2(Mathf.Sin(a), Mathf.Cos(a)); }
            if (_notches > 0)
            {
                for (int i = 0; i < _notches; i++)
                {
                    Vector2 d = Dir((float)i / _notches), side = new Vector2(d.y, -d.x) * (_notchWidth * 0.5f);
                    int k = vh.currentVertCount;
                    vh.AddVert(ctr + d * ri - side, c, Vector4.zero);
                    vh.AddVert(ctr + d * ro - side, c, Vector4.zero);
                    vh.AddVert(ctr + d * ro + side, c, Vector4.zero);
                    vh.AddVert(ctr + d * ri + side, c, Vector4.zero);
                    vh.AddTriangle(k, k + 1, k + 2);
                    vh.AddTriangle(k, k + 2, k + 3);
                }
                return;
            }
            float fill = Mathf.Clamp01(_fill);
            if (fill <= 1e-4f) return;
            int n = Mathf.Max(2, Mathf.CeilToInt(_segments * fill));
            for (int i = 0; i <= n; i++)
            {
                Vector2 d = Dir(fill * i / n);
                vh.AddVert(ctr + d * ri, c, Vector4.zero);
                vh.AddVert(ctr + d * ro, c, Vector4.zero);
                if (i == 0) continue;
                int k = vh.currentVertCount - 4;
                vh.AddTriangle(k, k + 1, k + 3);
                vh.AddTriangle(k, k + 3, k + 2);
            }
        }
    }

    /// <summary>An upward chevron ("^"), drawn as two slanted strokes (the charging arrows over the power gauge).</summary>
    public sealed class ChevronShape : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            float th = r.height * 0.45f;
            Vector2 bl = new(r.xMin, r.yMin), tc = new(r.center.x, r.yMax), br = new(r.xMax, r.yMin);
            Vector2 down = new(0f, -th);
            Color c = color;
            void Q(Vector2 a, Vector2 b, Vector2 cc, Vector2 d)
            {
                int k = vh.currentVertCount;
                vh.AddVert(a, c, Vector4.zero); vh.AddVert(b, c, Vector4.zero); vh.AddVert(cc, c, Vector4.zero); vh.AddVert(d, c, Vector4.zero);
                vh.AddTriangle(k, k + 1, k + 2); vh.AddTriangle(k, k + 2, k + 3);
            }
            Q(bl, tc, tc + down, bl + new Vector2(th * 0.9f, 0f));
            Q(tc + down, tc, br, br - new Vector2(th * 0.9f, 0f));
        }
    }
}
