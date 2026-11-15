using LightMMORPG.Core;
using NUnit.Framework;

namespace LightMMORPG.Tests
{
    public class ComboChainTests
    {
        [Test]
        public void FullChainWithinWindow_GivesSpecDamageAndKnockbackOnLast()
        {
            var combo = new ComboChain();
            float[] expected = { 30f, 30f, 40f, 60f };
            for (int i = 0; i < 4; i++)
            {
                var hit = combo.RegisterHit(i * 0.5);
                Assert.AreEqual(i, hit.Index);
                Assert.AreEqual(expected[i], hit.Damage, 1e-4f);
                Assert.AreEqual(i == 3, hit.Knockback);
            }
        }

        [Test]
        public void HitAfterWindow_ResetsToFirst()
        {
            var combo = new ComboChain();
            combo.RegisterHit(0.0);
            combo.RegisterHit(0.5);
            var hit = combo.RegisterHit(0.5 + CombatConstants.ComboWindow + 0.01);
            Assert.AreEqual(0, hit.Index);
        }

        [Test]
        public void HitExactlyAtWindowEdge_Continues()
        {
            var combo = new ComboChain();
            combo.RegisterHit(1.0);
            Assert.AreEqual(1, combo.RegisterHit(1.0 + CombatConstants.ComboWindow).Index);
        }

        [Test]
        public void AfterFinisher_ChainRestarts()
        {
            var combo = new ComboChain();
            for (int i = 0; i < 4; i++) combo.RegisterHit(i * 0.1);
            Assert.AreEqual(0, combo.RegisterHit(0.45).Index);
        }

        [Test]
        public void Reset_ClearsChain()
        {
            var combo = new ComboChain();
            combo.RegisterHit(0);
            combo.RegisterHit(0.1);
            combo.Reset();
            Assert.IsFalse(combo.IsWindowOpen(0.2));
            Assert.AreEqual(0, combo.RegisterHit(0.2).Index);
        }
    }

    public class DamageCalculatorTests
    {
        [Test]
        public void Unblocked_FullDamage()
        {
            var r = DamageCalculator.Calculate(100f, false, false);
            Assert.AreEqual(100f, r.HealthDamage, 1e-4f);
            Assert.AreEqual(0f, r.GuardDamage, 1e-4f);
        }

        [Test]
        public void Blocked_Reduces80Percent_GuardDrainsHalfOfBlocked()
        {
            var r = DamageCalculator.Calculate(100f, false, true);
            Assert.AreEqual(20f, r.HealthDamage, 1e-4f);
            Assert.AreEqual(80f, r.BlockedDamage, 1e-4f);
            Assert.AreEqual(40f, r.GuardDamage, 1e-4f);
        }

        [Test]
        public void Surge_Adds35Percent()
        {
            Assert.AreEqual(135f, DamageCalculator.Calculate(100f, true, false).HealthDamage, 1e-3f);
            Assert.AreEqual(27f, DamageCalculator.Calculate(100f, true, true).HealthDamage, 1e-3f);
        }

        [Test]
        public void BeamDamage_ScalesWithChargeAndClamps()
        {
            Assert.AreEqual(150f, DamageCalculator.BeamDamage(0f), 1e-3f);
            Assert.AreEqual(275f, DamageCalculator.BeamDamage(0.75f), 1e-3f);
            Assert.AreEqual(400f, DamageCalculator.BeamDamage(1.5f), 1e-3f);
            Assert.AreEqual(400f, DamageCalculator.BeamDamage(5f), 1e-3f);
        }
    }

    public class GuardMeterTests
    {
        [Test]
        public void DrainToZero_BreaksAndStuns()
        {
            var g = new GuardMeter();
            Assert.IsFalse(g.Drain(60f));
            Assert.AreEqual(40f, g.Current, 1e-4f);
            Assert.IsTrue(g.Drain(60f));
            Assert.IsTrue(g.IsBroken);
            Assert.AreEqual(CombatConstants.GuardBreakStun, g.BreakStunRemaining, 1e-4f);
        }

        [Test]
        public void BlockedHeavyHitsBreakGuard()
        {
            // Heavy 110 blocked -> 88 absorbed -> 44 guard drain. Three blocked heavies break a full guard.
            var g = new GuardMeter();
            float drain = DamageCalculator.Calculate(CombatConstants.HeavyDamage, false, true).GuardDamage;
            Assert.IsFalse(g.Drain(drain));
            Assert.IsFalse(g.Drain(drain));
            Assert.IsTrue(g.Drain(drain));
        }

        [Test]
        public void AfterStun_RefillsToFull()
        {
            var g = new GuardMeter();
            g.Drain(200f);
            g.Tick(0.5f, true);
            Assert.IsTrue(g.IsBroken);
            g.Tick(0.6f, true);
            Assert.IsFalse(g.IsBroken);
            Assert.AreEqual(g.Max, g.Current, 1e-4f);
        }

