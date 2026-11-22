namespace LightMMORPG.Core
{
    /// <summary>Prototype gameplay numbers (see docs/ARCHITECTURE.md "Gameplay rules"). Shared by prediction and server code.</summary>
    public static class CombatConstants
    {
        // Stats
        public const float MaxHealth = 1000f;
        public const float MaxEnergy = 100f;
        public const float EnergyRegenPerSecond = 4f;
        public const float EnergyChargePerSecond = 30f;

        // Movement: walking is the default, holding Run (Shift) runs (the Locomotion Bench gaits are made for these speeds)
        public const float WalkSpeed = 2.2f;
        public const float RunSpeed = 8f;
        public const float FlySpeed = 11f;
        public const float DashSpeed = 25f;
        public const float DashDuration = 0.18f;
        public const float DashCooldown = 0.6f;
        public const float FlashStepDistance = 7f;
        public const float FlashStepCooldown = 2.5f;

        // Melee
        public static readonly float[] LightComboDamage = { 30f, 30f, 40f, 60f };
        public const int LightComboLength = 4;
        public const float ComboWindow = 0.6f;
        /// <summary>Heavy attack damage for a tap (no charge). Holding Heavy charges it up to <see cref="HeavyMaxDamage"/>.</summary>
        public const float HeavyDamage = 110f;
        /// <summary>Heavy attack damage when held for the full <see cref="HeavyMaxChargeTime"/>.</summary>
        public const float HeavyMaxDamage = 300f;
        /// <summary>Seconds of holding Heavy for a full charge (damage, knockback and hitstun all grow until then).</summary>
        public const float HeavyMaxChargeTime = 1.5f;
        /// <summary>Movement speed while winding up a heavy (share of the walk / fly speed; no running).</summary>
        public const float HeavyChargeMoveMultiplier = 0.35f;
        public const float HeavyCooldown = 1.2f;
        public const float MeleeHitRadius = 1.2f;
        public const float MeleeHitForwardOffset = 1.3f;

        // Block / guard
        public const float BlockMoveMultiplier = 0.35f;
        public const float BlockDamageReduction = 0.8f;
        public const float MaxGuard = 100f;
        /// <summary>Guard meter drain per point of blocked (absorbed) damage.</summary>
        public const float GuardDrainPerBlockedDamage = 0.5f;
        public const float GuardBreakStun = 1.0f;
        /// <summary>Prototype value (not in spec): guard refill rate while not blocking.</summary>
        public const float GuardRecoverPerSecond = 20f;

        // Hitstun
        public const float LightHitstun = 0.35f;
        public const float HeavyHitstun = 0.6f;
        /// <summary>Hitstun of a fully charged heavy: covers the flight, so the victim can't act until it has landed.</summary>
        public const float HeavyMaxHitstun = 1.3f;

        // Powers
        public const float SurgeDamageMultiplier = 1.35f;
        public const float SurgeSpeedMultiplier = 1.2f;
        public const float SurgeDuration = 10f;

        public const float BeamMinDamage = 150f;
        public const float BeamMaxDamage = 400f;
        public const float BeamMaxChargeTime = 1.5f;

        // Power level + aura reserve (AuraReserve): battles start weak with a full reserve that never refills
        /// <summary>Power level at the start of every battle (share of full strength).</summary>
        public const float StartPower = 0.1f;
        /// <summary>Power level gained per second of holding Charge (10 % → 100 % in 3.6 s; Ki speeds it up).</summary>
        public const float PowerChargePerSecond = 0.25f;
        /// <summary>Aura reserve with no Ki points.</summary>
        public const float AuraReserve = 1000f;
        /// <summary>Aura burnt per second while at 100 % power (none at the start level).</summary>
        public const float AuraUpkeepAtFullPower = 10f;
        /// <summary>Upkeep curve: (power above the start level)^this, so half power costs ~1/3 of full.</summary>
        public const float AuraUpkeepExponent = 1.5f;
        /// <summary>Seconds after the reserve runs dry until exhaustion is total.</summary>
        public const float ExhaustRampTime = 30f;
        /// <summary>Strength lost at total exhaustion (0.4 = down to 60 %).</summary>
        public const float ExhaustStrengthLoss = 0.4f;
        /// <summary>Extra damage taken at total exhaustion (0.4 = 140 %).</summary>
        public const float ExhaustDefenseLoss = 0.4f;

        // KO
        public const float KnockedOutDuration = 5f;
    }
}
