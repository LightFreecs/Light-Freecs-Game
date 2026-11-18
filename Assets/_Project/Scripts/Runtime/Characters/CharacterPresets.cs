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

        /// <summary>Slot colour per player (orange, blue, green, purple), matching the fighter palette materials: the trunks' waistband (and the jacket on procedural bodies).</summary>
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
        /// The base hero: the owner's anime base mesh body (<see cref="BaseBody"/>) in navy trunks with a waistband in the
        /// player slot colour, the Light hair (Hair Bench, green-black), amber eyes. Head = the measured Toon Head with every
        /// slider at its default; body sliders at 1 = the base mesh as modelled. Clothes come later (the old jacket / shorts /
        /// boots fields below only apply to the procedural body).
        /// </summary>
        public static CharacterRecipe Hero() => new()
        {
            displayName = "Fighter",
            toonHead = new ToonHeadSettings(),
            seed = 101,
            body = BodyModel.BaseMesh, trunksColor = new Color(0.17f, 0.18f, 0.27f),
            height = 2.28f, muscle = 1f, bulk = 1f, shoulderWidth = 1f, waist = 1f, headSize = 1.3f,
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
