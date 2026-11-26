using LightMMORPG.Core;
using NUnit.Framework;
using UnityEngine;

namespace LightMMORPG.Tests
{
    /// <summary>The shared fight-feel math: eased dashes, hit-stop, circling a locked-on opponent.</summary>
    public class CombatFeelTests
    {
        [Test]
        public void EasedDashCoversTheExactDistance()
        {
            foreach (int n in new[] { 1, 2, 3, 7, 10 })
            foreach (float ease in new[] { 0f, 0.55f, 0.8f, 1f })
            {
                float sum = 0f;
                for (int k = n; k >= 1; k--) sum += CombatMath.EaseOutStep(k, n, ease);
                Assert.AreEqual(n, sum, 1e-4f, "ticks " + n + ", ease " + ease);
            }
        }

        [Test]
        public void EasedDashBurstsOutAndSlowsIntoTheArrival()
        {
            float first = CombatMath.EaseOutStep(7, 7, 0.8f), last = CombatMath.EaseOutStep(1, 7, 0.8f);
            Assert.Greater(first, 1f);
            Assert.Less(last, 1f);
            Assert.Greater(last, 0f);
            for (int k = 7; k > 1; k--) Assert.Greater(CombatMath.EaseOutStep(k, 7, 0.8f), CombatMath.EaseOutStep(k - 1, 7, 0.8f));
            Assert.AreEqual(1f, CombatMath.EaseOutStep(3, 5, 0f), 1e-6f);   // no easing: constant speed
        }

        [Test]
        public void HitStopIsLongerForBigAndChargedBlows()
        {
            float light = CombatMath.HitStop(false, 0f), big = CombatMath.HitStop(true, 0f), full = CombatMath.HitStop(true, 1f);
            Assert.Greater(big, light);
            Assert.Greater(full, big);
            Assert.AreEqual(full, CombatMath.HitStop(true, 5f), 1e-6f);   // the charge is capped
            Assert.Less(full, 0.5f);   // the motor caps a freeze at half a second
        }

        [Test]
        public void SidestepsCircleTheOpponentAtTheSameDistance()
        {
            // walk sideways round a centre 3 m away at 8 m/s, 30 ticks a second, for two seconds
            const float r = 3f, step = 8f / 30f;
            Vector2 centre = new Vector2(0f, r), withChord = Vector2.zero, tangentOnly = Vector2.zero;
            for (int i = 0; i < 60; i++)
            {
                withChord += Sidestep(centre - withChord, step, CombatMath.OrbitChordDegrees(step, (centre - withChord).magnitude));
                tangentOnly += Sidestep(centre - tangentOnly, step, 0f);
            }
            Assert.AreEqual(r, (centre - withChord).magnitude, 0.05f);
            Assert.Greater((centre - tangentOnly).magnitude, r + 0.5f);   // without the correction it spirals out
        }

        /// <summary>A step to the right of the bearing <paramref name="toCentre"/>, turned towards the centre by <paramref name="turnDeg"/>.</summary>
        private static Vector2 Sidestep(Vector2 toCentre, float step, float turnDeg)
        {
            Vector2 f = toCentre.normalized;
            Vector2 right = new Vector2(f.y, -f.x);
            float a = turnDeg * Mathf.Deg2Rad;
            return (right * Mathf.Cos(a) + f * Mathf.Sin(a)) * step;
        }
    }
}
