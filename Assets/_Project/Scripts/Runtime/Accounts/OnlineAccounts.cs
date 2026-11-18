using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Core;
using UnityEngine;

namespace LightMMORPG.Accounts
{
    /// <summary>
    /// Online accounts: Unity Authentication (username + password) for sign-up / sign-in, Cloud Save player data
    /// (key <see cref="ProfileKey"/>, the <see cref="AccountRecord"/> as JSON) for progress. The profile lives in
    /// <see cref="AccountStore"/> as usual; every change the store makes is uploaded from here (latest wins, retried
    /// while offline). The session token is kept by Unity Authentication, so players stay signed in: at launch the
    /// cached profile shows at once and the session is resumed in the background, then the cloud copy is pulled.
    /// Needs the project linked to a Unity Cloud project with the Username &amp; Password provider turned on.
    /// </summary>
    public static class OnlineAccounts
    {
        public const string ProfileKey = "profile";
        private const float RetrySeconds = 30f;

        private static Task _init;
        private static bool _busy, _uploading;
        private static AccountRecord _queued;

        /// <summary>A sign-in or sign-up is running (the dialog shows "Working...").</summary>
        public static bool Busy => _busy;

        /// <summary>Signed in to Unity Authentication right now (the cached profile can show while this is still false).</summary>
        public static bool Connected => UnityServices.State == ServicesInitializationState.Initialized &&
                                        AuthenticationService.Instance.IsSignedIn;

