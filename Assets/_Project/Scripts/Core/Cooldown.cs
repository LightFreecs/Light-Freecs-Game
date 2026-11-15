using UnityEngine;

namespace LightMMORPG.Core
{
    /// <summary>
    /// Countdown timer. Drive it with <see cref="Tick"/> + <see cref="TryUse()"/> (tick delta inside prediction) or with
    /// absolute time via <see cref="TryUse(double)"/> / <see cref="IsReadyAt"/> (server clock). Use one style per instance.
    /// </summary>
    public sealed class Cooldown
    {
        public float Duration { get; set; }
        public float Remaining { get; private set; }
        private double _readyTime = double.NegativeInfinity;

        public Cooldown(float duration)
        {
            Duration = duration;
        }

        // ---- Tick-delta style ----
        public bool IsReady => Remaining <= 0f;
        public float Progress01 => Duration <= 0f ? 1f : 1f - Mathf.Clamp01(Remaining / Duration);

        public void Tick(float dt)
        {
            if (Remaining > 0f) Remaining = Mathf.Max(0f, Remaining - dt);
        }

        public bool TryUse()
        {
            if (!IsReady) return false;
            Remaining = Duration;
            return true;
        }

        /// <summary>Set directly (reconcile).</summary>
        public void SetRemaining(float remaining) => Remaining = Mathf.Max(0f, remaining);

        // ---- Absolute-time style ----
        public bool IsReadyAt(double now) => now >= _readyTime;
        public float RemainingAt(double now) => (float)System.Math.Max(0.0, _readyTime - now);

        public bool TryUse(double now)
        {
            if (!IsReadyAt(now)) return false;
            _readyTime = now + Duration;
            return true;
        }

        public void Reset()
        {
            Remaining = 0f;
            _readyTime = double.NegativeInfinity;
        }
    }
}
