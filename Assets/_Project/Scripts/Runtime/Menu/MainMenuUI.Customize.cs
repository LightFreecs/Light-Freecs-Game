using System;
using System.Collections.Generic;
using LightMMORPG.Characters;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LightMMORPG.Menu
{
    /// <summary>
    /// Customize (Fighters item), design bench <c>docs/bench/customize-ui.html</c>, "Bevel" style with no panel: eight
    /// icon tabs (six head tabs, Hair, Body, Skin) floating along the top left, Random top right, and a frameless column on the right with (Eyes, Brows, Head, Jaw &amp; Chin, Nose &amp; Mouth, Ears &amp; Neck), eye- and brow-style tiles
    /// showing the real painted eyes, slider rows with − / + buttons, a centred fill, a value tag and a per-row reset,
    /// and Random / Reset all / Done. Buttons are 9-sliced boxes with two cut corners (<see cref="MenuUi.BevelFill"/>).
    /// Keys: Z / C or LB / RB switch tabs, R / Y random, Backspace / X reset all, Enter / Start done, Esc / B done.
    /// </summary>
    public sealed partial class MainMenuUI
    {
        private sealed class Bevel { public Image fill, edge, top; }

        private sealed class CustButton
        {
            public GlowSelectable sel;
            public RectTransform rt;
            public Bevel bevel;
            public bool primary;
            public Func<bool> selected;
            public Action<float> extra;
            public readonly List<(Graphic g, float a)> ink = new();
        }

        private sealed class FaceRow
        {
            public int index, tab;
            public Slider s;
            public Text label, value;
            public Image handle, fill, chip, chipEdge, highlight;
            public RectTransform chipRt;
            public CustButton minus, plus, undo;
        }

        private static readonly (string name, MenuUi.IconKind icon, int[] rows)[] CustTabs =
        {
            ("Eyes", MenuUi.IconKind.Eye, new[] { 14, 15, 16 }),
            ("Brows", MenuUi.IconKind.Brow, new[] { PlayerAppearance.BrowSliders, PlayerAppearance.BrowSliders + 1, 17, PlayerAppearance.BrowSliders + 2 }),
            ("Head", MenuUi.IconKind.Head, new[] { 0, 1, 2, 3, 4, 5 }),
            ("Jaw & Chin", MenuUi.IconKind.Jaw, new[] { 6, 7, 8, 9, 10 }),
            ("Nose & Mouth", MenuUi.IconKind.Nose, new[] { 11, 12, 13 }),
            ("Ears & Neck", MenuUi.IconKind.Ear, new[] { 18, 19, 20, 21 }),
            ("Hair", MenuUi.IconKind.Hair, new int[0]),
            ("Body", MenuUi.IconKind.Body, new[] { 22, 23, 24, 25 }),
            ("Skin", MenuUi.IconKind.Skin, new int[0]),
        };

        private const int BrowTab = 1, HairTab = 6, BodyTab = 7, SkinTab = 8;

        /// <summary>The Body tab is open on Customize (aura colour preview).</summary>
        public bool BodyTabOpen => Current == State.Customize && _custTab == BodyTab;
        /// <summary>The Body or Skin tab is open (the menu camera pulls back to show the whole fighter).</summary>
        public bool WholeFighterView => Current == State.Customize && (_custTab == BodyTab || _custTab == SkinTab);

        private const float CustPW = 880f, CustPad = 40f, CustW = CustPW - CustPad * 2f, RowStep = 72f, RowH = 66f;

        private FaceCustomization _face;
        private readonly List<FaceRow> _faceRows = new();
        private readonly List<CustButton> _custButtons = new();
        private readonly List<CustButton> _eyeTiles = new();
        private readonly List<CustButton> _browTiles = new();
        private readonly List<Image> _browPreviews = new();
        private readonly List<CustButton> _hairTiles = new();
        private readonly List<CustButton> _hairColorTiles = new();
        private readonly List<CustButton> _auraColorTiles = new();
        private readonly List<CustButton> _skinTiles = new();
        private readonly List<Image> _hairPreviews = new();
        private Texture2D[] _hairTex;
        private string _hairPreviewKey;
        private readonly List<Image> _eyePreviews = new();
        private readonly List<CustButton> _tabButtons = new();
        private readonly List<Image> _tabDots = new();
        private readonly List<GameObject> _tabPages = new();
        private readonly List<(GameObject go, Text t, string kb, string pad)> _keyGlyphs = new();
        private CustButton _custDone, _custReset, _custRandom;
        private Text _custStatus;
        private int _custTab;
        private bool _padGlyphs, _glyphsShown = true, _glyphsInit, _eyePreviewsMade;

        private static RectTransform TL(RectTransform rt, float x, float y, float w, float h) =>
            rt.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(w, h));

        // ---------------- build ----------------

        private void BuildCustomizeDialog(RectTransform parent)
        {
            _custDlg = Group("Customize", parent);
            // no panel: tabs float along the top-left, Random in the top-right corner, and the style tiles / sliders / footer
            // float in a frameless column on the right; text gets drop shadows so it reads over the sky
            var panel = MenuUi.Rect("Column", _custDlg.transform);

            // tab bar (top left)
            const float tabH = 82f, tw = 140f, slot = 48f, gap = 8f;
            float barW = slot * 2f + tw * CustTabs.Length + gap * (CustTabs.Length - 1);
            var bar = MenuUi.Rect("TabBar", _custDlg.transform)
                .Place(new Vector2(T.edgeMargin, 1f - T.cornerTop), new Vector2(0f, 1f), Vector2.zero, new Vector2(barW, tabH + 40f));
            var prev = KeyGlyph(bar, "Z", "LB", null);
            prev.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(0f, -tabH * 0.5f), Vector2.zero);
            var next = KeyGlyph(bar, "C", "RB", null);
            next.Place(new Vector2(0f, 1f), new Vector2(1f, 0.5f), new Vector2(barW, -tabH * 0.5f), Vector2.zero);
            _custStatus = MenuUi.Label(bar, "Status", "", T.bodyFont, 21, A(Color.white, 0.8f), TextAnchor.MiddleLeft);
            TL(_custStatus.rectTransform, slot, -tabH - 10f, 700f, 28f);
            Shade(_custStatus);

            // Random (top right)
            _custRandom = CustomButton(_custDlg.transform as RectTransform, "Random", Vector2.zero, new Vector2(200f, 52f), 8, Randomize);
            _custRandom.rt.Place(new Vector2(1f - T.edgeMargin, 1f - T.cornerTop), new Vector2(1f, 1f), Vector2.zero, new Vector2(200f, 52f));
            var dice = Icon(_custRandom, MenuUi.IconKind.Dice, 26f);
            dice.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(26f, 26f));
            var rl = MenuUi.Label(_custRandom.rt, "Label", "RANDOM", T.bodyFont, 17, Color.white, TextAnchor.MiddleLeft, 0.12f);
            rl.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(52f, 0f), new Vector2(110f, 30f));
            _custRandom.ink.Add((rl, 1f));
            KeyGlyph(_custRandom.rt, "R", "Y", _custRandom).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), Vector2.zero);

            for (int i = 0; i < CustTabs.Length; i++)
            {
                int ti = i;
                var b = CustomButton(bar, "Tab_" + CustTabs[i].name, new Vector2(slot + i * (tw + gap), 0f), new Vector2(tw, tabH), 9,
                    () => SetCustTab(ti, false), () => _custTab == ti);
                var ic = Icon(b, CustTabs[i].icon, 30f);
                ic.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(30f, 30f));
                var tl = MenuUi.Label(b.rt, "Label", CustTabs[i].name.ToUpperInvariant(), T.bodyFont, 14, Color.white, TextAnchor.MiddleCenter, 0.08f);
                tl.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(tw, 22f));
                b.ink.Add((tl, 1f));
                var dot = MenuUi.Img(b.rt, "Changed", null, Color.white);
                dot.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-10f, -9f), new Vector2(7f, 7f));
                Bind(p => dot.color = p.accent);
                _tabDots.Add(dot);
                _tabButtons.Add(b);
            }
            // pages
            const float bodyY = 0f;
            for (int t = 0; t < CustTabs.Length; t++)
            {
                var page = TL(MenuUi.Rect("Page_" + CustTabs[t].name, panel), CustPad, bodyY, CustW, 500f);
                _tabPages.Add(page.gameObject);
                float y = 0f;
                if (t == 0)
                {
                    SectionHead(page, "STYLE", y);
                    y -= 32f;
                    const float eg = 14f, eh = 120f;
                    int n = PlayerAppearance.EyeNames.Length;
                    float ew = (CustW - eg * (n - 1)) / n;
                    for (int i = 0; i < n; i++)
                    {
                        var style = (ToonEyeStyle)i;
                        Image badge = null;
                        var b = CustomButton(page, "Eye_" + PlayerAppearance.EyeNames[i], new Vector2(i * (ew + eg), y), new Vector2(ew, eh), 10,
                            () => { _face.eyeStyle = style; RefreshCust(); FaceChanged(); }, () => _face.eyeStyle == style);
                        var pic = MenuUi.Img(b.rt, "Preview", null, Color.white);
                        pic.preserveAspect = true;
                        pic.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(ew - 28f, 58f));
                        pic.enabled = false;
                        _eyePreviews.Add(pic);
                        var name = MenuUi.Label(b.rt, "Name", PlayerAppearance.EyeNames[i], T.bodyFont, 18, Color.white, TextAnchor.MiddleCenter, 0.06f);
                        name.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(ew, 24f));
                        b.ink.Add((name, 1f));
                        badge = MenuUi.Img(b.rt, "Badge", null, Color.white);
                        badge.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -8f), new Vector2(24f, 24f));
                        Bind(p => badge.color = p.accent);
                        var check = MenuUi.Img(badge.transform, "Check", MenuUi.Icon(MenuUi.IconKind.Check), Color.white);
                        check.rectTransform.Fill(4f);
                        Bind(p => { var d = p.dialog; d.a = 1f; check.color = d; });
                        b.extra = _ => badge.gameObject.SetActive(_face.eyeStyle == style);
                        _eyeTiles.Add(b);
                    }
                    y -= eh + 22f;
                    SectionHead(page, "SHAPE", y);
                }
                else if (t == BrowTab)
                {
                    SectionHead(page, "STYLE", y);
                    y -= 32f;
                    const float bg = 14f, bh = 120f;
                    int n = PlayerAppearance.BrowNames.Length;
                    float bw = (CustW - bg * (n - 1)) / n;
                    for (int i = 0; i < n; i++)
                    {
                        var style = (ToonBrowStyle)i;
                        var b = CustomButton(page, "Brow_" + PlayerAppearance.BrowNames[i], new Vector2(i * (bw + bg), y), new Vector2(bw, bh), 10,
                            () => { _face.browStyle = style; RefreshCust(); FaceChanged(); }, () => _face.browStyle == style);
                        var pic = MenuUi.Img(b.rt, "Preview", null, Color.white);
                        pic.preserveAspect = true;
                        pic.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(bw - 28f, 66f));
                        pic.enabled = false;
                        _browPreviews.Add(pic);
                        var name = MenuUi.Label(b.rt, "Name", PlayerAppearance.BrowNames[i], T.bodyFont, 18, Color.white, TextAnchor.MiddleCenter, 0.06f);
                        name.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(bw, 24f));
                        b.ink.Add((name, 1f));
                        var badge = MenuUi.Img(b.rt, "Badge", null, Color.white);
                        badge.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -8f), new Vector2(24f, 24f));
                        Bind(p => badge.color = p.accent);
                        var check = MenuUi.Img(badge.transform, "Check", MenuUi.Icon(MenuUi.IconKind.Check), Color.white);
                        check.rectTransform.Fill(4f);
                        Bind(p => { var d = p.dialog; d.a = 1f; check.color = d; });
                        b.extra = _ => badge.gameObject.SetActive(_face.browStyle == style);
                        _browTiles.Add(b);
                    }
                    y -= bh + 22f;
                    SectionHead(page, "SHAPE", y);
                }
                else if (t == HairTab)
                {
                    SectionHead(page, "STYLE", y);
                    y -= 32f;
                    const float hg = 14f, hh = 210f;
                    int n = PlayerAppearance.HairNames.Length;
                    float hw = (CustW - hg * (n - 1)) / n;
                    for (int i = 0; i < n; i++)
                    {
                        var hair = (PlayerHair)i;
                        var b = CustomButton(page, "Hair_" + PlayerAppearance.HairNames[i], new Vector2(i * (hw + hg), y), new Vector2(hw, hh), 10,
                            () => { _face.hair = hair; RefreshCust(); FaceChanged(); }, () => _face.hair == hair);
                        var pic = MenuUi.Img(b.rt, "Preview", null, Color.white);
                        pic.preserveAspect = true;
                        pic.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(hw - 24f, 156f));
                        pic.enabled = false;
                        _hairPreviews.Add(pic);
                        var name = MenuUi.Label(b.rt, "Name", PlayerAppearance.HairNames[i], T.bodyFont, 18, Color.white, TextAnchor.MiddleCenter, 0.06f);
                        name.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(hw, 24f));
                        b.ink.Add((name, 1f));
                        var badge = MenuUi.Img(b.rt, "Badge", null, Color.white);
                        badge.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -8f), new Vector2(24f, 24f));
                        Bind(p => badge.color = p.accent);
                        var check = MenuUi.Img(badge.transform, "Check", MenuUi.Icon(MenuUi.IconKind.Check), Color.white);
                        check.rectTransform.Fill(4f);
                        Bind(p => { var d = p.dialog; d.a = 1f; check.color = d; });
                        b.extra = _ => badge.gameObject.SetActive(_face.hair == hair);
                        _hairTiles.Add(b);
                    }
                    y -= hh + 22f;
                    SectionHead(page, "COLOUR", y);
                    y -= 32f;
                    const float cg = 12f;
                    int nc = PlayerAppearance.HairColors.Length;
                    float cw = (CustW - cg * (nc - 1)) / nc;
                    for (int i = 0; i < nc; i++)
                    {
                        int ci = i;
                        var b = CustomButton(page, "HairColour_" + PlayerAppearance.HairColors[i].name, new Vector2(i * (cw + cg), y), new Vector2(cw, cw), 7,
                            () => { _face.hairColor = ci; RefreshCust(); FaceChanged(); }, () => _face.hairColor == ci);
                        var sw = MenuUi.Img(b.rt, "Swatch", MenuUi.RoundedRect(3), PlayerAppearance.HairColors[i].color, true);
                        sw.rectTransform.Fill(9f);
                        var ring = MenuUi.Img(b.rt, "Ring", MenuUi.RoundedRectOutline(3, 1.2f), A(Color.white, 0.35f), true);
                        ring.rectTransform.Fill(9f);
                        var badge = MenuUi.Img(b.rt, "Badge", null, Color.white);
                        badge.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-4f, -4f), new Vector2(20f, 20f));
                        Bind(p => badge.color = p.accent);
                        var check = MenuUi.Img(badge.transform, "Check", MenuUi.Icon(MenuUi.IconKind.Check), Color.white);
                        check.rectTransform.Fill(3f);
                        Bind(p => { var d = p.dialog; d.a = 1f; check.color = d; });
                        b.extra = _ => badge.gameObject.SetActive(_face.hairColor == ci);
                        _hairColorTiles.Add(b);
                    }
                    y -= cw + 22f;
                    continue;
                }
                else if (t == SkinTab)
                {
                    BuildSkinTones(page, y);
                    continue;
                }
                else SectionHead(page, CustTabs[t].name.ToUpperInvariant(), y);
                y -= 32f;
                foreach (int idx in CustTabs[t].rows)
                {
                    BuildFaceRow(page, idx, t, y);
                    y -= RowStep;
                }
                if (t == BodyTab) y = BuildAuraColours(page, y - 4f);
            }

            // footer
            float fy = bodyY - 494f - 18f, rw = Mathf.Round((CustW - 16f) * 0.42f);
            _custReset = CustomButton(panel, "ResetAll", new Vector2(CustPad, fy), new Vector2(rw, 68f), 12, ResetFace);
            FooterContent(_custReset, MenuUi.IconKind.Undo, "RESET ALL", T.bodyFont, "Bksp", "X");
            _custDone = CustomButton(panel, "Done", new Vector2(CustPad + rw + 16f, fy), new Vector2(CustW - rw - 16f, 68f), 12, CloseCustomize, primary: true);
            FooterContent(_custDone, MenuUi.IconKind.Check, "DONE", T.boldFont, "Enter", "Start");
            float height = -(fy - 68f - 34f);
            panel.Place(new Vector2(1f - T.edgeMargin, 0.5f), new Vector2(1f, 0.5f), new Vector2(CustPad, 0f), new Vector2(CustPW, height)); // content edge lines up with Random

            Bind(_ => { RefreshCust(); foreach (var r in _faceRows) ShowSliderValue(r); });
            SetCustTab(0, false);
        }

        /// <summary>Skin tab: the skin tones, two rows of big swatches with their names.</summary>
        private void BuildSkinTones(RectTransform page, float y)
        {
            SectionHead(page, "SKIN TONE", y);
            y -= 32f;
            const float g = 14f, h = 150f;
            int n = PlayerAppearance.SkinTones.Length, perRow = (n + 1) / 2;
            float w = (CustW - g * (perRow - 1)) / perRow;
            for (int i = 0; i < n; i++)
            {
                int si = i, row = i / perRow, col = i % perRow;
                var b = CustomButton(page, "Skin_" + PlayerAppearance.SkinTones[i].name, new Vector2(col * (w + g), y - row * (h + g)), new Vector2(w, h), 10,
                    () => { _face.skinTone = si; RefreshCust(); FaceChanged(); }, () => _face.skinTone == si);
                var sw = MenuUi.Img(b.rt, "Swatch", MenuUi.RoundedRect(6), PlayerAppearance.SkinTones[i].color, true);
                sw.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(w - 28f, h - 56f));
                var ring = MenuUi.Img(b.rt, "Ring", MenuUi.RoundedRectOutline(6, 1.2f), A(Color.white, 0.35f), true);
                ring.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(w - 28f, h - 56f));
                var name = MenuUi.Label(b.rt, "Name", PlayerAppearance.SkinTones[i].name, T.bodyFont, 17, Color.white, TextAnchor.MiddleCenter, 0.06f);
                name.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(w, 24f));
                b.ink.Add((name, 1f));
                var badge = MenuUi.Img(b.rt, "Badge", null, Color.white);
                badge.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-6f, -6f), new Vector2(22f, 22f));
                Bind(p => badge.color = p.accent);
                var check = MenuUi.Img(badge.transform, "Check", MenuUi.Icon(MenuUi.IconKind.Check), Color.white);
                check.rectTransform.Fill(3f);
                Bind(p => { var d = p.dialog; d.a = 1f; check.color = d; });
                b.extra = _ => badge.gameObject.SetActive(_face.skinTone == si);
                _skinTiles.Add(b);
            }
        }

        /// <summary>Body tab: the aura colour swatches (the menu fighter shows the aura while the Body tab is open).</summary>
        private float BuildAuraColours(RectTransform page, float y)
        {
            SectionHead(page, "AURA", y);
            y -= 32f;
            const float cg = 12f;
            int nc = PlayerAppearance.AuraColors.Length;
            float cw = (CustW - cg * (nc - 1)) / nc;
            for (int i = 0; i < nc; i++)
            {
                int ci = i;
                var b = CustomButton(page, "AuraColour_" + PlayerAppearance.AuraColors[i].name, new Vector2(i * (cw + cg), y), new Vector2(cw, cw), 7,
                    () => { _face.auraColor = ci; RefreshCust(); FaceChanged(); }, () => _face.auraColor == ci);
                var sw = MenuUi.Img(b.rt, "Swatch", MenuUi.RoundedRect(3), PlayerAppearance.AuraColors[i].color, true);
                sw.rectTransform.Fill(9f);
                var ring = MenuUi.Img(b.rt, "Ring", MenuUi.RoundedRectOutline(3, 1.2f), A(Color.white, 0.35f), true);
                ring.rectTransform.Fill(9f);
                var badge = MenuUi.Img(b.rt, "Badge", null, Color.white);
                badge.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-4f, -4f), new Vector2(20f, 20f));
                Bind(p => badge.color = p.accent);
                var check = MenuUi.Img(badge.transform, "Check", MenuUi.Icon(MenuUi.IconKind.Check), Color.white);
                check.rectTransform.Fill(3f);
                Bind(p => { var d = p.dialog; d.a = 1f; check.color = d; });
                b.extra = _ => badge.gameObject.SetActive(_face.auraColor == ci);
                _auraColorTiles.Add(b);
            }
            return y - cw - 22f;
        }

        private void SectionHead(RectTransform page, string text, float y)
        {
            var head = MenuUi.Label(page, "Head_" + text, text, T.boldFont, 19, Color.white, TextAnchor.MiddleLeft, 0.2f);
            TL(head.rectTransform, 0f, y, 400f, 26f);
            Shade(head);
            Bind(p => head.color = p.newsHeader);
        }

        /// <summary>Drop shadow in the sky's text-shadow colour, so frameless text reads over bright clouds.</summary>
        private void Shade(Graphic g)
        {
            var s = MenuUi.AddShadow(g, Color.black, new Vector2(0f, -2f));
            Bind(p => s.effectColor = p.textShadow);
        }

        private void FooterContent(CustButton b, MenuUi.IconKind icon, string label, Font font, string kb, string pad)
        {
            var t = MenuUi.Label(b.rt, "Label", label, font, 22, Color.white, TextAnchor.MiddleCenter, 0.22f);
            float lw = MenuUi.Width(t);
            t.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(17f, 0f), new Vector2(lw + 20f, 34f));
            b.ink.Add((t, 1f));
            var ic = Icon(b, icon, 26f);
            ic.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(17f - lw * 0.5f - 24f, 0f), new Vector2(26f, 26f));
            KeyGlyph(b.rt, kb, pad, b).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-16f, 0f), Vector2.zero);
        }

        private Image Icon(CustButton b, MenuUi.IconKind kind, float size)
        {
            var img = MenuUi.Img(b.rt, "Icon", MenuUi.Icon(kind), Color.white);
            img.rectTransform.sizeDelta = new Vector2(size, size);
            b.ink.Add((img, 1f));
            return img;
        }

        /// <summary>A bevel button (or, with cut &lt; 0, a frameless icon button) at a top-left position inside parent.</summary>
        private CustButton CustomButton(RectTransform parent, string name, Vector2 pos, Vector2 size, int cut, Action onClick, Func<bool> selected = null, bool primary = false)
        {
            var b = new CustButton { primary = primary, selected = selected };
            b.rt = TL(MenuUi.Rect(name, parent), pos.x, pos.y, size.x, size.y);
            if (cut >= 0)
            {
                b.bevel = new Bevel
                {
                    fill = MenuUi.Img(b.rt, "Fill", MenuUi.BevelFill(cut), Color.white, true),
                    edge = MenuUi.Img(b.rt, "Edge", MenuUi.BevelEdge(cut, 1.5f), Color.white, true),
                    top = MenuUi.Img(b.rt, "Top", MenuUi.BevelTop(cut, 1.5f), Color.white, true),
                };
                b.bevel.fill.rectTransform.Fill();
                b.bevel.edge.rectTransform.Fill();
                b.bevel.top.rectTransform.Fill();
            }
            b.sel = AddSelectable(b.rt.gameObject);
            b.sel.OnClick = onClick;
            b.sel.OnGlow = g => PaintCust(b, g);
            _custButtons.Add(b);
            return b;
        }

        private RectTransform KeyGlyph(RectTransform parent, string kb, string pad, CustButton owner)
        {
            var rt = MenuUi.Rect("Key", parent);
            var edge = MenuUi.Img(rt, "Edge", MenuUi.RoundedRectOutline(3, 1.2f), A(Color.white, 0.35f), true);
            edge.rectTransform.Fill();
            var t = MenuUi.Label(rt, "Text", pad, T.bodyFont, 14, A(Color.white, 0.7f), TextAnchor.MiddleCenter, 0.06f);
            t.rectTransform.Fill();
            float w = MenuUi.Width(t);
            t.text = kb;
            w = Mathf.Max(34f, Mathf.Max(w, MenuUi.Width(t)) + 16f);
            rt.sizeDelta = new Vector2(w, 28f);
            if (owner != null) { owner.ink.Add((edge, 0.4f)); owner.ink.Add((t, 0.7f)); }
            _keyGlyphs.Add((rt.gameObject, t, kb, pad));
            return rt;
        }

        private void BuildFaceRow(RectTransform page, int index, int tab, float y)
        {
            var def = PlayerAppearance.Sliders[index];
            var r = new FaceRow { index = index, tab = tab };
            var root = TL(MenuUi.Rect("Row_" + def.Label, page), 0f, y, CustW, RowH);
            r.highlight = MenuUi.Img(root, "Highlight", MenuUi.RoundedRect(2), Color.clear, true);
            r.highlight.rectTransform.Fill();
            r.label = MenuUi.Label(root, "Label", def.Label, T.bodyFont, 22, Color.white, TextAnchor.MiddleLeft);
            TL(r.label.rectTransform, 12f, 0f, 200f, RowH);
            Shade(r.label);

            const float step = 48f, valueW = 150f;
            float plusX = CustW - 12f - valueW - 10f - step, trackX = 222f + step + 12f, trackW = plusX - 12f - trackX;
            r.minus = CustomButton(root, "Minus", new Vector2(222f, -9f), new Vector2(step, step), 7, () => Nudge(r, -0.1f));
            Icon(r.minus, MenuUi.IconKind.Minus, 20f);
            r.plus = CustomButton(root, "Plus", new Vector2(plusX, -9f), new Vector2(step, step), 7, () => Nudge(r, 0.1f));
            Icon(r.plus, MenuUi.IconKind.Plus, 20f);

            // hit area (the Slider's target graphic), thin track, centre tick, centred fill, bar handle, end labels
            var area = MenuUi.Img(root, "Track", null, Color.clear);
            area.raycastTarget = true;
            TL(area.rectTransform, trackX, -(RowH - 40f) * 0.5f, trackW, 40f);
            var bg = MenuUi.Img(area.transform, "Bg", null, A(Color.white, 0.3f));
            bg.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            bg.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            bg.rectTransform.sizeDelta = new Vector2(0f, 4f);
            r.fill = MenuUi.Img(area.transform, "Fill", null, Color.white);
            r.fill.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            r.fill.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            r.fill.rectTransform.sizeDelta = new Vector2(0f, 4f);
            var tick = MenuUi.Img(area.transform, "Centre", null, A(Color.white, 0.45f));
            tick.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1.5f, 18f));
            var lo = MenuUi.Label(area.transform, "Low", def.Low, T.bodyFont, 13, A(Color.white, 0.6f), TextAnchor.LowerLeft);
            lo.rectTransform.Place(new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 2f), new Vector2(120f, 16f));
            var hi = MenuUi.Label(area.transform, "High", def.High, T.bodyFont, 13, A(Color.white, 0.6f), TextAnchor.LowerRight);
            hi.rectTransform.Place(new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 2f), new Vector2(120f, 16f));
            Shade(lo);
            Shade(hi);
            var handleArea = MenuUi.Rect("HandleArea", area.transform).Fill();
            var handleEdge = MenuUi.Img(handleArea, "Handle", null, new Color(0f, 0f, 0f, 0.45f));
            handleEdge.rectTransform.anchorMin = handleEdge.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            handleEdge.rectTransform.sizeDelta = new Vector2(12f, -4f); // the Slider stretches the handle over the 40 px area height
            r.handle = MenuUi.Img(handleEdge.transform, "Bar", null, Color.white);
            r.handle.rectTransform.Fill(1f);

            var s = area.gameObject.AddComponent<Slider>();
            s.transition = Selectable.Transition.None;
            s.targetGraphic = area;
            s.handleRect = handleEdge.rectTransform;
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = -1f;
            s.maxValue = 1f;
            s.wholeNumbers = false;
            s.SetValueWithoutNotify(0f);
            r.s = s;
            s.onValueChanged.AddListener(v =>
            {
                v = Mathf.Abs(v) < 0.04f ? 0f : v; // easy to drop back onto the default
                _face.sliders[r.index] = v;
                ShowSliderValue(r);
                RefreshTabDots();
                FaceChanged();
            });

            // value tag + reset-this-row
            r.chipRt = MenuUi.Rect("Value", root).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f - 40f, 0f), new Vector2(90f, 30f));
            r.chip = MenuUi.Img(r.chipRt, "Bg", MenuUi.RoundedRect(2), Color.clear, true);
            r.chip.rectTransform.Fill();
            r.chipEdge = MenuUi.Img(r.chipRt, "Edge", MenuUi.RoundedRectOutline(2, 1.2f), Color.white, true);
            r.chipEdge.rectTransform.Fill();
            r.value = MenuUi.Label(r.chipRt, "Text", "", T.bodyFont, 17, Color.white);
            r.value.rectTransform.Fill();
            Shade(r.value);
            r.undo = CustomButton(root, "Undo", new Vector2(CustW - 12f - 34f, -(RowH - 34f) * 0.5f), new Vector2(34f, 34f), -1, () => r.s.value = 0f);
            Icon(r.undo, MenuUi.IconKind.Undo, 20f);

            _faceRows.Add(r);
            ShowSliderValue(r);
        }

        // ---------------- state ----------------

        private void PaintCust(CustButton b, float g)
        {
            var p = _sky.Preset;
            var solid = p.dialog; solid.a = 1f;
            bool sel = b.selected != null && b.selected();
            Color ink;
            if (b.bevel == null)
                ink = Color.Lerp(A(Color.white, 0.55f), p.accent, g);
            else if (b.primary)
            {
                b.bevel.fill.color = Color.Lerp(p.accent, Color.Lerp(p.accent, Color.white, 0.18f), g);
                b.bevel.edge.color = p.accent;
                b.bevel.top.color = A(Color.white, 0.45f + 0.35f * g);
                ink = solid;
            }
            else
            {
                var idle = sel ? Color.Lerp(solid, p.accent, 0.16f) : Color.Lerp(solid, Color.white, 0.05f);
                var hover = sel ? Color.Lerp(idle, Color.white, 0.05f) : Color.Lerp(solid, Color.white, 0.11f);
                b.bevel.fill.color = Color.Lerp(idle, hover, g);
                b.bevel.edge.color = sel ? A(p.accent, 0.7f) : Color.Lerp(A(Color.white, 0.2f), A(Color.white, 0.55f), g);
                b.bevel.top.color = Color.Lerp(A(Color.white, 0.55f), p.accent, sel ? 1f : g);
                ink = sel ? Color.white : Color.Lerp(A(Color.white, 0.78f), Color.white, g);
            }
            foreach (var (gr, a) in b.ink) gr.color = A(ink, a);
            b.extra?.Invoke(g);
        }

        private void RefreshCust()
        {
            foreach (var b in _custButtons) PaintCust(b, b.sel.Glow);
            RefreshTabDots();
        }

        private void RefreshTabDots()
        {
            for (int t = 0; t < _tabDots.Count; t++)
            {
                bool changed = (t == 0 && _face.eyeStyle != ToonEyeStyle.Drawn) || (t == BrowTab && _face.browStyle != ToonBrowStyle.Classic)
                    || (t == HairTab && (_face.hair != PlayerHair.Light || _face.hairColor != 0)) || (t == BodyTab && _face.auraColor != 0)
                    || (t == SkinTab && _face.skinTone != 0);
                foreach (int i in CustTabs[t].rows) changed |= _face.sliders[i] != 0f;
                _tabDots[t].gameObject.SetActive(changed);
            }
        }

        private void SetCustTab(int t, bool select)
        {
            _custTab = (t + CustTabs.Length) % CustTabs.Length;
            for (int i = 0; i < _tabPages.Count; i++) _tabPages[i].SetActive(i == _custTab);
            RefreshHairPreviews();
            RebuildCustNav();
            RefreshCust();
            if (select && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_tabButtons[_custTab].sel.gameObject);
        }

        private static void Nav(Selectable s, Selectable up, Selectable down, Selectable left, Selectable right)
        {
            var n = s.navigation;
            n.mode = Navigation.Mode.Explicit;
            n.selectOnUp = up; n.selectOnDown = down; n.selectOnLeft = left; n.selectOnRight = right;
            s.navigation = n;
        }

        /// <summary>Explicit navigation for the current tab: Random, tab row, eye / hair tiles, slider rows, Reset all / Done.</summary>
        private void RebuildCustNav()
        {
            var rows = _faceRows.FindAll(r => r.tab == _custTab);
            var tab = _tabButtons[_custTab].sel;
            Selectable eye = _eyeTiles[(int)_face.eyeStyle].sel;
            Selectable brow = _browTiles[(int)_face.browStyle].sel;
            Selectable hair = _hairTiles[(int)_face.hair].sel;
            Selectable colour = _hairColorTiles[_face.hairColor].sel;
            Selectable skinSel = _skinTiles[_face.skinTone].sel;
            Selectable first = _custTab == 0 ? eye : _custTab == BrowTab ? brow : _custTab == HairTab ? hair : _custTab == SkinTab ? skinSel : rows[0].s;
            Selectable aura = _auraColorTiles[_face.auraColor].sel;
            Selectable last = _custTab == BodyTab ? aura : rows.Count > 0 ? rows[^1].s : _custTab == HairTab ? colour : _custTab == SkinTab ? skinSel : tab;
            for (int i = 0; i < _tabButtons.Count; i++)
                Nav(_tabButtons[i].sel, _custRandom.sel, first, i > 0 ? _tabButtons[i - 1].sel : null, i < _tabButtons.Count - 1 ? _tabButtons[i + 1].sel : null);
            Nav(_custRandom.sel, null, tab, null, null);
            if (_custTab == 0)
                for (int i = 0; i < _eyeTiles.Count; i++)
                    Nav(_eyeTiles[i].sel, tab, rows[0].s, i > 0 ? _eyeTiles[i - 1].sel : null, i < _eyeTiles.Count - 1 ? _eyeTiles[i + 1].sel : null);
            if (_custTab == BrowTab)
                for (int i = 0; i < _browTiles.Count; i++)
                    Nav(_browTiles[i].sel, tab, rows[0].s, i > 0 ? _browTiles[i - 1].sel : null, i < _browTiles.Count - 1 ? _browTiles[i + 1].sel : null);
            if (_custTab == HairTab)
                for (int i = 0; i < _hairTiles.Count; i++)
                    Nav(_hairTiles[i].sel, tab, colour, i > 0 ? _hairTiles[i - 1].sel : null, i < _hairTiles.Count - 1 ? _hairTiles[i + 1].sel : null);
            if (_custTab == HairTab)
                for (int i = 0; i < _hairColorTiles.Count; i++)
                    Nav(_hairColorTiles[i].sel, hair, _custDone.sel, i > 0 ? _hairColorTiles[i - 1].sel : null, i < _hairColorTiles.Count - 1 ? _hairColorTiles[i + 1].sel : null);
            if (_custTab == SkinTab)
            {
                int per = (_skinTiles.Count + 1) / 2;
                for (int i = 0; i < _skinTiles.Count; i++)
                {
                    int r = i / per, c = i % per;
                    Selectable up = r == 0 ? tab : _skinTiles[i - per].sel;
                    Selectable down = r == 0 && i + per < _skinTiles.Count ? _skinTiles[i + per].sel : _custDone.sel;
                    Nav(_skinTiles[i].sel, up, down, c > 0 ? _skinTiles[i - 1].sel : null, c < per - 1 && i + 1 < _skinTiles.Count ? _skinTiles[i + 1].sel : null);
                }
            }
            if (_custTab == BodyTab)
                for (int i = 0; i < _auraColorTiles.Count; i++)
                    Nav(_auraColorTiles[i].sel, rows[^1].s, _custDone.sel, i > 0 ? _auraColorTiles[i - 1].sel : null, i < _auraColorTiles.Count - 1 ? _auraColorTiles[i + 1].sel : null);
            for (int k = 0; k < rows.Count; k++)
            {
                Selectable up = k > 0 ? rows[k - 1].s : _custTab == 0 ? eye : _custTab == BrowTab ? brow : tab;
                Selectable down = k < rows.Count - 1 ? rows[k + 1].s : _custTab == BodyTab ? aura : _custDone.sel;
                Nav(rows[k].s, up, down, null, null); // left / right move the slider
                foreach (var b in new[] { rows[k].minus, rows[k].plus, rows[k].undo }) Nav(b.sel, up, down, rows[k].s, rows[k].s);
            }
            Nav(_custReset.sel, last, tab, null, _custDone.sel);
            Nav(_custDone.sel, last, tab, _custReset.sel, null);
        }

        private void Nudge(FaceRow r, float d) => r.s.value = Mathf.Clamp(Mathf.Round((r.s.value + d) * 100f) / 100f, -1f, 1f);

        private void Randomize()
        {
            _face.eyeStyle = (ToonEyeStyle)UnityEngine.Random.Range(0, PlayerAppearance.EyeNames.Length);
            _face.browStyle = (ToonBrowStyle)UnityEngine.Random.Range(0, PlayerAppearance.BrowNames.Length);
            _face.hair = (PlayerHair)UnityEngine.Random.Range(0, PlayerAppearance.HairNames.Length);
            _face.hairColor = UnityEngine.Random.Range(0, PlayerAppearance.HairColors.Length);
            _face.auraColor = UnityEngine.Random.Range(0, PlayerAppearance.AuraColors.Length);
            _face.skinTone = UnityEngine.Random.Range(0, PlayerAppearance.SkinTones.Length);
            for (int i = 0; i < FaceSliders.Count; i++) _face.sliders[i] = Mathf.Round(UnityEngine.Random.Range(-0.7f, 0.7f) * 100f) / 100f;
            SyncFaceControls();
            RebuildCustNav();
            FaceChanged();
        }

        private void ResetFace()
        {
            _face = default;
            SyncFaceControls();
            RebuildCustNav();
            FaceChanged();
        }

        private void ShowSliderValue(FaceRow r)
        {
            var def = PlayerAppearance.Sliders[r.index];
            float v = _face.sliders[r.index];
            bool set = Mathf.Abs(v) >= 0.005f;
            r.value.text = set ? (v < 0f ? def.Low : def.High) + " " + Mathf.RoundToInt(Mathf.Abs(v) * 100f) + "%" : "Default";
            r.chipRt.sizeDelta = new Vector2(MenuUi.Width(r.value) + 26f, 30f);
            var p = _sky.Preset;
            var solid = p.dialog; solid.a = 1f;
            r.chip.color = set ? Color.Lerp(solid, p.accent, 0.16f) : Color.clear;
            r.chipEdge.color = set ? p.accent : A(Color.white, 0.2f);
            r.value.color = set ? Color.white : A(Color.white, 0.65f);
            r.undo.rt.gameObject.SetActive(set);
            float t = (r.s.value + 1f) * 0.5f;
            r.fill.rectTransform.anchorMin = new Vector2(Mathf.Min(0.5f, t), 0.5f);
            r.fill.rectTransform.anchorMax = new Vector2(Mathf.Max(0.5f, t), 0.5f);
            r.fill.color = p.accent;
        }

        private void SyncFaceControls()
        {
            foreach (var r in _faceRows)
            {
                r.s.SetValueWithoutNotify(_face.sliders[r.index]);
                ShowSliderValue(r);
            }
            RefreshCust();
        }

        private GameObject CustomizeFirstSelection() => _tabButtons[_custTab].sel.gameObject;

        private void FaceChanged()
        {
            if (_fighter != null) _fighter.SetFace(_face);
            RefreshHairPreviews();
        }

        private void OpenCustomize()
        {
            if (_fighter == null) { ShowToast("Fighters · " + T.comingSoon); return; }
            _face = PlayerAppearance.Load();
            EnsureEyePreviews();
            SetCustTab(0, false);
            SyncFaceControls();
            SetState(State.Customize);
        }

        private void CloseCustomize()
        {
            PlayerAppearance.Save(_face);
            SetState(State.Main);
        }

        /// <summary>Screenshots / tests: opens Customize with these face choices (and optionally another tab).</summary>
        public void ShowCustomize(FaceCustomization face, int tab = 0)
        {
            _face = face.Sanitized();
            EnsureEyePreviews();
            SetCustTab(tab, false);
            SyncFaceControls();
            FaceChanged();
            SetState(State.Customize, instant: true);
        }

        private void UpdateCustomize()
        {
            if (_custDlg.alpha <= 0.01f) return;
            _custStatus.text = _fighter != null && _fighter.Rebuilding ? "Updating your fighter…"
                : TouchLayout ? "Drag your fighter to turn them, tap for an expression." : "Drag your fighter to turn them  ·  Q / E  ·  click for an expression";
            var kb = Keyboard.current;
            var gp = Gamepad.current;

            // key hints follow the last device used; hidden on touch
            bool pad = _padGlyphs;
            if (gp != null && (gp.buttonSouth.wasPressedThisFrame || gp.buttonEast.wasPressedThisFrame || gp.buttonWest.wasPressedThisFrame || gp.buttonNorth.wasPressedThisFrame
                || gp.leftShoulder.wasPressedThisFrame || gp.rightShoulder.wasPressedThisFrame || gp.startButton.wasPressedThisFrame
                || gp.dpad.ReadValue() != Vector2.zero || gp.leftStick.ReadValue().sqrMagnitude > 0.25f)) pad = true;
            if (kb != null && kb.anyKey.wasPressedThisFrame) pad = false;
            bool shown = !TouchLayout;
            if (!_glyphsInit || pad != _padGlyphs || shown != _glyphsShown)
            {
                _glyphsInit = true;
                _padGlyphs = pad;
                _glyphsShown = shown;
                foreach (var g in _keyGlyphs) { g.go.SetActive(shown); g.t.text = pad ? g.pad : g.kb; }
            }

            var es = EventSystem.current;
            var current = es != null ? es.currentSelectedGameObject : null;
            if (Current == State.Customize && _custDlg.interactable)
            {
                if ((kb != null && kb.zKey.wasPressedThisFrame) || (gp != null && gp.leftShoulder.wasPressedThisFrame)) SetCustTab(_custTab - 1, true);
                else if ((kb != null && kb.cKey.wasPressedThisFrame) || (gp != null && gp.rightShoulder.wasPressedThisFrame)) SetCustTab(_custTab + 1, true);
                else if ((kb != null && kb.rKey.wasPressedThisFrame) || (gp != null && gp.buttonNorth.wasPressedThisFrame)) Randomize();
                else if ((kb != null && kb.backspaceKey.wasPressedThisFrame) || (gp != null && gp.buttonWest.wasPressedThisFrame)) ResetFace();
                else if (gp != null && gp.startButton.wasPressedThisFrame) { CloseCustomize(); return; }
                else if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                    && (current == null || current.GetComponent<GlowSelectable>() == null)) { CloseCustomize(); return; }
            }

            var accent = _sky.Preset.accent;
            foreach (var r in _faceRows)
            {
                bool on = current == r.s.gameObject;
                r.label.color = on ? Color.Lerp(Color.white, accent, 0.6f) : Color.white;
                r.handle.color = on ? accent : Color.white;
                r.handle.rectTransform.parent.localScale = Vector3.one * (on ? 1.15f : 1f);
                r.highlight.color = on ? A(Color.white, 0.06f) : Color.clear;
            }
        }

        // ---------------- hair previews ----------------

        /// <summary>
        /// Redraws the hair tiles' portraits (the player's head, eyes and hair colour wearing each style) when the Hair tab
        /// is showing and something they depend on has changed. Picking a hairstyle alone doesn't redraw them.
        /// </summary>
        private void RefreshHairPreviews()
        {
            if (_custTab != HairTab || _hairPreviews.Count == 0) return;
            var keyFace = _face.Sanitized();
            keyFace.hair = PlayerHair.Light;
            string key = JsonUtility.ToJson(keyFace);
            if (key == _hairPreviewKey) return;
            _hairPreviewKey = key;
            _hairTex ??= new Texture2D[_hairPreviews.Count];
            if (keyFace.eyeStyle == ToonEyeStyle.HandMade) ToonEyeLayers.EnsureLoaded();
            for (int i = 0; i < _hairPreviews.Count; i++)
            {
                try
                {
                    var f = keyFace;
                    f.hair = (PlayerHair)i;
                    var old = _hairTex[i];
                    _hairTex[i] = HairPreview.Render(PlayerAppearance.Apply(CharacterPresets.Hero(), f), old);
                    if (_hairPreviews[i].sprite == null || old != _hairTex[i])
                        _hairPreviews[i].sprite = Sprite.Create(_hairTex[i], new Rect(0, 0, HairPreview.Width, HairPreview.Height), new Vector2(0.5f, 0.5f), 100f);
                    _hairPreviews[i].enabled = true;
                }
                catch (Exception e) { Debug.LogWarning("[Menu] Hair preview " + (PlayerHair)i + " failed: " + e.Message); }
            }
        }

        // ---------------- eye previews ----------------

        /// <summary>Paints each eye style once with the real face painter and keeps the eye strip for the tiles.</summary>
        private void EnsureEyePreviews()
        {
            if (_eyePreviewsMade) return;
            _eyePreviewsMade = true;
            for (int i = 0; i < _eyePreviews.Count; i++)
            {
                try
                {
                    _eyePreviews[i].sprite = EyePreview((ToonEyeStyle)i);
                    _eyePreviews[i].enabled = true;
                }
                catch (Exception e) { Debug.LogWarning("[Menu] Eye preview " + (ToonEyeStyle)i + " failed: " + e.Message); }
            }
            for (int i = 0; i < _browPreviews.Count; i++)
            {
                try
                {
                    _browPreviews[i].sprite = BrowPreview((ToonBrowStyle)i);
                    _browPreviews[i].enabled = true;
                }
                catch (Exception e) { Debug.LogWarning("[Menu] Brow preview " + (ToonBrowStyle)i + " failed: " + e.Message); }
            }
        }

        /// <summary>Paints the hero's face wearing a brow style (over the Sharp eye, as on the Brow Bench) and keeps one brow and eye.</summary>
        private static Sprite BrowPreview(ToonBrowStyle style)
        {
            var hero = CharacterPresets.Hero();
            var settings = hero.toonHead?.Clone() ?? new ToonHeadSettings();
            settings.eyeStyle = ToonEyeStyle.Sharp;
            settings.browStyle = style;
            var H = ToonHead.Apply(settings);
            var px = ToonFacePainter.Paint(H, settings, hero.eyeColor, hero.hairColor, hero.skinTone);
            return Crop(px, new Vector3(H.EyeX - 0.14f, H.EyeY - 0.06f, 0f), new Vector3(H.EyeX + 0.16f, H.BrowY + 0.08f, 0f), "BrowPreview_" + style);
        }

        private static Sprite EyePreview(ToonEyeStyle style)
        {
            if (style == ToonEyeStyle.HandMade) ToonEyeLayers.EnsureLoaded();
            var hero = CharacterPresets.Hero();
            var settings = hero.toonHead?.Clone() ?? new ToonHeadSettings();
            settings.eyeStyle = style;
            var H = ToonHead.Apply(settings);
            var px = ToonFacePainter.Paint(H, style, hero.eyeColor, hero.hairColor, hero.skinTone);
            float hx = H.EyeX + 0.12f;
            return Crop(px, new Vector3(-hx, H.EyeY - 0.075f, 0f), new Vector3(hx, H.EyeY + 0.085f, 0f), "EyePreview_" + style);
        }

        /// <summary>A half-size sprite of the painted face between two head-unit corners.</summary>
        private static Sprite Crop(Color[] px, Vector3 headLo, Vector3 headHi, string name)
        {
            int N = ToonFacePainter.Size;
            Vector2 lo = ToonFacePainter.Uv(headLo) * N;
            Vector2 hi = ToonFacePainter.Uv(headHi) * N;
            int x0 = Mathf.Clamp((int)lo.x, 0, N - 2), y0 = Mathf.Clamp((int)lo.y, 0, N - 2);
            int w = Mathf.Min((int)(hi.x - lo.x), N - x0) / 2, h = Mathf.Min((int)(hi.y - lo.y), N - y0) / 2;
            var outPx = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // 2×2 box filter on premultiplied colour (the painter's transparent pixels carry skin colour)
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int dy = 0; dy < 2; dy++)
                        for (int dx = 0; dx < 2; dx++)
                        {
                            var c = px[(y0 + y * 2 + dy) * N + x0 + x * 2 + dx];
                            r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                        }
                    outPx[y * w + x] = a > 0.0001f ? new Color(r / a, g / a, b / a, a * 0.25f) : Color.clear;
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(outPx);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
