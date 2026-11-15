using UnityEngine.InputSystem;

namespace LightMMORPG.Input
{
    /// <summary>
    /// Code-built Input System actions (no .inputactions asset). One shared map for the local player.
    /// PC: WASD move, mouse look, Shift sprint, Space jump/fly up, Ctrl fly down, F flight, Q dash, E flash step,
    /// LMB light, RMB heavy, R block, C charge, 1 blast, 2 beam (hold+release), 3 surge, Tab lock-on.
    /// Gamepad: LS move, RS look, L3 sprint, A jump/fly up, B dash, X light, Y heavy, LT block, RT blast, RB beam,
    /// LB flash step, R3 lock-on, D-pad up flight / down fly down / left charge / right surge.
    /// </summary>
    public static class GameInputBindings
    {
        public static InputActionMap Map { get; private set; }
        public static InputAction Move { get; private set; }
        public static InputAction MouseLook { get; private set; }
        public static InputAction StickLook { get; private set; }
        public static InputAction UnlockCursor { get; private set; }
        private static InputAction[] _buttons;

        public static InputAction Get(GameAction a)
        {
            EnsureCreated();
            return _buttons[(int)a];
        }

        public static void EnsureCreated()
        {
            if (Map != null) return;
            Map = new InputActionMap("Gameplay");

            Move = Map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            Move.AddBinding("<Gamepad>/leftStick").WithProcessor("stickDeadzone");

            MouseLook = Map.AddAction("MouseLook", InputActionType.Value, "<Mouse>/delta", expectedControlLayout: "Vector2");
            StickLook = Map.AddAction("StickLook", InputActionType.Value, "<Gamepad>/rightStick", processors: "stickDeadzone", expectedControlLayout: "Vector2");
            UnlockCursor = Map.AddAction("UnlockCursor", InputActionType.Button, "<Keyboard>/escape");

            _buttons = new InputAction[GameActions.Count];
            Button(GameAction.Jump, "<Keyboard>/space", "<Gamepad>/buttonSouth");
            Button(GameAction.Sprint, "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
            Button(GameAction.FlyDown, "<Keyboard>/leftCtrl", "<Gamepad>/dpad/down");
            Button(GameAction.ToggleFlight, "<Keyboard>/f", "<Gamepad>/dpad/up");
            Button(GameAction.Dash, "<Keyboard>/q", "<Gamepad>/buttonEast");
            Button(GameAction.FlashStep, "<Keyboard>/e", "<Gamepad>/leftShoulder");
            Button(GameAction.Light, "<Mouse>/leftButton", "<Gamepad>/buttonWest");
            Button(GameAction.Heavy, "<Mouse>/rightButton", "<Gamepad>/buttonNorth");
            Button(GameAction.Block, "<Keyboard>/r", "<Gamepad>/leftTrigger");
            Button(GameAction.Charge, "<Keyboard>/c", "<Gamepad>/dpad/left");
            Button(GameAction.Blast, "<Keyboard>/1", "<Gamepad>/rightTrigger");
            Button(GameAction.Beam, "<Keyboard>/2", "<Gamepad>/rightShoulder");
            Button(GameAction.Surge, "<Keyboard>/3", "<Gamepad>/dpad/right");
            Button(GameAction.LockOn, "<Keyboard>/tab", "<Gamepad>/rightStickPress");
            // Extra keyboard aliases.
            _buttons[(int)GameAction.Sprint].AddBinding("<Keyboard>/rightShift");
            _buttons[(int)GameAction.FlyDown].AddBinding("<Keyboard>/rightCtrl");

            Map.Enable();
        }

        private static void Button(GameAction a, string kb, string pad)
        {
            var action = Map.AddAction(a.ToString(), InputActionType.Button);
            action.AddBinding(kb);
            action.AddBinding(pad);
            _buttons[(int)a] = action;
        }
    }
}
