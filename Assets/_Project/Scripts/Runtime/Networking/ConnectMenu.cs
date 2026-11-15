using LightMMORPG.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace LightMMORPG.Networking
{
    /// <summary>
    /// Runtime-built uGUI connect menu: Host / Join (IP, default 127.0.0.1) / Server Only, port 7770.
    /// Hidden while connected; a small "Disconnect" button shows while the cursor is unlocked (Esc).
    /// Creates an EventSystem with the Input System UI module if the scene has none. Session 4's HUD can reuse
    /// <see cref="Canvas"/> / <see cref="CreateButton"/>.
    /// </summary>
    public sealed class ConnectMenu : MonoBehaviour
    {
        public Canvas Canvas { get; private set; }
        public static Font UIFont => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        private static Font _font;

        private GameObject _panel;
        private GameObject _disconnectButton;
        private InputField _ipField;
        private Text _status;

        private void Start()
        {
            if (Application.isBatchMode) { enabled = false; return; }
            EnsureEventSystem();
            Build();
            if (NetworkBootstrap.Instance != null) NetworkBootstrap.Instance.OnStatusChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (NetworkBootstrap.Instance != null) NetworkBootstrap.Instance.OnStatusChanged -= Refresh;
        }

        private void Update()
        {
            var b = NetworkBootstrap.Instance;
            bool running = b != null && b.IsRunning;
            if (_panel.activeSelf == running) Refresh();
            if (_disconnectButton != null) _disconnectButton.SetActive(running && !PlayerInputReader.CursorLocked);
        }

        private void Refresh()
        {
            var b = NetworkBootstrap.Instance;
            bool running = b != null && b.IsRunning;
            _panel.SetActive(!running);
            PlayerInputReader.MenuOpen = !running;
            if (!running) PlayerInputReader.LockCursor(false);
            if (_status != null) _status.text = b != null ? b.Status : "No NetworkBootstrap in scene";
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(es);
        }

        private void Build()
        {
            var canvasGo = new GameObject("ConnectMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            Canvas = canvasGo.GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 100;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;

            _panel = CreatePanel(canvasGo.transform, new Vector2(420, 400), new Color(0.06f, 0.05f, 0.12f, 0.88f));
            var layout = _panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 24, 24);
            layout.spacing = 12;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;

            CreateText(_panel.transform, "LIGHT MMORPG", 40, FontStyle.Bold, new Color(1f, 0.75f, 0.35f));
            CreateText(_panel.transform, "Arena prototype", 18, FontStyle.Italic, new Color(0.8f, 0.8f, 0.95f));
            CreateButton(_panel.transform, "Host", () => NetworkBootstrap.Instance?.StartHost());
            _ipField = CreateInputField(_panel.transform, NetworkBootstrap.DefaultAddress);
            CreateButton(_panel.transform, "Join", () =>
            {
                string ip = string.IsNullOrWhiteSpace(_ipField.text) ? NetworkBootstrap.DefaultAddress : _ipField.text.Trim();
                NetworkBootstrap.Instance?.StartClient(ip);
            });
            CreateButton(_panel.transform, "Server Only", () => NetworkBootstrap.Instance?.StartServerOnly());
            _status = CreateText(_panel.transform, "", 16, FontStyle.Normal, new Color(0.7f, 0.9f, 1f));
            CreateText(_panel.transform, "Port " + NetworkBootstrap.DefaultPort + "  ·  Esc frees the mouse", 14, FontStyle.Normal, new Color(0.6f, 0.6f, 0.7f));

            _disconnectButton = CreateButton(canvasGo.transform, "Disconnect", () => NetworkBootstrap.Instance?.StopAll()).gameObject;
            var rt = (RectTransform)_disconnectButton.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(16, -16);
            rt.sizeDelta = new Vector2(160, 40);
            _disconnectButton.SetActive(false);
        }

        // ---------------- small uGUI helpers (reusable) ----------------

        public static GameObject CreatePanel(Transform parent, Vector2 size, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = color;
            return go;
        }

        public static Text CreateText(Transform parent, string text, int size, FontStyle style, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = UIFont;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            go.GetComponent<LayoutElement>().preferredHeight = size + 10;
            return t;
        }

        public static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.95f, 0.55f, 0.15f, 1f);
            go.GetComponent<LayoutElement>().preferredHeight = 46;
            var b = go.GetComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1f, 0.8f, 0.5f);
            colors.pressedColor = new Color(0.7f, 0.35f, 0.1f);
            b.colors = colors;
            b.onClick.AddListener(onClick);
            var t = CreateText(go.transform, label, 22, FontStyle.Bold, new Color(0.1f, 0.05f, 0.1f));
            Stretch((RectTransform)t.transform);
            return b;
        }

        public static InputField CreateInputField(Transform parent, string value)
        {
            var go = new GameObject("InputField", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.92f);
            go.GetComponent<LayoutElement>().preferredHeight = 42;
            var text = CreateText(go.transform, "", 20, FontStyle.Normal, Color.black);
            text.supportRichText = false;
            var rt = (RectTransform)text.transform;
            Stretch(rt);
            rt.offsetMin = new Vector2(10, 4);
            rt.offsetMax = new Vector2(-10, -4);
            var field = go.GetComponent<InputField>();
            field.textComponent = text;
            field.text = value;
            return field;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
