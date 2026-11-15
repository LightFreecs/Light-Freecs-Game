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
    /// Runtime-built HUD: own HP/energy/guard bars (top-left), ability icons with cooldown sweep + energy dimming
    /// (bottom-centre, PC only — the touch buttons show their own), locked-on target bar (top-centre), name plates with
    /// HP above other players, KO / respawn countdown + KO feed (centre), and a corner scoreboard (top-right).
    /// </summary>
    public sealed class GameHud : MonoBehaviour
    {
        private static readonly Color HpColor = new(0.35f, 0.95f, 0.45f);
        private static readonly Color HpLow = new(1f, 0.3f, 0.25f);
        private static readonly Color EnergyColor = new(0.3f, 0.75f, 1f);
        private static readonly Color GuardColor = new(1f, 0.85f, 0.3f);

        private Canvas _canvas;
        private RectTransform _safe, _plates;
        private UiKit.Bar _hp, _energy, _guard, _surge, _targetHp;
        private Text _targetName, _center, _feed, _score;
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
            public UiKit.Bar Hp;
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

            // own bars
            var tl = new Vector2(0f, 1f);
            _hp = UiKit.CreateBar(_safe, "HP", tl, tl, new Vector2(30f, -30f), new Vector2(460f, 34f), HpColor, 20);
            _energy = UiKit.CreateBar(_safe, "Energy", tl, tl, new Vector2(30f, -70f), new Vector2(380f, 22f), EnergyColor, 15);
            _guard = UiKit.CreateBar(_safe, "Guard", tl, tl, new Vector2(30f, -98f), new Vector2(260f, 14f), GuardColor);
            _surge = UiKit.CreateBar(_safe, "Surge", tl, tl, new Vector2(300f, -98f), new Vector2(110f, 14f), new Color(1f, 0.5f, 0.15f));

            // abilities
            var bc = new Vector2(0.5f, 0f);
            _abilityRow = UiKit.Rect("Abilities", _safe, bc, bc, new Vector2(0f, 30f), new Vector2(5 * 96f, 86f));
            AddIcon(AttackKind.Heavy, "RMB\nHeavy", 0);
            AddIcon(AttackKind.Blast, "1\nBlast", 1);
            AddIcon(AttackKind.Beam, "2\nBeam", 2);
            AddIcon(AttackKind.Surge, "3\nSurge", 3);
            AddIcon(AttackKind.Light, "C\nCharge", 4);

            // target
            var tc = new Vector2(0.5f, 1f);
            _targetRoot = UiKit.Rect("Target", _safe, tc, tc, new Vector2(0f, -24f), new Vector2(520f, 60f));
            _targetName = UiKit.Text(UiKit.Rect("Name", _targetRoot, tc, tc, Vector2.zero, new Vector2(520f, 26f)), "", 22, TextAnchor.MiddleCenter, Color.white);
            _targetHp = UiKit.CreateBar(_targetRoot, "HP", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(520f, 24f), HpLow, 15);

            // centre messages
            var c = new Vector2(0.5f, 0.5f);
            _center = UiKit.Text(UiKit.Rect("Center", _safe, c, c, new Vector2(0f, 120f), new Vector2(900f, 140f)), "", 56, TextAnchor.MiddleCenter, Color.white);
            _feed = UiKit.Text(UiKit.Rect("Feed", _safe, tc, tc, new Vector2(0f, -110f), new Vector2(900f, 40f)), "", 26, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.5f));

            // scoreboard
            var tr = new Vector2(1f, 1f);
            var sbRoot = UiKit.Rect("Scoreboard", _safe, tr, tr, new Vector2(-24f, -24f), new Vector2(380f, 180f));
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

            // own bars
            _hp.Set(me.Health.Value / me.MaxHealth);
            _hp.FillImage.color = me.Health.Value / me.MaxHealth < 0.3f ? HpLow : HpColor;
            _hp.Label.text = Mathf.CeilToInt(me.Health.Value) + " / " + me.MaxHealth;
            _energy.Set(me.Energy.Value / me.MaxEnergy);
            _energy.Label.text = Mathf.FloorToInt(me.Energy.Value) + " EN";
            _guard.Set(me.Guard.Value / me.MaxGuard);
            _guard.FillImage.color = me.GuardBroken.Value ? HpLow : GuardColor;
            _surge.Root.gameObject.SetActive(me.SurgeActive);
            if (me.SurgeActive) _surge.Set(me.SurgeRemaining.Value / Mathf.Max(0.01f, me.SurgeDuration));

            // abilities (PC)
            _abilityRow.gameObject.SetActive(!PlayerInputReader.TouchMode);
            var combat = PlayerCombat.Local;
            if (combat != null)
                foreach (var ic in _icons)
                {
                    bool charge = ic.Kind == AttackKind.Light; // "Charge energy" slot: shows charging state
                    float cd = charge ? 0f : combat.Cooldown01(ic.Kind);
                    ic.Sweep.anchorMax = new Vector2(1f, cd);
                    bool afford = charge || combat.CanAfford(ic.Kind);
                    bool lit = charge ? me.Motor != null && me.Motor.IsCharging : (ic.Kind == AttackKind.Surge && me.SurgeActive) || (ic.Kind == AttackKind.Beam && combat.IsChargingBeam);
                    ic.Bg.color = lit ? new Color(0.9f, 0.55f, 0.15f, 0.95f) : afford ? new Color(0.15f, 0.2f, 0.35f, 0.85f) : new Color(0.08f, 0.08f, 0.1f, 0.7f);
                    ic.Label.color = afford ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                }

            // target
            var cam = ThirdPersonCamera.Instance;
            PlayerStats target = cam != null && cam.CurrentTarget != null ? cam.CurrentTarget.GetComponent<PlayerStats>() : null;
            _targetRoot.gameObject.SetActive(target != null);
            if (target != null)
            {
                _targetName.text = target.DisplayName + (target.IsDown.Value ? "  (down)" : "");
                _targetHp.Set(target.Health.Value / target.MaxHealth);
                _targetHp.Label.text = Mathf.CeilToInt(target.Health.Value).ToString();
            }

            // centre
            if (me.IsDown.Value) _center.text = "K.O.!\n<size=30>Respawning in " + me.DownRemaining.Value.ToString("0.0") + "</size>";
            else if (me.GuardBroken.Value) _center.text = "<size=40>GUARD BREAK</size>";
            else _center.text = "";
            _feed.gameObject.SetActive(Time.time < _feedUntil);

            UpdateScoreboard();
            UpdatePlates(cam != null ? cam.GetComponent<UnityEngine.Camera>() : UnityEngine.Camera.main, me);
        }

        private void UpdateScoreboard()
        {
            _sb.Clear();
            _sb.Append("<b>PLAYER                      KO   DEATHS</b>\n");
            foreach (var p in PlayerStats.All)
            {
                if (p == null) continue;
                string n = p.DisplayName;
                if (n.Length > 24) n = n.Substring(0, 24);
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
                        Hp = UiKit.CreateBar(rt, "HP", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(150f, 14f), HpLow),
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
                plate.Name.text = p.DisplayName + (p.IsDown.Value ? " (K.O.)" : "");
                plate.Hp.Set(p.Health.Value / p.MaxHealth);
            }
        }
    }
}
