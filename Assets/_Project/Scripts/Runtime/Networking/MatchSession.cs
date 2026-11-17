using LightMMORPG.Combat;
using LightMMORPG.Input;
using LightMMORPG.Menu;
using LightMMORPG.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LightMMORPG.Networking
{
    /// <summary>
    /// In-arena session UI (replaces the old connect panel): a "Connecting to …" card while a client connects (Cancel →
    /// menu; failure or timeout → back to the menu with an error toast), a server-only status card, and the Esc /
    /// gamepad Start / touch ❚❚ pause menu with Resume and "Leave match" (stops networking, loads MainMenu). In Training
    /// the pause menu also switches the dummy between standing and blocking, and a small banner names the mode.
    /// Uses the main menu's theme (fonts, rounded panels) so both screens match.
    /// </summary>
    public sealed class MatchSession : MonoBehaviour
    {
        private const float ConnectTimeout = 15f;

        private MenuTheme _t;
        private SkyPreset _p;
        private CanvasGroup _connecting, _pause, _serverCard, _trainingBanner;
        private RectTransform _pauseCard, _pauseTitle, _leaveRect, _dummyRect;
        private Selectable _dummyButton, _leave;
        private Text _dummyLabel, _leaveLabel;
        private bool _laidOut;
        private Text _connectingText, _serverText;
        private GameObject _pauseButton;
        private Selectable _resume;
        private bool _wasConnected;
        private bool _leaving;
        private float _startedAt;

        public bool PauseOpen { get; private set; }

        private void Start()
        {
            if (Application.isBatchMode) { enabled = false; return; }
            _t = MenuTheme.Load();
            _p = _t.GetSky(SkyId.Dusk);
            UiKit.EnsureEventSystem();
            Build();
            _startedAt = Time.unscaledTime;
            var b = NetworkBootstrap.Instance;
            if (b != null) b.Manager.ClientManager.OnClientConnectionState += OnClientState;
        }

        private void OnDestroy()
        {
            var b = NetworkBootstrap.Instance;
            if (b != null && b.Manager != null) b.Manager.ClientManager.OnClientConnectionState -= OnClientState;
            PlayerInputReader.MenuOpen = false;
        }

        private void OnClientState(FishNet.Transporting.ClientConnectionStateArgs a)
        {
            if (a.ConnectionState == FishNet.Transporting.LocalConnectionState.Started) _wasConnected = true;
            if (a.ConnectionState != FishNet.Transporting.LocalConnectionState.Stopped || _leaving) return;
            var b = NetworkBootstrap.Instance;
            if (b == null || b.StartedMode == NetMode.Server) return;
            string msg = _wasConnected ? "Disconnected from the match" : "Could not connect to " + b.Address;
            Leave(msg);
        }

        private void Build()
        {
            var canvas = UiKit.CreateCanvas("MatchSessionCanvas", 150);
            canvas.transform.SetParent(transform, false);
            var safe = MenuUi.Rect("Safe", canvas.transform).Fill();
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // connecting card
            _connecting = Group(safe, "Connecting");
            var card = Card(_connecting.transform, new Vector2(760f, 260f));
            _connectingText = Text(card.transform, "Connecting…", _t.lightFont, 44, new Vector2(0f, 40f));
            var cancel = Button(card.transform, "Cancel", new Vector2(0f, -60f), () => Leave(null));
            _connecting.alpha = 0f;

            // server-only card (top-centre)
            _serverCard = Group(safe, "ServerCard");
            var sc = Card(_serverCard.transform, new Vector2(820f, 96f));
            sc.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(820f, 96f));
            _serverText = Text(sc.transform, "", _t.boldFont, 28, new Vector2(-90f, 0f));
            var stop = Button(sc.transform, "Stop", new Vector2(310f, 0f), () => Leave(null), 150f, 64f);
            _serverCard.alpha = 0f;

            // pause menu
            _pause = Group(safe, "Pause");
            var dim = _pause.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.45f);
            var pc = Card(_pause.transform, new Vector2(620f, 400f));
            _pauseCard = pc.rectTransform;
            _pauseTitle = Text(pc.transform, "Paused", _t.lightFont, 56, new Vector2(0f, 120f)).rectTransform;
            _resume = Button(pc.transform, "Resume", new Vector2(0f, 10f), () => SetPause(false));
            _leave = Button(pc.transform, "Leave match", new Vector2(0f, -110f), () => Leave(null));
            _leaveRect = (RectTransform)_leave.transform;
            _leaveLabel = _leave.GetComponentInChildren<Text>();
            _dummyButton = Button(pc.transform, "Dummy: Standing", new Vector2(0f, -110f), ToggleDummy);
            _dummyRect = (RectTransform)_dummyButton.transform;
            _dummyLabel = _dummyButton.GetComponentInChildren<Text>();
            _dummyButton.gameObject.SetActive(false);
            Link(_resume, _leave);
            _pause.alpha = 0f;

            // training banner (top-left, under the HUD bars)
            _trainingBanner = Group(safe, "TrainingBanner");
            var tb = MenuUi.Img(_trainingBanner.transform, "Card", MenuUi.RoundedRect(24), new Color(0f, 0f, 0f, 0.4f), true);
            tb.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(560f, 64f));
            var tbt = MenuUi.Label(tb.transform, "Text", "TRAINING  ·  dummy options in the pause menu", _t.boldFont, 22, Color.white);
            tbt.rectTransform.Fill();
            _trainingBanner.alpha = 0f;

            // touch pause button (top-centre-right, clear of the HUD bars)
            var pb = MenuUi.Img(safe, "PauseButton", MenuUi.RoundedRect(36), new Color(0f, 0f, 0f, 0.45f), true);
            pb.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(380f, -20f), new Vector2(72f, 72f));
            pb.raycastTarget = true;
            for (int i = -1; i <= 1; i += 2)
            {
                var bar = MenuUi.Img(pb.transform, "Bar", null, Color.white);
                bar.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(i * 10f, 0f), new Vector2(10f, 32f));
            }
            var pbb = pb.gameObject.AddComponent<Button>();
            pbb.transition = Selectable.Transition.None;
            pbb.onClick.AddListener(() => SetPause(!PauseOpen));
            _pauseButton = pb.gameObject;
        }

        private static CanvasGroup Group(Transform parent, string name)
        {
            var g = MenuUi.Rect(name, parent).Fill().gameObject.AddComponent<CanvasGroup>();
            g.interactable = g.blocksRaycasts = false;
            return g;
        }

        private Image Card(Transform parent, Vector2 size)
        {
            var bg = MenuUi.Img(parent, "Card", MenuUi.RoundedRect(36), _p.dialog, true);
            bg.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            bg.raycastTarget = true;
            var border = MenuUi.Img(bg.transform, "Border", MenuUi.RoundedRectOutline(36, 3f), _p.accent, true);
            border.rectTransform.Fill();
            return bg;
        }

        private static Text Text(Transform parent, string text, Font font, int size, Vector2 pos)
        {
            var t = MenuUi.Label(parent, "Text", text, font, size, Color.white);
            t.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(700f, size * 1.4f));
            return t;
        }

        private Selectable Button(Transform parent, string label, Vector2 pos, System.Action onClick, float w = 420f, float h = 88f)
        {
            var bg = MenuUi.Img(parent, "Button_" + label, MenuUi.RoundedRect(22), new Color(1f, 1f, 1f, 0.08f), true);
            bg.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(w, h));
            var border = MenuUi.Img(bg.transform, "Border", MenuUi.RoundedRectOutline(22, 2f), _p.accent, true);
            border.rectTransform.Fill();
            var t = MenuUi.Label(bg.transform, "Label", label, _t.boldFont, 30, Color.white);
            t.rectTransform.Fill();
            var sel = bg.gameObject.AddComponent<GlowSelectable>();
            bg.raycastTarget = true;
            sel.targetGraphic = bg;
            sel.transition = Selectable.Transition.None;
            sel.OnClick = onClick;
            var dark = _p.dialog;
            dark.a = 1f;
            sel.OnGlow = g =>
            {
                bg.color = Color.Lerp(new Color(1f, 1f, 1f, 0.08f), _p.accent, g);
                t.color = Color.Lerp(Color.white, dark, g);
            };
            return sel;
        }

        private static void Link(Selectable up, Selectable down)
        {
            var a = up.navigation; a.mode = Navigation.Mode.Explicit; a.selectOnDown = down; up.navigation = a;
            var b = down.navigation; b.mode = Navigation.Mode.Explicit; b.selectOnUp = up; down.navigation = b;
        }

        /// <summary>Training: dummy row between Resume and Leave, "Leave training".</summary>
        private void LayoutPause(bool training)
        {
            _laidOut = true;
            if (!training) return;
            _pauseCard.sizeDelta = new Vector2(620f, 520f);
            _pauseTitle.anchoredPosition = new Vector2(0f, 180f);
            ((RectTransform)_resume.transform).anchoredPosition = new Vector2(0f, 70f);
            _dummyButton.gameObject.SetActive(true);
            _dummyRect.anchoredPosition = new Vector2(0f, -40f);
            _leaveRect.anchoredPosition = new Vector2(0f, -150f);
            _leaveLabel.text = "Leave training";
            Link(_resume, _dummyButton);
            Link(_dummyButton, _leave);
        }

        private void ToggleDummy()
        {
            var d = TrainingDummy.Instance;
            if (d == null) return;
            d.Mode = d.Mode == TrainingDummy.Behaviour.Stand ? TrainingDummy.Behaviour.Block : TrainingDummy.Behaviour.Stand;
        }

        private void SetPause(bool open)
        {
            PauseOpen = open;
            PlayerInputReader.MenuOpen = open;
            if (!PlayerInputReader.TouchMode) PlayerInputReader.LockCursor(!open);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(open ? _resume.gameObject : null);
        }

        private void Leave(string error)
        {
            if (_leaving) return;
            _leaving = true;
            Debug.Log("[Net] Leaving match" + (error != null ? ": " + error : ""));
            PlayerInputReader.MenuOpen = false;
            PlayerInputReader.LockCursor(false);
            Accounts.MatchTracker.Finish(); // XP + stats for the account (needs the players' stats, so before stopping)
            var b = NetworkBootstrap.Instance;
            if (b != null) b.StopAll();
            NetLaunch.ReturnToMenu(error);
        }

        private void Update()
        {
            var b = NetworkBootstrap.Instance;
            if (b == null || _leaving) return;
            var nm = b.Manager;
            bool training = b.StartedMode == NetMode.Training;
            if (!_laidOut && b.StartedMode != NetMode.None) LayoutPause(training);
            bool clientMode = b.StartedMode is NetMode.Client or NetMode.Host or NetMode.Training;
            bool connecting = clientMode && !nm.IsClientStarted;
            if (b.StartedMode == NetMode.Client && connecting && Time.unscaledTime - _startedAt > ConnectTimeout)
            {
                Leave("Could not connect to " + b.Address);
                return;
            }
            bool serverOnly = b.StartedMode == NetMode.Server;
            if (connecting) _connectingText.text = training ? "Starting training…" : b.StartedMode == NetMode.Host ? "Starting match…" : "Connecting to " + b.Address + "…";
            if (training && TrainingDummy.Instance != null)
                _dummyLabel.text = TrainingDummy.Instance.Mode == TrainingDummy.Behaviour.Block ? "Dummy: Blocking" : "Dummy: Standing";
            if (serverOnly) _serverText.text = "Server running · port " + b.Port + " · " + nm.ServerManager.Clients.Count + " connected";

            bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            bool start = Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
            if (!connecting && !serverOnly && (escape || start)) SetPause(!PauseOpen);
            if (connecting && PauseOpen) SetPause(false);

            Show(_connecting, connecting);
            Show(_serverCard, serverOnly);
            Show(_pause, PauseOpen);
            Show(_trainingBanner, training && !connecting && !PauseOpen);
            _trainingBanner.interactable = _trainingBanner.blocksRaycasts = false;
            _pauseButton.SetActive(!connecting && !serverOnly && PlayerInputReader.TouchMode);
            if (connecting || serverOnly) PlayerInputReader.MenuOpen = true;
            else if (!PauseOpen && PlayerInputReader.MenuOpen) PlayerInputReader.MenuOpen = false;
        }

        private static void Show(CanvasGroup g, bool on)
        {
            g.alpha = Mathf.MoveTowards(g.alpha, on ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            g.interactable = g.blocksRaycasts = on;
        }
    }
}
