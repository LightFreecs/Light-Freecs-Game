using UnityEngine;

namespace LightMMORPG.Core
{
    /// <summary>Energy resource: passive regen, faster charge while holding charge, spend, regen pause (Power Surge).</summary>
    public sealed class EnergyPool
    {
        public float Max { get; private set; }
        public float RegenPerSecond { get; private set; }
        public float ChargePerSecond { get; private set; }
        public float Current { get; private set; }
        /// <summary>When true (Power Surge active), passive regen and charging both stop.</summary>
        public bool RegenPaused { get; set; }

        public EnergyPool() : this(CombatConstants.MaxEnergy, CombatConstants.EnergyRegenPerSecond, CombatConstants.EnergyChargePerSecond) { }

        public EnergyPool(float max, float regenPerSecond, float chargePerSecond)
        {
            Max = max;
            RegenPerSecond = regenPerSecond;
            ChargePerSecond = chargePerSecond;
            Current = max;
        }

        public bool IsFull => Current >= Max;

        /// <summary>Change maximum and rates (stat build applied). Current energy keeps its fraction of the maximum.</summary>
        public void Configure(float max, float regenPerSecond, float chargePerSecond)
        {
            max = Mathf.Max(1f, max);
            float frac = Max > 0f ? Current / Max : 1f;
            Max = max;
            RegenPerSecond = regenPerSecond;
            ChargePerSecond = chargePerSecond;
            Current = max * frac;
        }

        /// <summary>Advance by dt seconds. Charging uses the charge rate instead of passive regen.</summary>
        public void Tick(float dt, bool charging)
        {
            if (dt <= 0f || RegenPaused) return;
            float rate = charging ? ChargePerSecond : RegenPerSecond;
            Current = Mathf.Min(Max, Current + rate * dt);
        }

        public bool CanSpend(float amount) => amount >= 0f && amount <= Current;

        public bool TrySpend(float amount)
        {
            if (!CanSpend(amount)) return false;
            Current -= amount;
            return true;
        }

        /// <summary>Set directly (reconcile / respawn).</summary>
        public void Set(float value) => Current = Mathf.Clamp(value, 0f, Max);

        public void Refill() => Current = Max;
    }
}
