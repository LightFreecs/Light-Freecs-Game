using UnityEngine;

namespace LightMMORPG.Combat
{
    /// <summary>
    /// A sword skill (sword stance + held Heavy). The held wind-up is the pre-motion: the longer it is held, the stronger the
    /// skill it readies, and the blade glows in that skill's colour. On release the system carries the fighter: one dash to
    /// the target, a fixed string of strikes, then a post-motion delay in which they can't act or move.
    /// Each strike replays a Blender-keyed basic move (a light combo index, or -1 = the heavy swing at full charge) until
    /// sword moves are keyed in Blender (docs/COMBAT_PLAN.md).
    /// </summary>
    public sealed class SwordSkill
    {
        public string Name;
        /// <summary>Seconds of pre-motion before this skill is ready.</summary>
        public float MinCharge;
        /// <summary>Blade / trail colour (HDR).</summary>
        public Color Glow;
        /// <summary>When each strike's animation starts, seconds after the release.</summary>
        public float[] Starts;
        /// <summary>Light combo index per strike; -1 = the heavy swing.</summary>
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
        public static readonly SwordSkill[] All =
        {
            new SwordSkill
            {
                Name = "Twin Fang", MinCharge = 0f, Glow = new Color(0.45f, 1.4f, 3f),
                Starts = new[] { 0f, 0.17f }, Moves = new[] { 0, 1 }, Damage = new[] { 50f, 70f },
                Energy = 5f, Cooldown = 0.9f, PostDelay = 0.2f, MaxLunge = 4.5f, BaseLunge = 1.5f,
            },
            new SwordSkill
            {
                Name = "Quartet Edge", MinCharge = 0.45f, Glow = new Color(0.8f, 3f, 0.7f),
                Starts = new[] { 0f, 0.13f, 0.26f, 0.42f }, Moves = new[] { 0, 1, 2, 3 }, Damage = new[] { 40f, 40f, 45f, 85f },
                FinalLaunch = 0.35f, Energy = 15f, Cooldown = 1.6f, PostDelay = 0.4f, MaxLunge = 6f, BaseLunge = 2.5f,
            },
            new SwordSkill
            {
                Name = "Comet Thrust", MinCharge = 1.1f, Glow = new Color(3f, 1.1f, 0.3f),
                Starts = new[] { 0f }, Moves = new[] { -1 }, Damage = new[] { 290f },
                FinalLaunch = 1f, Energy = 25f, Cooldown = 2.4f, PostDelay = 0.6f, MaxLunge = 10f, BaseLunge = 6f,
            },
        };

        /// <summary>The strongest skill a pre-motion held for <paramref name="charge"/> seconds has readied.</summary>
        public static int TierFor(float charge)
        {
            int t = 0;
            for (int i = 1; i < All.Length; i++) if (charge >= All[i].MinCharge) t = i;
            return t;
        }
    }
}
