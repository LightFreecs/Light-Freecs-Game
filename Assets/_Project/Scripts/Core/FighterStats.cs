using System;

namespace LightMMORPG.Core
{
    /// <summary>The five fighter stats a player spends level-up points on.</summary>
    public enum StatId { Power, Vitality, Speed, Ki, Defense }

    /// <summary>
    /// Points in each stat. Used for what a player spent (saved on the account, sent to the server) and for bonus
    /// points from other sources (equipment later). Plain public ints so JsonUtility can save it.
    /// </summary>
    [Serializable]
    public struct StatAllocation : IEquatable<StatAllocation>
    {
        public int power, vitality, speed, ki, defense;

        public StatAllocation(int power, int vitality, int speed, int ki, int defense)
        {
            this.power = power; this.vitality = vitality; this.speed = speed; this.ki = ki; this.defense = defense;
        }

        public int Total => power + vitality + speed + ki + defense;

        public int this[StatId id]
        {
            readonly get => id switch
            {
                StatId.Power => power,
                StatId.Vitality => vitality,
                StatId.Speed => speed,
                StatId.Ki => ki,
                _ => defense,
            };
            set
            {
                switch (id)
                {
                    case StatId.Power: power = value; break;
                    case StatId.Vitality: vitality = value; break;
                    case StatId.Speed: speed = value; break;
                    case StatId.Ki: ki = value; break;
                    default: defense = value; break;
                }
            }
        }

        public static StatAllocation operator +(StatAllocation a, StatAllocation b) =>
            new(a.power + b.power, a.vitality + b.vitality, a.speed + b.speed, a.ki + b.ki, a.defense + b.defense);

        /// <summary>All five stats in one int for the network (6 bits each, values 0–63; anything else is clamped).</summary>
        public readonly int Pack()
        {
            int v = 0;
            for (int i = 0; i < FighterStats.StatCount; i++)
                v |= Math.Max(0, Math.Min(63, this[(StatId)i])) << (i * 6);
            return v;
        }

        public static StatAllocation Unpack(int packed)
        {
            var a = new StatAllocation();
            for (int i = 0; i < FighterStats.StatCount; i++) a[(StatId)i] = (packed >> (i * 6)) & 63;
            return a;
        }

        public readonly bool Equals(StatAllocation o) =>
            power == o.power && vitality == o.vitality && speed == o.speed && ki == o.ki && defense == o.defense;
        public override readonly bool Equals(object obj) => obj is StatAllocation o && Equals(o);
        public override readonly int GetHashCode() => Pack();
        public static bool operator ==(StatAllocation a, StatAllocation b) => a.Equals(b);
        public static bool operator !=(StatAllocation a, StatAllocation b) => !a.Equals(b);
        public override readonly string ToString() => $"P{power} V{vitality} S{speed} K{ki} D{defense}";
    }

    /// <summary>
    /// What a stat allocation does in combat, as multipliers on the base values (GameConfig / CombatConstants).
    /// Everything is exactly 1 with no points, so an unallocated fighter plays like the original balance.
    /// </summary>
    public struct FighterNumbers
    {
        /// <summary>Power: damage dealt by every attack.</summary>
        public float DamageMultiplier;
        /// <summary>Vitality: maximum health.</summary>
        public float HealthMultiplier;
        /// <summary>Speed: run, sprint, flight and dash speed.</summary>
        public float SpeedMultiplier;
        /// <summary>Ki: maximum energy.</summary>
        public float EnergyMultiplier;
        /// <summary>Ki: passive energy regen and hold-to-charge rate.</summary>
        public float EnergyRegenMultiplier;
        /// <summary>Defense: damage received (below 1 = tougher).</summary>
        public float DamageTakenMultiplier;
        /// <summary>Defense: size of the guard meter.</summary>
        public float GuardMultiplier;

        // Whole numbers for the pools, so the HUD shows "1150", not "1150.0001".
        public readonly float MaxHealth(float baseValue = CombatConstants.MaxHealth) => (float)Math.Round(baseValue * HealthMultiplier);
        public readonly float MaxEnergy(float baseValue = CombatConstants.MaxEnergy) => (float)Math.Round(baseValue * EnergyMultiplier);
        public readonly float MaxGuard(float baseValue = CombatConstants.MaxGuard) => (float)Math.Round(baseValue * GuardMultiplier);

        public static FighterNumbers Base => new()
        {
            DamageMultiplier = 1f, HealthMultiplier = 1f, SpeedMultiplier = 1f, EnergyMultiplier = 1f,
            EnergyRegenMultiplier = 1f, DamageTakenMultiplier = 1f, GuardMultiplier = 1f,
        };
    }

