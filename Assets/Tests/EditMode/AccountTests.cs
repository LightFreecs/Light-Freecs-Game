using LightMMORPG.Accounts;
using LightMMORPG.Core;
using NUnit.Framework;

namespace LightMMORPG.Tests
{
    public sealed class AccountTests
    {
        private string _disk;
        private AccountStore NewStore() => new(() => _disk, json => _disk = json);

        [SetUp]
        public void SetUp() => _disk = null;

        [Test]
        public void Create_ValidatesNameAndPassword()
        {
            var s = NewStore();
            Assert.AreEqual(AccountError.NameInvalid, s.Create("ab", "secret1"));
            Assert.AreEqual(AccountError.NameInvalid, s.Create("bad name", "secret1"));
            Assert.AreEqual(AccountError.NameInvalid, s.Create("this_name_is_too_long", "secret1"));
            Assert.AreEqual(AccountError.PasswordTooShort, s.Create("Sora_7", "12345"));
            Assert.IsFalse(s.SignedIn);
            Assert.AreEqual(AccountError.None, s.Create("Sora_7", "secret1"));
            Assert.IsTrue(s.SignedIn);
            Assert.AreEqual(AccountError.NameTaken, s.Create("sora_7", "another1"), "names are case-insensitive");
        }

        [Test]
        public void Password_IsNeverStored_AndChecked()
        {
            var s = NewStore();
            s.Create("Sora_7", "hunter22");
            StringAssert.DoesNotContain("hunter22", _disk);
            s.SignOut();
            Assert.AreEqual(AccountError.WrongPassword, s.SignIn("Sora_7", "hunter23"));
            Assert.AreEqual(AccountError.NoSuchAccount, s.SignIn("Nobody", "hunter22"));
            Assert.IsFalse(s.SignedIn);
            Assert.AreEqual(AccountError.None, s.SignIn("SORA_7", "hunter22"));
            Assert.AreEqual("Sora_7", s.Current.username);
        }

        [Test]
        public void StaysSignedIn_AcrossRestarts_UntilSignOut()
        {
            NewStore().Create("Kaien", "secret1");
            var again = NewStore();
            Assert.IsTrue(again.SignedIn);
            Assert.AreEqual("Kaien", again.Current.username);
            again.SignOut();
            Assert.IsFalse(NewStore().SignedIn);
        }

        [Test]
        public void Record_SavesXpAndStats_ForSignedInAccount()
        {
            var s = NewStore();
            s.Create("Kaien", "secret1");
            var m = s.Record(new MatchResult { Kos = 3, Deaths = 2, Minutes = 6f, Won = true });
            Assert.IsTrue(m.Saved);
            Assert.AreEqual(560, m.Xp.Total);
            Assert.AreEqual(2, m.Level.NewLevel);
            s.Record(new MatchResult { Kos = 1, Minutes = 4f, Training = true });
            var a = NewStore().Current; // reloaded from "disk"
            Assert.AreEqual(1, a.matches);
            Assert.AreEqual(1, a.wins);
            Assert.AreEqual(1, a.trainingSessions);
            Assert.AreEqual(4, a.kos);
            Assert.AreEqual(2, a.deaths);
            Assert.AreEqual(560 + (int)((100 + 40 + 50) * Progression.TrainingFactor), a.totalXp);
            Assert.AreEqual("Rookie", a.Title);
        }

        [Test]
        public void Guest_SeesXpButNothingIsSaved()
        {
            var s = NewStore();
            var m = s.Record(new MatchResult { Kos = 2, Minutes = 5f });
            Assert.IsFalse(m.Saved);
            Assert.Greater(m.Xp.Total, 0);
            Assert.AreSame(m, s.TakeLastMatch());
            Assert.IsNull(s.TakeLastMatch(), "results are shown once");
            Assert.IsFalse(NewStore().SignedIn);
        }

        [Test]
        public void CorruptFile_StartsEmpty()
        {
            _disk = "{ not json";
            var s = NewStore();
            Assert.IsFalse(s.SignedIn);
            Assert.AreEqual(AccountError.None, s.Create("Fresh_1", "secret1"));
        }
    }
}
