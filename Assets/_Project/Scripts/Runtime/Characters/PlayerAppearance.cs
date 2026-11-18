using System;
using UnityEngine;

namespace LightMMORPG.Characters
{
    /// <summary>
    /// Player adjustments of the Toon Head (<see cref="ToonHeadSettings"/>), each -1..1 with 0 = the base head:
    /// -1 / +1 reach the low / high end of the matching <see cref="ToonHeadSettings"/> slider.
    /// Indexed in the order of <see cref="PlayerAppearance.Sliders"/>.
    /// </summary>
    [Serializable]
    public struct FaceSliders
    {
        public float headWidth, skullHeight, backOfHead, faceDepth;
        public float cheekWidth, cheekFull, jawWidth, faceLength;
        public float chinWidth, chinShape, chinForward;
        public float noseSize, noseHeight, mouthHeight;
        public float eyeSize, eyeHeight, eyeSpacing, browHeight;
        public float earSize, earHeight, earFlare, neckWidth;

        public const int Count = 22;

        public float this[int i]
        {
            readonly get => i switch
            {
                0 => headWidth, 1 => skullHeight, 2 => backOfHead, 3 => faceDepth,
                4 => cheekWidth, 5 => cheekFull, 6 => jawWidth, 7 => faceLength,
                8 => chinWidth, 9 => chinShape, 10 => chinForward,
                11 => noseSize, 12 => noseHeight, 13 => mouthHeight,
                14 => eyeSize, 15 => eyeHeight, 16 => eyeSpacing, 17 => browHeight,
                18 => earSize, 19 => earHeight, 20 => earFlare, _ => neckWidth,
            };
            set
            {
                switch (i)
                {
                    case 0: headWidth = value; break;
                    case 1: skullHeight = value; break;
                    case 2: backOfHead = value; break;
                    case 3: faceDepth = value; break;
                    case 4: cheekWidth = value; break;
                    case 5: cheekFull = value; break;
                    case 6: jawWidth = value; break;
                    case 7: faceLength = value; break;
                    case 8: chinWidth = value; break;
                    case 9: chinShape = value; break;
                    case 10: chinForward = value; break;
                    case 11: noseSize = value; break;
                    case 12: noseHeight = value; break;
                    case 13: mouthHeight = value; break;
                    case 14: eyeSize = value; break;
                    case 15: eyeHeight = value; break;
                    case 16: eyeSpacing = value; break;
                    case 17: browHeight = value; break;
                    case 18: earSize = value; break;
                    case 19: earHeight = value; break;
                    case 20: earFlare = value; break;
                    default: neckWidth = value; break;
                }
            }
        }
    }

    /// <summary>Hairstyles the player can pick (the Hair Bench styles), in Customize order.</summary>
    public enum PlayerHair { Light, Messy }

    /// <summary>The player's choices from the Customize screen: any eye style, a hairstyle and the head sliders.</summary>
    [Serializable]
    public struct FaceCustomization
    {
        public ToonEyeStyle eyeStyle;
        public PlayerHair hair;
        public FaceSliders sliders;

        /// <summary>Copy with every slider clamped to -1..1 and a valid eye style (values can come from other players).</summary>
        public FaceCustomization Sanitized()
        {
            var f = this;
            for (int i = 0; i < FaceSliders.Count; i++)
            {
                float v = f.sliders[i];
                f.sliders[i] = float.IsNaN(v) ? 0f : Mathf.Clamp(v, -1f, 1f);
            }
            if (!Enum.IsDefined(typeof(ToonEyeStyle), f.eyeStyle)) f.eyeStyle = ToonEyeStyle.Drawn;
            if (!Enum.IsDefined(typeof(PlayerHair), f.hair)) f.hair = PlayerHair.Light;
            return f;
        }
    }

    /// <summary>
    /// Saves the local player's <see cref="FaceCustomization"/> on this device (PlayerPrefs) and applies one to a recipe.
    /// </summary>
    public static class PlayerAppearance
    {
        // v4: sliders drive the Toon Head and the eye is a free pick, so older saves (old SDF face parts) are ignored
        public const string PrefKey = "profile.face.v4";

        /// <summary>One Customize slider: labels and the <see cref="ToonHeadSettings"/> range it spans.</summary>
        public readonly struct SliderDef
        {
            public readonly string Label, Low, High;
            public readonly float Min, Max;
            public SliderDef(string label, string low, string high, float min, float max) { Label = label; Low = low; High = high; Min = min; Max = max; }
        }

