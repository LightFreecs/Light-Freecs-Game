using System;
using System.Collections.Generic;
using LightMMORPG.Input;
using LightMMORPG.Settings;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LightMMORPG.Menu
{
    // Settings rows (graphics, frame rate, look sensitivity, invert look, key bindings) and the Controls dialog
    // (keyboard / mouse rebinding). Values live in GameSettings (PlayerPrefs).
    public sealed partial class MainMenuUI
    {
        private CanvasGroup _controlsDlg;
        private readonly List<(Text text, Func<string> value)> _settingValues = new();
        private readonly List<GlowSelectable> _settingRows = new();
        private readonly List<GlowSelectable> _bindButtons = new();
        private InputActionRebindingExtensions.RebindingOperation _rebind;
        private float _rebindEndedAt = -10f;

        private bool RebindBusy => _rebind != null || Time.unscaledTime - _rebindEndedAt < 0.5f;

        private static readonly (GameAction action, string name)[] BindableActions =
        {
            (GameAction.Light, "Light attack"), (GameAction.Heavy, "Heavy attack (hold)"), (GameAction.Block, "Block"),
            (GameAction.Charge, "Charge energy"), (GameAction.Blast, "Energy Blast"), (GameAction.Beam, "Charged Beam"),
            (GameAction.Surge, "Power Surge"), (GameAction.Jump, "Jump / fly up"), (GameAction.FlyDown, "Fly down"),
            (GameAction.ToggleFlight, "Toggle flight"), (GameAction.Sprint, "Run (hold)"), (GameAction.Dash, "Dash"),
            (GameAction.FlashStep, "Flash step"), (GameAction.LockOn, "Lock-on"),
        };

        /// <summary>Settings rows under the sky picker. Returns the y below the last row.</summary>
        private float BuildSettingRows(RectTransform panel, float y, float w)
        {
            const float bw = 280f, rowH = 64f;
            float bx = w * 0.5f - bw * 0.5f;

            void Row(string label, Func<string> value, Action onClick)
            {
                var l = MenuUi.Label(panel, "Row_" + label, label, T.boldFont, 28, Color.white, TextAnchor.MiddleLeft);
                l.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(40f, y - 26f), new Vector2(w - bw - 20f, 50f));
                if (onClick == null)
                {
                    var v = MenuUi.Label(panel, "Value_" + label, value(), T.bodyFont, 24, new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleRight);
                    v.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-40f, y - 26f), new Vector2(bw, 50f));
                }
                else
                {
                    var b = DialogButton(panel, value(), null, y, bw, 52f, () => { onClick(); RefreshSettingValues(); }, bx, centred: true);
                    _settingValues.Add((b.transform.Find("Label").GetComponent<Text>(), value));
                    _settingRows.Add(b);
                }
                y -= rowH;
            }

            Row("Graphics quality", () => GameSettings.Quality.ToString(),
                () => GameSettings.Quality = (GraphicsQuality)(((int)GameSettings.Quality + 1) % 3));
            Row("Frame rate cap", () => GameSettings.FpsLabel(GameSettings.FpsCap),
                () => GameSettings.FpsCap = GameSettings.Next(GameSettings.FpsCaps, GameSettings.FpsCap, (a, b) => a == b ? 0f : 1f));
            Row("Look sensitivity", () => GameSettings.SensitivityLabel(GameSettings.LookSensitivity),
                () => GameSettings.LookSensitivity = GameSettings.Next(GameSettings.Sensitivities, GameSettings.LookSensitivity, (a, b) => Mathf.Abs(a - b)));
            Row("Invert look up/down", () => GameSettings.InvertLookY ? "On" : "Off", () => GameSettings.InvertLookY = !GameSettings.InvertLookY);
            Row("Key bindings", () => "Edit  ›", () => SetState(State.Controls));
            Row("Audio", () => "No sound yet", null);
            Row("Account", () => Accounts.AccountStore.Instance.Current?.username ?? "Sign in  ›", () => OpenAccount(State.Settings));

            for (int i = 1; i < _settingRows.Count; i++) Chain(_settingRows[i - 1], _settingRows[i]);
            return y;
        }

        private void RefreshSettingValues()
        {
            foreach (var (text, value) in _settingValues) text.text = value();
            foreach (var b in _bindButtons) RefreshBindLabel(b);
        }

        private void BuildControlsDialog(RectTransform parent)
        {
            float pw = Mathf.Max(T.subPanelWidth, 1000f), w = pw - 80f; // two columns need more room than the other dialogs
            var panel = Dialog(parent, "Controls", out _controlsDlg, 760f);
            DialogHeader(panel, "Controls", "Pick an action, then press the new key. Esc cancels.");
            int perCol = (BindableActions.Length + 1) / 2;
            float colW = (w - 32f) * 0.5f, bw = 190f, rowH = 56f, top = -150f;
            for (int i = 0; i < BindableActions.Length; i++)
            {
                var (action, name) = BindableActions[i];
                int col = i / perCol, row = i % perCol;
                float left = 40f + col * (colW + 32f), y = top - row * rowH;
                var l = MenuUi.Label(panel, "Name_" + action, name, T.bodyFont, 24, Color.white, TextAnchor.MiddleLeft);
                l.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(left, y - 23f), new Vector2(colW - bw - 8f, 46f));
                float bx = -pw * 0.5f + left + colW - bw * 0.5f;
                GlowSelectable b = null;
                b = DialogButton(panel, "", null, y, bw, 46f, () => StartRebind(action, b), bx, centred: true);
                b.name = "Bind_" + action;
                _bindButtons.Add(b);
                RefreshBindLabel(b);
            }
            // navigation: down the columns, left/right across them
            for (int i = 0; i < _bindButtons.Count; i++)
            {
                var n = _bindButtons[i].navigation;
                n.mode = Navigation.Mode.Explicit;
                int col = i / perCol, row = i % perCol;
                if (row > 0) n.selectOnUp = _bindButtons[i - 1];
                if (row < perCol - 1 && i + 1 < _bindButtons.Count) n.selectOnDown = _bindButtons[i + 1];
                int other = col == 0 ? i + perCol : i - perCol;
                if (other >= 0 && other < _bindButtons.Count) { if (col == 0) n.selectOnRight = _bindButtons[other]; else n.selectOnLeft = _bindButtons[other]; }
                _bindButtons[i].navigation = n;
            }
            float yb = top - perCol * rowH - 20f;
            var reset = DialogButton(panel, "Reset to defaults", null, yb, 320f, 52f, () =>
            {
                if (RebindBusy) return;
                GameSettings.ResetBindings(GameInputBindings.Map);
                RefreshSettingValues();
                ShowToast("Controls reset");
            }, centred: true);
            yb -= 74f;
            var back = TextButton(panel, "Back", yb, () => { if (!RebindBusy) SetState(State.Settings); });
            panel.sizeDelta = new Vector2(pw, -yb + 84f);
            Chain(reset, back);
            foreach (int i in new[] { perCol - 1, _bindButtons.Count - 1 })
            {
                var n = _bindButtons[i].navigation; n.selectOnDown = reset; _bindButtons[i].navigation = n;
            }
            var nr = reset.navigation; nr.selectOnUp = _bindButtons[perCol - 1]; reset.navigation = nr;
        }

        private static GameAction ActionOf(GlowSelectable b) => Enum.Parse<GameAction>(b.name.Substring("Bind_".Length));

        private static void RefreshBindLabel(GlowSelectable b)
        {
            var action = GameInputBindings.Get(ActionOf(b));
            string s = action.GetBindingDisplayString(0);
            b.transform.Find("Label").GetComponent<Text>().text = string.IsNullOrEmpty(s) ? "—" : s;
        }

        private void StartRebind(GameAction a, GlowSelectable button)
        {
            if (RebindBusy) return;
            var action = GameInputBindings.Get(a);
            var label = button.transform.Find("Label").GetComponent<Text>();
            label.text = "Press a key…";
            EventSystem.current?.SetSelectedGameObject(null); // so Space/Enter don't re-press this button
            action.Disable();
            _rebind = action.PerformInteractiveRebinding(0) // binding 0 = keyboard / mouse (see GameInputBindings.Button)
                .WithControlsExcluding("<Gamepad>")
                .WithControlsExcluding("<Touchscreen>")
                .WithControlsExcluding("<Pointer>/position")
                .WithControlsExcluding("<Pointer>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.1f)
                .OnComplete(_ => EndRebind(action, button, true))
                .OnCancel(_ => EndRebind(action, button, false))
                .Start();
        }

        private void EndRebind(InputAction action, GlowSelectable button, bool saved)
        {
            _rebind?.Dispose();
            _rebind = null;
            _rebindEndedAt = Time.unscaledTime;
            action.Enable();
            if (saved) GameSettings.SaveBindings(GameInputBindings.Map);
            RefreshBindLabel(button);
            EventSystem.current?.SetSelectedGameObject(button.gameObject);
            if (saved) WarnDuplicate(action);
        }

        private void WarnDuplicate(InputAction changed)
        {
            string path = changed.bindings[0].effectivePath;
            foreach (var (other, name) in BindableActions)
            {
                var o = GameInputBindings.Get(other);
                if (o != changed && o.bindings[0].effectivePath == path)
                {
                    ShowToast("Also used by " + name);
                    return;
                }
            }
        }

        private void OnDisable()
        {
            _rebind?.Cancel();
        }
    }
}