        [Test]
        public void RecoversOnlyWhenNotBlocking()
        {
            var g = new GuardMeter();
            g.Drain(50f);
            g.Tick(1f, true);
            Assert.AreEqual(50f, g.Current, 1e-4f);
            g.Tick(1f, false);
            Assert.AreEqual(50f + CombatConstants.GuardRecoverPerSecond, g.Current, 1e-4f);
        }

        [Test]
        public void DrainIgnoredWhileBroken()
        {
            var g = new GuardMeter();
            g.Drain(100f);
            Assert.IsFalse(g.Drain(10f));
        }
    }

    public class EnergyPoolTests
    {
        [Test]
        public void StartsFull_SpendAndRefuseOverspend()
        {
            var e = new EnergyPool();
            Assert.AreEqual(100f, e.Current, 1e-4f);
            Assert.IsTrue(e.TrySpend(35f));
            Assert.AreEqual(65f, e.Current, 1e-4f);
            Assert.IsTrue(e.TrySpend(50f));
            Assert.IsFalse(e.TrySpend(50f));
            Assert.AreEqual(15f, e.Current, 1e-4f);
        }

        [Test]
        public void Regen4PerSecond_Charge30PerSecond_ClampedToMax()
        {
            var e = new EnergyPool();
            e.Set(0f);
            e.Tick(1f, false);
            Assert.AreEqual(4f, e.Current, 1e-4f);
            e.Tick(1f, true);
            Assert.AreEqual(34f, e.Current, 1e-4f);
            e.Tick(10f, true);
            Assert.AreEqual(100f, e.Current, 1e-4f);
        }

        [Test]
        public void RegenPaused_NoGain()
        {
            var e = new EnergyPool();
            e.Set(20f);
            e.RegenPaused = true;
            e.Tick(2f, false);
            e.Tick(2f, true);
            Assert.AreEqual(20f, e.Current, 1e-4f);
            e.RegenPaused = false;
            e.Tick(1f, false);
            Assert.AreEqual(24f, e.Current, 1e-4f);
        }

        [Test]
        public void TickDeltaAccumulation_MatchesOneSecond()
        {
            var e = new EnergyPool();
            e.Set(0f);
            for (int i = 0; i < 60; i++) e.Tick(1f / 60f, false);
            Assert.AreEqual(4f, e.Current, 1e-3f);
        }
    }

    public class CooldownTests
    {
        [Test]
        public void TickStyle_BlocksUntilElapsed()
        {
            var c = new Cooldown(0.6f);
            Assert.IsTrue(c.TryUse());
            Assert.IsFalse(c.TryUse());
            c.Tick(0.3f);
            Assert.IsFalse(c.IsReady);
            Assert.AreEqual(0.5f, c.Progress01, 1e-4f);
            c.Tick(0.3f);
            Assert.IsTrue(c.IsReady);
            Assert.IsTrue(c.TryUse());
        }

        [Test]
        public void TimeStyle_BlocksUntilReadyTime()
        {
            var c = new Cooldown(4f);
            Assert.IsTrue(c.TryUse(10.0));
            Assert.IsFalse(c.TryUse(13.9));
            Assert.AreEqual(0.1f, c.RemainingAt(13.9), 1e-4f);
            Assert.IsTrue(c.TryUse(14.0));
        }

        [Test]
        public void Reset_MakesReady()
        {
            var c = new Cooldown(20f);
            c.TryUse();
            c.TryUse(0.0);
            c.Reset();
            Assert.IsTrue(c.IsReady);
            Assert.IsTrue(c.IsReadyAt(0.0));
        }
    }

    public class HealthPoolTests
    {
        [Test]
        public void DamageReducesAndKOsAtZero()
        {
            var h = new HealthPool();
            Assert.IsFalse(h.ApplyDamage(400f));
            Assert.AreEqual(600f, h.Current, 1e-4f);
            Assert.IsTrue(h.ApplyDamage(700f));
            Assert.AreEqual(0f, h.Current, 1e-4f);
            Assert.IsTrue(h.IsKnockedOut);
            Assert.IsFalse(h.ApplyDamage(10f), "no second KO while down");
        }

        [Test]
        public void DownTimer_SignalsRespawnAfter5s_ResetRestores()
        {
            var h = new HealthPool();
            h.ApplyDamage(5000f);
            Assert.IsFalse(h.Tick(4.9f));
            Assert.IsTrue(h.Tick(0.2f));
            Assert.IsFalse(h.Tick(0.1f), "only fires once");
            h.Reset();
            Assert.IsFalse(h.IsKnockedOut);
            Assert.AreEqual(1000f, h.Current, 1e-4f);
        }

        [Test]
        public void HealClampsAndIsIgnoredWhileKO()
        {
            var h = new HealthPool();
            h.ApplyDamage(100f);
            h.Heal(500f);
            Assert.AreEqual(1000f, h.Current, 1e-4f);
            h.ApplyDamage(2000f);
            h.Heal(100f);
            Assert.AreEqual(0f, h.Current, 1e-4f);
        }
    }
}
