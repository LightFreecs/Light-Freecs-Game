using System.Collections.Generic;
using System.Text;
using LightMMORPG.Cameras;
using LightMMORPG.Combat;
using LightMMORPG.Input;
using UnityEngine;
using UnityEngine.UI;

namespace LightMMORPG.UI
{
    /// <summary>Keeps a RectTransform inside <see cref="Screen.safeArea"/> (iPhone notch / home bar).</summary>
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private Rect _last;
        private Vector2Int _lastScreen;

        private void LateUpdate()
        {
            Rect sa = Screen.safeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (sa == _last && screen == _lastScreen) return;
            _last = sa;
            _lastScreen = screen;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            rt.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>
    /// Runtime-built HUD (Battle HUD bench, <c>docs/bench/hud-bench.html</c>): power gauge (strength ring) + slanted HP /
    /// guard / energy / aura reserve bars with damage trails (top-left), ability icons with cooldown sweep + energy dimming
    /// (bottom-centre, PC only — the touch buttons show their own), mirrored locked-on target bar (top-right; no power level,
    /// so an opponent's strength ceiling stays hidden), name plates with HP above other players, KO / respawn countdown +
    /// KO feed (centre), and a scoreboard (top-right, under the target).
    /// </summary>
    public sealed class GameHud : MonoBehaviour
    {
        // Battle HUD bench colours (docs/bench/hud-bench.html)
        private static readonly Color HpA = Hex(0xc6ff5c), HpB = Hex(0x36d98a), LowA = Hex(0xffb03d), LowB = Hex(0xff4d43);
        private static readonly Color EnA = Hex(0x6fe6ff), EnB = Hex(0x2c8dff), GuardColor = Hex(0xffd25a), Ko = Hex(0xff4d43);
        private static readonly Color HpLow = new(1f, 0.3f, 0.25f), ExhaustText = Hex(0xff8a7a);
        private static readonly Color StA = Hex(0xd6fff4), StB = Hex(0x3fd0b0);

        private static Color Hex(int rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        private Canvas _canvas;
        private RectTransform _safe, _plates;
        private HudBar _hp, _energy, _guard, _aura, _surge, _targetHp, _targetGuard, _stamina;
        private Text _staminaTag;
        private Text _targetName, _center, _feed, _score;
        // player cluster: power gauge (strength ring), name, numbers
        private RingShape _ring, _ringGlow, _surgeRing, _ember;
        private readonly ChevronShape[] _chevrons = new ChevronShape[3];
        private Text _powVal, _powLbl, _name, _hpNum, _enNum, _auraNum, _guardTag, _targetHpNum;
        private RectTransform _surgeRoot, _emberRt;
        private Color _auraColor = new(1f, 0.85f, 0.1f);
        private string _nameKey;
        private RectTransform _abilityRow, _targetRoot;
        private readonly List<AbilityIcon> _icons = new();
        private readonly Dictionary<PlayerStats, Plate> _plateMap = new();
        private readonly List<PlayerStats> _remove = new();
        private readonly StringBuilder _sb = new();
        private float _feedUntil;

        private sealed class AbilityIcon
        {
            public AttackKind Kind;
            public Image Bg;
            public RectTransform Sweep;
            public Text Label;
        }

        private sealed class Plate
        {
            public RectTransform Root;
            public Text Name;
            public HudBar Hp;
        }

        private void Awake()
        {
            if (Application.isBatchMode) { enabled = false; return; }
            _canvas = UiKit.CreateCanvas("HUDCanvas", 10);
            _canvas.transform.SetParent(transform, false);
            _plates = UiKit.Rect("NamePlates", _canvas.transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiKit.Stretch(_plates);
            _safe = UiKit.Rect("SafeArea", _canvas.transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiKit.Stretch(_safe);
            _safe.gameObject.AddComponent<SafeAreaFitter>();

            // own bars (Battle HUD bench layout, 1920 x 1080 reference)
            BuildPlayer();

            // abilities
            var bc = new Vector2(0.5f, 0f);
            _abilityRow = UiKit.Rect("Abilities", _safe, bc, bc, new Vector2(0f, 30f), new Vector2(5 * 96f, 86f));
            AddIcon(AttackKind.Heavy, "RMB hold\nHeavy", 0);
            AddIcon(AttackKind.Blast, "1\nBlast", 1);
            AddIcon(AttackKind.Beam, "2\nBeam", 2);
            AddIcon(AttackKind.Surge, "3\nSurge", 3);
            AddIcon(AttackKind.Light, "C\nCharge", 4);

            // locked-on target, top right, mirrored (no power level: an opponent's strength ceiling stays hidden)
            var tr0 = new Vector2(1f, 1f);
            _targetRoot = UiKit.Rect("Target", _safe, tr0, tr0, new Vector2(-44f, -40f), new Vector2(640f, 90f));
            var ttl = new Vector2(0f, 1f);
            _targetName = HudBar.Label(_targetRoot, "Name", ttl, ttl, new Vector2(0f, 0f), new Vector2(614f, 24f), 22, TextAnchor.UpperRight, FontStyle.Bold);
            _targetHp = HudBar.Create(_targetRoot, "HP", ttl, ttl, new Vector2(118f, 26f), new Vector2(500f, 32f), 10, mirror: true);
            _targetHpNum = HudBar.Label(_targetRoot, "HPNum", ttl, ttl, new Vector2(136f, 26f), new Vector2(200f, 32f), 24, TextAnchor.MiddleLeft);
            _targetGuard = HudBar.Create(_targetRoot, "Guard", ttl, ttl, new Vector2(418f, 66f), new Vector2(200f, 8f), mirror: true, trail: false, shadow: false);
            _targetGuard.SetFillColors(GuardColor, GuardColor);

            // centre messages
            var c = new Vector2(0.5f, 0.5f);
            _center = UiKit.Text(UiKit.Rect("Center", _safe, c, c, new Vector2(0f, 120f), new Vector2(900f, 140f)), "", 56, TextAnchor.MiddleCenter, Color.white);
            _feed = UiKit.Text(UiKit.Rect("Feed", _safe, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(900f, 40f)), "", 26, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.5f));

            // scoreboard
            var tr = new Vector2(1f, 1f);
            var sbRoot = UiKit.Rect("Scoreboard", _safe, tr, tr, new Vector2(-24f, -150f), new Vector2(380f, 180f)); // under the target bar
            UiKit.Image(sbRoot, new Color(0f, 0f, 0f, 0.35f));
            _score = UiKit.Text(sbRoot, "", 19, TextAnchor.UpperLeft, Color.white, FontStyle.Normal);
            _score.rectTransform.offsetMin = new Vector2(12f, 8f);
            _score.rectTransform.offsetMax = new Vector2(-12f, -8f);

            PlayerStats.OnKnockout += OnKnockout;
        }

        private void OnDestroy() => PlayerStats.OnKnockout -= OnKnockout;

        private void AddIcon(AttackKind kind, string label, int slot)
        {
            var rt = UiKit.Rect(kind.ToString(), _abilityRow, Vector2.zero, Vector2.zero, new Vector2(slot * 96f + 5f, 0f), new Vector2(86f, 86f));
            var icon = new AbilityIcon { Kind = kind, Bg = UiKit.Image(rt, new Color(0.15f, 0.2f, 0.35f, 0.85f)) };
            icon.Sweep = UiKit.Rect("Cooldown", rt, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            icon.Sweep.anchorMin = Vector2.zero;
            icon.Sweep.anchorMax = new Vector2(1f, 0f);
            icon.Sweep.offsetMin = icon.Sweep.offsetMax = Vector2.zero;
            UiKit.Image(icon.Sweep, new Color(0f, 0f, 0f, 0.65f));
            icon.Label = UiKit.Text(rt, label, 17, TextAnchor.MiddleCenter, Color.white);
            _icons.Add(icon);
        }

        private void OnKnockout(PlayerStats victim, PlayerStats attacker)
        {
            _feed.text = (attacker != null ? attacker.DisplayName : "?") + "  K.O.  " + victim.DisplayName;
            _feedUntil = Time.time + 4f;
        }

        private void LateUpdate()
        {
            var me = PlayerStats.Local;
            bool show = me != null && !PlayerInputReader.MenuOpen;
            _safe.gameObject.SetActive(show);
            _plates.gameObject.SetActive(show);
            if (!show) return;

            float dt = Time.unscaledDeltaTime;
            UpdatePlayer(me, dt);

            // abilities (PC)
            _abilityRow.gameObject.SetActive(!PlayerInputReader.TouchMode);
            var combat = PlayerCombat.Local;
            if (combat != null)
                foreach (var ic in _icons)
                {
                    bool charge = ic.Kind == AttackKind.Light; // "Charge energy" slot: shows charging state
                    float cd = charge ? 0f : combat.Cooldown01(ic.Kind);
                    ic.Sweep.anchorMin = Vector2.zero;
                    ic.Sweep.anchorMax = new Vector2(1f, cd);
                    bool afford = charge || combat.CanAfford(ic.Kind);
                    bool lit = charge ? me.Motor != null && me.Motor.IsCharging : (ic.Kind == AttackKind.Surge && me.SurgeActive) || (ic.Kind == AttackKind.Beam && combat.IsChargingBeam);
                    ic.Bg.color = lit ? new Color(0.9f, 0.55f, 0.15f, 0.95f) : afford ? new Color(0.15f, 0.2f, 0.35f, 0.85f) : new Color(0.08f, 0.08f, 0.1f, 0.7f);
                    if (ic.Kind == AttackKind.Heavy && combat.IsChargingHeavy)
                    {
                        // held heavy: the icon fills up from the bottom with the charge (the dark overlay shrinks to the top), then
                        // pulses white when full
                        float c = combat.HeavyCharge01;
                        ic.Sweep.anchorMin = new Vector2(0f, c);
                        ic.Sweep.anchorMax = Vector2.one;
                        ic.Bg.color = c >= 1f ? Color.Lerp(new Color(1f, 0.6f, 0.2f, 0.95f), Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 18f))
                                              : Color.Lerp(new Color(0.9f, 0.55f, 0.15f, 0.95f), new Color(1f, 0.25f, 0.1f, 0.98f), c);
                    }
                    ic.Label.color = afford ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                }

            // target
            var cam = ThirdPersonCamera.Instance;
            PlayerStats target = cam != null && cam.CurrentTarget != null ? cam.CurrentTarget.GetComponent<PlayerStats>() : null;
            _targetRoot.gameObject.SetActive(target != null);
            if (target != null)
            {
                _targetName.text = target.DisplayName + (target.Level.Value > 0 ? "  <color=#ffffffb3>Lv " + target.Level.Value + "</color>" : "") + (target.IsDown.Value ? "  (K.O.)" : "");
                float tf = target.Health.Value / Mathf.Max(1f, target.MaxHealth);
                _targetHp.Set(tf, dt);
                HpColors(_targetHp, tf);
                _targetHpNum.text = Mathf.CeilToInt(target.Health.Value).ToString();
                bool tBroken = target.GuardBroken.Value;
                _targetGuard.Set(tBroken ? 1f : target.Guard.Value / Mathf.Max(1f, target.MaxGuard), dt);
                _targetGuard.SetFillColors(tBroken ? Ko : GuardColor, tBroken ? Ko : GuardColor);
            }

            // centre
            if (me.IsDown.Value) _center.text = "K.O.!\n<size=30>Respawning in " + me.DownRemaining.Value.ToString("0.0") + "</size>";
            else if (me.GuardBroken.Value) _center.text = "<size=40>GUARD BREAK</size>";
            else _center.text = "";
            _feed.gameObject.SetActive(Time.time < _feedUntil);

            UpdateScoreboard();
            UpdatePlates(cam != null ? cam.GetComponent<UnityEngine.Camera>() : UnityEngine.Camera.main, me);
        }

        // ---------------- player cluster ----------------

        private void BuildPlayer()
        {
            var tl = new Vector2(0f, 1f);
            _auraColor = Characters.PlayerAppearance.AuraColors[Characters.PlayerAppearance.Load().auraColor].color;

            // power gauge: dark disc, track, strength ring in the aura colour, notches every 10 % (the 10 % start mark bright),
            // a pulsing glow and rising chevrons while charging, an outer Power Surge countdown
            var g = UiKit.Rect("PowerGauge", _safe, tl, tl, new Vector2(44f, -34f), new Vector2(168f, 168f));
            RingShape.Create(g, "Edge", 0.821f, 0.845f, HudBar.EdgeColor);
            RingShape.Create(g, "Disc", 0f, 0.833f, HudBar.BackColor);
            RingShape.Create(g, "Track", 0.643f, 0.786f, new Color(1f, 1f, 1f, 0.12f));
            _ringGlow = RingShape.Create(g, "Glow", 0.6f, 0.83f, Color.clear);
            _ring = RingShape.Create(g, "Ring", 0.643f, 0.786f, _auraColor);
            var notches = RingShape.Create(g, "Notches", 0.631f, 0.798f, new Color(7f / 255f, 11f / 255f, 24f / 255f, 0.8f));
            notches.Notches = 10;
            var start = RingShape.Create(g, "StartMark", 0.631f, 0.798f, new Color(1f, 1f, 1f, 0.85f));
            start.Notches = 1;
            start.NotchWidth = 3f;
            start.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -36f); // 10 % of a turn, clockwise
            _surgeRing = RingShape.Create(g, "Surge", 0.899f, 0.958f, Hex(0xffb347));
            var c = new Vector2(0.5f, 1f);
            _powVal = HudBar.Label(g, "Value", c, c, new Vector2(0f, 42f), new Vector2(168f, 64f), 58, TextAnchor.MiddleCenter);
            _powLbl = HudBar.Label(g, "Label", c, c, new Vector2(0f, 106f), new Vector2(168f, 20f), 14, TextAnchor.MiddleCenter, FontStyle.Bold);
            for (int i = 0; i < _chevrons.Length; i++)
            {
                var rt = UiKit.Rect("Chevron" + i, g, c, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22f, 12f));
                _chevrons[i] = rt.gameObject.AddComponent<ChevronShape>();
                _chevrons[i].raycastTarget = false;
            }

            _name = HudBar.Label(_safe, "Name", tl, tl, new Vector2(240f, 30f), new Vector2(600f, 26f), 19, TextAnchor.MiddleLeft, FontStyle.Bold);
            _hp = HudBar.Create(_safe, "HP", tl, tl, new Vector2(246f, 66f), new Vector2(620f, 40f), 10);
            _hpNum = HudBar.Label(_safe, "HPNum", tl, tl, new Vector2(600f, 66f), new Vector2(250f, 40f), 30, TextAnchor.MiddleRight);
            _guard = HudBar.Create(_safe, "Guard", tl, tl, new Vector2(236f, 112f), new Vector2(300f, 10f), trail: false, shadow: false);
            _guardTag = HudBar.Label(_safe, "GuardBreak", tl, tl, new Vector2(552f, 108f), new Vector2(200f, 18f), 15, TextAnchor.MiddleLeft, FontStyle.Bold);
            _guardTag.text = "GUARD BREAK";
            _guardTag.color = Ko;
            _energy = HudBar.Create(_safe, "Energy", tl, tl, new Vector2(226f, 132f), new Vector2(520f, 20f), 10);
            _energy.SetFillColors(EnB, EnA);
            _energy.TrailColor = new Color(1f, 1f, 1f, 0.75f);
            _energy.TrailHold = 0.35f;
            _energy.TrailRate = 1.2f;
            _enNum = HudBar.Label(_safe, "ENNum", tl, tl, new Vector2(760f, 130f), new Vector2(200f, 24f), 22, TextAnchor.MiddleLeft);
            // aura reserve: a fuse in the aura colour that only ever burns down, with an ember at its end while burning
            _aura = HudBar.Create(_safe, "Aura", tl, tl, new Vector2(216f, 162f), new Vector2(520f, 16f), trail: false);
            _aura.SetFillColors(Color.Lerp(_auraColor, Color.black, 0.45f), _auraColor);
            _emberRt = UiKit.Rect("Ember", _aura.Root, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 28f));
            _ember = _emberRt.gameObject.AddComponent<RingShape>();
            _ember.raycastTarget = false;
            _ember.Inner = 0f;
            _auraNum = HudBar.Label(_safe, "AuraNum", tl, tl, new Vector2(756f, 158f), new Vector2(360f, 24f), 20, TextAnchor.MiddleLeft);
            // stamina (overhaul T1): flashes when an action is refused for want of it; WINDED once it ran dry
            _stamina = HudBar.Create(_safe, "Stamina", tl, tl, new Vector2(206f, 188f), new Vector2(420f, 12f), trail: false, shadow: false);
            _stamina.SetFillColors(StB, StA);
            _staminaTag = HudBar.Label(_safe, "Winded", tl, tl, new Vector2(640f, 184f), new Vector2(240f, 20f), 15, TextAnchor.MiddleLeft, FontStyle.Bold);
            _staminaTag.color = ExhaustText;
            // power surge countdown
            _surgeRoot = UiKit.Rect("Surge", _safe, tl, tl, new Vector2(236f, -214f), new Vector2(320f, 20f));
            var sl = HudBar.Label(_surgeRoot, "Label", tl, tl, Vector2.zero, new Vector2(90f, 20f), 15, TextAnchor.MiddleLeft, FontStyle.Bold);
            sl.text = "SURGE";
            _surge = HudBar.Create(_surgeRoot, "Bar", tl, tl, new Vector2(94f, 6f), new Vector2(200f, 8f), trail: false, shadow: false);
            _surge.SetFillColors(Hex(0xff9a3d), Hex(0xffe07a));
            _surge.Gloss.enabled = false;
        }

        private static void HpColors(HudBar b, float frac)
        {
            if (frac >= 0.3f) { b.SetFillColors(HpB, HpA); return; }
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7.7f);
            b.SetFillColors(Color.Lerp(LowB, Color.white, 0.2f * pulse), Color.Lerp(LowA, Color.white, 0.2f * pulse));
        }

