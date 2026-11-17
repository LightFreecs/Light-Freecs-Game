using System.Collections.Generic;
using System.Globalization;
using LightMMORPG.Config;
using LightMMORPG.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LightMMORPG.Menu
{
    // The Stats screen: level, XP bar, points to spend, the five fighter stats with − / + and what they do in combat,
    // Reset (free respec) and Done. Rules live in Core/FighterStats.cs; the allocation is saved on the account
    // (AccountStore.SetStats) when the screen closes. Guests see the base numbers and cannot spend.
    public sealed partial class MainMenuUI
    {
        private CanvasGroup _statsDlg;
        private Text _statsLevel, _statsPoints, _statsXp, _statsNote;
        private RectTransform _statsXpFill;
        private readonly List<(StatId id, Text value, Text result, GlowSelectable minus, GlowSelectable plus)> _statRows = new();
        private GlowSelectable _statsDone;
        private StatAllocation _statsEdit;
        /// <summary>Screenshots: total XP to show instead of the signed-in account (−1 = use the account).</summary>
        private long _statsPreviewXp = -1;

        /// <summary>Screenshots: show the guest view even when an account is signed in.</summary>
        private bool _statsForceGuest;

        private bool StatsPreview => _statsPreviewXp >= 0 || _statsForceGuest;
        private bool StatsCanSpend => !_statsForceGuest && (StatsPreview || Store.SignedIn);
        private long StatsXp => _statsForceGuest ? 0 : _statsPreviewXp >= 0 ? _statsPreviewXp : Store.Current != null ? Store.Current.totalXp : 0;
        private int StatsLevel => Progression.LevelFor(StatsXp);

        private void BuildStatsDialog(RectTransform parent)
        {
            float pw = Mathf.Max(T.subPanelWidth, 1000f), w = pw - 80f;
            var panel = Dialog(parent, "Stats", out _statsDlg, 840f);
            DialogHeader(panel, "Stats", null);

            Text Line(string name, Font font, int size, float x, float y, float width, float height, TextAnchor align, float alpha = 1f)
            {
                var t = MenuUi.Label(panel, name, "", font, size, new Color(1f, 1f, 1f, alpha), align);
                t.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, y), new Vector2(width, height));
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                return t;
            }

            _statsLevel = Line("Level", T.boldFont, 30, -w * 0.25f, -110f, w * 0.5f, 40f, TextAnchor.MiddleLeft);
            _statsPoints = Line("Points", T.boldFont, 30, w * 0.25f, -110f, w * 0.5f, 40f, TextAnchor.MiddleRight);
            Bind(p => _statsPoints.color = p.accent);
            var bar = MenuUi.Img(panel, "XpBar", MenuUi.RoundedRect(6), new Color(0f, 0f, 0f, 0.35f), true);
            bar.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(w, 14f));
            var fill = MenuUi.Img(bar.transform, "Fill", MenuUi.RoundedRect(6), Color.white, true);
            Bind(p => fill.color = p.accent);
            _statsXpFill = fill.rectTransform;
            SetFill(_statsXpFill, 0f);
            _statsXp = Line("Xp", T.bodyFont, 22, 0f, -180f, w, 30f, TextAnchor.MiddleLeft, 0.7f);

            const float rowH = 88f, top = -232f;
            for (int i = 0; i < FighterStats.All.Length; i++)
            {
                StatId id = FighterStats.All[i];
                float y = top - i * rowH;
                var name = Line("Name_" + id, T.boldFont, 30, -w * 0.5f + 150f, y, 300f, 40f, TextAnchor.MiddleLeft);
                name.text = FighterStats.DisplayName(id);
                var desc = Line("Desc_" + id, T.bodyFont, 20, -w * 0.5f + 150f, y - 40f, 300f, 28f, TextAnchor.MiddleLeft, 0.6f);
                desc.text = FighterStats.Describe(id);
                var minus = DialogButton(panel, "−", null, y - 4f, 72f, 64f, () => ChangeStat(id, -1), -136f, centred: true);
                minus.name = "Minus_" + id;
                var value = Line("Value_" + id, T.boldFont, 28, -42f, y - 4f, 100f, 64f, TextAnchor.MiddleCenter);
                var plus = DialogButton(panel, "+", null, y - 4f, 72f, 64f, () => ChangeStat(id, +1), 52f, centred: true);
                plus.name = "Plus_" + id;
                var result = Line("Result_" + id, T.boldFont, 24, 112f + 180f, y, 360f, 40f, TextAnchor.MiddleLeft);
                var per = Line("Per_" + id, T.bodyFont, 20, 112f + 180f, y - 40f, 360f, 28f, TextAnchor.MiddleLeft, 0.6f);
                per.text = PerPoint(id);
                _statRows.Add((id, value, result, minus, plus));
            }

            float yb = top - FighterStats.All.Length * rowH - 4f;
            _statsNote = Line("Note", T.bodyFont, 22, 0f, yb, w, 30f, TextAnchor.MiddleLeft, 0.6f);
            yb -= 48f;
            float hw = (w - 16f) * 0.5f;
            var reset = DialogButton(panel, "Reset", null, yb, hw, 76f, ResetStats, -hw * 0.5f - 8f, centred: true);
            _statsDone = DialogButton(panel, "Done", null, yb, hw, 76f, CloseStats, hw * 0.5f + 8f, centred: true);
            panel.sizeDelta = new Vector2(pw, -yb + 76f + 40f);

            // navigation: − / + side by side, up / down along the rows, then Reset / Done (wraps round)
            int last = _statRows.Count - 1;
            for (int i = 0; i <= last; i++)
            {
                var m = _statRows[i].minus.navigation;
                m.selectOnRight = _statRows[i].plus;
                m.selectOnUp = i > 0 ? _statRows[i - 1].minus : reset;
                m.selectOnDown = i < last ? _statRows[i + 1].minus : reset;
                _statRows[i].minus.navigation = m;
                var p = _statRows[i].plus.navigation;
                p.selectOnLeft = _statRows[i].minus;
                p.selectOnUp = i > 0 ? _statRows[i - 1].plus : _statsDone;
                p.selectOnDown = i < last ? _statRows[i + 1].plus : _statsDone;
                _statRows[i].plus.navigation = p;
            }
            var nr = reset.navigation; nr.selectOnUp = _statRows[last].minus; nr.selectOnDown = _statRows[0].minus; nr.selectOnRight = _statsDone; reset.navigation = nr;
            var nd = _statsDone.navigation; nd.selectOnUp = _statRows[last].plus; nd.selectOnDown = _statRows[0].plus; nd.selectOnLeft = reset; _statsDone.navigation = nd;
        }

        private static string Pct(float v) => (v * 100f).ToString("0.##", CultureInfo.InvariantCulture) + "%";

        private static string PerPoint(StatId id) => id switch
        {
            StatId.Power => "+" + Pct(FighterStats.DamagePerPower) + " damage per point",
            StatId.Vitality => "+" + Pct(FighterStats.HealthPerVitality) + " health per point",
            StatId.Speed => "+" + Pct(FighterStats.SpeedPerSpeed) + " speed per point",
            StatId.Ki => "+" + Pct(FighterStats.EnergyPerKi) + " energy and regen per point",
            _ => "−" + Pct(FighterStats.DamageTakenPerDefense) + " damage, +" + Pct(FighterStats.GuardPerDefense) + " guard per point",
        };

        /// <summary>The concrete combat number(s) a stat gives with the current points.</summary>
        private static string ResultText(StatId id, FighterNumbers n, GameConfig c)
        {
            var inv = CultureInfo.InvariantCulture;
            return id switch
            {
                StatId.Power => "Damage " + Pct(n.DamageMultiplier),
                StatId.Vitality => "Health " + n.MaxHealth(c.maxHealth).ToString("N0", inv),
                StatId.Speed => "Run " + (c.runSpeed * n.SpeedMultiplier).ToString("0.0#", inv) + " · Sprint " + (c.sprintSpeed * n.SpeedMultiplier).ToString("0.0#", inv) + " m/s",
                StatId.Ki => "Energy " + n.MaxEnergy(c.maxEnergy).ToString("N0", inv) + " · Regen " + (c.energyRegenPerSecond * n.EnergyRegenMultiplier).ToString("0.0#", inv) + "/s",
                _ => "Damage taken " + Pct(n.DamageTakenMultiplier) + " · Guard " + n.MaxGuard(c.maxGuard).ToString("N0", inv),
            };
        }

        private void RefreshStats()
        {
            if (_statsLevel == null) return;
            long xp = StatsXp;
            int level = StatsLevel;
            string title = "";
            foreach (var u in Progression.UnlockedAt(level))
                if (u.Kind == UnlockKind.Title) title = u.Id;
            _statsLevel.text = StatsCanSpend ? "Level " + level + (title.Length > 0 ? " · " + title : "") : "Guest · Level 1";
            int left = FighterStats.Unspent(level, _statsEdit);
            _statsPoints.text = !StatsCanSpend ? "" : left == 1 ? "1 point to spend" : left + " points to spend";
            var (into, needed) = Progression.LevelProgress(xp);
            _statsXp.text = !StatsCanSpend ? "Sign in to earn XP, levels and stat points."
                : needed > 0 ? $"{into:N0} / {needed:N0} XP to level {level + 1}" : $"Max level · {xp:N0} XP";
            SetFill(_statsXpFill, StatsCanSpend ? LevelFraction(xp) : 0f);
            var n = FighterStats.Compute(_statsEdit);
            var config = GameConfig.Load();
            foreach (var row in _statRows)
            {
                row.value.text = _statsEdit[row.id] + " / " + FighterStats.MaxPerStat;
                row.result.text = ResultText(row.id, n, config);
            }
            _statsNote.text = "1 point per level · up to " + FighterStats.MaxPerStat + " per stat · Reset gives every point back for free";
        }

        private void ChangeStat(StatId id, int delta)
        {
            if (!StatsCanSpend) { ShowToast("Sign in to earn stat points"); return; }
            int level = StatsLevel;
            if (delta > 0 && !FighterStats.TryAdd(level, ref _statsEdit, id))
                ShowToast(_statsEdit[id] >= FighterStats.MaxPerStat ? FighterStats.DisplayName(id) + " is at the maximum" : "No points left · level up to earn more");
            else if (delta < 0) FighterStats.TryRemove(ref _statsEdit, id);
            RefreshStats();
        }

        private void ResetStats()
        {
            if (_statsEdit.Total > 0) ShowToast("All points returned");
            _statsEdit = FighterStats.Respec();
            RefreshStats();
        }

        private void OpenStats()
        {
            _statsPreviewXp = -1;
            _statsForceGuest = false;
            _statsEdit = Store.Stats;
            RefreshStats();
            SetState(State.Stats);
        }

        private void CloseStats()
        {
            if (!StatsPreview && Store.SignedIn && !Store.SetStats(_statsEdit)) ShowToast("Could not save stats");
            _statsPreviewXp = -1;
            _statsForceGuest = false;
            SetState(State.Main);
        }

        /// <summary>Screenshots / tests: opens Stats showing this much XP and this allocation; nothing is saved.</summary>
        public void ShowStatsPreview(long totalXp, StatAllocation stats)
        {
            _statsForceGuest = false;
            _statsPreviewXp = System.Math.Max(0, totalXp);
            _statsEdit = FighterStats.Validated(StatsLevel, stats);
            RefreshStats();
            SetState(State.Stats, instant: true);
        }

        /// <summary>Screenshots: opens Stats as a guest sees it.</summary>
        public void ShowStatsGuest()
        {
            _statsPreviewXp = -1;
            _statsForceGuest = true;
            _statsEdit = default;
            RefreshStats();
            SetState(State.Stats, instant: true);
        }
    }
}
