using UnityEngine;

namespace LightMMORPG.Core
{
    /// <summary>
    /// Power level + aura reserve (owner's battle rule, 2026-11-19): every battle (each spawn) starts at
    /// <see cref="CombatConstants.StartPower"/> (10 %) strength with a full aura reserve (size from the Ki stat).
    /// Holding Charge raises the power level towards 100 %. The reserve never refills during the battle: energy attacks
    /// (blast, beam, surge, later specials) spend it, and holding a high power level burns it every second (more the
    /// higher the power: start strong, or save the aura). Once it is empty the power level can't be charged any more and
    /// strength and defence sink little by little (<see cref="Exhaustion"/> 0 → 1 over
    /// <see cref="CombatConstants.ExhaustRampTime"/>).
    /// </summary>
    public sealed class AuraReserve
    {
        public float Max { get; private set; }
        public float Current { get; private set; }
        /// <summary>Power level, <see cref="CombatConstants.StartPower"/> .. 1: the share of full strength in use.</summary>
        public float Power { get; private set; }
        /// <summary>0 while there is aura left; then rises to 1 and weakens strength and defence.</summary>
        public float Exhaustion { get; private set; }

        public AuraReserve() : this(CombatConstants.AuraReserve) { }

        public AuraReserve(float max)
        {
            Max = Mathf.Max(1f, max);
            Reset();
        }

        public bool Depleted => Current <= 0f;

        /// <summary>A new battle: full reserve, power back to the start level, no exhaustion.</summary>
        public void Reset()
        {
            Current = Max;
            Power = CombatConstants.StartPower;
            Exhaustion = 0f;
        }

        /// <summary>Change the reserve size (stat build applied); the reserve keeps its fraction.</summary>
        public void SetMax(float max)
        {
            max = Mathf.Max(1f, max);
            float frac = Current / Max;
            Max = max;
            Current = max * frac;
        }

        /// <summary>Advance by dt seconds. Charging raises the power level at <paramref name="chargePerSecond"/> (share per second).</summary>
        public void Tick(float dt, bool charging, float chargePerSecond)
        {
            if (dt <= 0f) return;
            if (Depleted)
            {
                Exhaustion = Mathf.Min(1f, Exhaustion + dt / CombatConstants.ExhaustRampTime);
                return;
            }
            if (charging) Power = Mathf.Min(1f, Power + chargePerSecond * dt);
            Current = Mathf.Max(0f, Current - Upkeep(Power) * dt);
        }

        public bool CanSpend(float amount) => amount <= 0f || Current >= amount;

        public bool TrySpend(float amount)
        {
            if (amount <= 0f) return true;
            if (Current < amount) return false;
            Current -= amount;
            return true;
        }

        /// <summary>Set directly (tests, reconcile).</summary>
        public void Set(float current, float power, float exhaustion)
        {
            Current = Mathf.Clamp(current, 0f, Max);
            Power = Mathf.Clamp(power, CombatConstants.StartPower, 1f);
            Exhaustion = Mathf.Clamp01(exhaustion);
        }

        public float StrengthMultiplier => Strength(Power, Exhaustion);
        public float DamageTakenMultiplier => DamageTaken(Exhaustion);

        /// <summary>Aura burnt per second at this power level: nothing at the start level, <see cref="CombatConstants.AuraUpkeepAtFullPower"/> at 100 %.</summary>
        public static float Upkeep(float power)
        {
            float above = Mathf.Clamp01((power - CombatConstants.StartPower) / (1f - CombatConstants.StartPower));
            return CombatConstants.AuraUpkeepAtFullPower * Mathf.Pow(above, CombatConstants.AuraUpkeepExponent);
        }

        /// <summary>Damage dealt multiplier: the power level, sinking with exhaustion.</summary>
        public static float Strength(float power, float exhaustion) =>
            Mathf.Clamp(power, 0f, 1f) * (1f - CombatConstants.ExhaustStrengthLoss * Mathf.Clamp01(exhaustion));

        /// <summary>Damage received multiplier: 1, rising with exhaustion (defence sinks).</summary>
        public static float DamageTaken(float exhaustion) => 1f + CombatConstants.ExhaustDefenseLoss * Mathf.Clamp01(exhaustion);
    }
}
