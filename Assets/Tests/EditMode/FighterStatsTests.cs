using LightMMORPG.Accounts;
using LightMMORPG.Core;
using NUnit.Framework;

namespace LightMMORPG.Tests
{
    public sealed class FighterStatsTests
    {
        private string _disk;
        private AccountStore NewStore() => new(() => _disk, json => _disk = json);

        [SetUp]
        public void SetUp() => _disk = null;

        [Test]
        public void PointsPerLevel_OnePerLevelAfterTheFirst()
        {
            Assert.AreEqual(0, FighterStats.PointsFor(1));
            Assert.AreEqual(1, FighterStats.PointsFor(2));
            Assert.AreEqual(23, FighterStats.PointsFor(24));
            Assert.AreEqual(49, FighterStats.PointsFor(Progression.MaxLevel));
            Assert.AreEqual(49, FighterStats.PointsFor(999), "levels above the cap earn nothing extra");
            Assert.AreEqual(0, FighterStats.PointsFor(-5));
        }

        [Test]
        public void Level1_Unallocated_EqualsTodaysBalance()
        {
            var n = FighterStats.Compute(default);
            Assert.AreEqual(1f, n.DamageMultiplier);
            Assert.AreEqual(1f, n.DamageTakenMultiplier);
            Assert.AreEqual(1f, n.SpeedMultiplier);
            Assert.AreEqual(1f, n.EnergyRegenMultiplier);
            Assert.AreEqual(CombatConstants.MaxHealth, n.MaxHealth());
            Assert.AreEqual(CombatConstants.MaxEnergy, n.MaxEnergy());
            Assert.AreEqual(CombatConstants.MaxGuard, n.MaxGuard());
            Assert.AreEqual(CombatConstants.RunSpeed, CombatConstants.RunSpeed * n.SpeedMultiplier);
            Assert.AreEqual(CombatConstants.HeavyDamage, CombatConstants.HeavyDamage * n.DamageMultiplier * n.DamageTakenMultiplier);
            Assert.AreEqual(CombatConstants.EnergyRegenPerSecond, CombatConstants.EnergyRegenPerSecond * n.EnergyRegenMultiplier);
        }

        [Test]
        public void Formulas_AtTheCap_StayModest()
        {
            int c = FighterStats.MaxPerStat;
            var n = FighterStats.Compute(new StatAllocation(c, c, c, c, c));
            Assert.AreEqual(1.10f, n.DamageMultiplier, 1e-4f);
            Assert.AreEqual(1150f, n.MaxHealth());
            Assert.AreEqual(1.05f, n.SpeedMultiplier, 1e-4f);
            Assert.AreEqual(120f, n.MaxEnergy());
            Assert.AreEqual(1.20f, n.EnergyRegenMultiplier, 1e-4f);
            Assert.AreEqual(0.92f, n.DamageTakenMultiplier, 1e-4f);
            Assert.AreEqual(120f, n.MaxGuard());
        }

        [Test]
        public void Bonus_AddsOnTop_UpToTheEffectiveCap()
        {
            var spent = new StatAllocation(20, 0, 0, 0, 0);
            var some = FighterStats.Compute(spent, new StatAllocation(5, 2, 0, 0, 0));
            Assert.AreEqual(1f + 25 * FighterStats.DamagePerPower, some.DamageMultiplier, 1e-4f);
            Assert.AreEqual(1015f, some.MaxHealth());
            var huge = FighterStats.Compute(spent, new StatAllocation(500, 0, 0, 0, -3));
            Assert.AreEqual(1f + FighterStats.MaxEffectivePerStat * FighterStats.DamagePerPower, huge.DamageMultiplier, 1e-4f);
            Assert.AreEqual(1f, huge.DamageTakenMultiplier, "negative totals count as zero");
        }

        [Test]
        public void Spending_RespectsPointsAndCap_AndRespecReturnsEverything()
        {
            var a = new StatAllocation();
            Assert.IsFalse(FighterStats.TryAdd(1, ref a, StatId.Power), "level 1 has no points");
            Assert.IsTrue(FighterStats.TryAdd(3, ref a, StatId.Power));
            Assert.IsTrue(FighterStats.TryAdd(3, ref a, StatId.Ki));
            Assert.IsFalse(FighterStats.TryAdd(3, ref a, StatId.Speed), "level 3 has 2 points");
            Assert.AreEqual(new StatAllocation(1, 0, 0, 1, 0), a);
            Assert.AreEqual(0, FighterStats.Unspent(3, a));

            Assert.IsTrue(FighterStats.TryRemove(ref a, StatId.Ki));
            Assert.IsFalse(FighterStats.TryRemove(ref a, StatId.Ki), "nothing left in Ki");
            Assert.AreEqual(1, FighterStats.Unspent(3, a));

            var b = new StatAllocation();
            for (int i = 0; i < 30; i++) FighterStats.TryAdd(50, ref b, StatId.Vitality);
            Assert.AreEqual(FighterStats.MaxPerStat, b.vitality, "per-stat cap");
            Assert.AreEqual(49 - FighterStats.MaxPerStat, FighterStats.Unspent(50, b));

            b = FighterStats.Respec();
            Assert.AreEqual(0, b.Total);
            Assert.AreEqual(49, FighterStats.Unspent(50, b));
        }