    /// <summary>
    /// Stat rules (pure, engine-free): points earned per level, spending with a cap per stat, respec, validation of
    /// an allocation a client claims, and the formulas that turn points into combat numbers. Levels only give points;
    /// a level by itself changes nothing, and a fully built level-50 fighter is at most 10–20% ahead in any one area
    /// (see the Per-point constants), so matches between levels stay fair.
    /// </summary>
    public static class FighterStats
    {
        public const int StatCount = 5;
        /// <summary>Points earned for each level after the first: level 1 has 0, level 50 has 49.</summary>
        public const int PointsPerLevel = 1;
        /// <summary>Most points a player can spend on one stat.</summary>
        public const int MaxPerStat = 20;
        /// <summary>Spent + bonus points (equipment) that still count for one stat.</summary>
        public const int MaxEffectivePerStat = 30;

        // Effect of one point. At the 20-point cap: +10% damage, +15% health, +5% speed, +20% energy and regen,
        // −8% damage taken and +20% guard.
        public const float DamagePerPower = 0.005f;
        public const float HealthPerVitality = 0.0075f;
        public const float SpeedPerSpeed = 0.0025f;
        public const float EnergyPerKi = 0.01f;
        public const float EnergyRegenPerKi = 0.01f;
        public const float DamageTakenPerDefense = 0.004f;
        public const float GuardPerDefense = 0.01f;

        public static readonly StatId[] All = { StatId.Power, StatId.Vitality, StatId.Speed, StatId.Ki, StatId.Defense };

        public static string DisplayName(StatId id) => id.ToString();

        /// <summary>One line for the Stats screen: what a point in this stat does.</summary>
        public static string Describe(StatId id) => id switch
        {
            StatId.Power => "Every attack hits harder",
            StatId.Vitality => "More health",
            StatId.Speed => "Run, fly and dash faster",
            StatId.Ki => "More energy, faster regen",
            _ => "Take less damage, bigger guard",
        };

        public static int ClampLevel(int level) => Math.Max(1, Math.Min(Progression.MaxLevel, level));

        /// <summary>Total stat points a fighter of this level has earned.</summary>
        public static int PointsFor(int level) => (ClampLevel(level) - 1) * PointsPerLevel;

        public static int Unspent(int level, StatAllocation a) => PointsFor(level) - a.Total;

        /// <summary>True when every stat is within 0..<see cref="MaxPerStat"/> and the total fits the level.</summary>
        public static bool IsValid(int level, StatAllocation a)
        {
            foreach (var id in All)
                if (a[id] < 0 || a[id] > MaxPerStat) return false;
            return a.Total <= PointsFor(level);
        }

        /// <summary>
        /// What the server (and the save file loader) uses: a valid allocation is returned as is, anything else
        /// (over-spent, over the cap, negative) is thrown away and the fighter gets the base stats.
        /// </summary>
        public static StatAllocation Validated(int level, StatAllocation a) => IsValid(level, a) ? a : default;

        public static bool CanAdd(int level, StatAllocation a, StatId id) => a[id] < MaxPerStat && Unspent(level, a) > 0;
        public static bool CanRemove(StatAllocation a, StatId id) => a[id] > 0;

        /// <summary>Spends one point. False (and no change) when the stat is capped or no points are left.</summary>
        public static bool TryAdd(int level, ref StatAllocation a, StatId id)
        {
            if (!CanAdd(level, a, id)) return false;
            a[id] = a[id] + 1;
            return true;
        }

        /// <summary>Takes one point back out of a stat.</summary>
        public static bool TryRemove(ref StatAllocation a, StatId id)
        {
            if (!CanRemove(a, id)) return false;
            a[id] = a[id] - 1;
            return true;
        }

        /// <summary>Respec: every point comes back (free).</summary>
        public static StatAllocation Respec() => default;

        /// <summary>
        /// Combat numbers for spent points plus optional bonus points (equipment). Each stat counts up to
        /// <see cref="MaxEffectivePerStat"/>; negative totals count as 0.
        /// </summary>
        public static FighterNumbers Compute(StatAllocation spent, StatAllocation bonus = default)
        {
            int P(StatId id) => Math.Max(0, Math.Min(MaxEffectivePerStat, spent[id] + bonus[id]));
            return new FighterNumbers
            {
                DamageMultiplier = 1f + DamagePerPower * P(StatId.Power),
                HealthMultiplier = 1f + HealthPerVitality * P(StatId.Vitality),
                SpeedMultiplier = 1f + SpeedPerSpeed * P(StatId.Speed),
                EnergyMultiplier = 1f + EnergyPerKi * P(StatId.Ki),
                EnergyRegenMultiplier = 1f + EnergyRegenPerKi * P(StatId.Ki),
                DamageTakenMultiplier = 1f - DamageTakenPerDefense * P(StatId.Defense),
                GuardMultiplier = 1f + GuardPerDefense * P(StatId.Defense),
            };
        }
    }
}
