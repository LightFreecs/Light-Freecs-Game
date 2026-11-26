using UnityEngine;

namespace LightMMORPG.Core
{
    /// <summary>
    /// Stamina (overhaul T1): every attack, dash and technique costs some; it comes back after a moment without spending.
    /// Running dry makes the fighter <see cref="Winded"/> until it has refilled to <see cref="CombatConstants.WindedRecoverAt"/>.
    /// Pure, like <see cref="EnergyPool"/>: the motor carries one in its predicted state, so the owner and the server agree on
    /// every gate (see <c>PlayerMotor</c>).
    /// </summary>
    public sealed class StaminaPool
    {
        public float Max { get; private set; }
        public float Current { get; private set; }
        /// <summary>Ran dry: no dash / rush / flash step / skills, heavy = tap only, slower recovery, guard hurts more.</summary>
        public bool Winded { get; private set; }
        /// <summary>Seconds since stamina was last spent (it comes back after <see cref="CombatConstants.StaminaRegenDelay"/>).</summary>
        public float SinceSpend { get; private set; }

        public StaminaPool() : this(CombatConstants.MaxStamina) { }

        public StaminaPool(float max)
        {
            Max = Mathf.Max(1f, max);
            Current = Max;
            SinceSpend = 10f;
        }

        public bool CanSpend(float amount) => amount <= Current + 1e-4f;

        /// <summary>Spend if there is enough; false (nothing spent) otherwise.</summary>
        public bool TrySpend(float amount)
        {
            if (!CanSpend(amount)) return false;
            Spend(amount);
            return true;
        }

        /// <summary>Spend what there is, down to 0 (a tired swing, a charge paid at the release). Hitting 0 = winded.</summary>
        public void Spend(float amount)
        {
            if (amount <= 0f) return;
            Current = Mathf.Max(0f, Current - amount);
            SinceSpend = 0f;
            if (Current <= 1e-4f) { Current = 0f; Winded = true; }
        }

        /// <summary>Give some back (a perfect dodge). Doesn't restart the regen delay.</summary>
        public void Refund(float amount)
        {
            if (amount <= 0f) return;
            Current = Mathf.Min(Max, Current + amount);
            if (Winded && Current >= CombatConstants.WindedRecoverAt) Winded = false;
        }

        /// <summary>Advance time: after the regen delay stamina comes back at the regen rate times <paramref name="regenMultiplier"/>.</summary>
        public void Tick(float dt, float regenMultiplier)
        {
            if (dt <= 0f) return;
            SinceSpend = Mathf.Min(SinceSpend + dt, 10f);
            if (SinceSpend >= CombatConstants.StaminaRegenDelay)
                Current = Mathf.Min(Max, Current + CombatConstants.StaminaRegenPerSecond * Mathf.Max(0f, regenMultiplier) * dt);
            if (Winded && Current >= CombatConstants.WindedRecoverAt) Winded = false;
        }

        /// <summary>Set directly (reconcile).</summary>
        public void Set(float current, float sinceSpend, bool winded)
        {
            Current = Mathf.Clamp(current, 0f, Max);
            SinceSpend = Mathf.Max(0f, sinceSpend);
            Winded = winded;
        }

        public void Refill()
        {
            Current = Max;
            Winded = false;
            SinceSpend = 10f;
        }

        /// <summary>
        /// How fast stamina comes back in this state: standing still x1.4, walking x1, running or flying x0.6, guarding or
        /// charging (powering up, winding up a heavy) x0.35.
        /// </summary>
        public static float RegenMultiplier(bool still, bool runningOrFlying, bool guardingOrCharging) =>
            guardingOrCharging ? CombatConstants.StaminaRegenGuarding
            : runningOrFlying ? CombatConstants.StaminaRegenMoving
            : still ? CombatConstants.StaminaRegenStill : 1f;
    }
}
