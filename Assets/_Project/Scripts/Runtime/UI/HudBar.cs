using UnityEngine;
using UnityEngine.UI;

namespace LightMMORPG.UI
{
    /// <summary>
    /// One slanted HUD bar from the Battle HUD bench (<c>docs/bench/hud-bench.html</c>): drop shadow, white edge, dark
    /// back, damage trail, fill (horizontal gradient) with a gloss band, and optional tick marks. <see cref="Set"/>
    /// runs the trail: a drop leaves the lost part behind, held for <see cref="TrailHold"/> s (white), then it drains
    /// (red) down to the new value. Mirrored bars slant the other way and empty towards the right edge.
    /// </summary>
    public sealed class HudBar
    {
        public static readonly Color BackColor = new(7f / 255f, 11f / 255f, 24f / 255f, 0.74f);
        public static readonly Color EdgeColor = new(1f, 1f, 1f, 0.22f);
        public static readonly Color TrailHoldColor = new(1f, 1f, 1f, 0.9f);
        public static readonly Color TrailDrainColor = new(1f, 0.353f, 0.29f, 1f);

        public RectTransform Root;
        public SlantShape Edge, Back, Trail, Fill, Gloss, Ticks;
        public float TrailHold = 0.5f;
        /// <summary>How fast the trail drains (share of the bar per second).</summary>
        public float TrailRate = 0.9f;
        /// <summary>Trail colour after the hold (null = <see cref="TrailDrainColor"/>).</summary>
        public Color? TrailColor;
        public bool Holding => _hold > 0f;

        private float _value = -1f, _trail, _hold;

        /// <summary>A bar at <paramref name="pos"/> (x right, y down from the anchor) inside <paramref name="parent"/>.</summary>
        public static HudBar Create(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size,
            int ticks = 0, bool mirror = false, bool trail = true, bool shadow = true)
        {
            var b = new HudBar { Root = UiKit.Rect(name, parent, anchor, pivot, new Vector2(pos.x, -pos.y), size) };
            if (shadow)
            {
                var sh = SlantShape.Create(b.Root, "Shadow", mirror);
                sh.rectTransform.anchoredPosition = new Vector2(0f, -4f);
                sh.Grow = 2f;
                sh.color = new Color(0f, 0f, 0f, 0.3f);
            }
            b.Edge = SlantShape.Create(b.Root, "Edge", mirror);
            b.Edge.Grow = 2f;
            b.Edge.color = EdgeColor;
            b.Back = SlantShape.Create(b.Root, "Back", mirror);
            b.Back.color = BackColor;
            if (trail)
            {
                b.Trail = SlantShape.Create(b.Root, "Trail", mirror);
                b.Trail.SetFill(1f);
            }
            b.Fill = SlantShape.Create(b.Root, "Fill", mirror);
            b.Fill.SetFill(1f);
            b.Gloss = SlantShape.Create(b.Root, "Gloss", mirror);
            b.Gloss.VMin = 0.62f;
            b.Gloss.SetColors(new Color(1f, 1f, 1f, 0.35f));
            b.Gloss.SetFill(1f);
            if (ticks > 1)
            {
                b.Ticks = SlantShape.Create(b.Root, "Ticks", mirror);
                b.Ticks.Ticks = ticks;
                b.Ticks.SetColors(new Color(0f, 0f, 0f, 0.28f));
            }
            return b;
        }

        /// <summary>Gradient from the bar's start to the fill's end (start = the left, or the right edge when mirrored).</summary>
        public void SetFillColors(Color start, Color end)
        {
            if (Fill.Mirror) { Fill.Left = end; Fill.Right = start; }
            else { Fill.Left = start; Fill.Right = end; }
        }

        /// <summary>Shows this fill (0..1) and advances the damage trail by dt.</summary>
        public void Set(float frac, float dt)
        {
            frac = Mathf.Clamp01(frac);
            if (_value < 0f) _value = _trail = frac;
            if (frac < _value - 1e-4f)
            {
                if (_trail <= _value + 1e-4f) _trail = _value;   // a fresh drop: hold what was lost
                _hold = TrailHold;
            }
            _value = frac;
            if (_trail < frac) _trail = frac;
            else if (_hold > 0f) _hold -= dt;
            else _trail = Mathf.Max(frac, _trail - TrailRate * dt);
            Fill.SetFill(frac);
            Gloss.SetFill(frac);
            if (Trail != null)
            {
                Trail.SetFill(_trail);
                Trail.SetColors(_hold > 0f ? TrailHoldColor : TrailColor ?? TrailDrainColor);
            }
        }

        /// <summary>Show a value straight away (no trail), e.g. on respawn.</summary>
        public void Snap(float frac)
        {
            _value = _trail = Mathf.Clamp01(frac);
            _hold = 0f;
            Set(frac, 0f);
        }

        /// <summary>A HUD number: bold italic legacy text with a hard drop shadow, placed like a bar.</summary>
        public static Text Label(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, int fontSize,
            TextAnchor align, FontStyle style = FontStyle.BoldAndItalic)
        {
            var rt = UiKit.Rect(name, parent, anchor, pivot, new Vector2(pos.x, -pos.y), size);
            var t = UiKit.Text(rt, "", fontSize, align, Color.white, style);
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.6f);
            s.effectDistance = new Vector2(0f, -2f);
            return t;
        }
    }
}