        private void UpdatePlayer(PlayerStats me, float dt)
        {
            float now = Time.unscaledTime;
            bool exhausted = me.AuraDepleted;
            float strength = me.Strength;
            bool full = me.Power.Value >= 0.999f;

            // gauge
            _ring.Fill = strength;
            _ring.color = exhausted ? Hex(0xff5a4a) : _auraColor;
            _powVal.text = Mathf.RoundToInt(strength * 100f) + "<size=26>%</size>";
            _powVal.color = exhausted ? ExhaustText : Color.white;
            _powLbl.text = exhausted ? (me.Exhaustion.Value > 0f ? "EXHAUSTED" : "AURA SPENT") : full ? "FULL POWER" : "POWER";
            _powLbl.color = exhausted ? ExhaustText : full ? _auraColor : Color.white;
            bool charging = !exhausted && !full && me.Motor != null && me.Motor.IsCharging;
            var glow = _auraColor;
            glow.a = charging ? 0.22f + 0.12f * Mathf.Sin(now * 11f) : 0f;
            _ringGlow.color = glow;
            for (int i = 0; i < _chevrons.Length; i++)
            {
                float k = Mathf.Repeat(now / 0.6f + i / 3f, 1f);
                _chevrons[i].rectTransform.anchoredPosition = new Vector2(0f, k * 28f);
                var cc = _auraColor;
                cc.a = charging ? Mathf.Clamp01(k < 0.3f ? k / 0.3f : (1f - k) / 0.7f) : 0f;
                _chevrons[i].color = cc;
            }
            _surgeRing.enabled = me.SurgeActive;
            if (me.SurgeActive) _surgeRing.Fill = me.SurgeRemaining.Value / Mathf.Max(0.01f, me.SurgeDuration);

            // name, health, guard
            string key = me.DisplayName + "|" + me.Level.Value;
            if (key != _nameKey)
            {
                _nameKey = key;
                _name.text = me.DisplayName.ToUpperInvariant() + (me.Level.Value > 0 ? "  <color=#ffffffbf>Lv " + me.Level.Value + "</color>" : "");
            }
            float hf = me.Health.Value / Mathf.Max(1f, me.MaxHealth);
            _hp.Set(hf, dt);
            HpColors(_hp, hf);
            _hpNum.text = Mathf.CeilToInt(me.Health.Value) + "<size=20><color=#ffffffbf> / " + me.MaxHealth + "</color></size>";
            bool broken = me.GuardBroken.Value;
            _guard.Set(broken ? 1f : me.Guard.Value / Mathf.Max(1f, me.MaxGuard), dt);
            _guard.SetFillColors(broken ? Ko : GuardColor, broken ? Color.Lerp(Ko, Color.black, 0.5f) : GuardColor);
            bool idleGuard = !broken && me.Guard.Value >= me.MaxGuard - 0.01f && (me.Motor == null || !me.Motor.IsBlocking);
            _guard.Fill.color = new Color(1f, 1f, 1f, idleGuard ? 0.7f : 1f);
            _guardTag.gameObject.SetActive(broken);

            // energy
            _energy.Set(me.Energy.Value / Mathf.Max(1f, me.MaxEnergy), dt);
            _enNum.text = Mathf.FloorToInt(me.Energy.Value) + " <size=13>EN</size>";

            // aura reserve
            float af = me.Aura.Value / Mathf.Max(1f, me.MaxAura);
            _aura.Set(af, dt);
            float burn = Core.AuraReserve.Upkeep(me.Power.Value);
            _emberRt.anchorMin = _emberRt.anchorMax = new Vector2(af, 0.5f);
            var ec = _auraColor;
            ec.a = burn > 0f && !exhausted ? 0.35f + 0.25f * Mathf.Sin(now * 14f) : 0f;
            _ember.color = ec;
            if (exhausted)
            {
                _auraNum.text = "0 <size=14>AURA SPENT</size>";
                _auraNum.color = ExhaustText;
            }
            else
            {
                _auraNum.text = Mathf.CeilToInt(me.Aura.Value) + " <size=14>AURA" + (burn > 0.05f ? "   <color=#ffffffbf>-" + burn.ToString("0.0") + "/s</color>" : "") + "</size>";
                _auraNum.color = Color.white;
            }

            _surgeRoot.gameObject.SetActive(me.SurgeActive);
            if (me.SurgeActive) _surge.Set(me.SurgeRemaining.Value / Mathf.Max(0.01f, me.SurgeDuration), dt);

            // stamina: our own predicted motor (instant), the synced value otherwise
            var mm = me.Motor;
            float st = mm != null ? mm.Stamina : me.Stamina.Value;
            float stMax = mm != null ? mm.MaxStamina : Core.CombatConstants.MaxStamina;
            bool winded = mm != null ? mm.Winded : me.Winded.Value;
            _stamina.Set(st / Mathf.Max(1f, stMax), dt);
            float flash = mm != null ? Mathf.Clamp01(1f - (Time.time - mm.StaminaRefusedAt) / 0.35f) : 0f;
            Color sa = winded ? ExhaustText : StA, sb = winded ? Ko : StB;
            _stamina.SetFillColors(Color.Lerp(sb, Color.white, flash), Color.Lerp(sa, Color.white, flash));
            _stamina.Back.color = Color.Lerp(HudBar.BackColor, new Color(1f, 0.3f, 0.25f, 0.85f), flash);
            _staminaTag.text = winded ? "WINDED" : flash > 0f ? "NO STAMINA" : "";
        }

