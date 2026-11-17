using System;
using UnityEngine;

namespace LightMMORPG.Characters
{
    /// <summary>
    /// Player adjustments of the face parts (<see cref="FaceParts"/>), each -1..1 with 0 = the fighter's own value:
    /// -1 / +1 reach the low / high end of the matching <see cref="CharacterRecipe"/> slider.
    /// </summary>
    [Serializable]
    public struct FaceSliders
    {
        public float jawWidth, jawLength, jawForward, jawThickness;
        public float chinSize, chinLength, chinForward;
        public float noseSize, noseLength, noseHeight;
        public float earSize, earHeight, earFlare;
    }

    /// <summary>The player's face choices from the Customize screen: sliders on top of the fighter's face parts.</summary>
    [Serializable]
    public struct FaceCustomization
    {
        public FaceSliders sliders;

        /// <summary>Copy with every slider clamped to -1..1 (values can come from other players).</summary>
        public FaceCustomization Sanitized()
        {
            var f = this;
            var s = f.sliders;
            static float C(float v) => float.IsNaN(v) ? 0f : Mathf.Clamp(v, -1f, 1f);
            s.jawWidth = C(s.jawWidth); s.jawLength = C(s.jawLength); s.jawForward = C(s.jawForward); s.jawThickness = C(s.jawThickness);
            s.chinSize = C(s.chinSize); s.chinLength = C(s.chinLength); s.chinForward = C(s.chinForward);
            s.noseSize = C(s.noseSize); s.noseLength = C(s.noseLength); s.noseHeight = C(s.noseHeight);
            s.earSize = C(s.earSize); s.earHeight = C(s.earHeight); s.earFlare = C(s.earFlare);
            f.sliders = s;
            return f;
        }
    }

    /// <summary>
    /// Saves the local player's <see cref="FaceCustomization"/> on this device (PlayerPrefs) and applies one to a recipe.
    /// </summary>
    public static class PlayerAppearance
    {
        // v3: sliders now adjust the face parts (jaw, chin, nose, ears), so older saves are ignored
        public const string PrefKey = "profile.face.v3";

        public static FaceCustomization Load()
        {
            return FromJson(PlayerPrefs.GetString(PrefKey, string.Empty));
        }

        public static void Save(FaceCustomization f)
        {
            PlayerPrefs.SetString(PrefKey, ToJson(f));
            PlayerPrefs.Save();
        }

        public static string ToJson(FaceCustomization f) => JsonUtility.ToJson(f.Sanitized());

        public static FaceCustomization FromJson(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 2048) return default;
            try { return JsonUtility.FromJson<FaceCustomization>(json).Sanitized(); }
            catch (Exception) { return default; }
        }

        /// <summary>Moves a recipe value toward the low (v &lt; 0) or high (v &gt; 0) end of its slider range.</summary>
        private static float Adjust(float value, float v, float min, float max) => v < 0f ? Mathf.Lerp(value, min, -v) : Mathf.Lerp(value, max, v);

        /// <summary>Applies the sliders to the recipe's face part values (ranges as on <see cref="CharacterRecipe"/>) and returns it.</summary>
        public static CharacterRecipe Apply(CharacterRecipe recipe, FaceCustomization f)
        {
            var s = f.Sanitized().sliders;
            recipe.jawWidth = Adjust(recipe.jawWidth, s.jawWidth, 0.85f, 1.2f);
            recipe.jawLength = Adjust(recipe.jawLength, s.jawLength, -0.012f, 0.015f);
            recipe.jawForward = Adjust(recipe.jawForward, s.jawForward, -0.02f, 0.02f);
            recipe.jawThickness = Adjust(recipe.jawThickness, s.jawThickness, 0.7f, 1.4f);
            recipe.chinSize = Adjust(recipe.chinSize, s.chinSize, 0.6f, 1.5f);
            recipe.chinLength = Adjust(recipe.chinLength, s.chinLength, -0.008f, 0.012f);
            recipe.chinForward = Adjust(recipe.chinForward, s.chinForward, -0.015f, 0.015f);
            recipe.noseSize = Adjust(recipe.noseSize, s.noseSize, 0.6f, 1.6f);
            recipe.noseLength = Adjust(recipe.noseLength, s.noseLength, 0.6f, 1.5f);
            recipe.noseHeight = Adjust(recipe.noseHeight, s.noseHeight, -0.012f, 0.012f);
            recipe.earSize = Adjust(recipe.earSize, s.earSize, 0.7f, 1.5f);
            recipe.earHeight = Adjust(recipe.earHeight, s.earHeight, -0.03f, 0.03f);
            recipe.earFlare = Adjust(recipe.earFlare, s.earFlare, -20f, 30f);
            return recipe;
        }

        /// <summary>The palette's hero wearing these face choices.</summary>
        public static CharacterRecipe ForPalette(int palette, FaceCustomization f) => Apply(CharacterPresets.ForPalette(palette), f);
    }
}
