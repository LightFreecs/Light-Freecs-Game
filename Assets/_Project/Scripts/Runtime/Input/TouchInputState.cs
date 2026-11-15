using UnityEngine;

namespace LightMMORPG.Input
{
    /// <summary>
    /// Written by the on-screen touch overlay (session 4), read by <see cref="PlayerInputReader"/>.
    /// Buttons call <see cref="Press"/> on pointer-down and <see cref="Release"/> on pointer-up; the joystick writes
    /// <see cref="Move"/>; the look area adds to <see cref="LookDelta"/> (pixels, consumed once per frame by the reader).
    /// </summary>
    public static class TouchInputState
    {
        /// <summary>Virtual joystick, magnitude 0..1.</summary>
        public static Vector2 Move;
        /// <summary>Accumulated drag delta in pixels since the reader last consumed it.</summary>
        public static Vector2 LookDelta;

        private static readonly bool[] _held = new bool[GameActions.Count];
        private static readonly bool[] _pressedLatch = new bool[GameActions.Count];

        public static bool IsHeld(GameAction a) => _held[(int)a];

        public static void Press(GameAction a)
        {
            _held[(int)a] = true;
            _pressedLatch[(int)a] = true;
        }

        public static void Release(GameAction a) => _held[(int)a] = false;

        /// <summary>Tap = Press + Release in one call (still registers as pressed this frame).</summary>
        public static void Tap(GameAction a)
        {
            _pressedLatch[(int)a] = true;
        }

        /// <summary>Returns true once per press. Used by the reader.</summary>
        public static bool ConsumePressed(GameAction a)
        {
            bool v = _pressedLatch[(int)a];
            _pressedLatch[(int)a] = false;
            return v;
        }

        public static Vector2 ConsumeLookDelta()
        {
            var v = LookDelta;
            LookDelta = Vector2.zero;
            return v;
        }

        public static void Clear()
        {
            Move = Vector2.zero;
            LookDelta = Vector2.zero;
            for (int i = 0; i < _held.Length; i++) { _held[i] = false; _pressedLatch[i] = false; }
        }
    }
}
