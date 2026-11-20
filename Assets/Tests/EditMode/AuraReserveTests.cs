using LightMMORPG.Core;
using NUnit.Framework;

namespace LightMMORPG.Tests
{
    public class AuraReserveTests
    {
        [Test]
        public void Battle_StartsAtTenPercentWithFullReserve()
        {
            var a = new AuraReserve(1000f);
            Assert.AreEqual(CombatConstants.StartPower, a.Power, 1e-5f);
            Assert.AreEqual(1000f, a.Current, 1e-3f);
            Assert.AreEqual(0.1f, a.StrengthMultiplier, 1e-5f);
            Assert.AreEqual(1f, a.DamageTakenMultiplier, 1e-5f);
        }

        [Test]
        public void StartLevel_CostsNoUpkeep()
        {
            var a = new AuraReserve(1000f);
            for (int i = 0; i < 600; i++) a.Tick(0.1f, false, CombatConstants.PowerChargePerSecond);
            Assert.AreEqual(1000f, a.Current, 1e-3f);
        }

        [Test]
        public void Charging_RaisesPowerToFull_AndHighPowerBurnsAura()
        {
            var a = new AuraReserve(1000f);
            for (int i = 0; i < 40; i++) a.Tick(0.1f, true, CombatConstants.PowerChargePerSecond);
            Assert.AreEqual(1f, a.Power, 1e-5f);
            float before = a.Current;
            a.Tick(1f, false, 0f);
            Assert.AreEqual(CombatConstants.AuraUpkeepAtFullPower, before - a.Current, 1e-3f);
            Assert.Less(AuraReserve.Upkeep(0.55f), AuraReserve.Upkeep(1f) * 0.5f, "half power costs well under half of full");
        }

        [Test]
        public void Spending_NeverRefills_AndFailsWhenShort()
        {
            var a = new AuraReserve(100f);
            Assert.IsTrue(a.TrySpend(60f));
            Assert.IsFalse(a.TrySpend(60f));
            Assert.AreEqual(40f, a.Current, 1e-3f);
            for (int i = 0; i < 100; i++) a.Tick(0.1f, true, CombatConstants.PowerChargePerSecond);
            Assert.LessOrEqual(a.Current, 40f);
        }

        [Test]
        public void Depleted_StopsCharging_AndExhaustionSinksStrengthAndDefenseGradually()
        {
            var a = new AuraReserve(100f);
            a.Set(0f, 0.5f, 0f);
            a.Tick(1f, true, 1f);
            Assert.AreEqual(0.5f, a.Power, 1e-5f, "no charging without aura");
            float s1 = a.StrengthMultiplier;
            Assert.Less(s1, 0.5f);
            Assert.Greater(s1, 0.49f, "a little at a time");
            for (int i = 0; i < 400; i++) a.Tick(0.1f, false, 0f);
            Assert.AreEqual(1f, a.Exhaustion, 1e-5f);
            Assert.AreEqual(0.5f * (1f - CombatConstants.ExhaustStrengthLoss), a.StrengthMultiplier, 1e-5f);
            Assert.AreEqual(1f + CombatConstants.ExhaustDefenseLoss, a.DamageTakenMultiplier, 1e-5f);
        }

        [Test]
        public void Reset_IsANewBattle()
        {
            var a = new AuraReserve(100f);
            a.Set(0f, 1f, 1f);
            a.Reset();
            Assert.AreEqual(100f, a.Current, 1e-3f);
            Assert.AreEqual(CombatConstants.StartPower, a.Power, 1e-5f);
            Assert.AreEqual(0f, a.Exhaustion, 1e-5f);
        }

        [Test]
        public void Ki_GrowsTheReserve()
        {
            var n = FighterStats.Compute(new StatAllocation(0, 0, 0, 20, 0));
            Assert.AreEqual(CombatConstants.AuraReserve * 1.3f, n.MaxAura(), 0.5f);
            Assert.AreEqual(CombatConstants.AuraReserve, FighterNumbers.Base.MaxAura(), 1e-3f);
        }
    }
}
