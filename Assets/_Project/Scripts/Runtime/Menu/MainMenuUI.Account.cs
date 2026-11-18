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
                _acctBlurb.text = a == null
                    ? "Online account: your name, level and stats on every device."
                    : a.username + " is saved on this device only. Create an online account with that name to take it online.";
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

        private void OpenAccount(State returnTo)
        {
            _accountReturn = returnTo;
            SetState(State.Account);
        }

        // ------------------------------------------------------------------ account dialog

        private InputField Field(RectTransform panel, string placeholder, float y, float width, bool password)
        {
            var field = Panel(panel, "Field_" + placeholder, new Vector2(width, 80f), 22, _ => new Color(0f, 0f, 0f, 0.28f), p => p.accent, 2f);
            field.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(width, 80f));
            field.raycastTarget = true;
            var text = MenuUi.Label(field.transform, "Text", "", T.bodyFont, 30, Color.white, TextAnchor.MiddleLeft);
            text.rectTransform.Fill();
            text.rectTransform.offsetMin = new Vector2(26f, 0f);
            text.rectTransform.offsetMax = new Vector2(-26f, 0f);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var ph = MenuUi.Label(field.transform, "Placeholder", placeholder, T.bodyFont, 28, new Color(1f, 1f, 1f, 0.4f), TextAnchor.MiddleLeft);
            ph.rectTransform.Fill();
            ph.rectTransform.offsetMin = new Vector2(26f, 0f);
            var f = field.gameObject.AddComponent<InputField>();
            f.textComponent = text;
            f.placeholder = ph;
            f.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Alphanumeric;
            f.characterLimit = password ? 64 : 16;
            f.transition = Selectable.Transition.None;
            f.caretWidth = 3;
            f.customCaretColor = true;
            f.caretColor = Color.white;
            var nav = f.navigation; nav.mode = Navigation.Mode.Explicit; f.navigation = nav;
            Bind(p => f.selectionColor = A(p.accent, 0.45f));
            field.gameObject.AddComponent<IpFieldGlow>().Init(f, field);
            return f;
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

        private void BuildAccountDialog(RectTransform parent)
        {
            float w = T.subPanelWidth - 80f;
            _accountPanel = Dialog(parent, "Account", out _accountDlg, 700f);
            DialogHeader(_accountPanel, "Account", null);

            // signed out: sign in or create
            _acctOut = MenuUi.Rect("SignedOut", _accountPanel).Fill().gameObject;
            var outRt = (RectTransform)_acctOut.transform;
            _acctBlurb = Body(outRt, "Blurb", 24, -100f, 52f, TextAnchor.MiddleLeft, 0.75f);
            _userField = Field(outRt, "Name", -160f, w, false);
            _passField = Field(outRt, "Password", -256f, w, true);
            float bw = (w - 16f) * 0.5f;
            _signInButton = DialogButton(outRt, "Sign in", null, -356f, bw, 80f, () => DoAccount(false), -(bw + 16f) * 0.5f, centred: true);
            var create = DialogButton(outRt, "Create account", null, -356f, bw, 80f, () => DoAccount(true), (bw + 16f) * 0.5f, centred: true);
            var hint = Body(outRt, "Hint", 22, -452f, 34f, TextAnchor.MiddleCenter, 0.55f);
            hint.text = "Names: 3–16 letters, numbers or _ · passwords: " + AccountStore.MinOnlinePassword + "+ with Aa, 1 and !";
            var backOut = TextButton(outRt, "Back", -516f, () => SetState(_accountReturn));
            _accountPanelOutH = 516f + 84f;
            var nu = _userField.navigation; nu.selectOnDown = _passField; _userField.navigation = nu;
            var np = _passField.navigation; np.selectOnUp = _userField; np.selectOnDown = _signInButton; _passField.navigation = np;
            var ns = _signInButton.navigation; ns.selectOnUp = _passField; ns.selectOnRight = create; ns.selectOnDown = backOut; _signInButton.navigation = ns;
            var nc = create.navigation; nc.selectOnUp = _passField; nc.selectOnLeft = _signInButton; nc.selectOnDown = backOut; create.navigation = nc;
            var nb = backOut.navigation; nb.selectOnUp = _signInButton; backOut.navigation = nb;
            _passField.onSubmit.AddListener(_ => DoAccount(false));

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
            ShowToast(create ? "Creating account…" : "Signing in…");
            var err = create ? await OnlineAccounts.Create(user, pass) : await OnlineAccounts.SignIn(user, pass);
            if (this == null) return; // menu closed meanwhile
            if (err != AccountError.None)
            {
                ShowToast(AccountStore.Describe(err));
                return;
            }
            _passField.text = "";
            _userField.text = "";
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
            _resultsCreate = DialogButton(_resultsPanel, "Create account", null, -546f, 300f, 72f, () => OpenAccount(State.Main), 170f, centred: true);
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
}
