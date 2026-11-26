using UnityEngine;

namespace LightMMORPG.Combat
{
    /// <summary>
    /// A sword skill (sword stance + held Heavy). The held wind-up is the pre-motion (the Sword moveset's heavy wind-up: low,
    /// blade drawn back at the hip, tip forward): the longer it is held, the stronger the skill it readies, and the blade glows in
    /// that skill's colour. On release the system carries the fighter: one dash to the target, a fixed string of strikes, then a
    /// post-motion delay in which they can't act or move. Each strike plays one of the Sword moveset's skill moves
    /// (<see cref="Characters.BasicMoveset.Skills"/>, keyed in Blender), and the server resolves it at that move's <c>Hit</c>.
    /// </summary>
    public sealed class SwordSkill
    {
        public string Name;
        /// <summary>Seconds of pre-motion before this skill is ready.</summary>
        public float MinCharge;
        /// <summary>Blade / trail colour (HDR).</summary>
        public Color Glow;
        /// <summary>When each strike's animation starts, seconds after the release (each cross-fades from the strike before).</summary>
        public float[] Starts;
        /// <summary>Per strike: its move in <c>BasicMovesets.Sword.Skills</c>; -1 = the sword's heavy swing (the Comet Thrust).</summary>
        public int[] Moves;
        public float[] Damage;
        /// <summary>Last strike: 0 = finisher knockback, above 0 = heavy launch at this charge (0..1).</summary>
        public float FinalLaunch;
        public float Energy, Cooldown, PostDelay;
        /// <summary>The dash to the target (m), and the dash along the aim when no one is in reach.</summary>
        public float MaxLunge, BaseLunge;
        public float Duration => Starts[Starts.Length - 1] + 0.3f;
    }

    public static class SwordSkills
    {
        // the Sword moveset's skill moves (BasicMovesets.BuildSword: Skills = fang1, fang2, quartet1..4, comet)
        private const int Fang1 = 0, Fang2 = 1, Quartet1 = 2, Quartet2 = 3, Quartet3 = 4, Quartet4 = 5, Comet = 6;

        public static readonly SwordSkill[] All =
        {
            // up over the right shoulder and down to the left hip, then over the left shoulder and down to the right: an X
            new SwordSkill
            {
                Name = "Twin Fang", MinCharge = 0f, Glow = new Color(0.45f, 1.4f, 3f),
                Starts = new[] { 0f, 0.17f }, Moves = new[] { Fang1, Fang2 }, Damage = new[] { 50f, 70f },
                Energy = 5f, Cooldown = 0.9f, PostDelay = 0.2f, MaxLunge = 4.5f, BaseLunge = 1.5f,
            },
            // a square: across the top, down the left side, across the bottom, up the right side (the launcher)
            new SwordSkill
            {
                Name = "Quartet Edge", MinCharge = 0.45f, Glow = new Color(0.8f, 3f, 0.7f),
                Starts = new[] { 0f, 0.13f, 0.26f, 0.42f }, Moves = new[] { Quartet1, Quartet2, Quartet3, Quartet4 },
                Damage = new[] { 40f, 40f, 45f, 85f },
                FinalLaunch = 0.35f, Energy = 15f, Cooldown = 1.6f, PostDelay = 0.4f, MaxLunge = 6f, BaseLunge = 2.5f,
            },
            // from the coiled pre-motion, a long lunging thrust held through the dash
            new SwordSkill
            {
                Name = "Comet Thrust", MinCharge = 1.1f, Glow = new Color(3f, 1.1f, 0.3f),
                Starts = new[] { 0f }, Moves = new[] { Comet }, Damage = new[] { 290f },
                FinalLaunch = 1f, Energy = 25f, Cooldown = 2.4f, PostDelay = 0.6f, MaxLunge = 10f, BaseLunge = 6f,
            },
        };

        /// <summary>The move strike <paramref name="i"/> of <paramref name="sk"/> plays (null: none, a -1 strike is the sword's heavy swing).</summary>
        public static Characters.AttackMove StrikeMove(SwordSkill sk, int i)
        {
            var set = Characters.BasicMovesets.Sword;
            int m = sk.Moves[i];
            if (m < 0) return set.Heavy?.Swing;
            return set.Skills != null && m < set.Skills.Length ? set.Skills[m] : null;
        }

        /// <summary>The strongest skill a pre-motion held for <paramref name="charge"/> seconds has readied.</summary>
        public static int TierFor(float charge)
        {
            int t = 0;
            for (int i = 1; i < All.Length; i++) if (charge >= All[i].MinCharge) t = i;
            return t;
        }
    }
}
