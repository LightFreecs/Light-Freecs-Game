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
        /// <summary>Stat points spent (file version 2+; older files load as all zero = everything unspent).</summary>
        public StatAllocation stats;
        /// <summary>
        /// True for an online account (Unity Authentication + Cloud Save): the record is the cloud profile, cached on
        /// this device, and has no password data. False for a device-only account from before online accounts.
        /// </summary>
        public bool online;

        public AccountRecord Clone() => (AccountRecord)MemberwiseClone();

        public int Level => Progression.LevelFor(totalXp);
        /// <summary>Stat points earned but not spent yet.</summary>
        public int UnspentStatPoints => FighterStats.Unspent(Level, stats);

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
        public int version = AccountStore.FileVersion;
        public string lastSignedIn = "";
        public List<AccountRecord> accounts = new();
        /// <summary>The signed-in online account's profile, cached so the menu works offline (empty username = none).</summary>
        public AccountRecord onlineCache;
        /// <summary>The cache holds progress the cloud does not have yet (upload failed or still running).</summary>
        public bool onlinePending;
    }

    public enum AccountError
    {
        None, NameInvalid, NameTaken, PasswordTooShort, NoSuchAccount, WrongPassword,
        // online only
        PasswordWeak, Offline, NotSetUp, Busy, Failed,
    }

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
    /// The signed-in account and its progress: record matches (XP via <see cref="Progression"/>), stat points, stay
    /// signed in across launches. Online accounts (<see cref="OnlineAccounts"/>: Unity Authentication + Cloud Save) are
    /// attached with <see cref="AttachOnline"/>, cached in <c>accounts.json</c>, and every change goes to
    /// <see cref="Upload"/>. Device-only accounts from before (same file) still sign in and can be moved online.
    /// Guests play normally but nothing is saved. Storage is behind two delegates so tests can replace the file.
    /// </summary>
    public sealed class AccountStore
    {
        /// <summary>1 = accounts + XP; 2 = adds the stat allocation per account.</summary>
        public const int FileVersion = 2;
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
            AccountError.NameTaken => "That name is already taken",
            AccountError.PasswordTooShort => "Passwords need at least " + MinPassword + " characters",
            AccountError.NoSuchAccount => "No account with that name",
            AccountError.WrongPassword => "Wrong name or password",
            AccountError.PasswordWeak => "Passwords need " + MinOnlinePassword + "–" + MaxOnlinePassword + " characters with upper and lower case letters, a number and a symbol",
            AccountError.Offline => "Can't reach the account server. Check your connection",
            AccountError.NotSetUp => "Online accounts aren't turned on yet (Unity Cloud: Username & Password sign-in)",
            AccountError.Busy => "Still working on the last request",
            AccountError.Failed => "Something went wrong. Try again",
            _ => "",
        };

        public const int MinOnlinePassword = 8, MaxOnlinePassword = 30;

        /// <summary>Unity Authentication's password rule: 8–30 characters, upper + lower case, a digit and a symbol.</summary>
        public static bool IsStrongPassword(string p)
        {
            if (p == null || p.Length < MinOnlinePassword || p.Length > MaxOnlinePassword) return false;
            bool up = false, low = false, digit = false, sym = false;
            foreach (char c in p)
            {
                if (char.IsUpper(c)) up = true;
                else if (char.IsLower(c)) low = true;
                else if (char.IsDigit(c)) digit = true;
                else if (!char.IsWhiteSpace(c)) sym = true;
            }
            return up && low && digit && sym;
        }

        /// <summary>Checks a new online account's name and password before anything is sent.</summary>
        public static AccountError ValidateOnline(string username, string password)
        {
            if (!IsValidName(username?.Trim())) return AccountError.NameInvalid;
            return IsStrongPassword(password) ? AccountError.None : AccountError.PasswordWeak;
        }

        // ------------------------------------------------------------------ online accounts
        // OnlineAccounts does the network part (Unity Authentication + Cloud Save); the store only holds the profile.

        /// <summary>Called with a copy of the online profile whenever it changes; the online layer uploads it.</summary>
        public Action<AccountRecord> Upload;

        public bool OnlinePending => _file.onlinePending;

        /// <summary>The online profile as it is now (a copy), or null when no online account is signed in.</summary>
        public AccountRecord OnlineSnapshot => Current != null && Current.online ? Current.Clone() : null;

        /// <summary>Signs in with an online profile (just loaded from the cloud, or just created).</summary>
        public void AttachOnline(AccountRecord profile, bool pending)
        {
            profile.online = true;
            profile.salt = profile.hash = null;
            profile.iterations = 0;
            profile.stats = FighterStats.Validated(profile.Level, profile.stats);
            _file.onlineCache = profile;
            _file.onlinePending = pending;
            _file.lastSignedIn = "";
            Current = profile;
            Save();
            Changed?.Invoke();
            if (pending) Upload?.Invoke(profile.Clone());
        }

        /// <summary>The cloud now has the profile as it was when <paramref name="sent"/> was taken.</summary>
        public void MarkUploaded(AccountRecord sent)
        {
            var a = Current;
            if (a == null || !a.online || !SameProgress(a, sent)) return; // changed again since: another upload follows
            if (!_file.onlinePending) return;
            _file.onlinePending = false;
            Save();
            Changed?.Invoke();
        }

        /// <summary>
        /// A device account to move into a new online account with the same name: the one signed in on this device
        /// before online accounts existed, or any whose device password matches. Null if there is none.
        /// </summary>
        public AccountRecord DeviceAccountFor(string username, string password)
        {
            var a = Find(username?.Trim());
            if (a == null) return null;
            if (string.Equals(_file.lastSignedIn, a.username, StringComparison.OrdinalIgnoreCase)) return a;
            return FixedTimeEquals(Hash(password ?? "", Convert.FromBase64String(a.salt), a.iterations), a.hash) ? a : null;
        }

        /// <summary>The device-only account signed in from before online accounts (offered "move it online"), or null.</summary>
        public AccountRecord DeviceSignedIn => Current != null && !Current.online ? Current : null;

        /// <summary>Removes a device account after its progress moved online.</summary>
        public void RemoveDeviceAccount(AccountRecord a)
        {
            if (a == null || !_file.accounts.Remove(a)) return;
            if (string.Equals(_file.lastSignedIn, a.username, StringComparison.OrdinalIgnoreCase)) _file.lastSignedIn = "";
            Save();
        }

        private static bool SameProgress(AccountRecord a, AccountRecord b) =>
            b != null && a.totalXp == b.totalXp && a.matches == b.matches && a.trainingSessions == b.trainingSessions &&
            a.stats == b.stats && a.kos == b.kos && a.deaths == b.deaths;

        /// <summary>Saves a change to the signed-in account: device accounts to the file, online ones to cache + cloud.</summary>
        private void Persist(AccountRecord a)
        {
            if (a.online)
            {
                _file.onlinePending = true;
                Save();
                Upload?.Invoke(a.Clone());
            }
            else Save();
        }

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

        /// <summary>Signs out. An online account's cached profile is dropped too (OnlineAccounts ends the session).</summary>
        public void SignOut()
        {
            _file.onlineCache = null;
            _file.onlinePending = false;
            SetCurrent(null);
        }

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
                Persist(a);
            }
            _lastMatch = rec;
            Changed?.Invoke();
            return rec;
        }

        /// <summary>
        /// XP from anything that is not a match (minigames, quests later): added to the signed-in account and saved.
        /// Guests get the would-be result back but nothing is stored (same rule as matches).
        /// </summary>
        public LevelUpResult GrantXp(int amount)
        {
            var a = Current;
            var res = Progression.AddXp(a != null ? a.totalXp : 0, amount);
            if (a == null) return res;
            a.totalXp = res.NewXp;
            Persist(a);
            Changed?.Invoke();
            return res;
        }

        /// <summary>The signed-in account's stat points (guests: none, base stats).</summary>
        public StatAllocation Stats => Current != null ? Current.stats : default;

        /// <summary>
        /// Saves a new stat allocation for the signed-in account. Refused (false, nothing changes) for guests and for
        /// an allocation that does not fit the account's level. <c>default</c> is a full respec.
        /// </summary>
        public bool SetStats(StatAllocation stats)
        {
            var a = Current;
            if (a == null || !FighterStats.IsValid(a.Level, stats)) return false;
            if (a.stats == stats) return true;
            a.stats = stats;
            Persist(a);
            Changed?.Invoke();
            return true;
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
            if (a != null) { _file.onlineCache = null; _file.onlinePending = false; } // a device account replaces online
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
            // older files have no stats (all zero); a hand-edited or out-of-date allocation is refunded, never kept
            foreach (var a in _file.accounts) a.stats = FighterStats.Validated(a.Level, a.stats);
            _file.version = FileVersion;
            if (_file.onlineCache != null && IsValidName(_file.onlineCache.username))
            {
                // stay signed in online: the cached profile shows at once, OnlineAccounts resumes the session
                _file.onlineCache.online = true;
                _file.onlineCache.stats = FighterStats.Validated(_file.onlineCache.Level, _file.onlineCache.stats);
                Current = _file.onlineCache;
            }
            else
            {
                _file.onlineCache = null;
                _file.onlinePending = false;
                Current = Find(_file.lastSignedIn); // stay signed in
            }
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
