using System.Collections.Generic;
using System.Text;
using LightMMORPG.Accounts;
using LightMMORPG.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LightMMORPG.Menu
{
    // Accounts in the menu: player card (name, level, XP bar), the Account dialog (sign in / create, or the profile),
    // and the results dialog shown after a match or training session.
    public sealed partial class MainMenuUI
    {
        private CanvasGroup _accountDlg, _resultsDlg;
        private RectTransform _accountPanel, _resultsPanel;
        private GameObject _acctOut, _acctIn;
        private InputField _userField, _passField;
        private GlowSelectable _signInButton, _signOutButton, _resultsContinue, _resultsCreate;
        private Text _acctName, _acctLevel, _acctXp, _acctStats, _acctUnlocks, _acctNext;
        private RectTransform _acctXpFill;
        private Text _cardName, _cardSub, _cardInitial;
        private RectTransform _cardXpFill;
        private State _accountReturn = State.Main;
        private Text _resTitle, _resLines, _resTotal, _resLevel, _resLevelUp, _resUnlocks, _resGuest;
        private RectTransform _resXpFill;
        private float _accountPanelOutH, _accountPanelInH;
        private Text _acctBlurb;
        private string _prefilledFor;

        private static AccountStore Store => AccountStore.Instance;

        // ------------------------------------------------------------------ player card

        /// <summary>Called from BuildPlayerCard: keeps the labels, adds an XP bar and makes the card open the Account dialog.</summary>
        private void MakeAccountCard(Image card, Text initial, Text name, Text sub)
        {
            _cardInitial = initial;
            _cardName = name;
            _cardSub = sub;
            var bg = MenuUi.Img(card.transform, "XpBar", MenuUi.RoundedRect(4), new Color(0f, 0f, 0f, 0.35f), true);
            bg.rectTransform.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(104f, 12f), new Vector2(200f, 6f));
            var fill = MenuUi.Img(bg.transform, "Fill", MenuUi.RoundedRect(4), Color.white, true);
            _cardXpFill = fill.rectTransform;
            Bind(p => fill.color = p.accent);
            SetFill(_cardXpFill, 0f);
            var sel = AddSelectable(card.gameObject);
            sel.OnClick = () => OpenAccount(State.Main);
            sel.OnGlow = g => card.rectTransform.localScale = Vector3.one * (1f + 0.03f * g);
            Store.Changed += RefreshAccountUI;
            RefreshAccountUI();
        }

        private void OnDestroyAccount() => Store.Changed -= RefreshAccountUI;

        private static void SetFill(RectTransform fill, float t)
        {
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(Mathf.Clamp01(t), 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
        }

        private static float LevelFraction(long xp)
        {
            var (into, needed) = Progression.LevelProgress(xp);
            return needed > 0 ? into / (float)needed : 1f;
        }

        private void RefreshAccountUI()
        {
            var a = Store.Current;
            if (_cardName != null)
            {
                _cardName.text = a != null ? a.username : "Guest";
                _cardInitial.text = a != null ? a.username.Substring(0, 1).ToUpperInvariant() : "?";
                int pts = a != null ? a.UnspentStatPoints : 0;
                _cardSub.text = a != null ? "Lv " + a.Level + (a.Title.Length > 0 ? " · " + a.Title : "") + (pts > 0 ? " · " + pts + (pts == 1 ? " stat point" : " stat points") : "") : "Sign in to save progress";
                SetFill(_cardXpFill, a != null ? LevelFraction(a.totalXp) : 0f);
                var card = (RectTransform)_cardName.transform.parent;
                float w = 104f + Mathf.Max(MenuUi.Width(_cardName), MenuUi.Width(_cardSub)) + 30f;
                card.sizeDelta = new Vector2(Mathf.Max(260f, w), 100f);
            }
            if (_acctIn == null) return;
            // a device-only account (from before online accounts) gets the form, to move it online under the same name
            bool form = a == null || !a.online;
            _acctOut.SetActive(form);
            _acctIn.SetActive(!form);
            _accountPanel.sizeDelta = new Vector2(T.subPanelWidth, form ? _accountPanelOutH : _accountPanelInH);
            if (form)
            {
                // a device-only account is moved online by creating an account with its name
                if (a != null && !_createMode) SetAuthMode(true, false);
                UpdateBlurb();
                if (a != null && _prefilledFor != a.username && string.IsNullOrEmpty(_userField.text)) _userField.text = a.username;
                _prefilledFor = a?.username;
                return;
            }
            _acctName.text = a.username + (Store.OnlinePending ? "  ·  saving…" : "");
            _acctLevel.text = "Level " + a.Level + (a.Title.Length > 0 ? " · " + a.Title : "");
            var (into, needed) = Progression.LevelProgress(a.totalXp);
            _acctXp.text = needed > 0 ? $"{into:N0} / {needed:N0} XP to level {a.Level + 1}" : $"Max level · {a.totalXp:N0} XP";
            SetFill(_acctXpFill, LevelFraction(a.totalXp));
            int h = (int)(a.minutesPlayed / 60f), m = (int)(a.minutesPlayed % 60f);
            _acctStats.text = $"Matches  {a.matches}\nWins  {a.wins}\nTraining  {a.trainingSessions}";
            _acctStats.transform.parent.Find("Stats2").GetComponent<Text>().text =
                $"KOs  {a.kos}\nKnocked out  {a.deaths}\nTime played  {(h > 0 ? h + " h " : "")}{m} min";
            var got = new List<string>();
            foreach (var u in Progression.UnlockedAt(a.Level)) got.Add(u.DisplayName);
            _acctUnlocks.text = got.Count > 0 ? "Unlocked: " + string.Join(", ", got) : "Unlocked: nothing yet. Play matches to earn XP.";
            Unlock? next = null;
            foreach (var u in Progression.Unlocks)
                if (u.Level > a.Level) { next = u; break; }
            _acctNext.text = next.HasValue ? $"Next: {next.Value.DisplayName} at level {next.Value.Level}" : "Everything unlocked";
        }

        private void OpenAccount(State returnTo, bool? create = null)
        {
            _accountReturn = returnTo;
            if (create.HasValue && _tabCreate != null) SetAuthMode(create.Value, false);
            SetState(State.Account);
        }

        // ------------------------------------------------------------------ account dialog

        private InputField Field(RectTransform panel, string placeholder, float width, bool password, out RectTransform box)
        {
            var field = Panel(panel, "Field_" + placeholder, new Vector2(width, FieldH), 20, _ => new Color(0f, 0f, 0f, 0.28f), p => p.accent, 2f);
            field.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(width, FieldH));
            field.raycastTarget = true;
            box = field.rectTransform;
            float right = password ? 120f : 24f;
            var text = MenuUi.Label(field.transform, "Text", "", T.bodyFont, 30, Color.white, TextAnchor.MiddleLeft);
            text.rectTransform.Fill();
            text.rectTransform.offsetMin = new Vector2(24f, 0f);
            text.rectTransform.offsetMax = new Vector2(-right, 0f);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var ph = MenuUi.Label(field.transform, "Placeholder", placeholder, T.bodyFont, 26, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleLeft);
            ph.rectTransform.Fill();
            ph.rectTransform.offsetMin = new Vector2(24f, 0f);
            ph.rectTransform.offsetMax = new Vector2(-right, 0f);
            var f = field.gameObject.AddComponent<InputField>();
            f.textComponent = text;
            f.placeholder = ph;
            f.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Alphanumeric;
            f.characterLimit = password ? AccountStore.MaxOnlinePassword : 16;
            f.transition = Selectable.Transition.None;
            f.caretWidth = 3;
            f.customCaretColor = true;
            f.caretColor = Color.white;
            var nav = f.navigation; nav.mode = Navigation.Mode.Explicit; f.navigation = nav;
            Bind(p => f.selectionColor = A(p.accent, 0.45f));
            field.gameObject.AddComponent<IpFieldGlow>().Init(f, field);
            if (password)
            {
                // SHOW / HIDE toggle inside the field, on the right
                var rt = MenuUi.Rect("Reveal", field.transform).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(104f, FieldH - 16f));
                var hit = rt.gameObject.AddComponent<Image>();
                hit.color = Color.clear;
                hit.raycastTarget = true;
                var lbl = MenuUi.Label(rt, "Label", "SHOW", T.boldFont, 20, Color.white, TextAnchor.MiddleCenter, 0.15f);
                lbl.rectTransform.Fill();
                rt.gameObject.AddComponent<PasswordReveal>().Init(f, lbl, () => _sky.Preset.accent);
            }
            return f;
        }

        private Text FieldLabel(RectTransform parent, string text)
        {
            var t = MenuUi.Label(parent, "Label_" + text, text.ToUpperInvariant(), T.boldFont, 18, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleLeft, 0.15f);
            t.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(T.subPanelWidth - 80f, 24f));
            return t;
        }

        private Text Body(RectTransform parent, string name, int size, float y, float height, TextAnchor align = TextAnchor.UpperLeft, float alpha = 1f)
        {
            float w = T.subPanelWidth - 80f;
            var t = MenuUi.Label(parent, name, "", T.bodyFont, size, new Color(1f, 1f, 1f, alpha), align);
            t.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(w, height));
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private RectTransform XpBar(RectTransform parent, float y)
        {
            float w = T.subPanelWidth - 80f;
            var bg = MenuUi.Img(parent, "XpBar", MenuUi.RoundedRect(6), new Color(0f, 0f, 0f, 0.35f), true);
            bg.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(w, 14f));
            var fill = MenuUi.Img(bg.transform, "Fill", MenuUi.RoundedRect(6), Color.white, true);
            Bind(p => fill.color = p.accent);
            SetFill(fill.rectTransform, 0f);
            return fill.rectTransform;
        }

        // ------------------------------------------------------------------ sign in / create form

        private const float FieldH = 72f;
        private static readonly Color BadColor = new(1f, 0.55f, 0.5f, 1f);
        private bool _createMode;
        private InputField _confirmField;
        private RectTransform _userBox, _passBox, _confirmBox, _rulesRt;
        private Text _userLabel, _passLabel, _confirmLabel, _matchText, _submitLabel;
        private GlowSelectable _tabSignIn, _tabCreate, _backOut;
        private readonly List<(Image dot, Text text, System.Func<string, bool> ok)> _rules = new();

        private GlowSelectable Tab(RectTransform bar, string label, float x, float width, bool create)
        {
            var rt = MenuUi.Rect("Tab_" + label, bar).Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(width, 48f));
            var pill = MenuUi.Img(rt, "Pill", MenuUi.RoundedRect(16), Color.clear, true);
            pill.rectTransform.Fill();
            var t = MenuUi.Label(rt, "Label", label, T.boldFont, 24, Color.white, TextAnchor.MiddleCenter);
            t.rectTransform.Fill();
            var sel = AddSelectable(rt.gameObject);
            sel.OnClick = () => SetAuthMode(create, true);
            sel.OnGlow = g =>
            {
                var p = _sky.Preset;
                bool on = _createMode == create;
                pill.color = on ? p.accent : A(Color.white, 0.10f * g);
                var dark = p.dialog; dark.a = 1f;
                t.color = on ? dark : A(Color.white, Mathf.Lerp(0.6f, 1f, g));
            };
            return sel;
        }

        private void BuildAuthForm(RectTransform outRt, float w)
        {
            _acctBlurb = Body(outRt, "Blurb", 22, -96f, 44f, TextAnchor.MiddleLeft, 0.75f);

            // Sign in | Create account
            var bar = Panel(outRt, "Tabs", new Vector2(w, 60f), 20, _ => new Color(0f, 0f, 0f, 0.28f), null);
            bar.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -156f), new Vector2(w, 60f));
            float tw = (w - 12f) * 0.5f;
            _tabSignIn = Tab(bar.rectTransform, "Sign in", -tw * 0.5f - 2f, tw - 4f, false);
            _tabCreate = Tab(bar.rectTransform, "Create account", tw * 0.5f + 2f, tw - 4f, true);

            _userLabel = FieldLabel(outRt, "Name");
            _userField = Field(outRt, "3–16 letters, numbers or _", w, false, out _userBox);
            _passLabel = FieldLabel(outRt, "Password");
            _passField = Field(outRt, "Password", w, true, out _passBox);
            _confirmLabel = FieldLabel(outRt, "Confirm password");
            _confirmField = Field(outRt, "Type it again", w, true, out _confirmBox);

            _matchText = Body(outRt, "Match", 20, 0f, 28f, TextAnchor.MiddleLeft);

            // live password rules, two columns
            _rulesRt = MenuUi.Rect("Rules", outRt).Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(w, 60f));
            AddRule(_rulesRt, AccountStore.MinOnlinePassword + "–" + AccountStore.MaxOnlinePassword + " characters", 0, w,
                p => p.Length >= AccountStore.MinOnlinePassword && p.Length <= AccountStore.MaxOnlinePassword);
            AddRule(_rulesRt, "Upper and lower case", 1, w, p => HasAny(p, char.IsUpper) && HasAny(p, char.IsLower));
            AddRule(_rulesRt, "A number", 2, w, p => HasAny(p, char.IsDigit));
            AddRule(_rulesRt, "A symbol (! ? # …)", 3, w, p => HasAny(p, c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)));

            _signInButton = DialogButton(outRt, "Sign in", null, 0f, w, 76f, () => DoAccount(_createMode), centred: true);
            _submitLabel = _signInButton.transform.Find("Label").GetComponent<Text>();
            _backOut = TextButton(outRt, "Back", 0f, () => SetState(_accountReturn));

            _passField.onValueChanged.AddListener(_ => RefreshPasswordHints());
            _confirmField.onValueChanged.AddListener(_ => RefreshPasswordHints());
            _userField.onSubmit.AddListener(_ => Focus(_passField));
            _passField.onSubmit.AddListener(_ => { if (_createMode) Focus(_confirmField); else DoAccount(false); });
            _confirmField.onSubmit.AddListener(_ => DoAccount(true));
            Bind(_ => RefreshPasswordHints());
            SetAuthMode(false, false);
        }

        private static bool HasAny(string s, System.Func<char, bool> f)
        {
            foreach (char c in s) if (f(c)) return true;
            return false;
        }

        private void AddRule(RectTransform parent, string label, int i, float w, System.Func<string, bool> ok)
        {
            float x = (i % 2) * (w * 0.5f), y = -(i / 2) * 30f;
            var dot = MenuUi.Img(parent, "Dot", MenuUi.RoundedRect(6), Color.white, true);
            dot.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(x, y - 14f), new Vector2(12f, 12f));
            var t = MenuUi.Label(parent, "Rule", label, T.bodyFont, 20, Color.white, TextAnchor.MiddleLeft);
            t.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(x + 24f, y - 14f), new Vector2(w * 0.5f - 30f, 28f));
            _rules.Add((dot, t, ok));
        }

        private static void Focus(InputField f)
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(f.gameObject);
            f.ActivateInputField();
        }

        private void RefreshPasswordHints()
        {
            if (_rules.Count == 0 || _sky == null) return;
            var accent = _sky.Preset.accent;
            string p = _passField.text ?? "";
            foreach (var (dot, text, ok) in _rules)
            {
                bool met = ok(p);
                dot.color = met ? accent : new Color(1f, 1f, 1f, 0.22f);
                text.color = new Color(1f, 1f, 1f, met ? 0.95f : 0.5f);
            }
            string c = _confirmField.text ?? "";
            bool match = c == p;
            _matchText.text = c.Length == 0 ? "" : match ? "Passwords match" : "Passwords don't match";
            _matchText.color = match ? accent : BadColor;
        }

        private void SetAuthMode(bool create, bool focus)
        {
            _createMode = create;
            float y = -236f;
            void Put(RectTransform rt, float h) { rt.anchoredPosition = new Vector2(0f, y); y -= h; }
            Put(_userLabel.rectTransform, 26f);
            Put(_userBox, FieldH + 20f);
            Put(_passLabel.rectTransform, 26f);
            Put(_passBox, FieldH + (create ? 20f : 32f));
            _confirmLabel.gameObject.SetActive(create);
            _confirmBox.gameObject.SetActive(create);
            _matchText.gameObject.SetActive(create);
            _rulesRt.gameObject.SetActive(create);
            if (create)
            {
                Put(_confirmLabel.rectTransform, 26f);
                Put(_confirmBox, FieldH + 6f);
                Put(_matchText.rectTransform, 34f);
                Put(_rulesRt, 78f);
            }
            else _confirmField.text = "";
            Put((RectTransform)_signInButton.transform, 96f);
            Put((RectTransform)_backOut.transform, 0f);
            _accountPanelOutH = -y + 84f;
            _submitLabel.text = create ? "Create account" : "Sign in";
            ((Text)_passField.placeholder).text = create ? "Choose a password" : "Password";
            if (_accountPanel != null && _acctOut != null && _acctOut.activeSelf) _accountPanel.sizeDelta = new Vector2(T.subPanelWidth, _accountPanelOutH);
            UpdateBlurb();

            // navigation: tabs → name → password → (confirm) → submit → back
            var nt = _tabSignIn.navigation; nt.selectOnRight = _tabCreate; nt.selectOnDown = _userField; _tabSignIn.navigation = nt;
            nt = _tabCreate.navigation; nt.selectOnLeft = _tabSignIn; nt.selectOnDown = _userField; _tabCreate.navigation = nt;
            var nu = _userField.navigation; nu.selectOnUp = create ? _tabCreate : _tabSignIn; nu.selectOnDown = _passField; _userField.navigation = nu;
            Selectable last = create ? _confirmField : _passField;
            var np = _passField.navigation; np.selectOnUp = _userField; np.selectOnDown = create ? _confirmField : _signInButton; _passField.navigation = np;
            var nc = _confirmField.navigation; nc.selectOnUp = _passField; nc.selectOnDown = _signInButton; _confirmField.navigation = nc;
            var ns = _signInButton.navigation; ns.selectOnUp = last; ns.selectOnDown = _backOut; _signInButton.navigation = ns;
            var nb = _backOut.navigation; nb.selectOnUp = _signInButton; _backOut.navigation = nb;

            if (_sky != null) { _tabSignIn.Snap(); _tabCreate.Snap(); }
            RefreshPasswordHints();
            if (focus && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(create ? _tabCreate.gameObject : _tabSignIn.gameObject);
        }

        private void UpdateBlurb()
        {
            if (_acctBlurb == null) return;
            var a = Store.Current;
            _acctBlurb.text = a != null && !a.online
                ? a.username + " is saved on this device only. Create an online account with that name to take it online."
                : _createMode
                    ? "An online account keeps your name, level and stats on every device."
                    : "Welcome back. Sign in to load your progress.";
        }

        private void BuildAccountDialog(RectTransform parent)
        {
            float w = T.subPanelWidth - 80f;
            _accountPanel = Dialog(parent, "Account", out _accountDlg, 700f);
            DialogHeader(_accountPanel, "Account", null);

            // signed out: sign in or create
            _acctOut = MenuUi.Rect("SignedOut", _accountPanel).Fill().gameObject;
            BuildAuthForm((RectTransform)_acctOut.transform, w);

            // signed in: profile
            _acctIn = MenuUi.Rect("SignedIn", _accountPanel).Fill().gameObject;
            var inRt = (RectTransform)_acctIn.transform;
            _acctName = MenuUi.Label(inRt, "Name", "", T.boldFont, 34, Color.white, TextAnchor.MiddleLeft);
            _acctName.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(w, 44f));
            _acctLevel = Body(inRt, "Level", 28, -160f, 40f, TextAnchor.MiddleLeft);
            _acctXpFill = XpBar(inRt, -208f);
            _acctXp = Body(inRt, "Xp", 22, -228f, 32f, TextAnchor.MiddleLeft, 0.7f);
            _acctStats = Body(inRt, "Stats", 26, -280f, 120f);
            _acctStats.rectTransform.sizeDelta = new Vector2(w * 0.5f, 120f);
            _acctStats.rectTransform.anchoredPosition = new Vector2(-w * 0.25f, -280f);
            var stats2 = Body(inRt, "Stats2", 26, -280f, 120f);
            stats2.rectTransform.sizeDelta = new Vector2(w * 0.5f, 120f);
            stats2.rectTransform.anchoredPosition = new Vector2(w * 0.25f, -280f);
            _acctUnlocks = Body(inRt, "Unlocks", 22, -414f, 70f, TextAnchor.UpperLeft, 0.85f);
            _acctNext = Body(inRt, "Next", 22, -488f, 32f, TextAnchor.MiddleLeft, 0.6f);
            _signOutButton = DialogButton(inRt, "Sign out", null, -540f, 320f, 72f, () => { OnlineAccounts.SignOut(); ShowToast("Signed out"); SetState(State.Account); }, centred: true);
            var backIn = TextButton(inRt, "Back", -626f, () => SetState(_accountReturn));
            _accountPanelInH = 626f + 84f;
            Chain(_signOutButton, backIn);
            RefreshAccountUI();
        }

        private async void DoAccount(bool create)
        {
            if (OnlineAccounts.Busy) return;
            string user = _userField.text, pass = _passField.text;
            if (create)
            {
                // check here first so the player sees what to fix before anything is sent
                var check = AccountStore.ValidateOnline(user, pass);
                if (check != AccountError.None) { ShowToast(AccountStore.Describe(check)); return; }
                if (_confirmField.text != pass) { ShowToast("The passwords don't match"); Focus(_confirmField); return; }
            }
            ShowToast(create ? "Creating account…" : "Signing in…");
            var err = create ? await OnlineAccounts.Create(user, pass) : await OnlineAccounts.SignIn(user, pass);
            if (this == null) return; // menu closed meanwhile
            if (err != AccountError.None)
            {
                ShowToast(AccountStore.Describe(err));
                return;
            }
            _passField.text = "";
            _confirmField.text = "";
            _userField.text = "";
            PasswordReveal.HideAll(_acctOut.transform);
            SetAuthMode(false, false);
            ShowToast((create ? "Welcome, " : "Signed in as ") + Store.Current.username);
            if (Current == State.Account) SetState(State.Account);
        }

        // ------------------------------------------------------------------ results dialog

        private void BuildResultsDialog(RectTransform parent)
        {
            _resultsPanel = Dialog(parent, "Results", out _resultsDlg, 700f);
            DialogHeader(_resultsPanel, "Match results", null);
            _resTitle = _resultsPanel.Find("Title").GetComponent<Text>();
            _resLines = Body(_resultsPanel, "Lines", 26, -116f, 170f);
            _resTotal = MenuUi.Label(_resultsPanel, "Total", "", T.boldFont, 40, Color.white, TextAnchor.MiddleLeft);
            _resTotal.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -294f), new Vector2(T.subPanelWidth - 80f, 52f));
            _resLevel = Body(_resultsPanel, "Level", 26, -352f, 36f, TextAnchor.MiddleLeft);
            _resXpFill = XpBar(_resultsPanel, -394f);
            _resLevelUp = MenuUi.Label(_resultsPanel, "LevelUp", "", T.boldFont, 30, Color.white, TextAnchor.MiddleLeft);
            _resLevelUp.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -420f), new Vector2(T.subPanelWidth - 80f, 44f));
            Bind(p => _resLevelUp.color = p.accent);
            _resUnlocks = Body(_resultsPanel, "Unlocks", 24, -466f, 64f, TextAnchor.UpperLeft, 0.9f);
            _resGuest = Body(_resultsPanel, "Guest", 24, -420f, 64f, TextAnchor.UpperLeft, 0.8f);
            _resGuest.text = "Playing as guest, so this XP was not saved. Create an account to keep your progress.";
            _resultsContinue = DialogButton(_resultsPanel, "Continue", null, -546f, 300f, 72f, () => SetState(State.Main), -170f, centred: true);
            _resultsCreate = DialogButton(_resultsPanel, "Create account", null, -546f, 300f, 72f, () => OpenAccount(State.Main, create: true), 170f, centred: true);
            var a = _resultsContinue.navigation; a.selectOnRight = _resultsCreate; _resultsContinue.navigation = a;
            var b = _resultsCreate.navigation; b.selectOnLeft = _resultsContinue; _resultsCreate.navigation = b;
            _resultsPanel.sizeDelta = new Vector2(T.subPanelWidth, 546f + 72f + 40f);
        }

        /// <summary>Shows the results of the match the player just left (if one was recorded).</summary>
        private bool ShowResults(RecordedMatch m)
        {
            if (m == null) return false;
            var r = m.Result;
            var x = m.Xp;
            _resTitle.text = r.Training ? "Training results" : "Match results";
            var sb = new StringBuilder();
            sb.Append(r.Kos == 1 ? "1 KO" : r.Kos + " KOs").Append("   +").Append(x.FromKos).Append('\n');
            sb.Append(Mathf.Max(1, Mathf.RoundToInt(r.Minutes))).Append(" min played   +").Append(x.FromTime).Append('\n');
            if (x.FromCompletion > 0) sb.Append(r.Training ? "Training session   +" : "Finished the match   +").Append(x.FromCompletion).Append('\n');
            if (x.FromWin > 0) sb.Append("Most KOs   +").Append(x.FromWin).Append('\n');
            if (r.Training) sb.Append("Training gives reduced XP (max ").Append(Progression.TrainingCap).Append(')');
            _resLines.text = sb.ToString().TrimEnd('\n');
            _resTotal.text = "+" + x.Total + " XP";
            bool saved = m.Saved;
            _resLevel.gameObject.SetActive(saved);
            _resXpFill.parent.gameObject.SetActive(saved);
            _resLevelUp.gameObject.SetActive(saved && m.Level.LeveledUp);
            _resUnlocks.gameObject.SetActive(saved && m.Level.Unlocks.Count > 0);
            _resGuest.gameObject.SetActive(!saved);
            _resultsCreate.gameObject.SetActive(!saved);
            var nc = _resultsContinue.navigation; nc.selectOnRight = saved ? null : _resultsCreate; _resultsContinue.navigation = nc;
            if (saved)
            {
                var lv = m.Level;
                var (into, needed) = Progression.LevelProgress(lv.NewXp);
                _resLevel.text = $"Level {lv.NewLevel}  ·  " + (needed > 0 ? $"{into:N0} / {needed:N0} XP" : "max level");
                SetFill(_resXpFill, LevelFraction(lv.NewXp));
                _resLevelUp.text = lv.NewLevel - lv.OldLevel > 1 ? $"LEVEL UP!  {lv.OldLevel} → {lv.NewLevel}" : $"LEVEL UP!  Level {lv.NewLevel}";
                int points = FighterStats.PointsFor(lv.NewLevel) - FighterStats.PointsFor(lv.OldLevel);
                if (points > 0) _resLevelUp.text += points == 1 ? "  ·  +1 stat point" : $"  ·  +{points} stat points";
                var names = new List<string>();
                foreach (var u in lv.Unlocks) names.Add(u.DisplayName);
                _resUnlocks.text = "Unlocked: " + string.Join(", ", names);
                _resUnlocks.rectTransform.anchoredPosition = new Vector2(0f, lv.LeveledUp ? -466f : -420f);
            }
            SetState(State.Results, instant: true);
            return true;
        }
    }

    /// <summary>SHOW / HIDE inside a password field: toggles between masked and plain text, keeping the field focused.</summary>
    public sealed class PasswordReveal : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private InputField _field;
        private Text _label;
        private System.Func<Color> _accent;
        private bool _shown, _hover, _wasFocused;

        public void Init(InputField field, Text label, System.Func<Color> accent)
        {
            _field = field;
            _label = label;
            _accent = accent;
            Paint();
        }

        public static void HideAll(Transform root)
        {
            foreach (var r in root.GetComponentsInChildren<PasswordReveal>(true)) r.Set(false);
        }

        public void Set(bool shown)
        {
            _shown = shown;
            _field.contentType = shown ? InputField.ContentType.Standard : InputField.ContentType.Password;
            _field.ForceLabelUpdate();
            Paint();
        }

        // the click deselects the field before it reaches us, so remember whether it was being typed in
        private void LateUpdate() => _wasFocused = _field != null && _field.isFocused;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            bool refocus = _wasFocused;
            Set(!_shown);
            if (refocus && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(_field.gameObject);
                _field.ActivateInputField();
                _field.MoveTextEnd(false);
            }
        }

        public void OnPointerEnter(PointerEventData e) { _hover = true; Paint(); }
        public void OnPointerExit(PointerEventData e) { _hover = false; Paint(); }

        private void Paint()
        {
            if (_label == null) return;
            _label.text = _shown ? "HIDE" : "SHOW";
            var c = _accent != null ? _accent() : Color.white;
            _label.color = _hover || _shown ? c : new Color(1f, 1f, 1f, 0.55f);
        }
    }
}
