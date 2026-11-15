using LightMMORPG.Combat;
using LightMMORPG.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LightMMORPG.UI
{
    /// <summary>
    /// On-screen touch controls writing into <see cref="TouchInputState"/>: floating virtual joystick (left third),
    /// drag-to-look (right half, behind the buttons), and action buttons (hold = Press/Release, so held actions like
    /// block, charge, beam and fly-up work). Shown when <see cref="PlayerInputReader.TouchMode"/> (mobile, touchscreen,
    /// or <c>-touchui</c>) and a local player exists. Laid out inside the safe area.
    /// </summary>
    public sealed class TouchControls : MonoBehaviour
    {
        private Canvas _canvas;
        private RectTransform _safe;

        private void Awake()
        {
            if (Application.isBatchMode) { enabled = false; return; }
            _canvas = UiKit.CreateCanvas("TouchCanvas", 20);
            _canvas.transform.SetParent(transform, false);
            _safe = UiKit.Rect("SafeArea", _canvas.transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiKit.Stretch(_safe);
            _safe.gameObject.AddComponent<SafeAreaFitter>();

            // look area: right half (added first so buttons sit on top of it)
            var look = UiKit.Rect("LookArea", _safe, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            look.anchorMin = new Vector2(0.4f, 0f);
            look.anchorMax = new Vector2(1f, 1f);
            look.offsetMin = look.offsetMax = Vector2.zero;
            UiKit.Image(look, new Color(1f, 1f, 1f, 0f), true);
            look.gameObject.AddComponent<TouchLookArea>();

            // joystick zone: left 40%, lower 70%
            var zone = UiKit.Rect("JoystickZone", _safe, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            zone.anchorMin = Vector2.zero;
            zone.anchorMax = new Vector2(0.4f, 0.7f);
            zone.offsetMin = zone.offsetMax = Vector2.zero;
            UiKit.Image(zone, new Color(1f, 1f, 1f, 0f), true);
            var stick = zone.gameObject.AddComponent<TouchJoystick>();
            stick.Build(zone);

            // action cluster bottom-right: big light/heavy, ring of others
            var br = new Vector2(1f, 0f);
            Button(GameAction.Light, "LIGHT", br, new Vector2(-150f, 120f), 150f, new Color(1f, 0.55f, 0.2f), null);
            Button(GameAction.Heavy, "HEAVY", br, new Vector2(-320f, 70f), 110f, new Color(1f, 0.35f, 0.2f), AttackKind.Heavy);
            Button(GameAction.Block, "BLOCK", br, new Vector2(-300f, 220f), 100f, new Color(0.9f, 0.8f, 0.3f), null);
            Button(GameAction.Jump, "JUMP", br, new Vector2(-150f, 290f), 110f, new Color(0.5f, 0.85f, 1f), null);
            Button(GameAction.Dash, "DASH", br, new Vector2(-440f, 60f), 90f, new Color(0.6f, 0.9f, 0.7f), null);
            Button(GameAction.FlashStep, "FLASH", br, new Vector2(-440f, 170f), 90f, new Color(0.6f, 0.9f, 0.7f), null);

            // powers column above
            Button(GameAction.Blast, "BLAST", br, new Vector2(-60f, 440f), 96f, new Color(0.3f, 0.75f, 1f), AttackKind.Blast);
            Button(GameAction.Beam, "BEAM\n(hold)", br, new Vector2(-170f, 440f), 96f, new Color(0.3f, 0.6f, 1f), AttackKind.Beam);
            Button(GameAction.Surge, "SURGE", br, new Vector2(-280f, 440f), 96f, new Color(1f, 0.6f, 0.2f), AttackKind.Surge);
            Button(GameAction.Charge, "CHARGE", br, new Vector2(-390f, 440f), 96f, new Color(0.4f, 0.9f, 1f), null);

            // utility, top-right / left
            var tr = new Vector2(1f, 1f);
            Button(GameAction.LockOn, "LOCK", tr, new Vector2(-70f, -260f), 90f, new Color(1f, 1f, 1f), null);
            Button(GameAction.ToggleFlight, "FLY", tr, new Vector2(-180f, -260f), 90f, new Color(0.7f, 0.9f, 1f), null);
            Button(GameAction.FlyDown, "DOWN", tr, new Vector2(-290f, -260f), 90f, new Color(0.7f, 0.9f, 1f), null);
            Button(GameAction.Sprint, "SPRINT", new Vector2(0f, 0f), new Vector2(90f, 330f), 90f, new Color(0.8f, 0.8f, 0.8f), null);

            _canvas.gameObject.SetActive(false);
        }

        private void Button(GameAction action, string label, Vector2 anchor, Vector2 pos, float size, Color color, AttackKind? cooldownKind)
        {
            var rt = UiKit.Rect(action.ToString(), _safe, anchor, new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
            var bg = UiKit.Image(rt, new Color(color.r, color.g, color.b, 0.35f), true);
            var b = rt.gameObject.AddComponent<TouchActionButton>();
            b.Action = action;
            b.Background = bg;
            b.BaseColor = color;
            b.CooldownKind = cooldownKind;
            var sweep = UiKit.Rect("Cooldown", rt, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            sweep.anchorMin = Vector2.zero;
            sweep.anchorMax = new Vector2(1f, 0f);
            sweep.offsetMin = sweep.offsetMax = Vector2.zero;
            UiKit.Image(sweep, new Color(0f, 0f, 0f, 0.55f));
            b.Sweep = sweep;
            UiKit.Text(rt, label, size > 120f ? 26 : 17, TextAnchor.MiddleCenter, Color.white);
        }

        private void Update()
        {
            bool show = PlayerInputReader.TouchMode && PlayerInputReader.Local != null && !PlayerInputReader.MenuOpen;
            if (_canvas.gameObject.activeSelf != show)
            {
                _canvas.gameObject.SetActive(show);
                if (!show) TouchInputState.Clear();
            }
        }
    }

    public sealed class TouchActionButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public GameAction Action;
        public Image Background;
        public Color BaseColor;
        public AttackKind? CooldownKind;
        public RectTransform Sweep;
        private int _pointer = int.MinValue;

        public void OnPointerDown(PointerEventData e)
        {
            if (_pointer != int.MinValue) return;
            _pointer = e.pointerId;
            TouchInputState.Press(Action);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            _pointer = int.MinValue;
            TouchInputState.Release(Action);
        }

        private void OnDisable()
        {
            if (_pointer != int.MinValue) TouchInputState.Release(Action);
            _pointer = int.MinValue;
        }

        private void Update()
        {
            bool held = _pointer != int.MinValue;
            var combat = PlayerCombat.Local;
            float cd = 0f;
            bool afford = true;
            if (combat != null && CooldownKind.HasValue)
            {
                cd = combat.Cooldown01(CooldownKind.Value);
                afford = combat.CanAfford(CooldownKind.Value);
            }
            Sweep.anchorMax = new Vector2(1f, cd);
            float a = held ? 0.8f : (afford ? 0.4f : 0.15f);
            Background.color = new Color(BaseColor.r, BaseColor.g, BaseColor.b, a);
        }
    }

    /// <summary>Floating joystick: appears where the thumb lands inside its zone.</summary>
    public sealed class TouchJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private const float Radius = 110f;
        private RectTransform _zone, _base, _knob;
        private int _pointer = int.MinValue;
        private Vector2 _origin;

        public void Build(RectTransform zone)
        {
            _zone = zone;
            _base = UiKit.Rect("Base", zone, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(220f, 220f), new Vector2(Radius * 2f, Radius * 2f));
            UiKit.Image(_base, new Color(1f, 1f, 1f, 0.12f));
            _knob = UiKit.Rect("Knob", _base, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(90f, 90f));
            UiKit.Image(_knob, new Color(1f, 1f, 1f, 0.45f));
        }

        private Vector2 Local(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_zone, e.position, e.pressEventCamera, out var p);
            return p - _zone.rect.min; // bottom-left origin, matching the base's anchor
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_pointer != int.MinValue) return;
            _pointer = e.pointerId;
            _origin = Local(e);
            _base.anchoredPosition = _origin;
            OnDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            Vector2 d = Vector2.ClampMagnitude(Local(e) - _origin, Radius);
            _knob.anchoredPosition = d;
            TouchInputState.Move = d / Radius;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            _pointer = int.MinValue;
            _knob.anchoredPosition = Vector2.zero;
            _base.anchoredPosition = new Vector2(220f, 220f);
            TouchInputState.Move = Vector2.zero;
        }

        private void OnDisable()
        {
            _pointer = int.MinValue;
            TouchInputState.Move = Vector2.zero;
        }
    }

    /// <summary>Drag anywhere in the right half to rotate the camera.</summary>
    public sealed class TouchLookArea : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private int _pointer = int.MinValue;

        public void OnPointerDown(PointerEventData e)
        {
            if (_pointer == int.MinValue) _pointer = e.pointerId;
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId == _pointer) TouchInputState.LookDelta += e.delta;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId == _pointer) _pointer = int.MinValue;
        }
    }
}