        /// <summary>The Customize sliders, in <see cref="FaceSliders"/> index order (ranges as on <see cref="ToonHeadSettings"/>).</summary>
        public static readonly SliderDef[] Sliders =
        {
            new("Head width", "Narrow", "Wide", 0.85f, 1.15f),
            new("Skull height", "Low", "Tall", 0.85f, 1.15f),
            new("Back of head", "Flat", "Round", 0.8f, 1.2f),
            new("Face depth", "Flat", "Deep", 0.85f, 1.2f),
            new("Cheek width", "Narrow", "Wide", 0.85f, 1.15f),
            new("Cheeks", "Hollow", "Full", -1f, 1f),
            new("Jaw width", "Narrow", "Wide", 0.7f, 1.4f),
            new("Face length", "Short", "Long", 0.8f, 1.25f),
            new("Chin width", "Narrow", "Wide", 0.4f, 2.5f),
            new("Chin shape", "Pointed", "Square", -1f, 1f),
            new("Chin position", "Back", "Forward", -0.03f, 0.04f),
            new("Nose size", "Small", "Large", 0f, 2f),
            new("Nose height", "Low", "High", -0.04f, 0.04f),
            new("Mouth height", "Low", "High", -0.04f, 0.04f),
            new("Eye size", "Small", "Large", 0.75f, 1.25f),
            new("Eye height", "Low", "High", -0.05f, 0.05f),
            new("Eye spacing", "Close", "Wide", -0.03f, 0.03f),
            new("Brow height", "Low", "High", -0.03f, 0.04f),
            new("Ear size", "Small", "Large", 0.7f, 1.4f),
            new("Ear height", "Low", "High", -0.06f, 0.06f),
            new("Ear angle", "Flat", "Out", -25f, 30f),
            new("Neck width", "Slim", "Thick", 0.75f, 1.35f),
        };

        /// <summary>Names of the eye styles for the Customize screen, in <see cref="ToonEyeStyle"/> order.</summary>
        public static readonly string[] EyeNames = { "Drawn", "Tall", "Wide", "Hand-made" };

        /// <summary>Names of the hairstyles for the Customize screen, in <see cref="PlayerHair"/> order.</summary>
        public static readonly string[] HairNames = { "Light", "Messy layers" };

        /// <summary>The recipe hairstyle for a player pick.</summary>
        public static HairStyle ToHairStyle(PlayerHair h) => h == PlayerHair.Messy ? HairStyle.Messy : HairStyle.Light;

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

        /// <summary>Moves a value toward the low (v &lt; 0) or high (v &gt; 0) end of its slider range.</summary>
        private static float Adjust(float value, float v, float min, float max) => v < 0f ? Mathf.Lerp(value, min, -v) : Mathf.Lerp(value, max, v);

        private static float GetHead(ToonHeadSettings h, int i) => i switch
        {
            0 => h.headWidth, 1 => h.skullHeight, 2 => h.backOfHead, 3 => h.faceDepth,
            4 => h.cheekWidth, 5 => h.cheekFull, 6 => h.jawWidth, 7 => h.faceLength,
            8 => h.chinWidth, 9 => h.chinShape, 10 => h.chinForward,
            11 => h.noseSize, 12 => h.noseHeight, 13 => h.mouthHeight,
            14 => h.eyeSize, 15 => h.eyeHeight, 16 => h.eyeSpacing, 17 => h.browHeight,
            18 => h.earSize, 19 => h.earHeight, 20 => h.earFlare, _ => h.neckWidth,
        };

        private static void SetHead(ToonHeadSettings h, int i, float v)
        {
            switch (i)
            {
                case 0: h.headWidth = v; break;
                case 1: h.skullHeight = v; break;
                case 2: h.backOfHead = v; break;
                case 3: h.faceDepth = v; break;
                case 4: h.cheekWidth = v; break;
                case 5: h.cheekFull = v; break;
                case 6: h.jawWidth = v; break;
                case 7: h.faceLength = v; break;
                case 8: h.chinWidth = v; break;
                case 9: h.chinShape = v; break;
                case 10: h.chinForward = v; break;
                case 11: h.noseSize = v; break;
                case 12: h.noseHeight = v; break;
                case 13: h.mouthHeight = v; break;
                case 14: h.eyeSize = v; break;
                case 15: h.eyeHeight = v; break;
                case 16: h.eyeSpacing = v; break;
                case 17: h.browHeight = v; break;
                case 18: h.earSize = v; break;
                case 19: h.earHeight = v; break;
                case 20: h.earFlare = v; break;
                default: h.neckWidth = v; break;
            }
        }

        /// <summary>Applies the eye style and the sliders to the recipe's Toon Head and returns the recipe.</summary>
        public static CharacterRecipe Apply(CharacterRecipe recipe, FaceCustomization f)
        {
            f = f.Sanitized();
            var head = recipe.toonHead = recipe.toonHead?.Clone() ?? new ToonHeadSettings();
            head.eyeStyle = f.eyeStyle;
            recipe.hairStyle = ToHairStyle(f.hair);
            recipe.hairColor = recipe.hairTipColor = ToonHair.LitColor(recipe.hairStyle);
            for (int i = 0; i < FaceSliders.Count; i++)
            {
                var d = Sliders[i];
                SetHead(head, i, Adjust(GetHead(head, i), f.sliders[i], d.Min, d.Max));
            }
            return recipe;
        }

        /// <summary>The palette's hero wearing these face choices.</summary>
        public static CharacterRecipe ForPalette(int palette, FaceCustomization f) => Apply(CharacterPresets.ForPalette(palette), f);
    }
}
