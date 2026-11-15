using UnityEngine;

namespace LightMMORPG.Core
{
    /// <summary>Timed buff (Power Surge): active while Remaining &gt; 0.</summary>
    public sealed class BuffTimer
    {
        public float Remaining { get; private set; }
        public float Duration { get; private set; }
        public bool IsActive => Remaining > 0f;
        public float Fraction01 => Duration <= 0f ? 0f : Mathf.Clamp01(Remaining / Duration);

        public void Activate(float duration)
        {
            Duration = Mathf.Max(0f, duration);
            Remaining = Duration;
        }

        /// <summary>Advances the timer; returns true on the tick the buff expires.</summary>
        public bool Tick(float dt)
        {
            if (Remaining <= 0f || dt <= 0f) return false;
            Remaining = Mathf.Max(0f, Remaining - dt);
            return Remaining <= 0f;
        }

        public void Clear() => Remaining = 0f;
    }
}
