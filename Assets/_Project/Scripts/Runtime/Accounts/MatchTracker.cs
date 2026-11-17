using LightMMORPG.Combat;
using LightMMORPG.Core;
using UnityEngine;

namespace LightMMORPG.Accounts
{
    /// <summary>
    /// Times the local player's match and turns it into a <see cref="MatchResult"/> when they leave (or quit the game
    /// mid-match). <see cref="Networking.NetworkBootstrap"/> calls <see cref="Begin"/>; <see cref="Networking.MatchSession"/>
    /// calls <see cref="Finish"/> before stopping the network, while the players' stats still exist.
    /// </summary>
    public static class MatchTracker
    {
        /// <summary>Shorter visits (e.g. join and leave at once) are not recorded.</summary>
        public const float MinSecondsToRecord = 30f;

        private static float _startedAt = -1f;
        private static bool _training;

        public static bool Active => _startedAt >= 0f;

        public static void Begin(bool training)
        {
            _startedAt = Time.realtimeSinceStartup;
            _training = training;
        }

        /// <summary>Records the match for the signed-in account (or as guest) and returns it; null if nothing to record.</summary>
        public static RecordedMatch Finish()
        {
            if (!Active) return null;
            float seconds = Time.realtimeSinceStartup - _startedAt;
            _startedAt = -1f;
            var me = PlayerStats.Local;
            if (me == null) return null; // never spawned (e.g. connection failed)
            int kos = me.Kos.Value, deaths = me.Deaths.Value;
            if (seconds < MinSecondsToRecord && kos == 0) return null;
            int best = 0;
            foreach (var p in PlayerStats.All)
                if (p != null && p != me) best = Mathf.Max(best, p.Kos.Value);
            var result = new MatchResult
            {
                Kos = kos,
                Deaths = deaths,
                Minutes = seconds / 60f,
                Won = kos > 0 && kos >= best,
                Training = _training,
            };
            var rec = AccountStore.Instance.Record(result);
            Debug.Log($"[Accounts] Recorded {(_training ? "training" : "match")}: {kos} KOs, {deaths} downs, {result.Minutes:0.0} min, +{rec.Xp.Total} XP" +
                      (rec.Saved ? $" → {rec.Username} level {rec.Level.NewLevel}" : " (guest, not saved)"));
            return rec;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void HookQuit()
        {
            Application.quitting -= OnQuit;
            Application.quitting += OnQuit;
        }

        private static void OnQuit()
        {
            if (Active) Finish(); // quitting from inside a match still saves it
        }
    }
}
