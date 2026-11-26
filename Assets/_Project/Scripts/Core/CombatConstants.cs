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

        // Stamina (overhaul T1, StaminaPool): an exchange is a short burst, then a breather. A full light string plus a dash
        // costs about half the bar (4 x 6 + 14 = 38), so mashing runs dry. Tuned from the clips' rhythm (docs/COMBAT_PLAN.md).
        public const float MaxStamina = 100f;
        public const float StaminaRegenPerSecond = 30f;
        /// <summary>Seconds without spending before stamina starts coming back.</summary>
        public const float StaminaRegenDelay = 0.6f;
        public const float StaminaRegenStill = 1.4f;
        /// <summary>Regen while running or flying.</summary>
        public const float StaminaRegenMoving = 0.6f;
        /// <summary>Regen while blocking, powering up or winding up a heavy.</summary>
        public const float StaminaRegenGuarding = 0.35f;
        public const float StaminaLight = 6f;
        /// <summary>A heavy's cost at the release (needs this much to start the wind-up); a full charge adds <see cref="StaminaHeavyCharge"/>.</summary>
        public const float StaminaHeavy = 10f;
        public const float StaminaHeavyCharge = 8f;
        /// <summary>Sword skills by tier (Twin Fang, Quartet Edge, Comet Thrust), on top of their energy.</summary>
        public static readonly float[] StaminaSkill = { 12f, 20f, 28f };
        public const float StaminaDash = 14f;
        public const float StaminaRush = 18f;
        public const float StaminaFlashStep = 22f;
        /// <summary>Extra cost of a dash that cuts a blow's recovery short.</summary>
        public const float StaminaDashCancel = 4f;
        /// <summary>Given back when a dash actually slips past a blow or a blast (a perfect dodge).</summary>
        public const float StaminaDodgeRefund = 10f;
        /// <summary>Winded (stamina hit 0) lasts until it has come back to this much.</summary>
        public const float WindedRecoverAt = 40f;
        /// <summary>Winded: a blow's recovery (the time until the next light, the heavy's cooldown, the committed recovery) x this.</summary>
        public const float WindedRecoveryMultiplier = 1.3f;
        /// <summary>Winded: guard damage taken x this.</summary>
        public const float WindedGuardDamageMultiplier = 1.5f;
        /// <summary>A light thrown without the stamina for it (a tired swing): damage x this, and no finisher knockback.</summary>
        public const float TiredDamageMultiplier = 0.6f;

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
