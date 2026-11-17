using System;
using System.Collections.Generic;

namespace LightMMORPG.Core
{
    /// <summary>What one player did in one match (or training session), as the account system records it.</summary>
    public struct MatchResult
    {
        public int Kos;
        public int Deaths;
        public float Minutes;
        /// <summary>Most KOs in the match (ties count) and at least one KO.</summary>
        public bool Won;
        /// <summary>Training mode against the dummy: reduced, capped XP; no win bonus.</summary>
        public bool Training;
        /// <summary>Left before the match had run <see cref="Progression.MinMinutesForCompletion"/>.</summary>
        public bool LeftEarly => Minutes < Progression.MinMinutesForCompletion;
    }

    /// <summary>XP earned from one match, itemised for the post-match screen.</summary>
    public struct XpBreakdown
    {
        public int FromKos, FromTime, FromCompletion, FromWin;
        public int Total => FromKos + FromTime + FromCompletion + FromWin;
    }

    /// <summary>Result of adding XP: levels before/after and everything unlocked on the way.</summary>
    public struct LevelUpResult
    {
        public int OldLevel, NewLevel;
        public long OldXp, NewXp;
        public List<Unlock> Unlocks;
        public bool LeveledUp => NewLevel > OldLevel;
    }

    public enum UnlockKind { JawPreset, ChinPreset, NosePreset, EarPreset, Title }

    /// <summary>Something earned at a level. <see cref="Id"/> is the enum name (e.g. "Square") or the title text.</summary>
    public readonly struct Unlock
    {
        public readonly int Level;
        public readonly UnlockKind Kind;
        public readonly string Id;
        public readonly string DisplayName;

        public Unlock(int level, UnlockKind kind, string id, string displayName)
        {
            Level = level; Kind = kind; Id = id; DisplayName = displayName;
        }

        /// <summary>Stable key for saving which unlocks a player has, e.g. "JawPreset:Square".</summary>
        public string Key => Kind + ":" + Id;
    }

    /// <summary>
    /// Leveling rules (pure, engine-free): XP per match, the level curve and the unlock table. The account system
    /// stores total XP; level and unlocks are always derived from it, so changing these rules re-levels everyone
    /// consistently.
    /// </summary>
    public static class Progression
    {
        public const int MaxLevel = 50;

        public const int XpPerKo = 100;
        public const int XpPerMinute = 10;
        public const int MaxTimeMinutes = 15;
        public const int XpForCompletion = 50;
        public const float MinMinutesForCompletion = 2f;
        public const int XpForWin = 150;
        /// <summary>Training earns this fraction of normal XP, capped per session.</summary>
        public const float TrainingFactor = 0.4f;
        public const int TrainingCap = 150;

        public static XpBreakdown XpFor(MatchResult r)
        {
            var b = new XpBreakdown
            {
                FromKos = Math.Max(0, r.Kos) * XpPerKo,
                FromTime = (int)(Math.Min(Math.Max(0f, r.Minutes), MaxTimeMinutes) * XpPerMinute),
                FromCompletion = r.LeftEarly ? 0 : XpForCompletion,
                FromWin = r.Won && !r.Training && r.Kos > 0 && !r.LeftEarly ? XpForWin : 0,
            };
            if (!r.Training) return b;
            // scale each part so the breakdown still adds up to the capped total
            int full = b.Total, scaled = Math.Min(TrainingCap, (int)(full * TrainingFactor));
            if (full == 0) return b;
            float k = scaled / (float)full;
            b.FromKos = (int)(b.FromKos * k);
            b.FromTime = (int)(b.FromTime * k);
            b.FromCompletion = scaled - b.FromKos - b.FromTime;
            b.FromWin = 0;
            return b;
        }

        /// <summary>XP needed to go from <paramref name="level"/> to the next one (grows gently: 300, 350, 400, …).</summary>
        public static int XpToNext(int level) => level >= MaxLevel ? 0 : 250 + 50 * Math.Max(1, level);

        /// <summary>Total XP at which <paramref name="level"/> starts (level 1 = 0).</summary>
        public static long XpForLevel(int level)
        {
            level = Math.Max(1, Math.Min(MaxLevel, level));
            long total = 0;
            for (int l = 1; l < level; l++) total += XpToNext(l);
            return total;
        }

        public static int LevelFor(long totalXp)
        {
            int level = 1;
            long need = 0;
            while (level < MaxLevel)
            {
                need += XpToNext(level);
                if (totalXp < need) break;
                level++;
            }
            return level;
        }

        /// <summary>Progress inside the current level: XP gained since it started, and XP the level needs (0 at max).</summary>
        public static (long into, int needed) LevelProgress(long totalXp)
        {
            int level = LevelFor(totalXp);
            return (totalXp - XpForLevel(level), XpToNext(level));
        }

        public static LevelUpResult AddXp(long totalXp, int gained)
        {
            long before = Math.Max(0, totalXp), after = before + Math.Max(0, gained);
            int a = LevelFor(before), b = LevelFor(after);
            var unlocks = new List<Unlock>();
            foreach (var u in Unlocks)
                if (u.Level > a && u.Level <= b) unlocks.Add(u);
            return new LevelUpResult { OldLevel = a, NewLevel = b, OldXp = before, NewXp = after, Unlocks = unlocks };
        }

        /// <summary>Everything unlocked at or below <paramref name="level"/> (Classic and Head1 face parts are free).</summary>
        public static IEnumerable<Unlock> UnlockedAt(int level)
        {
            foreach (var u in Unlocks)
                if (u.Level <= level) yield return u;
        }

        public static bool IsUnlocked(UnlockKind kind, string id, int level)
        {
            bool listed = false;
            foreach (var u in Unlocks)
            {
                if (u.Kind != kind || u.Id != id) continue;
                listed = true;
                if (u.Level <= level) return true;
            }
            return !listed; // not in the table = free (Classic, Head1)
        }

        /// <summary>The unlock table, by level. Ids match the face-part enums in LightMMORPG.Characters.</summary>
        public static readonly Unlock[] Unlocks =
        {
            new(2, UnlockKind.Title, "Rookie", "Title: Rookie"),
            new(3, UnlockKind.NosePreset, "Button", "Nose: Button"),
            new(4, UnlockKind.EarPreset, "Small", "Ears: Small"),
            new(5, UnlockKind.JawPreset, "Square", "Jaw: Square"),
            new(6, UnlockKind.ChinPreset, "Strong", "Chin: Strong"),
            new(8, UnlockKind.NosePreset, "Long", "Nose: Long"),
            new(10, UnlockKind.Title, "Brawler", "Title: Brawler"),
            new(12, UnlockKind.JawPreset, "Pointed", "Jaw: Pointed"),
            new(14, UnlockKind.ChinPreset, "Small", "Chin: Small"),
            new(16, UnlockKind.EarPreset, "Pointed", "Ears: Pointed"),
            new(20, UnlockKind.Title, "Ace", "Title: Ace"),
            new(30, UnlockKind.Title, "Champion", "Title: Champion"),
            new(50, UnlockKind.Title, "Legend", "Title: Legend"),
        };
    }
}