        private static AccountStore Store => AccountStore.Instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode) return; // dedicated server: no player accounts
            Store.Upload = QueueUpload;
            if (Store.Current != null && Store.Current.online) _ = Resume();
        }

        // ------------------------------------------------------------------ sign up / sign in / out

        /// <summary>Creates an online account and signs in. A device account with the same name moves its progress in.</summary>
        public static async Task<AccountError> Create(string username, string password)
        {
            username = username?.Trim();
            var err = AccountStore.ValidateOnline(username, password);
            if (err != AccountError.None) return err;
            if (_busy) return AccountError.Busy;
            _busy = true;
            try
            {
                err = await Ready();
                if (err != AccountError.None) return err;
                var device = Store.DeviceAccountFor(username, password);
                EndSession();
                await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username, password);
                var profile = device != null ? device.Clone() : new AccountRecord { createdUtc = DateTime.UtcNow.ToString("o") };
                profile.username = username;
                Store.AttachOnline(profile, pending: true); // uploads it
                Store.RemoveDeviceAccount(device);
                if (device != null) Debug.Log($"[Accounts] Moved device account {username} online ({profile.totalXp} XP)");
                return AccountError.None;
            }
            catch (Exception e) { return Map(e, signingUp: true); }
            finally { _busy = false; }
        }

        /// <summary>Signs in to an online account and loads its progress from the cloud.</summary>
        public static async Task<AccountError> SignIn(string username, string password)
        {
            username = username?.Trim();
            if (!AccountStore.IsValidName(username)) return AccountError.NoSuchAccount;
            if (string.IsNullOrEmpty(password)) return AccountError.WrongPassword;
            if (_busy) return AccountError.Busy;
            _busy = true;
            try
            {
                var err = await Ready();
                if (err != AccountError.None) return err;
                EndSession();
                await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);
                var cloud = await LoadProfile();
                bool fresh = cloud == null; // signed up but the first upload never arrived
                var profile = cloud ?? Store.DeviceAccountFor(username, password)?.Clone() ?? new AccountRecord { createdUtc = DateTime.UtcNow.ToString("o") };
                if (!AccountStore.IsValidName(profile.username)) profile.username = username;
                Store.AttachOnline(profile, pending: fresh);
                return AccountError.None;
            }
            catch (Exception e)
            {
                EndSession();
                return Map(e, signingUp: false);
            }
            finally { _busy = false; }
        }

        /// <summary>Signs out of the online account (or a device account) and forgets the session on this device.</summary>
        public static void SignOut()
        {
            _queued = null;
            EndSession();
            Store.SignOut();
        }

        // ------------------------------------------------------------------ resume + sync

        private static async Task Resume()
        {
            var err = await Ready();
            if (err != AccountError.None) { Debug.LogWarning("[Accounts] Online accounts offline: " + AccountStore.Describe(err)); return; }
            var auth = AuthenticationService.Instance;
            try
            {
                if (!auth.IsSignedIn)
                {
                    if (!auth.SessionTokenExists) { SignedOutElsewhere(); return; }
                    await auth.SignInAnonymouslyAsync(); // with a cached session token this resumes that account
                }
                if (Store.Current == null || !Store.Current.online) return; // signed out meanwhile
                if (Store.OnlinePending) { QueueUpload(Store.OnlineSnapshot); return; } // this device is newer
                var cloud = await LoadProfile();
                if (cloud != null && Store.Current != null && Store.Current.online && !Store.OnlinePending)
                {
                    if (!AccountStore.IsValidName(cloud.username)) cloud.username = Store.Current.username;
                    Store.AttachOnline(cloud, pending: false); // progress made on another device shows up here
                }
            }
            catch (AuthenticationException e) when (e.ErrorCode == AuthenticationErrorCodes.InvalidSessionToken ||
                                                    e.ErrorCode == CommonErrorCodes.InvalidToken ||
                                                    e.ErrorCode == AuthenticationErrorCodes.BannedUser)
            {
                SignedOutElsewhere();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Accounts] Could not resume the online session: " + e.Message);
                if (Store.OnlinePending) RetryLater();
            }
        }

        private static void SignedOutElsewhere()
        {
            Debug.Log("[Accounts] Online session ended; signing out");
            EndSession();
            Store.SignOut();
        }

        private static void QueueUpload(AccountRecord snapshot)
        {
            if (snapshot == null) return;
            _queued = snapshot;
            if (!_uploading) _ = UploadLoop();
        }

        private static async Task UploadLoop()
        {
            _uploading = true;
            try
            {
                while (_queued != null)
                {
                    var sending = _queued;
                    _queued = null;
                    if (!Connected)
                    {
                        // not connected yet (resume still running, or offline): Resume / the retry sends it later
                        if (await Ready() != AccountError.None || !AuthenticationService.Instance.IsSignedIn)
                        {
                            _queued ??= sending;
                            RetryLater();
                            return;
                        }
                    }
                    if (!Same(sending)) continue; // signed out or another account by now
                    try
                    {
                        sending.salt = sending.hash = null;
                        var data = new Dictionary<string, object> { { ProfileKey, JsonUtility.ToJson(sending) } };
                        await CloudSaveService.Instance.Data.Player.SaveAsync(data);
                        Store.MarkUploaded(sending);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[Accounts] Upload failed, will retry: " + e.Message);
                        _queued ??= sending;
                        RetryLater();
                        return;
                    }
                }
            }
            finally { _uploading = false; }
        }

        private static bool _retryScheduled;

        private static async void RetryLater()
        {
            if (_retryScheduled) return;
            _retryScheduled = true;
            await Task.Delay(TimeSpan.FromSeconds(RetrySeconds));
            _retryScheduled = false;
            if (!Application.isPlaying || Store.Current == null || !Store.Current.online) return;
            if (!Connected) { _ = Resume(); return; }
            if (Store.OnlinePending) QueueUpload(_queued ?? Store.OnlineSnapshot);
        }

        private static bool Same(AccountRecord a) =>
            Store.Current != null && Store.Current.online &&
            string.Equals(Store.Current.username, a.username, StringComparison.OrdinalIgnoreCase);

        private static async Task<AccountRecord> LoadProfile()
        {
            var items = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { ProfileKey });
            if (items == null || !items.TryGetValue(ProfileKey, out var item)) return null;
            var json = item.Value.GetAsString();
            if (string.IsNullOrEmpty(json)) return null;
            var a = JsonUtility.FromJson<AccountRecord>(json);
            if (a == null) return null;
            a.online = true;
            return a;
        }

        // ------------------------------------------------------------------ plumbing

        private static void EndSession()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized) return;
            var auth = AuthenticationService.Instance;
            if (auth.IsSignedIn || auth.SessionTokenExists) auth.SignOut(clearCredentials: true);
        }

        /// <summary>Starts Unity Services once (retried after a failure, e.g. no network at launch).</summary>
        private static async Task<AccountError> Ready()
        {
            if (UnityServices.State == ServicesInitializationState.Initialized) return AccountError.None;
            if (string.IsNullOrEmpty(Application.cloudProjectId)) return AccountError.NotSetUp;
            try
            {
                if (_init == null || _init.IsFaulted || _init.IsCanceled) _init = UnityServices.InitializeAsync();
                await _init;
                return AccountError.None;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Accounts] Unity Services did not start: " + e.Message);
                return e is RequestFailedException r ? Map(r, false) : AccountError.Offline;
            }
        }

        private static AccountError Map(Exception e, bool signingUp)
        {
            if (e is not RequestFailedException r)
            {
                Debug.LogWarning("[Accounts] " + e);
                return AccountError.Failed;
            }
            int c = r.ErrorCode;
            Debug.Log($"[Accounts] {(signingUp ? "Sign-up" : "Sign-in")} refused ({c}): {r.Message}");
            if (c == CommonErrorCodes.TransportError || c == CommonErrorCodes.Timeout || c == CommonErrorCodes.ServiceUnavailable)
                return AccountError.Offline;
            if (c == CommonErrorCodes.ApiMissing || c == CommonErrorCodes.ProjectPolicyAccessDenied ||
                c == AuthenticationErrorCodes.EnvironmentMismatch || c == AuthenticationErrorCodes.InvalidProvider)
                return AccountError.NotSetUp;
            if (c == CommonErrorCodes.TooManyRequests) return AccountError.Busy;
            if (signingUp)
            {
                if (c == AuthenticationErrorCodes.AccountAlreadyLinked || c == CommonErrorCodes.Conflict) return AccountError.NameTaken;
                if (c == AuthenticationErrorCodes.InvalidParameters) return AccountError.PasswordWeak;
                return AccountError.Failed;
            }
            if (c == AuthenticationErrorCodes.InvalidParameters || c == CommonErrorCodes.InvalidToken ||
                c == CommonErrorCodes.Forbidden || c == CommonErrorCodes.NotFound || c == CommonErrorCodes.InvalidRequest)
                return AccountError.WrongPassword;
            return AccountError.Failed;
        }
    }
}
