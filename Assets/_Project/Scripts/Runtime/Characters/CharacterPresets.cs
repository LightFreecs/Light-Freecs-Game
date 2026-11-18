using UnityEngine;

namespace LightMMORPG.Characters
{
    /// <summary>
    /// The one customizable hero. Every player starts from <see cref="Hero"/> and changes it on the Customize screen
    /// (<see cref="PlayerAppearance"/>); in a match the jacket takes the player slot's colour so fighters stay readable.
    /// </summary>
    public static class CharacterPresets
    {
        /// <summary>Number of player slots / palettes (0 orange, 1 blue, 2 green, 3 purple).</summary>
        public const int PaletteCount = 4;

        /// <summary>Jacket colour per player slot (orange, blue, green, purple), matching the fighter palette materials.</summary>
        public static readonly Color[] PaletteColors =
        {
            new(1f, 0.5f, 0.12f), new(0.16f, 0.42f, 1f), new(0.22f, 0.75f, 0.32f), new(0.6f, 0.28f, 0.92f),
        };

        /// <summary>The hero recoloured for a player slot.</summary>
        public static CharacterRecipe ForPalette(int paletteIndex)
        {
            int i = ((paletteIndex % PaletteCount) + PaletteCount) % PaletteCount;
            var r = Hero();
            r.outfitPrimary = PaletteColors[i];
            if (i != 0) r.displayName += "_" + i;
            return r;
        }

        /// <summary>
        /// The base hero: orange cropped high-collar jacket with navy piping, black undershirt, charcoal shorts, laced slate
        /// boots, the Light hair (Hair Bench, green-black), amber eyes. Head = the measured Toon Head with every slider at its default.
        /// </summary>
        public static CharacterRecipe Hero() => new()
        {
            displayName = "Fighter",
            toonHead = new ToonHeadSettings(),
            seed = 101,
            height = 2.28f, muscle = 0.75f, bulk = 0.95f, shoulderWidth = 1f, waist = 0.95f, headSize = 1.3f,
            jawPreset = JawPreset.Head1, chinPreset = ChinPreset.Head1, nosePreset = NosePreset.Head1, earPreset = EarPreset.Head1,
            skinTone = new Color(1f, 0.85f, 0.72f),
            hairStyle = HairStyle.Light, hairColor = new Color32(0x33, 0x50, 0x3b, 0xff), hairTipColor = new Color32(0x33, 0x50, 0x3b, 0xff),
            spikeCount = 20, spikeLength = 1.1f, spikeCurl = 0.2f,
            eyeShape = EyeShape.Big, eyeColor = new Color(0.9f, 0.52f, 0.1f), browAngle = 0.25f, browThickness = 0.85f, expression = Expression.Grin,
            outfit = OutfitType.CroppedJacket, outfitPrimary = new Color(1f, 0.5f, 0.12f), outfitSecondary = new Color(0.17f, 0.17f, 0.21f),
            outfitTrim = new Color(0.12f, 0.16f, 0.42f), sleeveLength = 1f, pantsBagginess = 1f, undershirt = true, legwear = LegWear.Shorts,
            belt = true, beltColor = new Color(0.1f, 0.1f, 0.12f),
            wristbands = false,
            boots = true, bootColor = new Color(0.3f, 0.36f, 0.5f), bootTrimColor = new Color(0.12f, 0.16f, 0.42f), bootLaces = true, laceColor = new Color(0.92f, 0.9f, 0.82f),
            accessory = Accessory.None,
            auraColor = new Color(1.8f, 1.1f, 0.15f),
        };
    }
}
