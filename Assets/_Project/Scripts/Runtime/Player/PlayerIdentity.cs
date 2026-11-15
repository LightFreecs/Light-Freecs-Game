using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using LightMMORPG.Config;
using UnityEngine;

namespace LightMMORPG.Player
{
    /// <summary>
    /// Per-player name + fighter palette (0 orange, 1 blue, 2 green, 3 purple). The server assigns the lowest free
    /// palette slot on spawn. <see cref="All"/> lists every spawned player on this machine (lock-on, HUD, scoreboard).
    /// </summary>
    public sealed class PlayerIdentity : NetworkBehaviour
    {
        public static readonly List<PlayerIdentity> All = new();
        public static event Action<PlayerIdentity> OnPlayerAdded;
        public static event Action<PlayerIdentity> OnPlayerRemoved;

        public readonly SyncVar<int> PaletteIndex = new(-1);
        public readonly SyncVar<string> DisplayName = new(string.Empty);

        [Tooltip("Renderer tinted with the palette material (the capsule body).")]
        [SerializeField] private Renderer _bodyRenderer;

        public PlayerMotor Motor { get; private set; }
        public int ClientId => Owner != null ? Owner.ClientId : -1;

        private static readonly string[] PaletteNames = { "Orange", "Blue", "Green", "Purple" };

        private void Awake()
        {
            Motor = GetComponent<PlayerMotor>();
            PaletteIndex.OnChange += (_, next, _) => ApplyPalette(next);
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

        public override void OnStartClient() => ApplyPalette(PaletteIndex.Value);

        private void ApplyPalette(int index)
        {
            if (index < 0 || _bodyRenderer == null) return;
            var mat = GameConfig.Load().GetFighterMaterial(index);
            if (mat != null) _bodyRenderer.sharedMaterial = mat;
        }
    }
}
