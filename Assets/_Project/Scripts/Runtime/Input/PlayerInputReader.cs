using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace LightMMORPG.Input
{
    /// <summary>
    /// Merges Input System (keyboard/mouse/gamepad) with <see cref="TouchInputState"/> into per-frame values.
    /// Only active while <see cref="InputEnabled"/> (set by PlayerMotor for the local owner).
    /// Runs early (execution order -100) so other scripts read this frame's values in their Update:
    ///   Held(a), Pressed(a) (true on the frame it went down), Released(a) (true on the frame it went up).
    /// Look is in degrees for this frame (already scaled by sensitivity / deltaTime).
    /// PC: cursor is locked while playing; Esc unlocks; click in the game view re-locks.
    /// Command line <c>-autowalk</c> makes the player walk in a circle (smoke tests).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        /// <summary>The reader of the locally owned player (null until spawned).</summary>
        public static PlayerInputReader Local { get; private set; }

        /// <summary>Set true while a menu is open; blocks gameplay input and cursor locking.</summary>
        public static bool MenuOpen;

        public static bool CursorLocked => Cursor.lockState == CursorLockMode.Locked;

        /// <summary>True when the touch overlay should be shown / used.</summary>
        public static bool TouchMode => Application.isMobilePlatform || Touchscreen.current != null || ForceTouchUI;

        /// <summary>Command line <c>-touchui</c>: show/use the touch overlay on PC (mouse drives it) for testing.</summary>
        public static bool ForceTouchUI => _forceTouch ??= Array.IndexOf(Environment.GetCommandLineArgs(), "-touchui") >= 0;
        private static bool? _forceTouch;

        [Tooltip("Degrees per mouse pixel.")] public float mouseSensitivity = 0.12f;
        [Tooltip("Degrees per second at full right-stick.")] public float stickSensitivity = 200f;
        [Tooltip("Degrees per touch-drag pixel.")] public float touchSensitivity = 0.25f;

        public Vector2 Move { get; private set; }
        /// <summary>x = yaw degrees, y = pitch degrees (positive = look up) for this frame.</summary>
        public Vector2 Look { get; private set; }

        private bool _inputEnabled;
        private readonly bool[] _held = new bool[GameActions.Count];
        private readonly bool[] _pressed = new bool[GameActions.Count];
        private readonly bool[] _released = new bool[GameActions.Count];
        private static bool _autoWalk;
        private static bool _argsParsed;

        public bool InputEnabled
        {
            get => _inputEnabled;
            set
            {
                _inputEnabled = value;
                if (value)
                {
                    Local = this;
                    GameInputBindings.EnsureCreated();
                    if (!TouchMode && !MenuOpen && !Application.isBatchMode) LockCursor(true);
                }
                else if (Local == this) Local = null;
            }
        }

        public bool Held(GameAction a) => _held[(int)a];
        public bool Pressed(GameAction a) => _pressed[(int)a];
        public bool Released(GameAction a) => _released[(int)a];

        public static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void Awake()
        {
            if (!_argsParsed)
            {
                _argsParsed = true;
                _autoWalk = Array.IndexOf(Environment.GetCommandLineArgs(), "-autowalk") >= 0;
            }
        }

        private void OnDisable()
        {
            if (Local == this) Local = null;
        }

        private void Update()
        {
            if (!_inputEnabled)
            {
                Clear();
                return;
            }

            bool touch = TouchMode;
            if (!touch && !Application.isBatchMode)
            {
                if (GameInputBindings.UnlockCursor.WasPressedThisFrame()) LockCursor(false);
                else if (!CursorLocked && !MenuOpen && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !PointerOverUI())
                {
                    LockCursor(true);
                    Clear();
                    return; // swallow the re-lock click
                }
            }

            bool blocked = MenuOpen;
            bool mouseOk = CursorLocked && !blocked;

            // Move
            Vector2 move = blocked ? Vector2.zero : GameInputBindings.Move.ReadValue<Vector2>() + TouchInputState.Move;
            if (_autoWalk)
            {
                float t = Time.time * 0.8f;
                move = new Vector2(Mathf.Cos(t), Mathf.Sin(t));
            }
            Move = Vector2.ClampMagnitude(move, 1f);

            // Look
            Vector2 look = Vector2.zero;
            if (mouseOk) look += GameInputBindings.MouseLook.ReadValue<Vector2>() * mouseSensitivity;
            if (!blocked) look += GameInputBindings.StickLook.ReadValue<Vector2>() * (stickSensitivity * Time.unscaledDeltaTime);
            Vector2 touchLook = TouchInputState.ConsumeLookDelta();
            if (!blocked) look += touchLook * touchSensitivity;
            look *= Settings.GameSettings.LookSensitivity;
            if (Settings.GameSettings.InvertLookY) look.y = -look.y;
            Look = look;

            // Buttons
            for (int i = 0; i < GameActions.Count; i++)
            {
                var a = (GameAction)i;
                InputAction action = GameInputBindings.Get(a);
                bool isMouse = action.activeControl != null && action.activeControl.device is Mouse;
                bool deviceHeld = action.IsPressed() && (!isMouse || mouseOk);
                bool devicePressed = action.WasPressedThisFrame() && (!isMouse || mouseOk);
                bool touchPressed = TouchInputState.ConsumePressed(a);

                bool held = !blocked && (deviceHeld || TouchInputState.IsHeld(a));
                bool wasHeld = _held[i];
                _pressed[i] = !blocked && (devicePressed || touchPressed || (held && !wasHeld));
                _released[i] = wasHeld && !held;
                _held[i] = held;
            }
        }

        private void Clear()
        {
            Move = Vector2.zero;
            Look = Vector2.zero;
            for (int i = 0; i < GameActions.Count; i++)
            {
                _released[i] = _held[i];
                _held[i] = false;
                _pressed[i] = false;
            }
        }

        private static bool PointerOverUI()
        {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }
    }
}