        [Test]
        public void Validation_RejectsOverSpentOrOutOfRange()
        {
            Assert.IsTrue(FighterStats.IsValid(10, new StatAllocation(3, 3, 3, 0, 0)));
            Assert.IsFalse(FighterStats.IsValid(10, new StatAllocation(4, 3, 3, 0, 0)), "10 points at level 10 (has 9)");
            Assert.IsFalse(FighterStats.IsValid(50, new StatAllocation(21, 0, 0, 0, 0)), "over the per-stat cap");
            Assert.IsFalse(FighterStats.IsValid(50, new StatAllocation(-1, 2, 0, 0, 0)), "negative");
            Assert.IsFalse(FighterStats.IsValid(1, new StatAllocation(0, 0, 0, 0, 1)));

            // what the server does with a claimed build: valid stays, anything else becomes the base stats
            var ok = new StatAllocation(5, 4, 0, 0, 0);
            Assert.AreEqual(ok, FighterStats.Validated(10, ok));
            Assert.AreEqual(default(StatAllocation), FighterStats.Validated(10, new StatAllocation(20, 20, 9, 0, 0)));
            Assert.AreEqual(default(StatAllocation), FighterStats.Validated(1, new StatAllocation(1, 0, 0, 0, 0)));
        }

        [Test]
        public void Pack_RoundTrips_AndGarbageFailsValidation()
        {
            var a = new StatAllocation(20, 0, 7, 13, 9);
            Assert.AreEqual(a, StatAllocation.Unpack(a.Pack()));
            Assert.AreEqual(0, default(StatAllocation).Pack());
            // every bit set = 63 in each stat: unpacks, but can never be a valid build
            var junk = StatAllocation.Unpack(-1);
            Assert.AreEqual(63, junk.power);
            Assert.IsFalse(FighterStats.IsValid(Progression.MaxLevel, junk));
        }

        [Test]
        public void Pools_KeepTheirFill_WhenTheMaximumChanges()
        {
            var hp = new HealthPool();
            hp.ApplyDamage(500f);
            hp.SetMax(1150f);
            Assert.AreEqual(1150f, hp.Max);
            Assert.AreEqual(575f, hp.Current, 1e-3f);
            hp.Reset();
            Assert.AreEqual(1150f, hp.Current);

            var en = new EnergyPool();
            en.Configure(120f, 4.8f, 36f);
            Assert.AreEqual(120f, en.Current);
            en.TrySpend(120f);
            en.Tick(1f, false);
            Assert.AreEqual(4.8f, en.Current, 1e-4f);

            var guard = new GuardMeter();
            guard.SetMax(120f);
            Assert.AreEqual(120f, guard.Current);
        }

        [Test]
        public void Account_SavesStats_AndReloadsThem()
        {
            var s = NewStore();
            s.Create("Kaien", "secret1");
            Assert.IsFalse(s.SetStats(new StatAllocation(1, 0, 0, 0, 0)), "level 1 has no points");
            s.GrantXp((int)Progression.XpForLevel(6)); // minigame-style XP → level 6, 5 points
            Assert.AreEqual(6, s.Current.Level);
            Assert.AreEqual(5, s.Current.UnspentStatPoints);
            Assert.IsFalse(s.SetStats(new StatAllocation(3, 3, 0, 0, 0)), "6 points, has 5");
            Assert.IsTrue(s.SetStats(new StatAllocation(2, 1, 0, 1, 0)));
            Assert.AreEqual(1, s.Current.UnspentStatPoints);

            var again = NewStore(); // reloaded from "disk"
            Assert.AreEqual(new StatAllocation(2, 1, 0, 1, 0), again.Current.stats);
            Assert.AreEqual(new StatAllocation(2, 1, 0, 1, 0), again.Stats);
            StringAssert.Contains("\"version\": 2", _disk);

            Assert.IsTrue(again.SetStats(FighterStats.Respec()));
            Assert.AreEqual(5, NewStore().Current.UnspentStatPoints);
        }

        [Test]
        public void Guest_HasBaseStats_AndCannotSave()
        {
            var s = NewStore();
            Assert.AreEqual(default(StatAllocation), s.Stats);
            Assert.IsFalse(s.SetStats(default));
            Assert.AreEqual(2, s.GrantXp(400).NewLevel, "guests see the result");
            Assert.IsNull(_disk, "but nothing is written");
        }

        [Test]
        public void OlderFile_WithoutStats_LoadsCleanly_AndBadStatsAreRefunded()
        {
            // a version 1 file: no "stats" field at all
            var v1 = NewStore();
            v1.Create("Old_One", "secret1");
            v1.GrantXp(5000);
            string json = _disk.Replace("\"version\": 2", "\"version\": 1");
            json = System.Text.RegularExpressions.Regex.Replace(json, ",\\s*\"stats\"\\s*:\\s*\\{[^}]*\\}", "");
            StringAssert.DoesNotContain("stats", json);
            StringAssert.Contains("\"version\": 1", json);
            _disk = json;
            var s = NewStore();
            Assert.IsTrue(s.SignedIn);
            Assert.AreEqual(5000, s.Current.totalXp);
            Assert.AreEqual(default(StatAllocation), s.Current.stats);
            Assert.Greater(s.Current.UnspentStatPoints, 0);
            Assert.IsTrue(s.SetStats(new StatAllocation(1, 0, 0, 0, 0)));
            StringAssert.Contains("\"version\": 2", _disk);

            // a hand-edited file claiming more points than the level has: refunded on load
            _disk = _disk.Replace("\"power\": 1", "\"power\": 20").Replace("\"vitality\": 0", "\"vitality\": 20");
            var edited = NewStore();
            Assert.AreEqual(default(StatAllocation), edited.Current.stats);
        }
    }
}
