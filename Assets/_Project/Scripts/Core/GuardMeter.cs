using UnityEngine;

namespace LightMMORPG.Core
{
    /// <summary>Block guard: drains on blocked hits, breaks at 0 (stun), refills afterwards / while not blocking.</summary>
    public sealed class GuardMeter
    {
        public float Max { get; }
        public float RecoverPerSecond { get; }
        public float BreakStunDuration { get; }
        public float Current { get; private set; }
        public float BreakStunRemaining { get; private set; }
        public bool IsBroken => BreakStunRemaining > 0f;

        public GuardMeter() : this(CombatConstants.MaxGuard, CombatConstants.GuardRecoverPerSecond, CombatConstants.GuardBreakStun) { }

        public GuardMeter(float max, float recoverPerSecond, float breakStunDuration)
        {
            Max = max;
            RecoverPerSecond = recoverPerSecond;
            BreakStunDuration = breakStunDuration;
            Current = max;
        }

        /// <summary>Drain the meter. Returns true when this drain caused a guard break.</summary>
        public bool Drain(float amount)
        {
            if (IsBroken || amount <= 0f) return false;
            Current = Mathf.Max(0f, Current - amount);
            if (Current > 0f) return false;
            BreakStunRemaining = BreakStunDuration;
            return true;
        }

        /// <summary>Advance time. While broken the stun counts down, then the meter refills to full.
        /// Otherwise it recovers gradually while the player is not blocking.</summary>
        public void Tick(float dt, bool blocking)
        {
            if (dt <= 0f) return;
            if (IsBroken)
            {
                BreakStunRemaining = Mathf.Max(0f, BreakStunRemaining - dt);
                if (BreakStunRemaining <= 0f) Current = Max;
                return;
            }
            if (!blocking) Current = Mathf.Min(Max, Current + RecoverPerSecond * dt);
        }

        public void Reset()
        {
            Current = Max;
            BreakStunRemaining = 0f;
        }
    }
}
