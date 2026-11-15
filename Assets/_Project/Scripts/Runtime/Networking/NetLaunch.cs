using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LightMMORPG.Networking
{
    public enum NetMode { None, Host, Client, Server }

    /// <summary>
    /// Hand-over between the main menu and the arena: the menu calls <see cref="Launch"/> (mode + address), the arena's
    /// <see cref="NetworkBootstrap"/> takes it with <see cref="TryConsume"/>. Command-line flags (<c>-host</c>,
    /// <c>-client [ip]</c>, <c>-server</c>, batch mode = server) skip the menu and go straight to the arena.
    /// </summary>
    public static class NetLaunch
    {
        public const string MainMenuScene = "MainMenu";
        public const string ArenaScene = "Arena_01";

        private static NetMode _pendingMode;
        private static string _pendingAddress;

        /// <summary>Error to show when the player is sent back to the menu (e.g. "Could not connect to …"). Read once.</summary>
        public static string LastError { get; set; }

        /// <summary>Last address typed in the menu (kept for the session; the menu also stores it in PlayerPrefs).</summary>
        public static string LastAddress { get; private set; } = NetworkBootstrap.DefaultAddress;

        public static void Launch(NetMode mode, string address = null)
        {
            _pendingMode = mode;
            _pendingAddress = string.IsNullOrWhiteSpace(address) ? NetworkBootstrap.DefaultAddress : address.Trim();
            if (mode == NetMode.Client) LastAddress = _pendingAddress;
            Debug.Log("[Net] Menu launch: " + mode + (mode == NetMode.Client ? " " + _pendingAddress : ""));
            SceneManager.LoadScene(ArenaScene);
        }

        public static bool TryConsume(out NetMode mode, out string address)
        {
            mode = _pendingMode;
            address = _pendingAddress;
            _pendingMode = NetMode.None;
            return mode != NetMode.None;
        }

        /// <summary>Mode requested on the command line (or Server in batch mode / UNITY_SERVER builds), else None.</summary>
        public static NetMode FromCommandLine(out string address)
        {
            string[] args = Environment.GetCommandLineArgs();
            address = NetworkBootstrap.DefaultAddress;
            if (Array.IndexOf(args, "-host") >= 0) return NetMode.Host;
            int c = Array.IndexOf(args, "-client");
            if (c >= 0)
            {
                if (c + 1 < args.Length && !args[c + 1].StartsWith("-")) address = args[c + 1];
                return NetMode.Client;
            }
            if (Array.IndexOf(args, "-server") >= 0) return NetMode.Server;
#if UNITY_SERVER
            return NetMode.Server;
#else
            bool menuTest = Array.IndexOf(args, "-menuCapture") >= 0 || Array.IndexOf(args, "-menuAutoplay") >= 0;
            return Application.isBatchMode && !menuTest ? NetMode.Server : NetMode.None;
#endif
        }

        /// <summary>True after <see cref="ReturnToMenu"/> so the menu skips its title screen (the menu clears it).</summary>
        public static bool ReturnedFromArena { get; set; }

        /// <summary>Back to the main menu (after leaving a match or a failed connection).</summary>
        public static void ReturnToMenu(string error = null)
        {
            if (!string.IsNullOrEmpty(error)) LastError = error;
            ReturnedFromArena = true;
            SceneManager.LoadScene(MainMenuScene);
        }
    }
}
