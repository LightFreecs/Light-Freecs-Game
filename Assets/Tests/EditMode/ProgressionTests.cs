using LightMMORPG.Core;
using NUnit.Framework;

namespace LightMMORPG.Tests
{
    public sealed class ProgressionTests
    {
        [Test]
        public void LevelCurve_StartsAtOneAndIsConsistent()
        {
            Assert.AreEqual(1, Progression.LevelFor(0));
            Assert.AreEqual(0, Progression.XpForLevel(1));
            Assert.AreEqual(300, Progression.XpToNext(1));
            Assert.AreEqual(1, Progression.LevelFor(299));
            Assert.AreEqual(2, Progression.LevelFor(300));
            for (int l = 1; l <= Progression.MaxLevel; l++)
            {
                long start = Progression.XpForLevel(l);
                Assert.AreEqual(l, Progression.LevelFor(start), "level " + l + " starts at its own XP");
                if (l > 1) Assert.AreEqual(l - 1, Progression.LevelFor(start - 1));
            }
            Assert.AreEqual(Progression.MaxLevel, Progression.LevelFor(long.MaxValue / 2));
            Assert.AreEqual(0, Progression.LevelProgress(long.MaxValue / 2).needed);
        }

        [Test]
        public void MatchXp_CountsKosTimeCompletionAndWin()
        {
            var b = Progression.XpFor(new MatchResult { Kos = 3, Deaths = 5, Minutes = 6f, Won = true });
            Assert.AreEqual(300, b.FromKos);
            Assert.AreEqual(60, b.FromTime);
            Assert.AreEqual(50, b.FromCompletion);
            Assert.AreEqual(150, b.FromWin);
            Assert.AreEqual(560, b.Total);
            // time is capped, deaths never cost XP
            Assert.AreEqual(Progression.MaxTimeMinutes * Progression.XpPerMinute, Progression.XpFor(new MatchResult { Minutes = 90f }).FromTime);
        }

        [Test]
        public void LeavingEarly_GetsNoCompletionOrWinBonus()
        {
            var b = Progression.XpFor(new MatchResult { Kos = 1, Minutes = 1f, Won = true });
            Assert.AreEqual(0, b.FromCompletion);
            Assert.AreEqual(0, b.FromWin);
            Assert.AreEqual(110, b.Total);
        }

        [Test]
        public void Training_IsReducedAndCapped()
        {
            var small = Progression.XpFor(new MatchResult { Kos = 1, Minutes = 3f, Training = true, Won = true });
            Assert.AreEqual((int)((100 + 30 + 50) * Progression.TrainingFactor), small.Total);
            Assert.AreEqual(0, small.FromWin);
            var big = Progression.XpFor(new MatchResult { Kos = 40, Minutes = 30f, Training = true });
            Assert.AreEqual(Progression.TrainingCap, big.Total);
        }

        [Test]
        public void AddXp_ReportsLevelUpsAndUnlocksOnTheWay()
        {
            var r = Progression.AddXp(0, (int)Progression.XpForLevel(5));
            Assert.AreEqual(1, r.OldLevel);
            Assert.AreEqual(5, r.NewLevel);
            Assert.IsTrue(r.LeveledUp);
            CollectionAssert.AreEquivalent(new[] { "Title:Rookie", "NosePreset:Button", "EarPreset:Small", "JawPreset:Square" },
                r.Unlocks.ConvertAll(u => u.Key));
            var none = Progression.AddXp(r.NewXp, 1);
            Assert.IsFalse(none.LeveledUp);
            Assert.IsEmpty(none.Unlocks);
        }

        [Test]
        public void Unlocks_FreePartsAlwaysAvailable_LockedUntilLevel()
        {
            Assert.IsTrue(Progression.IsUnlocked(UnlockKind.JawPreset, "Classic", 1));
            Assert.IsTrue(Progression.IsUnlocked(UnlockKind.JawPreset, "Head1", 1));
            Assert.IsFalse(Progression.IsUnlocked(UnlockKind.JawPreset, "Square", 4));
            Assert.IsTrue(Progression.IsUnlocked(UnlockKind.JawPreset, "Square", 5));
            int last = 0;
            foreach (var u in Progression.Unlocks)
            {
                Assert.GreaterOrEqual(u.Level, last, "table sorted by level");
                Assert.That(u.Level, Is.InRange(2, Progression.MaxLevel));
                last = u.Level;
            }
        }

        [Test]
        public void UnlockIds_MatchTheFacePartPresets()
        {
            foreach (var u in Progression.Unlocks)
            {
                System.Type t = u.Kind switch
                {
                    UnlockKind.JawPreset => typeof(Characters.JawPreset),
                    UnlockKind.ChinPreset => typeof(Characters.ChinPreset),
                    UnlockKind.NosePreset => typeof(Characters.NosePreset),
                    UnlockKind.EarPreset => typeof(Characters.EarPreset),
                    _ => null,
                };
                if (t != null) Assert.IsTrue(System.Enum.IsDefined(t, u.Id), u.Key + " is a real preset");
            }
        }
    }
}
