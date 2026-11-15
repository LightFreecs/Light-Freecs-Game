using UnityEngine;

namespace LightMMORPG.Core
{
    /// <summary>Hit points with knock-out at 0 and a down timer until respawn.</summary>
    public sealed class HealthPool
    {
        public float Max { get; }
        public float KnockedOutDuration { get; }
        public float Current { get; private set; }
        public bool IsKnockedOut { get; private set; }
        public float DownRemaining { get; private set; }

        public HealthPool() : this(CombatConstants.MaxHealth, CombatConstants.KnockedOutDuration) { }

        public HealthPool(float max, float knockedOutDuration)
        {
            Max = max;
            KnockedOutDuration = knockedOutDuration;
            Current = max;
        }

        /// <summary>Apply damage. Returns true if this hit knocked the player out. Ignored while already KO.</summary>
        public bool ApplyDamage(float amount)
        {
            if (IsKnockedOut || amount <= 0f) return false;
            Current = Mathf.Max(0f, Current - amount);
            if (Current > 0f) return false;
            IsKnockedOut = true;
            DownRemaining = KnockedOutDuration;
            return true;
        }

        public void Heal(float amount)
        {
            if (IsKnockedOut || amount <= 0f) return;
            Current = Mathf.Min(Max, Current + amount);
        }

        /// <summary>Advance the down timer. Returns true on the tick the player should respawn.</summary>
        public bool Tick(float dt)
        {
            if (!IsKnockedOut || DownRemaining <= 0f) return false;
            DownRemaining = Mathf.Max(0f, DownRemaining - dt);
            return DownRemaining <= 0f;
        }

        /// <summary>Full HP, clears KO (respawn).</summary>
        public void Reset()
        {
            Current = Max;
            IsKnockedOut = false;
            DownRemaining = 0f;
        }
    }
}
