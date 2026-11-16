using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightMMORPG.Characters
{
    /// <summary>
    /// Which head a fighter wears. <see cref="Procedural"/> = the SDF head with painted face and hair; anything else is a
    /// hand-made model loaded from <c>Resources/Heads/&lt;name&gt;.json</c> (made from a Blockbench FBX with
    /// <c>tools/blockbench_head.py</c>), rigid on the Head bone and blended into the Neck bone, without procedural hair.
    /// </summary>
    public enum HeadPreset { Procedural, Head1 }

    /// <summary>Mesh data of a head preset: head-bone space at headSize 1, Unity axes (+Y up, +Z forward).</summary>
    [Serializable]
    public sealed class HeadPresetData
    {
        public string name;
        [Tooltip("Lowest point of the neck and the chin (head units): vertices blend from the Neck to the Head bone between them.")]
        public float neckBottom, neckTop;
        public float[] positions;
        public float[] normals;
        public HeadPresetSubmesh[] submeshes;

        public int VertexCount => positions == null ? 0 : positions.Length / 3;
        public Vector3 Position(int i) => new(positions[3 * i], positions[3 * i + 1], positions[3 * i + 2]);
        public Vector3 Normal(int i) => new(normals[3 * i], normals[3 * i + 1], normals[3 * i + 2]);
    }

    [Serializable]
    public sealed class HeadPresetSubmesh
    {
        [Tooltip("Hex RGB from the model's palette.")]
        public string color;
        [Tooltip("Drawn with the recipe's skin tone and the body's skin material, so the neck matches the body.")]
        public bool skin;
        public int[] triangles;

        /// <summary>Parsed by hand: generation runs on worker threads, where Unity's ColorUtility must not be used.</summary>
        public Color Color =>
            color != null && color.Length == 6 && int.TryParse(color, System.Globalization.NumberStyles.HexNumber, null, out int rgb)
                ? new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f)
                : Color.white;
    }

    /// <summary>
    /// Loads and caches head presets. Resources can only be loaded on the main thread, so
    /// <see cref="ProceduralCharacterBuilder"/> calls <see cref="Preload"/> before generating on worker threads.
    /// </summary>
    public static class HeadPresetLibrary
    {
        private static readonly Dictionary<HeadPreset, HeadPresetData> Loaded = new();

        public const string ResourceFolder = "Heads/";

        /// <summary>Main thread: loads the preset's data if needed.</summary>
        public static void Preload(HeadPreset preset)
        {
            if (preset == HeadPreset.Procedural) return;
            lock (Loaded) if (Loaded.ContainsKey(preset)) return;
            var asset = Resources.Load<TextAsset>(ResourceFolder + preset);
            HeadPresetData data = null;
            if (asset == null) Debug.LogWarning("[HeadPreset] Missing Resources/" + ResourceFolder + preset + ".json; using the procedural head.");
            else
            {
                data = JsonUtility.FromJson<HeadPresetData>(asset.text);
                if (data == null || data.VertexCount == 0 || data.submeshes == null || data.normals == null || data.normals.Length != data.positions.Length)
                {
                    Debug.LogWarning("[HeadPreset] " + preset + ".json is invalid; using the procedural head.");
                    data = null;
                }
                Resources.UnloadAsset(asset);
            }
            lock (Loaded) Loaded[preset] = data;
        }

        /// <summary>
        /// Thread-safe once <see cref="Preload"/> ran for this preset (on the main thread it loads on demand).
        /// Null = procedural head (also when the preset's file is missing or invalid).
        /// </summary>
        public static HeadPresetData Get(HeadPreset preset)
        {
            if (preset == HeadPreset.Procedural) return null;
            lock (Loaded) if (Loaded.TryGetValue(preset, out var d)) return d;
            Preload(preset);
            lock (Loaded) return Loaded.TryGetValue(preset, out var d2) ? d2 : null;
        }
    }
}
