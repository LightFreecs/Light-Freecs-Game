using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using LightMMORPG.Characters;
using LightMMORPG.Config;
using UnityEngine;

namespace LightMMORPG.Player
{
    /// <summary>
    /// Per-player name + fighter palette (0 orange, 1 blue, 2 green, 3 purple). The server assigns the lowest free
    /// palette slot on spawn and a "Player N (Colour)" name; a signed-in owner replaces it with its account name. <see cref="All"/> lists every spawned player on this machine (lock-on, HUD, scoreboard).
    /// The owner sends its saved face choices (<see cref="PlayerAppearance"/>, from the menu's Customize screen) to the
    /// server, which clamps them and syncs them to everyone in <see cref="Face"/>.
    /// </summary>
    public sealed class PlayerIdentity : NetworkBehaviour
    {
        public static readonly List<PlayerIdentity> All = new();
        public static event Action<PlayerIdentity> OnPlayerAdded;
        public static event Action<PlayerIdentity> OnPlayerRemoved;

        public readonly SyncVar<int> PaletteIndex = new(-1);
        public readonly SyncVar<string> DisplayName = new(string.Empty);
        /// <summary>The player's <see cref="FaceCustomization"/> as JSON (empty = preset face).</summary>
        public readonly SyncVar<string> Face = new(string.Empty);

        [Tooltip("Procedural character generator on Graphics/Model (built per palette on clients).")]
        [SerializeField] private ProceduralCharacter _character;

        public PlayerMotor Motor { get; private set; }
        public int ClientId => Owner != null ? Owner.ClientId : -1;

        private static readonly string[] PaletteNames = { "Orange", "Blue", "Green", "Purple" };

        private void Awake()
        {
            Motor = GetComponent<PlayerMotor>();
            PaletteIndex.OnChange += (_, next, _) => ApplyPalette(next);
            Face.OnChange += (_, _, _) => ApplyPalette(PaletteIndex.Value);
        }

        public override void OnStartNetwork()
        {
            if (!All.Contains(this)) All.Add(this);
            OnPlayerAdded?.Invoke(this);
        }

        public override void OnStopNetwork()
        {
            All.Remove(this);
            OnPlayerRemoved?.Invoke(this);
        }

        public override void OnStartServer()
        {
            int slot = 0;
            for (; slot < 64; slot++)
            {
                bool used = false;
                foreach (var p in All)
                    if (p != this && p.PaletteIndex.Value == slot) { used = true; break; }
                if (!used) break;
            }
            PaletteIndex.Value = slot;
            DisplayName.Value = "Player " + (slot + 1) + " (" + PaletteNames[slot % PaletteNames.Length] + ")";
        }

        public override void OnStartClient()
        {
            ApplyPalette(PaletteIndex.Value);
            if (!IsOwner) return;
            CmdSetFace(PlayerAppearance.ToJson(PlayerAppearance.Load()));
            // signed-in players show their account name instead of "Player N (Colour)"
            var account = Accounts.AccountStore.Instance.Current;
            if (account != null) ServerSetName(account.username);
        }

        [ServerRpc]
        private void ServerSetName(string username)
        {
            if (Accounts.AccountStore.IsValidName(username)) DisplayName.Value = username;
        }

        [ServerRpc]
        private void CmdSetFace(string json)
        {
            // re-serialise the clamped values so nothing unchecked from a client reaches the others
            Face.Value = PlayerAppearance.ToJson(PlayerAppearance.FromJson(json));
        }

        private string _builtKey;

        /// <summary>Starts building the hero meshes on worker threads as soon as the game starts (clients with graphics only).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void PrewarmCharacters()
        {
            if (Application.isBatchMode) return;
            // the local player's own face on every palette (the server picks the palette)
            var face = PlayerAppearance.Load();
            var list = new List<CharacterRecipe>();
            for (int i = 0; i < CharacterPresets.Count; i++) list.Add(PlayerAppearance.ForPalette(i, face));
            ProceduralCharacterBuilder.Prewarm(list);
        }

        private void ApplyPalette(int index)
        {
            if (index < 0) return;
            var recipe = CharacterPresets.ForPalette(index);
            // capsule follows the hero's height (deterministic from the synced palette → same on server and clients)
            var cc = GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.height = recipe.height;
                cc.center = new Vector3(0f, recipe.height * 0.5f, 0f);
            }
            // meshes only where something renders them (not on a dedicated server)
            if (_character == null || !IsClientStarted) return;
            string key = index + "|" + Face.Value;
            if (_builtKey == key) return;
            _builtKey = key;
            _character.GenerateForPalette(index, PlayerAppearance.FromJson(Face.Value));
        }
    }
}
