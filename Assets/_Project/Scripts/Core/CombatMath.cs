using UnityEngine;

namespace LightMMORPG.Core
{
    /// <summary>Pure helpers used by the server combat code (kept here so they are unit-testable).</summary>
    public static class CombatMath
    {
        /// <summary>Minimum time between two light attacks (anti-spam; the combo window still applies).</summary>
        public const float LightAttackInterval = 0.25f;
        /// <summary>Extra charge time the server accepts beyond what it measured (latency slack).</summary>
        public const float ChargeTolerance = 0.25f;

        public const float LightKnockbackSpeed = 2.5f;
        public const float FinisherKnockbackSpeed = 13f;
        public const float FinisherLift = 5f;
        public const float HeavyKnockbackSpeed = 15f;
        public const float HeavyLift = 8f;
        public const float BlockedPushSpeed = 3f;
        public const float BlastKnockbackSpeed = 4f;
        public const float BeamKnockbackSpeed = 16f;

        /// <summary>
        /// Centre of the melee hit sphere: <paramref name="forwardOffset"/> metres from the chest along the aim,
        /// with the vertical part of the aim halved on the ground (so looking down does not punch the floor).
        /// </summary>
        public static Vector3 MeleeHitCenter(Vector3 chest, Vector3 aim, float forwardOffset, bool flying)
        {
            if (!flying) aim.y *= 0.5f;
            if (aim.sqrMagnitude < 1e-6f) aim = Vector3.forward;
            return chest + aim.normalized * forwardOffset;
        }

        /// <summary>
        /// Charge time the server will use: the client's claim, clamped to [0, max] and to what the server
        /// itself measured plus <see cref="ChargeTolerance"/>.
        /// </summary>
        public static float ValidateCharge(float claimed, float serverMeasured, float maxCharge)
        {
            float c = Mathf.Min(Mathf.Max(0f, claimed), Mathf.Max(0f, serverMeasured) + ChargeTolerance);
            return Mathf.Clamp(c, 0f, maxCharge);
        }

        /// <summary>Knockback velocity along a horizontal direction plus lift.</summary>
        public static Vector3 Knockback(Vector3 direction, float speed, float lift)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) direction = Vector3.forward;
            return direction.normalized * speed + Vector3.up * lift;
        }

        /// <summary>Picks a spawn index from a 0..1 random value, avoiding <paramref name="avoid"/> when possible.</summary>
        public static int PickSpawn(int count, float random01, int avoid = -1)
        {
            if (count <= 0) return -1;
            if (count == 1) return 0;
            int pool = avoid >= 0 && avoid < count ? count - 1 : count;
            int i = Mathf.Min(pool - 1, Mathf.FloorToInt(Mathf.Clamp01(random01) * pool));
            if (pool < count && i >= avoid) i++;
            return i;
        }
    }
}
