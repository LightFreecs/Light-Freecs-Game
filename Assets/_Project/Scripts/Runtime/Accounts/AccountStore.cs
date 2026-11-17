using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LightMMORPG.Core;
using UnityEngine;

namespace LightMMORPG.Accounts
{
    /// <summary>One account saved on this device. Passwords are never stored, only a salted PBKDF2-SHA256 hash.</summary>
    [Serializable]
    public sealed class AccountRecord
    {
        public string username;
        public string salt;
        public string hash;
        public int iterations;
        public string createdUtc;
        public long totalXp;
        public int matches, wins, kos, deaths, trainingSessions;
        public float minutesPlayed;

        public int Level => Progression.LevelFor(totalXp);

        /// <summary>The best title earned so far ("" before level 2).</summary>
        public string Title
        {
            get
            {
                string t = "";
                foreach (var u in Progression.UnlockedAt(Level))
                    if (u.Kind == UnlockKind.Title) t = u.Id;
                return t;
            }
        }
    }

    [Serializable]
    internal sealed class AccountFile
    {
        public int version = 1;
        public string lastSignedIn = "";
        public List<AccountRecord> accounts = new();
    }

    public enum AccountError { None, NameInvalid, NameTaken, PasswordTooShort, NoSuchAccount, WrongPassword }

    /// <summary>A finished match as shown on the results screen.</summary>
    public sealed class RecordedMatch
    {
        public MatchResult Result;
        public XpBreakdown Xp;
        /// <summary>Only meaningful when <see cref="Saved"/> (a signed-in account received the XP).</summary>
        public LevelUpResult Level;
        public bool Saved;
        public string Username;
    }

    /// <summary>
    /// Accounts on this device (JSON in <c>Application.persistentDataPath/accounts.json</c>): create, sign in / out,
    /// stay signed in across launches, and record matches (XP via <see cref="Progression"/>). Guests play normally but
    /// nothing is saved. Storage is behind two delegates so an online backend (or tests) can replace the file later.
    /// </summary>
    public sealed class AccountStore
    {
        public const int MinPassword = 6;
        private const int Iterations = 60000;
        private static readonly Regex NameRule = new("^[A-Za-z0-9_]{3,16}$");

        private static AccountStore _instance;
        public static AccountStore Instance => _instance ??= new AccountStore(
            () => File.Exists(FilePath) ? File.ReadAllText(FilePath) : null,
            json => { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)); File.WriteAllText(FilePath, json); });

        public static string FilePath => Path.Combine(Application.persistentDataPath, "accounts.json");

        /// <summary>The last match recorded this session, for the results screen. Cleared by <see cref="TakeLastMatch"/>.</summary>
        private RecordedMatch _lastMatch;

        private readonly Func<string> _load;
        private readonly Action<string> _save;
        private AccountFile _file;

        public event Action Changed;

        public AccountRecord Current { get; private set; }
        public bool SignedIn => Current != null;

        public AccountStore(Func<string> load, Action<string> save)
        {
            _load = load;
            _save = save;
            Load();
        }

        public static bool IsValidName(string name) => name != null && NameRule.IsMatch(name);

        public static string Describe(AccountError e) => e switch
        {
            AccountError.NameInvalid => "Names are 3–16 letters, numbers or _",
            AccountError.NameTaken => "That name is already used on this device",
            AccountError.PasswordTooShort => "Passwords need at least " + MinPassword + " characters",
            AccountError.NoSuchAccount => "No account with that name on this device",
            AccountError.WrongPassword => "Wrong password",
            _ => "",
        };

        public AccountError Create(string username, string password)
        {
            username = username?.Trim();
            if (!IsValidName(username)) return AccountError.NameInvalid;
            if (Find(username) != null) return AccountError.NameTaken;
            if (password == null || password.Length < MinPassword) return AccountError.PasswordTooShort;
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            var a = new AccountRecord
            {
                username = username,
                salt = Convert.ToBase64String(salt),
                iterations = Iterations,
                createdUtc = DateTime.UtcNow.ToString("o"),
            };
            a.hash = Hash(password, salt, a.iterations);
            _file.accounts.Add(a);
            SetCurrent(a);
            return AccountError.None;
        }

        public AccountError SignIn(string username, string password)
        {
            var a = Find(username?.Trim());
            if (a == null) return AccountError.NoSuchAccount;
            string h = Hash(password ?? "", Convert.FromBase64String(a.salt), a.iterations);
            if (!FixedTimeEquals(h, a.hash)) return AccountError.WrongPassword;
            SetCurrent(a);
            return AccountError.None;
        }

        public void SignOut() => SetCurrent(null);

        /// <summary>
        /// Adds a finished match: XP and stats go to the signed-in account (guests: XP is only shown). Training adds
        /// a training session instead of a match. Returns what the results screen shows.
        /// </summary>
        public RecordedMatch Record(MatchResult r)
        {
            var rec = new RecordedMatch { Result = r, Xp = Progression.XpFor(r) };
            var a = Current;
            if (a != null)
            {
                rec.Level = Progression.AddXp(a.totalXp, rec.Xp.Total);
                a.totalXp = rec.Level.NewXp;
                if (r.Training) a.trainingSessions++;
                else
                {
                    a.matches++;
                    if (rec.Xp.FromWin > 0) a.wins++;
                }
                a.kos += Math.Max(0, r.Kos);
                a.deaths += Math.Max(0, r.Deaths);
                a.minutesPlayed += Math.Max(0f, r.Minutes);
                rec.Saved = true;
                rec.Username = a.username;
                Save();
            }
            _lastMatch = rec;
            Changed?.Invoke();
            return rec;
        }

        public RecordedMatch TakeLastMatch()
        {
            var m = _lastMatch;
            _lastMatch = null;
            return m;
        }

        private AccountRecord Find(string username)
        {
            if (string.IsNullOrEmpty(username)) return null;
            foreach (var a in _file.accounts)
                if (string.Equals(a.username, username, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }

        private void SetCurrent(AccountRecord a)
        {
            Current = a;
            _file.lastSignedIn = a != null ? a.username : "";
            Save();
            Changed?.Invoke();
        }

        private void Load()
        {
            _file = null;
            try
            {
                string json = _load();
                if (!string.IsNullOrEmpty(json)) _file = JsonUtility.FromJson<AccountFile>(json);
            }
            catch (Exception e) { Debug.LogWarning("[Accounts] Could not read accounts: " + e.Message); }
            _file ??= new AccountFile();
            _file.accounts ??= new List<AccountRecord>();
            Current = Find(_file.lastSignedIn); // stay signed in
        }

        private void Save()
        {
            try { _save(JsonUtility.ToJson(_file, true)); }
            catch (Exception e) { Debug.LogWarning("[Accounts] Could not save accounts: " + e.Message); }
        }

        private static string Hash(string password, byte[] salt, int iterations)
        {
            using var kdf = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(password), salt, Math.Max(1000, iterations), HashAlgorithmName.SHA256);
            return Convert.ToBase64String(kdf.GetBytes(32));
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
