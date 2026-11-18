using System;
using UnityEngine;

namespace LightMMORPG.Characters
{
    /// <summary>
    /// Generates a procedural fighter as a child of this transform. In the Player prefab it sits on Graphics/Model and
    /// <see cref="Player.PlayerIdentity"/> calls <see cref="GenerateForPalette"/> on clients; in showcase scenes
    /// <see cref="_generateOnEnable"/> builds it in edit mode too (the generated objects are never saved).
    /// </summary>
    [ExecuteAlways]
    public sealed class ProceduralCharacter : MonoBehaviour
    {
        [SerializeField] private bool _generateOnEnable;
        [SerializeField] private int _presetIndex;
        [SerializeField] private bool _useCustomRecipe;
        [SerializeField] private CharacterRecipe _customRecipe = new();

        private GameObject _generated;

        public CharacterRig Rig { get; private set; }
        public CharacterRecipe Recipe { get; private set; }
        public event Action<CharacterRig> Generated;

        public void Configure(int presetIndex, bool generateOnEnable)
        {
            _presetIndex = presetIndex;
            _generateOnEnable = generateOnEnable;
        }

        private void OnEnable()
        {
            if (_generateOnEnable && _generated == null) Generate(_useCustomRecipe ? _customRecipe : CharacterPresets.ForPalette(_presetIndex));
        }

        private void OnDisable()
        {
            if (!Application.isPlaying) Clear();
        }

        public CharacterRig GenerateForPalette(int paletteIndex)
        {
            _presetIndex = paletteIndex;
            return Generate(CharacterPresets.ForPalette(paletteIndex));
        }

        /// <summary>The palette's hero with a player's face choices (eye style + Toon Head sliders).</summary>
        public CharacterRig GenerateForPalette(int paletteIndex, FaceCustomization face)
        {
            _presetIndex = paletteIndex;
            return Generate(PlayerAppearance.ForPalette(paletteIndex, face));
        }

        public CharacterRig Generate(CharacterRecipe recipe)
        {
            Clear();
            Recipe = recipe;
            _generated = ProceduralCharacterBuilder.Build(recipe);
            _generated.transform.SetParent(transform, false);
            if (!Application.isPlaying)
                foreach (var t in _generated.GetComponentsInChildren<Transform>(true))
                    t.gameObject.hideFlags = HideFlags.DontSave;
            Rig = _generated.GetComponent<CharacterRig>();
            Generated?.Invoke(Rig);
            return Rig;
        }

        public void Clear()
        {
            if (_generated == null) return;
            if (Application.isPlaying) Destroy(_generated);
            else DestroyImmediate(_generated);
            _generated = null;
            Rig = null;
        }
    }
}
