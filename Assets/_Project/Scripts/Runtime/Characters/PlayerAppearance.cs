using System;
using UnityEngine;

namespace LightMMORPG.Characters
{
    /// <summary>
    /// Player adjustments of the Toon Head (<see cref="ToonHeadSettings"/>, indices 0–21), the body (22–25: build, muscle,
    /// shoulders, waist) and the brows (26–28: size, angle, spacing; brow height is 17), each -1..1 with 0 = the base head /
    /// base body: -1 / +1 reach the low / high end of the slider.
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
        public float bodyBuild, bodyMuscle, bodyShoulders, bodyWaist;
        public float browSize, browAngle, browSpacing;

        public const int Count = 29;
        /// <summary>Indices below this drive the head; the rest drive the body.</summary>
        public const int HeadCount = 22;

        public float this[int i]
        {
            readonly get => i switch
            {
                0 => headWidth, 1 => skullHeight, 2 => backOfHead, 3 => faceDepth,
                4 => cheekWidth, 5 => cheekFull, 6 => jawWidth, 7 => faceLength,
                8 => chinWidth, 9 => chinShape, 10 => chinForward,
                11 => noseSize, 12 => noseHeight, 13 => mouthHeight,
                14 => eyeSize, 15 => eyeHeight, 16 => eyeSpacing, 17 => browHeight,
                18 => earSize, 19 => earHeight, 20 => earFlare, 21 => neckWidth,
                22 => bodyBuild, 23 => bodyMuscle, 24 => bodyShoulders, 25 => bodyWaist,
                26 => browSize, 27 => browAngle, _ => browSpacing,
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
                    case 21: neckWidth = value; break;
                    case 22: bodyBuild = value; break;
                    case 23: bodyMuscle = value; break;
                    case 24: bodyShoulders = value; break;
                    case 25: bodyWaist = value; break;
                    case 26: browSize = value; break;
                    case 27: browAngle = value; break;
                    default: browSpacing = value; break;
                }
            }
        }
    }

    /// <summary>Hairstyles the player can pick (the Hair Bench styles), in Customize order.</summary>
    public enum PlayerHair { Light, Messy }

    /// <summary>The player's choices from the Customize screen: any eye and brow style, a hairstyle, the head, brow and body sliders.</summary>
    [Serializable]
    public struct FaceCustomization
    {
        public ToonEyeStyle eyeStyle;
        public ToonBrowStyle browStyle;
        public PlayerHair hair;
        /// <summary>Index into <see cref="PlayerAppearance.HairColors"/>; any colour goes with any hairstyle.</summary>
        public int hairColor;
        /// <summary>Index into <see cref="PlayerAppearance.AuraColors"/> (0 = gold, also what older saves load as).</summary>
        public int auraColor;
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
            if (!Enum.IsDefined(typeof(ToonBrowStyle), f.browStyle)) f.browStyle = ToonBrowStyle.Classic;
            if (!Enum.IsDefined(typeof(PlayerHair), f.hair)) f.hair = PlayerHair.Light;
            if (f.hairColor < 0 || f.hairColor >= PlayerAppearance.HairColors.Length) f.hairColor = 0;
            if (f.auraColor < 0 || f.auraColor >= PlayerAppearance.AuraColors.Length) f.auraColor = 0;
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

        /// <summary>One Customize slider: labels and the range it spans (<see cref="ToonHeadSettings"/> field, or the recipe's body field).</summary>
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
            // body (CharacterRecipe bulk / muscle / shoulderWidth / waist on the base mesh body, 1 = as modelled)
            new("Build", "Slim", "Broad", 0.88f, 1.15f),
            new("Muscle", "Lean", "Muscular", 0.6f, 1.4f),
            new("Shoulders", "Narrow", "Wide", 0.9f, 1.12f),
            new("Waist", "Narrow", "Wide", 0.86f, 1.15f),
            // brows (Brow Bench sliders; ToonHeadSettings browSize / browAngle / browSpacing)
            new("Brow size", "Small", "Large", 0.7f, 1.4f),
            new("Brow angle", "Soft", "Fierce", -25f, 25f),
            new("Brow spacing", "Close", "Wide", -0.03f, 0.03f),
        };

        /// <summary>First brow slider index (size, angle, spacing follow).</summary>
        public const int BrowSliders = 26;

        /// <summary>Names of the eye styles for the Customize screen, in <see cref="ToonEyeStyle"/> order.</summary>
        public static readonly string[] EyeNames = { "Drawn", "Tall", "Wide", "Hand-made", "Sharp", "Arch" };

        /// <summary>Names of the brow styles for the Customize screen, in <see cref="ToonBrowStyle"/> order.</summary>
        public static readonly string[] BrowNames = { "Classic", "Sharp", "Peak", "Block" };

        /// <summary>Names of the hairstyles for the Customize screen, in <see cref="PlayerHair"/> order.</summary>
        public static readonly string[] HairNames = { "Light", "Messy layers" };

        /// <summary>Hair colours for the Customize screen (the first two are the Hair Bench colours of Light and Messy layers).</summary>
        public static readonly (string name, Color color)[] HairColors =
        {
            ("Moss black", new Color32(0x33, 0x50, 0x3b, 0xff)),
            ("Ink blue", new Color32(0x2d, 0x34, 0x48, 0xff)),
            ("Black", new Color32(0x1e, 0x1c, 0x22, 0xff)),
            ("Chestnut", new Color32(0x5a, 0x34, 0x24, 0xff)),
            ("Copper", new Color32(0xa3, 0x50, 0x2a, 0xff)),
            ("Gold", new Color32(0xd9, 0xa9, 0x4a, 0xff)),
            ("Silver", new Color32(0xb4, 0xb8, 0xc6, 0xff)),
            ("Rose", new Color32(0xd9, 0x84, 0xa2, 0xff)),
            ("Crimson", new Color32(0x8e, 0x26, 0x32, 0xff)),
            ("Violet", new Color32(0x5b, 0x43, 0x90, 0xff)),
        };

        /// <summary>Aura colours for the Customize screen (Body tab), as shown on the swatches; the aura draws them HDR-bright.</summary>
        public static readonly (string name, Color color)[] AuraColors =
        {
            ("Gold", new Color(1f, 0.85f, 0.1f)),
            ("Flame", new Color(1f, 0.45f, 0.08f)),
            ("Crimson", new Color(1f, 0.12f, 0.15f)),
            ("Rose", new Color(1f, 0.35f, 0.75f)),
            ("Violet", new Color(0.62f, 0.3f, 1f)),
            ("Blue", new Color(0.2f, 0.42f, 1f)),
            ("Sky", new Color(0.3f, 0.78f, 1f)),
            ("Emerald", new Color(0.2f, 1f, 0.45f)),
            ("Lime", new Color(0.7f, 1f, 0.2f)),
            ("White", new Color(0.92f, 0.95f, 1f)),
        };

        /// <summary>The recipe's (HDR) aura colour for an aura pick.</summary>
        public static Color AuraColor(int index)
        {
            var c = AuraColors[Mathf.Clamp(index, 0, AuraColors.Length - 1)].color * 1.8f;
            c.a = 1f;
            return c;
        }

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

        /// <summary>Applies the eye style, hair and head sliders to the recipe's Toon Head, the body sliders to its body, and returns the recipe.</summary>
        public static CharacterRecipe Apply(CharacterRecipe recipe, FaceCustomization f)
        {
            f = f.Sanitized();
            var head = recipe.toonHead = recipe.toonHead?.Clone() ?? new ToonHeadSettings();
            head.eyeStyle = f.eyeStyle;
            head.browStyle = f.browStyle;
            recipe.hairStyle = ToHairStyle(f.hair);
            recipe.hairColor = recipe.hairTipColor = HairColors[f.hairColor].color;
            recipe.auraColor = AuraColor(f.auraColor);
            for (int i = 0; i < FaceSliders.HeadCount; i++)
            {
                var d = Sliders[i];
                SetHead(head, i, Adjust(GetHead(head, i), f.sliders[i], d.Min, d.Max));
            }
            float Body(int i, float value) => Adjust(value, f.sliders[i], Sliders[i].Min, Sliders[i].Max);
            recipe.bulk = Body(22, recipe.bulk);
            recipe.muscle = Body(23, recipe.muscle);
            recipe.shoulderWidth = Body(24, recipe.shoulderWidth);
            recipe.waist = Body(25, recipe.waist);
            float Brow(int i, float value) => Adjust(value, f.sliders[i], Sliders[i].Min, Sliders[i].Max);
            head.browSize = Brow(BrowSliders, head.browSize);
            head.browAngle = Brow(BrowSliders + 1, head.browAngle);
            head.browSpacing = Brow(BrowSliders + 2, head.browSpacing);
            return recipe;
        }

        /// <summary>The palette's hero wearing these face choices.</summary>
        public static CharacterRecipe ForPalette(int palette, FaceCustomization f) => Apply(CharacterPresets.ForPalette(palette), f);
    }
}