        private void UpdateScoreboard()
        {
            _sb.Clear();
            _sb.Append("<b>PLAYER                      KO   DEATHS</b>\n");
            foreach (var p in PlayerStats.All)
            {
                if (p == null) continue;
                string n = p.DisplayName;
                string lv = p.Level.Value > 0 ? "  Lv " + p.Level.Value : "";
                if (n.Length + lv.Length > 24) n = n.Substring(0, 24 - lv.Length);
                n += lv;
                if (p.IsOwner) _sb.Append("<color=#ffd75a>");
                _sb.Append(n.PadRight(26)).Append(p.Kos.Value.ToString().PadLeft(4)).Append(p.Deaths.Value.ToString().PadLeft(8));
                if (p.IsOwner) _sb.Append("</color>");
                _sb.Append('\n');
            }
            _score.text = _sb.ToString();
        }

        private void UpdatePlates(UnityEngine.Camera cam, PlayerStats me)
        {
            _remove.Clear();
            foreach (var kv in _plateMap) if (kv.Key == null || !PlayerStats.All.Contains(kv.Key)) _remove.Add(kv.Key);
            foreach (var k in _remove) { if (_plateMap[k].Root != null) Destroy(_plateMap[k].Root.gameObject); _plateMap.Remove(k); }
            if (cam == null) return;

            Vector2 canvasSize = ((RectTransform)_canvas.transform).rect.size;
            foreach (var p in PlayerStats.All)
            {
                if (p == null || p == me) continue;
                if (!_plateMap.TryGetValue(p, out var plate))
                {
                    var rt = UiKit.Rect("Plate", _plates, Vector2.zero, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(200f, 44f));
                    plate = new Plate
                    {
                        Root = rt,
                        Name = UiKit.Text(UiKit.Rect("Name", rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(200f, 22f)), "", 18, TextAnchor.MiddleCenter, Color.white),
                        Hp = HudBar.Create(rt, "HP", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, -2f), new Vector2(160f, 10f), shadow: false),
                    };
                    _plateMap[p] = plate;
                }
                var gfx = p.transform.Find("Graphics");
                Vector3 world = (gfx != null ? gfx.position : p.transform.position) + Vector3.up * 3.0f;
                Vector3 sp = cam.WorldToScreenPoint(world);
                bool visible = sp.z > 0f && sp.z < 70f;
                plate.Root.gameObject.SetActive(visible);
                if (!visible) continue;
                plate.Root.anchoredPosition = new Vector2(sp.x / Screen.width * canvasSize.x, sp.y / Screen.height * canvasSize.y);
                float scale = Mathf.Clamp(12f / sp.z, 0.55f, 1.1f);
                plate.Root.localScale = Vector3.one * scale;
                plate.Name.text = p.NameWithLevel + (p.IsDown.Value ? " (K.O.)" : "");
                float pf = p.Health.Value / Mathf.Max(1f, p.MaxHealth);
                plate.Hp.Set(pf, Time.unscaledDeltaTime);
                HpColors(plate.Hp, pf);
            }
        }
    }
}
