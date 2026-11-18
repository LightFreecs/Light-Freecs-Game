using System;
using System.Collections.Generic;
using LightMMORPG.Characters;
using LightMMORPG.Networking;
using LightMMORPG.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LightMMORPG.Menu
{
    /// <summary>
    /// Runtime-built uGUI for the "Dusk Isle" start menu (design-bench pick): title state ("press any key / tap to
    /// start"), the Horizon Ribbon main menu (text items on a thin glowing horizon line, Play Online glowing like a
    /// small sun), player card + currency (top-left), Friends / Settings / Quit icons (top-right), patch news (right),
    /// toasts, the Play Online dialog (Quick Match placeholder, Host, Join by IP, Server Only), Settings (sky picker,
    /// graphics, frame rate, look, key bindings; see MainMenuUI.Settings.cs), the Controls (rebinding) dialog and
    /// Customize (Fighters item: eye style + Toon Head sliders on the right, the camera closes in on the face).
    /// All sizes are 1920×1080 reference pixels from <see cref="MenuTheme"/>; the canvas is Screen Space – Camera on the
    /// menu camera (so screenshots of a RenderTexture include it) and the content sits inside the safe area.
    /// Keyboard / gamepad navigate with explicit Selectable navigation; mouse hover selects; touch taps.
    /// </summary>
    public sealed partial class MainMenuUI : MonoBehaviour
    {
        public enum State { Title, Main, PlayOnline, Settings, Controls, Account, Results, Customize, Stats }

        /// <summary>Screenshots: safe area in camera pixels (null = Screen.safeArea).</summary>
        public static Rect? SafeAreaOverride;
        /// <summary>Screenshots: lay out as on a touch device ("TAP TO START", no Quit).</summary>
        public static bool ForceTouchLayout;

        public State Current { get; private set; } = State.Title;
        public bool TouchLayout => ForceTouchLayout || Application.isMobilePlatform || Input.PlayerInputReader.ForceTouchUI;

        private const string IpPref = "net.lastIp";

        private MenuTheme T;
        private MenuSky _sky;
        private Camera _cam;
        private Canvas _canvas;
        private CanvasScaler _scaler;
        private RectTransform _safe;
        private CanvasGroup _hud, _press, _backdrop, _playDlg, _settingsDlg, _custDlg, _logo, _toast;
        private MenuFighter _fighter;
        private FaceCustomization _face;
        private readonly List<(Slider s, Text label, Text value, Image handle, int index)> _faceSliders = new();
        private readonly List<GlowSelectable> _eyeButtons = new();
        private GlowSelectable _custDone;
        private Text _custStatus;
        private readonly List<Action<SkyPreset>> _recolor = new();
        private readonly List<GlowSelectable> _ribbon = new();
        private readonly List<GlowSelectable> _icons = new();
        private readonly List<GlowSelectable> _all = new();
        private readonly List<(GlowSelectable b, SkyId id)> _skyButtons = new();
        private GlowSelectable _playItem, _hostButton, _joinButton;
        private GameObject _lastMainSelection;
        private InputField _ip;
        private Text _toastText, _pressText;
        private RectTransform _toastRect, _sunDot, _iconRow;
        private GlowSelectable _quitIcon;
        private float _toastUntil = -1f;
        private bool _built;
        private Rect _lastSafe;
        private Vector2 _lastSize;

        public void Init(MenuTheme theme, MenuSky sky, Camera cam, MenuFighter fighter = null)
        {
            T = theme;
            _fighter = fighter;
            _sky = sky;
            _cam = cam;
            UiKit.EnsureEventSystem();
            Build();
            _sky.Changed += Recolor;
            Recolor(_sky.Preset);
            Input.PlayerInputReader.LockCursor(false);

            bool back = NetLaunch.LastError != null || NetLaunch.ReturnedFromArena;
            NetLaunch.ReturnedFromArena = false;
            SetState(back ? State.Main : State.Title, instant: true);
            if (back) ShowResults(Accounts.AccountStore.Instance.TakeLastMatch());
            if (NetLaunch.LastError != null)
            {
                ShowToast(NetLaunch.LastError);
                NetLaunch.LastError = null;
            }
        }

        private void OnDestroy()
        {
            OnDestroyAccount();
            if (_sky != null) _sky.Changed -= Recolor;
        }

        // ================================================================= build

        private void Bind(Action<SkyPreset> f) => _recolor.Add(f);

        private void Recolor(SkyPreset p)
        {
            if (p == null) return;
            foreach (var f in _recolor) f(p);
            foreach (var s in _all) s.Snap();
            foreach (var (b, id) in _skyButtons) b.OnGlow?.Invoke(b.Glow);
        }

        private static Color A(Color c, float a) { c.a *= a; return c; }

        private void Build()
        {
            var cgo = new GameObject("MenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            cgo.layer = 5;
            _canvas = cgo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = _cam;
            _canvas.planeDistance = 1f;
            _canvas.pixelPerfect = false;
            _scaler = cgo.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1920f, 1080f);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = 1f;

            _safe = MenuUi.Rect("SafeArea", cgo.transform).Fill();

            BuildLogo();
            BuildPress();

            // soft sky-tinted shade behind the ribbon / "press any key" so text reads over bright clouds
            var scrim = MenuUi.Img(_safe, "Scrim", MenuUi.VerticalFade(), Color.white);
            scrim.transform.SetAsFirstSibling();
            scrim.rectTransform.anchorMin = new Vector2(0f, 0f);
            scrim.rectTransform.anchorMax = new Vector2(1f, T.scrimHeight);
            scrim.rectTransform.offsetMin = new Vector2(-600f, -200f);
            scrim.rectTransform.offsetMax = new Vector2(600f, 0f);
            Bind(p => { var c = p.textShadow; c.a = T.scrimAlpha; scrim.color = c; });
            _hud = Group("Hud", _safe);
            BuildPlayerCard(_hud.transform);
            BuildCurrency(_hud.transform);
            BuildIcons(_hud.transform);
            BuildNews(_hud.transform);
            BuildRibbon(_hud.transform);

            // dim backdrop behind dialogs; extends past the safe area to cover the whole screen
            _backdrop = Group("Backdrop", _safe);
            ((RectTransform)_backdrop.transform).Fill(-600f);
            var bd = _backdrop.gameObject.AddComponent<Image>();
            bd.color = new Color(0f, 0f, 0f, 0.5f);
            var dialogs = MenuUi.Rect("Dialogs", _safe).Fill();
            BuildPlayDialog(dialogs);
            BuildSettingsDialog(dialogs);
            BuildControlsDialog(dialogs);
            BuildCustomizeDialog(dialogs);
            BuildAccountDialog(dialogs);
            BuildResultsDialog(dialogs);
            BuildStatsDialog(dialogs);
            BuildToast(_safe);
            WireRibbonNavigation();
            _built = true;
        }

        private static CanvasGroup Group(string name, Transform parent)
        {
            var rt = MenuUi.Rect(name, parent).Fill();
            return rt.gameObject.AddComponent<CanvasGroup>();
        }

        private Text Label(Transform parent, string name, string text, Font font, int size, Func<SkyPreset, Color> color, TextAnchor align = TextAnchor.MiddleCenter, float spacing = 0f, bool shadow = true)
        {
            var t = MenuUi.Label(parent, name, text, font, size, Color.white, align, spacing);
            Bind(p => t.color = color(p));
            if (shadow)
            {
                var s = MenuUi.AddShadow(t, Color.black, new Vector2(0f, -2f));
                Bind(p => s.effectColor = p.textShadow);
            }
            return t;
        }

        private Image Panel(Transform parent, string name, Vector2 size, int radius, Func<SkyPreset, Color> fill, Func<SkyPreset, Color> border, float stroke = 2f)
        {
            var bg = MenuUi.Img(parent, name, MenuUi.RoundedRect(radius), Color.white, true);
            bg.rectTransform.sizeDelta = size;
            Bind(p => bg.color = fill(p));
            if (border != null)
            {
                var b = MenuUi.Img(bg.transform, "Border", MenuUi.RoundedRectOutline(radius, stroke), Color.white, true);
                b.rectTransform.Fill();
                Bind(p => b.color = border(p));
            }
            return bg;
        }

        private void BuildLogo()
        {
            var logo = MenuUi.Rect("Logo", _safe).Place(new Vector2(0.5f, 1f - T.titleTop), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1200f, 220f));
            _logo = logo.gameObject.AddComponent<CanvasGroup>();
            _logo.blocksRaycasts = false;
            _logo.interactable = false;
            var glow = MenuUi.Img(logo, "Glow", MenuUi.SoftCircle(), Color.white);
            glow.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(1100f, 330f));
            Bind(p => glow.color = A(p.titleGlow, 0.45f));

            var small = Label(logo, "Project", T.titleSmall, T.lightFont, T.titleSmallSize, p => p.titleText, spacing: T.titleSmallSpacing);
            small.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(T.titleSmallSize * T.titleSmallSpacing * 0.5f, -T.titleSmallSize * 0.55f), new Vector2(900f, T.titleSmallSize * 1.4f));

            var big = MenuUi.Label(logo, "Light", T.titleBig, T.titleFont, T.titleBigSize, Color.white, TextAnchor.MiddleCenter, T.titleBigSpacing);
            big.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(T.titleBigSize * T.titleBigSpacing * 0.5f, -T.titleSmallSize * 1.1f - T.titleBigSize * 0.5f), new Vector2(1200f, T.titleBigSize * 1.2f));
            var drop = MenuUi.AddShadow(big, Color.black, new Vector2(0f, -5f));
            Bind(p => drop.effectColor = p.titleShadow);

            float tagY = -T.titleSmallSize * 1.1f - T.titleBigSize * 1.02f - T.titleTagSize * 0.5f;
            var tag = Label(logo, "Tag", T.titleTag, T.boldFont, T.titleTagSize, p => p.titleText, spacing: T.titleTagSpacing);
            tag.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, tagY), new Vector2(900f, T.titleTagSize * 1.4f));
            float half = MenuUi.Width(tag) * 0.5f + 24f;
            for (int s = -1; s <= 1; s += 2)
            {
                var line = MenuUi.Img(logo, "TagLine", null, Color.white);
                line.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(s < 0 ? 1f : 0f, 0.5f), new Vector2(s * half, tagY), new Vector2(T.titleTagLineWidth, 2f));
                Bind(p => line.color = A(p.titleText, 0.7f));
            }
        }

        private void BuildPress()
        {
            _press = Group("Press", _safe);
            _press.blocksRaycasts = false;
            _pressText = Label(_press.transform, "PressText", T.pressAnyKey, T.lightFont, T.pressSize, _ => Color.white, spacing: T.pressSpacing);
            _pressText.rectTransform.Place(new Vector2(0.5f, T.pressBottom), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 60f));
            var glow = _pressText.gameObject.AddComponent<Outline>();
            glow.effectDistance = new Vector2(2f, 2f);
            Bind(p => glow.effectColor = A(p.sunDotGlow, 0.35f));
        }

        private void BuildPlayerCard(Transform parent)
        {
            var card = Panel(parent, "PlayerCard", new Vector2(360f, 100f), 32, p => p.panel, p => p.panelBorder);
            card.rectTransform.Place(new Vector2(T.edgeMargin, 1f - T.cornerTop), new Vector2(0f, 1f), Vector2.zero, card.rectTransform.sizeDelta);
            var av = MenuUi.Img(card.transform, "Avatar", MenuUi.GradientAvatar(new Color(0.48f, 0.78f, 1f), new Color(0.26f, 0.16f, 0.42f)), Color.white);
            av.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(76f, 76f));
            var initial = MenuUi.Label(av.transform, "Initial", "?", T.titleFont, 32, Color.white);
            initial.rectTransform.Fill();
            var name = Label(card.transform, "Name", T.playerName, T.boldFont, 28, _ => Color.white, TextAnchor.MiddleLeft);
            name.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(104f, 16f), new Vector2(240f, 34f));
            var sub = Label(card.transform, "Sub", "Lv " + T.playerLevel + " · " + T.playerRank, T.boldFont, 22, p => p.cardSub, TextAnchor.MiddleLeft);
            sub.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(104f, -18f), new Vector2(240f, 28f));
            float w = 104f + Mathf.Max(MenuUi.Width(name), MenuUi.Width(sub)) + 30f;
            card.rectTransform.sizeDelta = new Vector2(Mathf.Max(260f, w), 100f);
            MakeAccountCard(card, initial, name, sub); // account name, level and XP (MainMenuUI.Account.cs)
        }

        private void BuildCurrency(Transform parent)
        {
            var row = MenuUi.Rect("Currency", parent).Place(new Vector2(T.edgeMargin, 1f - T.cornerTop), new Vector2(0f, 1f), new Vector2(0f, -120f), new Vector2(600f, 46f));
            float x = 0f;
            (MenuUi.IconKind icon, string value, Color tint)[] items =
            {
                (MenuUi.IconKind.Diamond, T.shards, new Color(0.55f, 0.85f, 1f)),
                (MenuUi.IconKind.Crystal, T.crystals, new Color(1f, 0.85f, 0.45f)),
            };
            foreach (var it in items)
            {
                var pill = Panel(row, "Pill", new Vector2(100f, 46f), 23, p => p.panel, null);
                var ic = MenuUi.Img(pill.transform, "Icon", MenuUi.Icon(it.icon), it.tint);
                ic.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(26f, 26f));
                var t = Label(pill.transform, "Value", it.value, T.boldFont, 26, _ => Color.white, TextAnchor.MiddleLeft);
                t.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(52f, 0f), new Vector2(200f, 40f));
                float w = 52f + MenuUi.Width(t) + 22f;
                pill.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, 0f), new Vector2(w, 46f));
                x += w + 16f;
            }
        }

        private void BuildIcons(Transform parent)
        {
            var row = MenuUi.Rect("IconButtons", parent).Place(new Vector2(1f - T.edgeMargin, 1f - T.cornerTop), new Vector2(1f, 1f), Vector2.zero, new Vector2(400f, T.iconButtonSize));
            _iconRow = row;
            var defs = new List<(string id, MenuUi.IconKind k, string label)>
            {
                ("friends", MenuUi.IconKind.Friends, "Friends"), ("settings", MenuUi.IconKind.Settings, "Settings"), ("quit", MenuUi.IconKind.Quit, "Quit"),
            };
            float s = T.iconButtonSize;
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                float x = -(defs.Count - 1 - i) * (s + 16f);
                var bg = Panel(row, "Icon_" + d.id, new Vector2(s, s), Mathf.RoundToInt(s * 0.5f), p => p.panel, p => p.panelBorder);
                bg.rectTransform.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(x, 0f), new Vector2(s, s));
                bg.raycastTarget = true;
                var icon = MenuUi.Img(bg.transform, "Glyph", MenuUi.Icon(d.k), Color.white);
                icon.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(s * 0.5f, s * 0.5f));
                var tip = Label(bg.transform, "Tip", d.label.ToUpperInvariant(), T.boldFont, 18, _ => Color.white, spacing: 0.2f);
                tip.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(200f, 24f));
                var sel = AddSelectable(bg.gameObject);
                string id = d.id, label = d.label;
                sel.OnClick = () => Activate(id, label);
                var bgImg = bg;
                sel.OnGlow = g =>
                {
                    var p = _sky.Preset;
                    bgImg.color = Color.Lerp(p.panel, A(p.accent, 0.55f), g);
                    bgImg.rectTransform.localScale = Vector3.one * (1f + 0.06f * g);
                    tip.color = A(Color.white, g);
                };
                _icons.Add(sel);
                if (id == "quit") _quitIcon = sel;
            }
        }

        /// <summary>iOS apps never quit themselves: hide Quit on touch layouts and close the gap.</summary>
        private void UpdateQuitIcon()
        {
            bool show = !TouchLayout;
            if (_quitIcon == null || _quitIcon.gameObject.activeSelf == show) return;
            _quitIcon.gameObject.SetActive(show);
            _iconRow.anchoredPosition = new Vector2(show ? 0f : T.iconButtonSize + 16f, 0f);
            var settings = _icons[1];
            var n = settings.navigation;
            n.selectOnRight = show ? _quitIcon : null;
            settings.navigation = n;
        }

        private void BuildNews(Transform parent)
        {
            float w = T.newsWidth;
            var card = Panel(parent, "News", new Vector2(w, 160f), 32, p => p.panel, p => p.panelBorder);
            var head = Label(card.transform, "Head", T.newsTitle, T.boldFont, 22, p => p.newsHeader, TextAnchor.UpperLeft, 0.2f, shadow: false);
            head.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -22f), new Vector2(w - 48f, 28f));
            var body = Label(card.transform, "Body", T.newsBody, T.bodyFont, 24, _ => Color.white, TextAnchor.UpperLeft, shadow: false);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.lineSpacing = 1.05f;
            body.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -58f), new Vector2(w - 48f, 120f));
            var gen = body.cachedTextGeneratorForLayout;
            var settings = body.GetGenerationSettings(new Vector2(w - 48f, 0f));
            float h = gen.GetPreferredHeight(T.newsBody, settings) / body.pixelsPerUnit;
            body.rectTransform.sizeDelta = new Vector2(w - 48f, h + 4f);
            card.rectTransform.Place(new Vector2(1f - T.edgeMargin, 1f - T.newsTop), new Vector2(1f, 1f), Vector2.zero, new Vector2(w, 58f + h + 26f));
        }

        private GlowSelectable AddSelectable(GameObject go)
        {
            var g = go.GetComponent<Graphic>();
            if (g == null)
            {
                var img = go.AddComponent<Image>();
                img.color = Color.clear;
                g = img;
            }
            g.raycastTarget = true;
            var s = go.AddComponent<GlowSelectable>();
            s.transition = Selectable.Transition.None;
            s.targetGraphic = g;
            s.speed = T.glowSpeed;
            var nav = s.navigation;
            nav.mode = Navigation.Mode.Explicit;
            s.navigation = nav;
            _all.Add(s);
            return s;
        }

        private void BuildRibbon(Transform parent)
        {
            var ribbon = MenuUi.Rect("HorizonRibbon", parent);
            ribbon.anchorMin = new Vector2(T.ribbonInset, T.ribbonBottom);
            ribbon.anchorMax = new Vector2(1f - T.ribbonInset, T.ribbonBottom);
            ribbon.pivot = new Vector2(0.5f, 0f);
            ribbon.anchoredPosition = Vector2.zero;
            ribbon.sizeDelta = new Vector2(0f, 150f);

            float subH = T.primarySubSize * 1.3f;
            float primaryH = T.primarySize * 1.15f;
            float lineY = subH + primaryH + 28f;
            float primaryCy = subH + primaryH * 0.5f;
            float baseline = primaryCy - T.primarySize * 0.32f;
            float itemCy = baseline + T.itemSize * 0.32f;

            var glowLine = MenuUi.Img(ribbon, "LineGlow", MenuUi.FadeLine(), Color.white);
            glowLine.rectTransform.anchorMin = new Vector2(0f, 0f);
            glowLine.rectTransform.anchorMax = new Vector2(1f, 0f);
            glowLine.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            glowLine.rectTransform.anchoredPosition = new Vector2(0f, lineY);
            glowLine.rectTransform.sizeDelta = new Vector2(0f, 12f);
            Bind(p => glowLine.color = A(p.accent, 0.18f));
            var line = MenuUi.Img(ribbon, "Line", MenuUi.FadeLine(), Color.white);
            line.rectTransform.anchorMin = new Vector2(0f, 0f);
            line.rectTransform.anchorMax = new Vector2(1f, 0f);
            line.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            line.rectTransform.anchoredPosition = new Vector2(0f, lineY);
            line.rectTransform.sizeDelta = new Vector2(0f, T.lineThickness);
            Bind(p => line.color = A(Color.white, T.lineAlpha));

            // measure
            var items = T.ribbonItems;
            var labels = new Text[items.Length];
            var widths = new float[items.Length];
            var subs = new Text[items.Length];
            float total = 0f;
            var row = MenuUi.Rect("Items", ribbon).Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(10f, 10f));
            for (int i = 0; i < items.Length; i++)
            {
                bool primary = items[i].id == "play";
                var item = MenuUi.Rect("Item_" + items[i].id, row);
                var t = Label(item, "Label", items[i].label.ToUpperInvariant(), primary ? T.titleFont : T.boldFont, primary ? T.primarySize : T.itemSize,
                    _ => Color.white, spacing: primary ? T.primarySpacing : T.itemSpacing);
                labels[i] = t;
                widths[i] = MenuUi.Width(t);
                if (primary && !string.IsNullOrEmpty(items[i].sub))
                {
                    subs[i] = Label(item, "Sub", items[i].sub, T.bodyFont, T.primarySubSize, _ => A(Color.white, 0.85f), spacing: 0.1f);
                    widths[i] = Mathf.Max(widths[i], MenuUi.Width(subs[i]));
                }
                total += widths[i];
            }
            total += T.itemGap * (items.Length - 1);

            float x = -total * 0.5f;
            for (int i = 0; i < items.Length; i++)
            {
                var def = items[i];
                bool primary = def.id == "play";
                var item = (RectTransform)labels[i].transform.parent;
                float w = widths[i];
                float cy = primary ? primaryCy : itemCy;
                item.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(x + w * 0.5f, primary ? (subH + primaryH) * 0.5f + 6f : itemCy), new Vector2(w + T.itemGap * 0.8f, primary ? subH + primaryH + 24f : T.itemSize * 2.6f));
                float localCy = primary ? cy - item.anchoredPosition.y : 0f;
                x += w + T.itemGap;

                var t = labels[i];
                float lw = MenuUi.Width(t);
                t.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, localCy), new Vector2(lw + 40f, t.fontSize * 1.3f));
                if (subs[i] != null)
                    subs[i].rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, localCy - primaryH * 0.5f - subH * 0.6f), new Vector2(w + 40f, subH));

                // focus visuals: soft glow behind, accent underline that grows from the centre, text glow
                var halo = MenuUi.Img(item, "Halo", MenuUi.SoftCircle(), Color.clear);
                halo.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, localCy), new Vector2(lw * 1.5f + 80f, t.fontSize * 3f));
                halo.transform.SetAsFirstSibling();
                var under = MenuUi.Img(item, "Underline", MenuUi.FadeLine(), Color.white);
                under.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, localCy - t.fontSize * 0.62f - 6f), new Vector2(lw + 10f, T.underlineThickness));
                var textGlow = t.gameObject.AddComponent<Outline>();
                textGlow.effectDistance = new Vector2(2f, 2f);

                RectTransform sunCore = null;
                Image sunHalo = null, sunImg = null;
                if (primary)
                {
                    var dot = MenuUi.Rect("Sun", ribbon).Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(item.anchoredPosition.x, lineY + T.sunDotSize * 0.75f), new Vector2(T.sunDotSize, T.sunDotSize));
                    sunHalo = MenuUi.Img(dot, "Halo", MenuUi.SoftCircle(), Color.white);
                    sunHalo.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * T.sunDotSize * 5f);
                    sunImg = MenuUi.Img(dot, "Core", MenuUi.Disc(), Color.white);
                    sunImg.rectTransform.Fill();
                    sunCore = dot;
                    _sunDot = dot;
                }

                var sel = AddSelectable(item.gameObject);
                string id = def.id, label = def.label;
                sel.OnClick = () => Activate(id, label);
                float idle = T.idleItemAlpha;
                var tt = t;
                sel.OnGlow = g =>
                {
                    var p = _sky.Preset;
                    tt.color = A(Color.white, primary ? 1f : Mathf.Lerp(idle, 1f, g));
                    textGlow.effectColor = A(p.accent, primary ? 0.35f + 0.3f * g : 0.45f * g);
                    halo.color = A(p.accent, (primary ? 0.16f : 0f) + 0.22f * g);
                    under.color = A(p.accent, g);
                    under.rectTransform.localScale = new Vector3(g, 1f, 1f);
                    if (sunImg != null)
                    {
                        sunImg.color = p.sunDot;
                        sunHalo.color = A(p.sunDotGlow, 0.75f + 0.25f * g);
                        sunCore.localScale = Vector3.one * (1f + 0.25f * g);
                    }
                };
                _ribbon.Add(sel);
                if (primary) _playItem = sel;
            }
        }

        private void WireRibbonNavigation()
        {
            for (int i = 0; i < _ribbon.Count; i++)
            {
                var nav = _ribbon[i].navigation;
                nav.selectOnLeft = i > 0 ? _ribbon[i - 1] : _ribbon[_ribbon.Count - 1];
                nav.selectOnRight = i < _ribbon.Count - 1 ? _ribbon[i + 1] : _ribbon[0];
                nav.selectOnUp = _icons.Count > 0 ? _icons[0] : null;
                _ribbon[i].navigation = nav;
            }
            for (int i = 0; i < _icons.Count; i++)
            {
                var nav = _icons[i].navigation;
                nav.selectOnLeft = i > 0 ? _icons[i - 1] : null;
                nav.selectOnRight = i < _icons.Count - 1 ? _icons[i + 1] : null;
                nav.selectOnDown = _playItem;
                _icons[i].navigation = nav;
            }
        }

        // ---------------- dialogs ----------------

        private RectTransform Dialog(Transform parent, string name, out CanvasGroup group, float height)
        {
            group = Group(name, parent);
            var panel = Panel(group.transform, "Panel", new Vector2(T.subPanelWidth, height), 36, p => p.dialog, p => p.accent, 3f);
            panel.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(T.subPanelWidth, height));
            panel.raycastTarget = true;
            var glow = MenuUi.Img(group.transform, "Glow", MenuUi.SoftCircle(), Color.white);
            glow.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(T.subPanelWidth * 1.6f, height * 1.6f));
            glow.transform.SetAsFirstSibling();
            Bind(p => glow.color = A(p.sunDotGlow, 0.22f));
            return panel.rectTransform;
        }

        private GlowSelectable DialogButton(RectTransform panel, string label, string sub, float y, float width, float height, Action onClick, float x = 0f, bool centred = false)
        {
            var bg = Panel(panel, "Button_" + label, new Vector2(width, height), 22, _ => Color.white, p => p.accent, 2f);
            bg.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, y), new Vector2(width, height));
            var t = MenuUi.Label(bg.transform, "Label", label, T.boldFont, 30, Color.white, centred ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
            Text s = null;
            if (!string.IsNullOrEmpty(sub))
            {
                t.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, 14f), new Vector2(width - 56f, 38f));
                s = MenuUi.Label(bg.transform, "Sub", sub, T.bodyFont, 22, Color.white, TextAnchor.MiddleLeft);
                s.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, -18f), new Vector2(width - 56f, 30f));
            }
            else if (centred) t.rectTransform.Fill();
            else t.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(width - 56f, 40f));
            var sel = AddSelectable(bg.gameObject);
            sel.OnClick = onClick;
            sel.OnGlow = g =>
            {
                var p = _sky.Preset;
                bg.color = Color.Lerp(new Color(1f, 1f, 1f, 0.08f), p.accent, g);
                var dark = p.dialog; dark.a = 1f;
                t.color = Color.Lerp(Color.white, dark, g);
                if (s != null) s.color = A(Color.Lerp(Color.white, dark, g), 0.75f);
            };
            return sel;
        }

        private GlowSelectable TextButton(RectTransform panel, string label, float y, Action onClick)
        {
            var rt = MenuUi.Rect("Back", panel).Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(240f, 56f));
            var t = MenuUi.Label(rt, "Label", label.ToUpperInvariant(), T.boldFont, 24, Color.white, TextAnchor.MiddleCenter, 0.2f);
            t.rectTransform.Fill();
            var under = MenuUi.Img(rt, "Underline", MenuUi.FadeLine(), Color.white);
            under.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -22f), new Vector2(MenuUi.Width(t) + 10f, 3f));
            var sel = AddSelectable(rt.gameObject);
            sel.OnClick = onClick;
            sel.OnGlow = g =>
            {
                t.color = A(Color.white, Mathf.Lerp(0.7f, 1f, g));
                under.color = A(_sky.Preset.accent, g);
                under.rectTransform.localScale = new Vector3(g, 1f, 1f);
            };
            return sel;
        }

        private void DialogHeader(RectTransform panel, string title, string blurb)
        {
            var h = MenuUi.Label(panel, "Title", title, T.lightFont, 56, Color.white, TextAnchor.MiddleLeft, 0.03f);
            h.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -36f), new Vector2(T.subPanelWidth - 80f, 64f));
            if (!string.IsNullOrEmpty(blurb))
            {
                var b = MenuUi.Label(panel, "Blurb", blurb, T.bodyFont, 26, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft);
                b.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -102f), new Vector2(T.subPanelWidth - 80f, 36f));
            }
        }

        private void BuildPlayDialog(RectTransform parent)
        {
            float w = T.subPanelWidth - 80f;
            var panel = Dialog(parent, "PlayOnline", out _playDlg, 800f);
            DialogHeader(panel, "Play Online", T.playOnlineBlurb);
            float y = -160f;
            var quick = DialogButton(panel, "Quick Match", "Find an open arena", y, w, 100f, () => ShowToast("Quick Match · " + T.comingSoon));
            y -= 116f;
            _hostButton = DialogButton(panel, "Host Match", "Others join with your IP · port " + NetworkBootstrap.DefaultPort, y, w, 100f, () => Launch(NetMode.Host));
            y -= 116f;

            // IP field + Join
            float joinW = 190f, ipW = w - joinW - 16f;
            var field = Panel(panel, "IpField", new Vector2(ipW, 92f), 22, _ => new Color(0f, 0f, 0f, 0.28f), p => p.accent, 2f);
            field.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-(w - ipW) * 0.5f, y), new Vector2(ipW, 92f));
            field.raycastTarget = true;
            var ipText = MenuUi.Label(field.transform, "Text", "", T.bodyFont, 32, Color.white, TextAnchor.MiddleLeft);
            ipText.rectTransform.Fill();
            ipText.rectTransform.offsetMin = new Vector2(26f, 0f);
            ipText.rectTransform.offsetMax = new Vector2(-26f, 0f);
            ipText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var placeholder = MenuUi.Label(field.transform, "Placeholder", "Host IP address", T.bodyFont, 30, new Color(1f, 1f, 1f, 0.4f), TextAnchor.MiddleLeft);
            placeholder.rectTransform.Fill();
            placeholder.rectTransform.offsetMin = new Vector2(26f, 0f);
            _ip = field.gameObject.AddComponent<InputField>();
            _ip.textComponent = ipText;
            _ip.placeholder = placeholder;
            _ip.contentType = InputField.ContentType.Standard;
            _ip.characterLimit = 64;
            _ip.transition = Selectable.Transition.None;
            _ip.text = PlayerPrefs.GetString(IpPref, NetLaunch.LastAddress);
            _ip.caretWidth = 3;
            _ip.customCaretColor = true;
            _ip.caretColor = Color.white;
            var ipNav = _ip.navigation;
            ipNav.mode = Navigation.Mode.Explicit;
            _ip.navigation = ipNav;
            Bind(p => _ip.selectionColor = A(p.accent, 0.45f));
            var fieldImg = field;
            _joinButton = DialogButton(panel, "Join", null, y, joinW, 92f, () => Launch(NetMode.Client), (w - joinW) * 0.5f, centred: true);
            var join = _joinButton;
            y -= 108f;
            var server = DialogButton(panel, "Server Only", "Dedicated server, no local fighter", y, w, 100f, () => Launch(NetMode.Server));
            y -= 124f;
            var back = TextButton(panel, "Back", y, () => SetState(State.Main));
            panel.sizeDelta = new Vector2(T.subPanelWidth, -y + 84f);

            Chain(quick, _hostButton);
            var n = _hostButton.navigation; n.selectOnDown = _ip; _hostButton.navigation = n;
            ipNav = _ip.navigation; ipNav.selectOnUp = _hostButton; ipNav.selectOnRight = join; ipNav.selectOnDown = server; _ip.navigation = ipNav;
            n = join.navigation; n.selectOnLeft = _ip; n.selectOnUp = _hostButton; n.selectOnDown = server; join.navigation = n;
            n = server.navigation; n.selectOnUp = _ip; n.selectOnDown = back; server.navigation = n;
            n = back.navigation; n.selectOnUp = server; back.navigation = n;
            n = quick.navigation; n.selectOnUp = back; quick.navigation = n;
            _ip.onSubmit.AddListener(_ => Launch(NetMode.Client));
            _playDlg.gameObject.AddComponent<IpFieldGlow>().Init(_ip, fieldImg);
        }

        private static void Chain(Selectable a, Selectable b)
        {
            var na = a.navigation; na.selectOnDown = b; a.navigation = na;
            var nb = b.navigation; nb.selectOnUp = a; b.navigation = nb;
        }

        private void BuildSettingsDialog(RectTransform parent)
        {
            float w = T.subPanelWidth - 80f;
            var panel = Dialog(parent, "Settings", out _settingsDlg, 700f);
            DialogHeader(panel, "Settings", null);
            var skyHead = MenuUi.Label(panel, "SkyHead", "SKY", T.boldFont, 22, Color.white, TextAnchor.MiddleLeft, 0.2f);
            skyHead.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -118f), new Vector2(300f, 30f));
            Bind(p => skyHead.color = p.newsHeader);
            float y = -152f, bw = (w - 32f) / 3f;
            GlowSelectable prev = null;
            for (int i = 0; i < 3; i++)
            {
                var id = (SkyId)i;
                var b = DialogButton(panel, T.GetSky(id).displayName, null, y, bw, 84f, () => { MenuSky.Saved = id; _sky.Apply(id); }, -w * 0.5f + bw * 0.5f + i * (bw + 16f), centred: true);
                var baseGlow = b.OnGlow;
                b.OnGlow = g => baseGlow(Mathf.Max(g, _sky.Current == id ? 0.75f : 0f));
                _skyButtons.Add((b, id));
                if (prev != null)
                {
                    var np = prev.navigation; np.selectOnRight = b; prev.navigation = np;
                    var nb = b.navigation; nb.selectOnLeft = prev; b.navigation = nb;
                }
                prev = b;
            }
            y -= 108f;
            y = BuildSettingRows(panel, y, w);
            y -= 12f;
            var back = TextButton(panel, "Back", y, () => SetState(State.Main));
            panel.sizeDelta = new Vector2(T.subPanelWidth, -y + 84f);
            var first = _settingRows[0];
            foreach (var (b, _) in _skyButtons)
            {
                var n = b.navigation; n.selectOnDown = first; b.navigation = n;
            }
            var nf = first.navigation; nf.selectOnUp = _skyButtons[0].b; first.navigation = nf;
            Chain(_settingRows[_settingRows.Count - 1], back);
        }

        // ---------------- customize (Fighters) ----------------

        private const int FaceRows = 8;

        private void BuildCustomizeDialog(RectTransform parent)
        {
            const float pw = 1040f;
            float w = pw - 80f;
            _custDlg = Group("Customize", parent);
            var panelImg = Panel(_custDlg.transform, "Panel", new Vector2(pw, 900f), 36, p => p.dialog, p => p.accent, 3f);
            panelImg.raycastTarget = true;
            var panel = panelImg.rectTransform;

            var h = MenuUi.Label(panel, "Title", "Customize", T.lightFont, 56, Color.white, TextAnchor.MiddleLeft, 0.03f);
            h.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -36f), new Vector2(w, 64f));
            _custStatus = MenuUi.Label(panel, "Blurb", "", T.bodyFont, 24, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleLeft);
            _custStatus.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -100f), new Vector2(w, 32f));

            // eyes: any style on the one hero
            float y = -140f;
            SectionHeader(panel, "EyesHead", "EYES", y);
            y -= 32f;
            const float eyeGap = 12f;
            int eyeCount = PlayerAppearance.EyeNames.Length;
            float ew = (w - eyeGap * (eyeCount - 1)) / eyeCount;
            for (int i = 0; i < eyeCount; i++)
            {
                var style = (ToonEyeStyle)i;
                float x = -w * 0.5f + ew * 0.5f + i * (ew + eyeGap);
                var b = DialogButton(panel, PlayerAppearance.EyeNames[i], null, y, ew, 56f, () => { _face.eyeStyle = style; RefreshEyeButtons(); FaceChanged(); }, x, centred: true);
                var glow = b.OnGlow;
                b.OnGlow = g => glow(Mathf.Max(g, _face.eyeStyle == style ? 0.5f : 0f));
                _eyeButtons.Add(b);
            }
            y -= 56f + 20f;

            // head sliders (the Toon Head bench sliders), three columns
            SectionHeader(panel, "FaceHead", "HEAD & FACE", y);
            y -= 30f;
            const float rowH = 70f, gap = 32f;
            float colW = (w - gap * 2f) / 3f;
            for (int i = 0; i < PlayerAppearance.Sliders.Length; i++)
            {
                int col = i / FaceRows, row = i % FaceRows;
                float x = -w * 0.5f + colW * 0.5f + col * (colW + gap);
                BuildFaceSlider(panel, i, x, y - row * rowH, colW);
            }
            y -= FaceRows * rowH + 10f;

            float hw = (w - 16f) * 0.5f;
            var reset = DialogButton(panel, "Reset", null, y, hw, 68f, () => { _face = default; SyncFaceControls(); FaceChanged(); }, -hw * 0.5f - 8f, centred: true);
            _custDone = DialogButton(panel, "Done", null, y, hw, 68f, CloseCustomize, hw * 0.5f + 8f, centred: true);
            y -= 68f + 32f;
            panel.Place(new Vector2(1f - T.edgeMargin, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(pw, -y));

            // navigation: eye row -> three slider columns -> Reset / Done (wrapping bottom -> top)
            int n = _faceSliders.Count;
            Selectable At(int i) => _faceSliders[Mathf.Clamp(i, 0, n - 1)].s;
            Selectable EyeFor(int col) => _eyeButtons[Mathf.Min(_eyeButtons.Count - 1, col == 0 ? 0 : col + 1)];
            Selectable BottomFor(int col) => col == 0 ? reset : _custDone;
            int ColTop(int col) => Mathf.Min(col * FaceRows, n - 1);
            for (int i = 0; i < _eyeButtons.Count; i++)
            {
                var nav = _eyeButtons[i].navigation;
                nav.mode = Navigation.Mode.Explicit;
                int col = i <= 1 ? 0 : i - 1;
                nav.selectOnLeft = i > 0 ? _eyeButtons[i - 1] : null;
                nav.selectOnRight = i < _eyeButtons.Count - 1 ? _eyeButtons[i + 1] : null;
                nav.selectOnDown = At(ColTop(col));
                nav.selectOnUp = BottomFor(col);
                _eyeButtons[i].navigation = nav;
            }
            for (int i = 0; i < n; i++)
            {
                int col = i / FaceRows, row = i % FaceRows;
                var nav = At(i).navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnUp = row > 0 ? At(i - 1) : EyeFor(col);
                nav.selectOnDown = row < FaceRows - 1 && i + 1 < n ? At(i + 1) : BottomFor(col);
                At(i).navigation = nav;
            }
            var nr = reset.navigation; nr.mode = Navigation.Mode.Explicit; nr.selectOnUp = At(FaceRows - 1); nr.selectOnDown = _eyeButtons[0]; nr.selectOnRight = _custDone; reset.navigation = nr;
            var nd = _custDone.navigation; nd.mode = Navigation.Mode.Explicit; nd.selectOnUp = At(n - 1); nd.selectOnDown = _eyeButtons[_eyeButtons.Count - 1]; nd.selectOnLeft = reset; _custDone.navigation = nd;
        }

        private void SectionHeader(RectTransform panel, string name, string text, float y)
        {
            var head = MenuUi.Label(panel, name, text, T.boldFont, 22, Color.white, TextAnchor.MiddleLeft, 0.2f);
            head.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, y), new Vector2(400f, 30f));
            Bind(p => head.color = p.newsHeader);
        }

        private void RefreshEyeButtons()
        {
            foreach (var b in _eyeButtons) b.OnGlow?.Invoke(b.Glow);
        }

        private void BuildFaceSlider(RectTransform panel, int index, float x, float y, float width)
        {
            var def = PlayerAppearance.Sliders[index];
            var root = MenuUi.Rect("Slider_" + def.Label, panel).Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, y), new Vector2(width, 70f));
            var label = MenuUi.Label(root, "Label", def.Label, T.boldFont, 23, Color.white, TextAnchor.MiddleLeft);
            label.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(width * 0.62f, 28f));
            var value = MenuUi.Label(root, "Value", "", T.bodyFont, 19, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleRight);
            value.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(width * 0.5f, 28f));

            // hit area (the Slider's target graphic), track, centre tick, fill and handle
            var area = MenuUi.Img(root, "Track", null, Color.clear);
            area.raycastTarget = true;
            area.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -50f), new Vector2(width - 30f, 36f));
            var track = MenuUi.Img(area.transform, "Bg", MenuUi.RoundedRect(5), new Color(0f, 0f, 0f, 0.3f), true);
            track.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0f, 10f);
            var tick = MenuUi.Img(area.transform, "Centre", null, new Color(1f, 1f, 1f, 0.45f));
            tick.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3f, 22f));
            var fillArea = MenuUi.Rect("FillArea", area.transform);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(0f, 10f);
            var fill = MenuUi.Img(fillArea, "Fill", MenuUi.RoundedRect(5), Color.white, true);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            Bind(p => fill.color = A(p.accent, 0.55f));
            var handleArea = MenuUi.Rect("HandleArea", area.transform).Fill();
            var handle = MenuUi.Img(handleArea, "Handle", MenuUi.Disc(), Color.white);
            handle.rectTransform.anchorMin = handle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            handle.rectTransform.sizeDelta = new Vector2(30f, -6f); // the Slider stretches the handle over the 36 px area height

            var s = area.gameObject.AddComponent<Slider>();
            s.transition = Selectable.Transition.None;
            s.targetGraphic = area;
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = -1f;
            s.maxValue = 1f;
            s.wholeNumbers = false;
            s.SetValueWithoutNotify(0f);
            int idx = index;
            s.onValueChanged.AddListener(v =>
            {
                v = Mathf.Abs(v) < 0.04f ? 0f : v; // easy to drop back onto the default
                _face.sliders[idx] = v;
                ShowSliderValue(idx, v);
                FaceChanged();
            });
            _faceSliders.Add((s, label, value, handle, index));
            ShowSliderValue(index, 0f);
        }

        private void ShowSliderValue(int i, float v)
        {
            if (i >= _faceSliders.Count) return;
            var def = PlayerAppearance.Sliders[i];
            _faceSliders[i].value.text = Mathf.Abs(v) < 0.005f ? "Default" : (v < 0f ? def.Low : def.High) + " " + Mathf.RoundToInt(Mathf.Abs(v) * 100f) + "%";
        }

        private void SyncFaceControls()
        {
            foreach (var f in _faceSliders)
            {
                float v = _face.sliders[f.index];
                f.s.SetValueWithoutNotify(v);
                ShowSliderValue(f.index, v);
            }
            RefreshEyeButtons();
        }

        private GameObject CustomizeFirstSelection() => _eyeButtons.Count > 0 ? _eyeButtons[0].gameObject : _faceSliders.Count > 0 ? _faceSliders[0].s.gameObject : _custDone.gameObject;

        private void FaceChanged()
        {
            if (_fighter != null) _fighter.SetFace(_face);
        }

        private void OpenCustomize()
        {
            if (_fighter == null) { ShowToast("Fighters · " + T.comingSoon); return; }
            _face = PlayerAppearance.Load();
            SyncFaceControls();
            SetState(State.Customize);
        }

        private void CloseCustomize()
        {
            PlayerAppearance.Save(_face);
            SetState(State.Main);
        }

        /// <summary>Screenshots / tests: opens Customize with these face choices.</summary>
        public void ShowCustomize(FaceCustomization face)
        {
            _face = face.Sanitized();
            SyncFaceControls();
            FaceChanged();
            SetState(State.Customize, instant: true);
        }

        private void UpdateCustomize()
        {
            if (_custDlg.alpha <= 0.01f) return;
            _custStatus.text = _fighter != null && _fighter.Rebuilding ? "Updating your fighter…" : TouchLayout ? "Drag your fighter to turn them." : "Drag your fighter to turn them  ·  Q / E";
            var es = EventSystem.current;
            var accent = _sky.Preset.accent;
            foreach (var f in _faceSliders)
            {
                bool on = es != null && es.currentSelectedGameObject == f.s.gameObject;
                f.label.color = on ? Color.Lerp(Color.white, accent, 0.6f) : Color.white;
                f.handle.color = on ? accent : Color.white;
                f.handle.rectTransform.localScale = Vector3.one * (on ? 1.2f : 1f);
            }
        }


        private void BuildToast(Transform parent)
        {
            _toast = Group("Toast", parent);
            _toast.blocksRaycasts = false;
            _toast.interactable = false;
            _toast.alpha = 0f;
            var panel = Panel(_toast.transform, "Panel", new Vector2(400f, 76f), 36, p => p.dialog, p => p.accent, 2f);
            _toastRect = panel.rectTransform;
            _toastRect.Place(new Vector2(0.5f, 0.95f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(400f, 76f));
            _toastText = MenuUi.Label(panel.transform, "Text", "", T.boldFont, 30, Color.white);
            _toastText.rectTransform.Fill();
        }

        // ================================================================= behaviour

        public void ShowToast(string text)
        {
            _toastText.text = text;
            _toastRect.sizeDelta = new Vector2(MenuUi.Width(_toastText) + 80f, 76f);
            _toastUntil = Time.unscaledTime + T.toastSeconds;
        }

        private void Activate(string id, string label)
        {
            switch (id)
            {
                case "play": SetState(State.PlayOnline); break;
                case "settings": SetState(State.Settings); break;
                case "fighters": OpenCustomize(); break;
                case "stats": OpenStats(); break;
                case "training": NetLaunch.Launch(NetMode.Training); break;
                case "quit":
                    Debug.Log("[Menu] Quit");
                    Application.Quit();
                    break;
                default: ShowToast(label + " · " + T.comingSoon); break;
            }
        }

        private void Launch(NetMode mode)
        {
            string ip = _ip != null && !string.IsNullOrWhiteSpace(_ip.text) ? _ip.text.Trim() : NetworkBootstrap.DefaultAddress;
            PlayerPrefs.SetString(IpPref, ip);
            PlayerPrefs.Save();
            NetLaunch.Launch(mode, ip);
        }

        /// <summary>Smoke tests (<c>-menuAutoplay host|join [ip]</c>): opens Play Online and presses Host or Join like a player.</summary>
        public void AutoPlay(string action, string ip)
        {
            SetState(State.PlayOnline, instant: true);
            if (action == "join")
            {
                if (!string.IsNullOrEmpty(ip)) _ip.text = ip;
                Debug.Log("[Menu] Autoplay: Join " + _ip.text);
                _joinButton.OnClick();
            }
            else
            {
                Debug.Log("[Menu] Autoplay: Host");
                _hostButton.OnClick();
            }
        }

        public void SetState(State s, bool instant = false)
        {
            var from = Current;
            Current = s;
            var es = EventSystem.current;
            GameObject select = s switch
            {
                State.Main => _lastMainSelection != null && from != State.Title ? _lastMainSelection : _playItem.gameObject,
                State.PlayOnline => _hostButton.gameObject,
                State.Settings => from == State.Controls ? _settingRows[0].gameObject : _skyButtons[(int)_sky.Current].b.gameObject,
                State.Controls => _bindButtons[0].gameObject,
                State.Account => Accounts.AccountStore.Instance.Current is { online: true } ? _signOutButton.gameObject : _userField.gameObject,
                State.Results => _resultsContinue.gameObject,
                State.Customize => CustomizeFirstSelection(),
                State.Stats => _statRows[0].plus.gameObject,
                _ => null,
            };
            if (from == State.Main && s != State.Main && es != null && es.currentSelectedGameObject != null) _lastMainSelection = es.currentSelectedGameObject;
            if (es != null) es.SetSelectedGameObject(select);
            if (instant)
            {
                _hud.alpha = s == State.Title ? 0f : 1f;
                _press.alpha = s == State.Title ? 1f : 0f;
                _playDlg.alpha = s == State.PlayOnline ? 1f : 0f;
                _settingsDlg.alpha = s == State.Settings ? 1f : 0f;
                _controlsDlg.alpha = s == State.Controls ? 1f : 0f;
                _accountDlg.alpha = s == State.Account ? 1f : 0f;
                _resultsDlg.alpha = s == State.Results ? 1f : 0f;
                _custDlg.alpha = s == State.Customize ? 1f : 0f;
                _statsDlg.alpha = s == State.Stats ? 1f : 0f;
                _logo.alpha = s == State.Customize ? 0f : 1f;
                if (s != State.Title) _hud.alpha = s == State.Customize ? 0f : 1f;
                _backdrop.alpha = s is State.PlayOnline or State.Settings or State.Controls or State.Account or State.Results or State.Stats ? 1f : 0f;
                foreach (var g in _all) g.Snap();
            }
            if (s is State.Settings or State.Controls) RefreshSettingValues();
            if (s == State.Account) RefreshAccountUI();
            UpdateGroups();
        }

        private void UpdateGroups()
        {
            float step = T.fadeTime > 0f ? Time.unscaledDeltaTime / T.fadeTime : 1f;
            Fade(_hud, Current != State.Title && Current != State.Customize, step, Current == State.Main);
            Fade(_logo, Current != State.Customize, step, false);
            Fade(_custDlg, Current == State.Customize, step, Current == State.Customize);
            Fade(_press, Current == State.Title, step, false);
            bool dialog = Current is State.PlayOnline or State.Settings or State.Controls or State.Account or State.Results or State.Stats;
            Fade(_backdrop, dialog, step, dialog);
            Fade(_playDlg, Current == State.PlayOnline, step, Current == State.PlayOnline);
            Fade(_settingsDlg, Current == State.Settings, step, Current == State.Settings);
            Fade(_controlsDlg, Current == State.Controls, step, Current == State.Controls);
            Fade(_accountDlg, Current == State.Account, step, Current == State.Account);
            Fade(_resultsDlg, Current == State.Results, step, Current == State.Results);
            Fade(_statsDlg, Current == State.Stats, step, Current == State.Stats);
        }

        private static void Fade(CanvasGroup g, bool show, float step, bool interactive)
        {
            g.alpha = Mathf.MoveTowards(g.alpha, show ? 1f : 0f, step);
            bool live = interactive && g.alpha > 0.9f;
            g.interactable = live;
            g.blocksRaycasts = live;
        }

        private void Update()
        {
            if (!_built) return;
            UpdateLayout();
            UpdateQuitIcon();
            UpdateGroups();

            // title: blink + any input
            if (_press.alpha > 0.01f)
            {
                _pressText.text = TouchLayout ? T.tapToStart : T.pressAnyKey;
                float k = 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(0.1f, T.pressBlinkPeriod));
                _pressText.color = A(Color.white, Mathf.Lerp(0.4f, 1f, k));
            }
            if (Current == State.Title && AnyPress()) SetState(State.Main);
            else if (Current is State.PlayOnline or State.Settings && CancelPressed()) SetState(State.Main);
            else if (Current == State.Controls && !RebindBusy && CancelPressed()) SetState(State.Settings);
            else if (Current == State.Account && CancelPressed()) SetState(_accountReturn);
            else if (Current == State.Results && CancelPressed()) SetState(State.Main);
            else if (Current == State.Customize && CancelPressed()) CloseCustomize();
            else if (Current == State.Stats && CancelPressed()) CloseStats();
            UpdateCustomize();

            // keep something selected for keyboard/gamepad (mouse clicks on empty space clear it)
            var es = EventSystem.current;
            if (Current != State.Title && es != null && (es.currentSelectedGameObject == null || !es.currentSelectedGameObject.activeInHierarchy) && NavPressed())
                SetState(Current);

            // toast
            bool showToast = Time.unscaledTime < _toastUntil;
            _toast.alpha = Mathf.MoveTowards(_toast.alpha, showToast ? 1f : 0f, Time.unscaledDeltaTime / 0.25f);
            _toastRect.anchoredPosition = new Vector2(0f, (1f - _toast.alpha) * 24f);
        }

        private void UpdateLayout()
        {
            int pw = _cam != null ? _cam.pixelWidth : Screen.width, ph = _cam != null ? _cam.pixelHeight : Screen.height;
            if (pw <= 0 || ph <= 0) return;
            float aspect = (float)pw / ph;
            _scaler.matchWidthOrHeight = aspect >= 16f / 9f - 0.01f ? 1f : 0f;
            Rect sa = SafeAreaOverride ?? (pw == Screen.width && ph == Screen.height ? Screen.safeArea : new Rect(0, 0, pw, ph));
            var size = new Vector2(pw, ph);
            if (sa == _lastSafe && size == _lastSize) return;
            _lastSafe = sa;
            _lastSize = size;
            _safe.anchorMin = new Vector2(sa.xMin / pw, sa.yMin / ph);
            _safe.anchorMax = new Vector2(sa.xMax / pw, sa.yMax / ph);
            _safe.offsetMin = _safe.offsetMax = Vector2.zero;
        }

        private static bool AnyPress()
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
            if (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)) return true;
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            return gp != null && (gp.buttonSouth.wasPressedThisFrame || gp.startButton.wasPressedThisFrame || gp.buttonEast.wasPressedThisFrame);
        }

        private bool CancelPressed()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            return gp != null && gp.buttonEast.wasPressedThisFrame;
        }

        private static bool NavPressed()
        {
            var k = Keyboard.current;
            if (k != null && (k.leftArrowKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame || k.tabKey.wasPressedThisFrame)) return true;
            var gp = Gamepad.current;
            return gp != null && (gp.dpad.ReadValue() != Vector2.zero || gp.leftStick.ReadValue().sqrMagnitude > 0.25f);
        }
    }

    /// <summary>Highlights the IP field's border while it is selected or being edited.</summary>
    public sealed class IpFieldGlow : MonoBehaviour
    {
        private InputField _field;
        private Image _bg;

        public void Init(InputField field, Image bg) { _field = field; _bg = bg; }

        private void Update()
        {
            if (_field == null) return;
            bool on = _field.isFocused || (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == _field.gameObject);
            _bg.color = on ? new Color(1f, 1f, 1f, 0.16f) : new Color(0f, 0f, 0f, 0.28f);
        }
    }
}
