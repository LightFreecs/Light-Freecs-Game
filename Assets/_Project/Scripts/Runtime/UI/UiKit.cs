using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace LightMMORPG.UI
{
    /// <summary>Tiny runtime-uGUI helpers for the HUD and touch overlay (legacy Text, no sprites needed).</summary>
    public static class UiKit
    {
        /// <summary>Unity's built-in legacy runtime font (HUD, touch overlay). The main menu uses the fonts in MenuTheme.</summary>
        public static Font UIFont => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        private static Font _font;

        /// <summary>Creates an EventSystem with the Input System UI module if the scene has none.</summary>
        public static EventSystem EnsureEventSystem()
        {
            var es = EventSystem.current != null ? EventSystem.current : Object.FindAnyObjectByType<EventSystem>();
            if (es != null) return es;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            return go.GetComponent<EventSystem>();
        }

        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return c;
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        public static Image Image(RectTransform rt, Color color, bool raycast = false)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static Text Text(Transform parent, string text, int size, TextAnchor align, Color color, FontStyle style = FontStyle.Bold)
        {
            var rt = Rect("Text", parent, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Stretch(rt);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = UIFont;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.8f);
            sh.effectDistance = new Vector2(2f, -2f);
            return t;
        }

        /// <summary>Horizontal bar: dark background + coloured fill whose width is set with <see cref="Bar.Set"/>.</summary>
        public sealed class Bar
        {
            public RectTransform Root;
            public RectTransform Fill;
            public Image FillImage;
            public Text Label;

            public void Set(float fraction01)
            {
                Fill.anchorMax = new Vector2(Mathf.Clamp01(fraction01), 1f);
            }
        }

        public static Bar CreateBar(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color fill, int labelSize = 0)
        {
            var root = Rect(name, parent, anchor, pivot, pos, size);
            Image(root, new Color(0f, 0f, 0f, 0.6f));
            var inner = Rect("Inner", root, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            inner.anchorMin = Vector2.zero;
            inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(3f, 3f);
            inner.offsetMax = new Vector2(-3f, -3f);
            var f = Rect("Fill", inner, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            f.anchorMin = Vector2.zero;
            f.anchorMax = Vector2.one;
            f.offsetMin = f.offsetMax = Vector2.zero;
            var bar = new Bar { Root = root, Fill = f, FillImage = Image(f, fill) };
            if (labelSize > 0) bar.Label = Text(root, "", labelSize, TextAnchor.MiddleCenter, Color.white);
            return bar;
        }
    }
}
