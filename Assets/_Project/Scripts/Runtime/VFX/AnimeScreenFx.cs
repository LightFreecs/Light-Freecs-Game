using LightMMORPG.Player;
using UnityEngine;
using UnityEngine.UI;

namespace LightMMORPG.VFX
{
    /// <summary>
    /// Screen-space anime cues for the local player (client only, no gameplay effect): radial speed lines while dashing,
    /// lunging or being sent flying (and in bursts on big blows), an impact flash on heavy hits, and the sword skill's name
    /// called out when it fires.
    /// </summary>
    public sealed class AnimeScreenFx : MonoBehaviour
    {
        private static AnimeScreenFx _i;
        private RectTransform _linesRect;
        private RawImage _lines;
        private Image _flash;
        private Text _callout;
        private float _burst, _burstDecay = 3f, _level, _flashA, _calloutAt = -10f, _nextJitter;

        private static AnimeScreenFx I
        {
            get
            {
                if (_i != null) return _i;
                var go = new GameObject("AnimeScreenFx");
                DontDestroyOnLoad(go);
                _i = go.AddComponent<AnimeScreenFx>();
                _i.Build();
                return _i;
            }
        }

        /// <summary>Speed lines at <paramref name="strength"/> (0..1) fading out over <paramref name="seconds"/>.</summary>
        public static void SpeedBurst(float strength, float seconds)
        {
            var i = I;
            i._burst = Mathf.Max(i._burst, strength);
            i._burstDecay = strength / Mathf.Max(0.05f, seconds);
        }

        /// <summary>A white impact frame (0..1 opacity) that drops away within a few frames.</summary>
        public static void ImpactFrame(float strength) => I._flashA = Mathf.Max(I._flashA, strength);

        /// <summary>Big shout of a move's name across the top of the screen.</summary>
        public static void Callout(string text, Color glow)
        {
            var i = I;
            i._callout.text = text.ToUpperInvariant();
            float m = Mathf.Max(1f, Mathf.Max(glow.r, Mathf.Max(glow.g, glow.b)));
            i._callout.color = new Color(glow.r / m, glow.g / m, glow.b / m, 1f);
            i._calloutAt = Time.unscaledTime;
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -20;   // under the HUD and menus

            var lines = new GameObject("SpeedLines", typeof(RectTransform)).GetComponent<RectTransform>();
            lines.SetParent(transform, false);
            lines.anchorMin = lines.anchorMax = new Vector2(0.5f, 0.5f);
            _lines = lines.gameObject.AddComponent<RawImage>();
            _lines.texture = MakeLines();
            _lines.raycastTarget = false;
            _linesRect = lines;

            var flash = new GameObject("ImpactFrame", typeof(RectTransform)).GetComponent<RectTransform>();
            flash.SetParent(transform, false);
            flash.anchorMin = Vector2.zero;
            flash.anchorMax = Vector2.one;
            flash.offsetMin = flash.offsetMax = Vector2.zero;
            _flash = flash.gameObject.AddComponent<Image>();
            _flash.raycastTarget = false;

            var call = new GameObject("Callout", typeof(RectTransform)).GetComponent<RectTransform>();
            call.SetParent(transform, false);
            call.anchorMin = call.anchorMax = new Vector2(0.5f, 0.8f);
            call.sizeDelta = new Vector2(1400f, 120f);
            _callout = call.gameObject.AddComponent<Text>();
            _callout.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _callout.fontSize = 64;
            _callout.fontStyle = FontStyle.BoldAndItalic;
            _callout.alignment = TextAnchor.MiddleCenter;
            _callout.raycastTarget = false;
            var outline = call.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(3f, -3f);
            Apply();
        }

        /// <summary>Radial streaks, clear in the middle (white; tinted by the image colour).</summary>
        private static Texture2D MakeLines()
        {
            const int S = 256, Rays = 160;
            var rnd = new System.Random(7);
            var width = new float[Rays];
            var start = new float[Rays];
            for (int r = 0; r < Rays; r++)
            {
                width[r] = rnd.NextDouble() < 0.5 ? (float)(0.2 + rnd.NextDouble() * 0.5) : 0f;
                start[r] = (float)(0.45 + rnd.NextDouble() * 0.35);
            }
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                float rad = Mathf.Sqrt(u * u + v * v);
                float ang = (Mathf.Atan2(v, u) / (2f * Mathf.PI) + 0.5f) * Rays;
                int ray = (int)ang % Rays;
                float w = width[ray];
                float line = w > 0f ? Mathf.Clamp01(1f - Mathf.Abs(ang - Mathf.Floor(ang) - 0.5f) * 2f / w) : 0f;
                float a = line * Mathf.Clamp01((rad - start[ray]) / 0.25f);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SpeedLines" };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var me = PlayerMotor.Local;
            float moving = 0f;
            if (me != null)
                moving = me.IsDashing || me.IsLunging ? 0.55f : Mathf.InverseLerp(13f, 26f, me.Velocity.magnitude) * 0.5f;
            _burst = Mathf.MoveTowards(_burst, 0f, _burstDecay * dt);
            _level = Mathf.MoveTowards(_level, Mathf.Max(moving, _burst), dt * 7f);
            _flashA = Mathf.MoveTowards(_flashA, 0f, dt * 6f);
            Apply();
        }

        private void Apply()
        {
            _lines.color = new Color(1f, 1f, 1f, _level * 0.6f);
            _lines.enabled = _level > 0.01f;
            if (_lines.enabled && Time.unscaledTime >= _nextJitter)
            {
                // anime speed lines flicker: a new random turn every few frames
                _nextJitter = Time.unscaledTime + 0.05f;
                float size = Mathf.Max(Screen.width, Screen.height) * 1.5f;
                _linesRect.sizeDelta = new Vector2(size, size);
                _linesRect.localEulerAngles = new Vector3(0f, 0f, Random.Range(0f, 360f));
            }
            _flash.color = new Color(1f, 1f, 1f, Mathf.Min(0.5f, _flashA));
            _flash.enabled = _flashA > 0.01f;
            float t = Time.unscaledTime - _calloutAt;
            _callout.enabled = t < 1f;
            if (_callout.enabled)
            {
                var c = _callout.color;
                c.a = Mathf.Clamp01((1f - t) / 0.3f);
                _callout.color = c;
                _callout.rectTransform.localScale = Vector3.one * (1f + 0.5f * Mathf.Clamp01(1f - t / 0.12f));
            }
        }
    }
}
