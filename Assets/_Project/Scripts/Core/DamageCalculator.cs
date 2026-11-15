using UnityEngine;

namespace LightMMORPG.Core
{
    public readonly struct DamageResult
    {
        /// <summary>Damage dealt to health.</summary>
        public readonly float HealthDamage;
        /// <summary>Damage absorbed by the block (0 when not blocking).</summary>
        public readonly float BlockedDamage;
        /// <summary>Amount to drain from the defender's guard meter.</summary>
        public readonly float GuardDamage;

        public DamageResult(float healthDamage, float blockedDamage, float guardDamage)
        {
            HealthDamage = healthDamage;
            BlockedDamage = blockedDamage;
            GuardDamage = guardDamage;
        }
    }

    /// <summary>Pure damage math: surge multiplier on the attacker, block reduction and guard drain on the defender.</summary>
    public static class DamageCalculator
    {
        public static float ApplySurge(float baseDamage, bool attackerSurging)
        {
            return attackerSurging ? baseDamage * CombatConstants.SurgeDamageMultiplier : baseDamage;
        }

        public static DamageResult Calculate(float baseDamage, bool attackerSurging, bool defenderBlocking)
        {
            float dmg = Mathf.Max(0f, ApplySurge(baseDamage, attackerSurging));
            if (!defenderBlocking)
                return new DamageResult(dmg, 0f, 0f);

            float blocked = dmg * CombatConstants.BlockDamageReduction;
            return new DamageResult(dmg - blocked, blocked, blocked * CombatConstants.GuardDrainPerBlockedDamage);
        }

        /// <summary>Charged Beam damage: linear min → max over 0 → maxChargeTime seconds of charge (clamped).</summary>
        public static float BeamDamage(float chargeTime,
            float minDamage = CombatConstants.BeamMinDamage,
            float maxDamage = CombatConstants.BeamMaxDamage,
            float maxChargeTime = CombatConstants.BeamMaxChargeTime)
        {
            float t = maxChargeTime <= 0f ? 1f : Mathf.Clamp01(chargeTime / maxChargeTime);
            return Mathf.Lerp(minDamage, maxDamage, t);
        }
    }
}
