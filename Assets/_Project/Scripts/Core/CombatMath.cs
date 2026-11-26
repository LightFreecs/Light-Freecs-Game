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
        /// <summary>Knockback of a tapped heavy (no charge): a shove of ~5 m.</summary>
        public const float HeavyKnockbackSpeed = 15f;
        public const float HeavyLift = 8f;
        /// <summary>Knockback of a fully charged heavy: sends the victim flying ~23 m, about a second in the air.</summary>
        public const float HeavyMaxKnockbackSpeed = 32f;
        public const float HeavyMaxLift = 14f;
        /// <summary>Gravity on a fighter on foot (PlayerMotor's default), for the hang time of a launch.</summary>
        public const float FighterGravity = 28f;
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

        /// <summary>How charged a held attack is: 0 = a tap, 1 = held for <paramref name="maxChargeTime"/> or longer.</summary>
        public static float Charge01(float chargeTime, float maxChargeTime) =>
            maxChargeTime <= 0f ? 1f : Mathf.Clamp01(chargeTime / maxChargeTime);

        /// <summary>
        /// Heavy attack knockback by charge (0..1): from a shove (15 m/s, lift 8: ~5 m) to sending the victim flying
        /// (32 m/s, lift 14: ~23 m, about a second in the air).
        /// </summary>
        public static Vector3 HeavyKnockback(Vector3 direction, float charge01)
        {
            float c = Mathf.Clamp01(charge01);
            return Knockback(direction, Mathf.Lerp(HeavyKnockbackSpeed, HeavyMaxKnockbackSpeed, c), Mathf.Lerp(HeavyLift, HeavyMaxLift, c));
        }

        /// <summary>Seconds a fighter launched with this upward speed spends in the air (flat ground).</summary>
        public static float AirTime(float lift) => 2f * Mathf.Max(0f, lift) / FighterGravity;

        /// <summary>Seconds in the air after a heavy of this charge (0..1): ~0.57 s for a tap, 1 s at full charge.</summary>
        public static float HeavyAirTime(float charge01) => AirTime(Mathf.Lerp(HeavyLift, HeavyMaxLift, Mathf.Clamp01(charge01)));

        // ---------------- fight feel (shared by the server's motor and every client's visuals) ----------------

        /// <summary>Seconds at the start of a dash (or a rush) in which blows and blasts slip past: a dodge.</summary>
        public const float DashEvadeTime = 0.15f;

        /// <summary>Hit-stop of a light blow: the clips hold a light impact's drawing 2-3 frames (0.07-0.10 s).</summary>
        public const float HitStopLight = 0.07f;
        /// <summary>Hit-stop of a big blow (finisher, tapped heavy, beam, guard break): flash + held drawing ~0.17 s in the clips.</summary>
        public const float HitStopBig = 0.15f;
        /// <summary>Extra hit-stop of a fully charged heavy / a launching skill: the clips' biggest blows hold 0.33-0.5 s.</summary>
        public const float HitStopCharge = 0.15f;

        /// <summary>
        /// Hit-stop of a blow in seconds: the impact freeze. The server holds the victim (and its knockback) still this long,
        /// and every client freezes the fighters' poses for as long. Crisp on lights (<see cref="HitStopLight"/>), long on big
        /// blows (finishers, heavies, beams, guard breaks), longest on a charged heavy or a launch (<paramref name="charge01"/>).
        /// Measured from the reference clips (docs/COMBAT_PLAN.md, "Reference breakdown").
        /// </summary>
        public static float HitStop(bool big, float charge01) => (big ? HitStopBig : HitStopLight) + HitStopCharge * Mathf.Clamp01(charge01);

        /// <summary>
        /// Share of the average speed for one tick of an eased dash (an attack's lunge, a rush): <paramref name="remaining"/>
        /// ticks left including this one, out of <paramref name="total"/>. Fast out of the start, slowing into the arrival;
        /// <paramref name="ease"/> 0 = constant speed, 1 = from twice the average down to nothing. The shares of all the
        /// ticks add up to <paramref name="total"/>, so the distance covered is exact.
        /// </summary>
        public static float EaseOutStep(int remaining, int total, float ease)
        {
            if (total <= 1) return 1f;
            int k = Mathf.Clamp(remaining, 1, total);
            return 1f + Mathf.Clamp01(ease) * ((2f * k - 1f) / total - 1f);
        }

        /// <summary>
        /// Degrees to turn a sideways step towards the one you circle round, so the step follows the chord of that circle
        /// instead of its tangent and the distance stays the same (<paramref name="lateralStep"/>: metres sideways this
        /// step, right positive; <paramref name="radius"/>: distance to the centre).
        /// </summary>
        public static float OrbitChordDegrees(float lateralStep, float radius) =>
            radius > 1e-3f ? Mathf.Rad2Deg * lateralStep / (2f * radius) : 0f;

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
