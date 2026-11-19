namespace LightMMORPG.Input
{
    /// <summary>Every button-like player action. Index into <see cref="TouchInputState"/> and <see cref="PlayerInputReader"/>.</summary>
    public enum GameAction
    {
        Jump,          // Space / South. Also "fly up" while flying (held).
        Sprint,        // Run (walk otherwise): hold Shift / click L3
        FlyDown,       // Ctrl / D-pad down
        ToggleFlight,  // F / D-pad up
        Dash,          // Q / East
        FlashStep,     // E / LB
        Light,         // LMB / West
        Heavy,         // RMB / North
        Block,         // R (hold) / LT
        Charge,        // C (hold) / D-pad left
        Blast,         // 1 / RT
        Beam,          // 2 (hold + release) / RB
        Surge,         // 3 / D-pad right
        LockOn,        // Tab / R3
    }

    public static class GameActions
    {
        public const int Count = (int)GameAction.LockOn + 1;
    }
}
