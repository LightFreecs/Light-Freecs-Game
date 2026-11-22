using LightMMORPG.Core;
using NUnit.Framework;
using UnityEngine;

namespace LightMMORPG.Tests
{
    public class BuffTimerTests
    {
        [Test]
        public void ActivatesAndExpiresOnce()
        {
            var b = new BuffTimer();
            Assert.IsFalse(b.IsActive);
            b.Activate(10f);
            Assert.IsTrue(b.IsActive);
            Assert.AreEqual(1f, b.Fraction01, 1e-4f);
            Assert.IsFalse(b.Tick(4f));
            Assert.AreEqual(0.6f, b.Fraction01, 1e-4f);
            Assert.IsTrue(b.Tick(7f));
            Assert.IsFalse(b.IsActive);
            Assert.IsFalse(b.Tick(1f));
        }

        [Test]
        public void ClearEndsImmediately()
        {
            var b = new BuffTimer();
            b.Activate(5f);
            b.Clear();
            Assert.IsFalse(b.IsActive);
        }
    }

    public class CombatMathTests
    {
        [Test]
        public void ChargeIsClampedToServerMeasurementAndMax()
        {
            Assert.AreEqual(1.0f, CombatMath.ValidateCharge(1.0f, 1.1f, 1.5f), 1e-4f);
            Assert.AreEqual(0.5f + CombatMath.ChargeTolerance, CombatMath.ValidateCharge(1.5f, 0.5f, 1.5f), 1e-4f);
            Assert.AreEqual(1.5f, CombatMath.ValidateCharge(9f, 9f, 1.5f), 1e-4f);
            Assert.AreEqual(0f, CombatMath.ValidateCharge(-1f, 1f, 1.5f), 1e-4f);
        }

        [Test]
        public void MeleeCenterIsInFrontAndFlattenedOnGround()
        {
            Vector3 c = CombatMath.MeleeHitCenter(Vector3.zero, new Vector3(0f, 0f, 1f), 1.3f, false);
            Assert.AreEqual(1.3f, c.z, 1e-4f);
            Vector3 down = CombatMath.MeleeHitCenter(Vector3.zero, new Vector3(0f, -1f, 1f).normalized, 1.3f, false);
            Vector3 downFly = CombatMath.MeleeHitCenter(Vector3.zero, new Vector3(0f, -1f, 1f).normalized, 1.3f, true);
            Assert.Greater(down.y, downFly.y);
            Assert.AreEqual(1.3f, down.magnitude, 1e-4f);
        }

        [Test]
        public void KnockbackIsHorizontalPlusLift()
        {
            Vector3 k = CombatMath.Knockback(new Vector3(3f, 5f, 0f), 10f, 4f);
            Assert.AreEqual(10f, k.x, 1e-4f);
            Assert.AreEqual(4f, k.y, 1e-4f);
            Assert.AreEqual(0f, k.z, 1e-4f);
        }

        [Test]
        public void HeavyDamageGrowsWithHoldTime()
        {
            Assert.AreEqual(CombatConstants.HeavyDamage, DamageCalculator.HeavyDamage(0f), 1e-3f);
            Assert.AreEqual(CombatConstants.HeavyMaxDamage, DamageCalculator.HeavyDamage(CombatConstants.HeavyMaxChargeTime), 1e-3f);
            Assert.AreEqual(CombatConstants.HeavyMaxDamage, DamageCalculator.HeavyDamage(9f), 1e-3f);
            Assert.AreEqual((CombatConstants.HeavyDamage + CombatConstants.HeavyMaxDamage) * 0.5f,
                DamageCalculator.HeavyDamage(CombatConstants.HeavyMaxChargeTime * 0.5f), 1e-3f);
        }

        [Test]
        public void ChargedHeavySendsFlyingFurther()
        {
            Vector3 tap = CombatMath.HeavyKnockback(Vector3.forward, 0f), full = CombatMath.HeavyKnockback(Vector3.forward, 1f);
            Assert.AreEqual(CombatMath.HeavyKnockbackSpeed, tap.z, 1e-4f);
            Assert.AreEqual(CombatMath.HeavyLift, tap.y, 1e-4f);
            Assert.AreEqual(CombatMath.HeavyMaxKnockbackSpeed, full.z, 1e-4f);
            Assert.AreEqual(CombatMath.HeavyMaxLift, full.y, 1e-4f);
            Assert.Greater(CombatMath.HeavyAirTime(1f), CombatMath.HeavyAirTime(0f));
            Assert.AreEqual(1f, CombatMath.Charge01(5f, CombatConstants.HeavyMaxChargeTime), 1e-4f);
            Assert.AreEqual(0f, CombatMath.Charge01(-1f, CombatConstants.HeavyMaxChargeTime), 1e-4f);
        }

        [Test]
        public void PickSpawnAvoidsIndexAndStaysInRange()
        {
            for (int r = 0; r <= 20; r++)
            {
                int i = CombatMath.PickSpawn(4, r / 20f, 2);
                Assert.That(i, Is.InRange(0, 3));
                Assert.AreNotEqual(2, i);
            }
            Assert.AreEqual(0, CombatMath.PickSpawn(1, 0.9f, 0));
            Assert.AreEqual(-1, CombatMath.PickSpawn(0, 0.5f));
        }
    }
}
