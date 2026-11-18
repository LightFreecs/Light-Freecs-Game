using UnityEngine;

namespace LightMMORPG.Characters
{
    /// <summary>The 4 hero recipes, one per player palette (0 orange, 1 blue, 2 green, 3 purple).</summary>
    public static class CharacterPresets
    {
        public const int Count = 4;

        /// <summary>Jacket colour per player slot (orange, blue, green, purple), matching the fighter palette materials.</summary>
        public static readonly Color[] PaletteColors =
        {
            new(1f, 0.5f, 0.12f), new(0.16f, 0.42f, 1f), new(0.22f, 0.75f, 0.32f), new(0.6f, 0.28f, 0.92f),
        };

        /// <summary>
        /// The in-game hero for a player slot: currently one design (<see cref="Brawler"/>) recoloured per palette.
        /// The other recipes (<see cref="Warrior"/>, <see cref="Berserker"/>, <see cref="Rival"/>) are kept for later.
        /// </summary>
        public static CharacterRecipe ForPalette(int paletteIndex)
        {
            int i = ((paletteIndex % Count) + Count) % Count;
            var r = Brawler();
            r.outfitPrimary = PaletteColors[i];
            if (i != 0) r.displayName += "_" + i;
            return r;
        }

        /// <summary>All four distinct designs (showcase / future character select).</summary>
        public static CharacterRecipe Distinct(int index) => (((index % Count) + Count) % Count) switch
        {
            0 => Brawler(),
            1 => Warrior(),
            2 => Berserker(),
            _ => Rival(),
        };

        /// <summary>Orange cropped high-collar jacket with navy piping, black undershirt, charcoal shorts, laced slate boots. Black-to-crimson spikes, big amber eyes, grin; jaw, chin, nose and ears use the Head1 part presets (traced from the user's Blockbench head).</summary>
        public static CharacterRecipe Brawler() => new()
        {
            displayName = "Kaien",
            toonHead = ToonHeadPresets.Get(0, ToonEyeStyle.Drawn),
            seed = 101,
            height = 2.28f, muscle = 0.75f, bulk = 0.95f, shoulderWidth = 1f, waist = 0.95f, headSize = 1.3f,
            jawPreset = JawPreset.Head1, chinPreset = ChinPreset.Head1, nosePreset = NosePreset.Head1, earPreset = EarPreset.Head1,
            skinTone = new Color(1f, 0.85f, 0.72f),
            hairStyle = HairStyle.SpikyUp, hairColor = new Color(0.07f, 0.06f, 0.09f), hairTipColor = new Color(0.72f, 0.1f, 0.1f),
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

        /// <summary>Blue bodysuit, white chest plate + shoulder pads with gold trim, swept-back hair.</summary>
        public static CharacterRecipe Warrior() => new()
        {
            displayName = "Seiran",
            toonHead = ToonHeadPresets.Get(3, ToonEyeStyle.Wide),
            seed = 202,
            height = 2.32f, muscle = 0.85f, bulk = 0.95f, shoulderWidth = 1.05f, waist = 0.95f, headSize = 1.25f,
            skinTone = new Color(0.98f, 0.8f, 0.68f),
            hairStyle = HairStyle.SweptBack, hairColor = new Color(0.1f, 0.14f, 0.35f), hairTipColor = new Color(0.35f, 0.7f, 1f), spikeCount = 32, spikeLength = 1f, spikeCurl = 0.35f,
            eyeShape = EyeShape.Narrow, eyeColor = new Color(0.15f, 0.5f, 0.95f), browAngle = 0.8f, browThickness = 0.9f,
            outfit = OutfitType.ArmoredSuit, outfitPrimary = new Color(0.08f, 0.2f, 0.65f), outfitSecondary = new Color(0.93f, 0.94f, 0.97f),
            outfitTrim = new Color(1f, 0.78f, 0.22f), sleeveLength = 1f, pantsBagginess = 1f,
            belt = false, beltColor = new Color(1f, 0.78f, 0.22f),
            wristbands = true, wristbandColor = new Color(0.93f, 0.94f, 0.97f),
            boots = true, bootColor = new Color(0.93f, 0.94f, 0.97f), bootTrimColor = new Color(1f, 0.78f, 0.22f),
            accessory = Accessory.ShoulderPads, accessoryColor = new Color(0.93f, 0.94f, 0.97f),
            auraColor = new Color(0.3f, 0.9f, 2.6f),
        };

        /// <summary>Wild ash-blond hair with lime tips, open sleeveless teal vest over a bare chest, khaki pants, gold bands.</summary>
        public static CharacterRecipe Berserker() => new()
        {
            displayName = "Gorran",
            toonHead = ToonHeadPresets.Get(4, ToonEyeStyle.Tall),
            seed = 303,
            height = 2.38f, muscle = 1.05f, bulk = 1.05f, shoulderWidth = 1.1f, waist = 1f, headSize = 1.22f,
            skinTone = new Color(0.86f, 0.6f, 0.42f),
            hairStyle = HairStyle.Wild, hairColor = new Color(0.88f, 0.8f, 0.52f), hairTipColor = new Color(0.45f, 0.88f, 0.2f), spikeCount = 30, spikeLength = 1.15f, spikeCurl = 0.45f,
            eyeShape = EyeShape.Fierce, eyeColor = new Color(0.45f, 0.8f, 0.15f), browAngle = 1.1f, browThickness = 1.3f, expression = Expression.Smirk,
            outfit = OutfitType.JacketPants, outfitPrimary = new Color(0.12f, 0.45f, 0.36f), outfitSecondary = new Color(0.55f, 0.47f, 0.33f),
            outfitTrim = new Color(0.95f, 0.75f, 0.2f), sleeveLength = 0f, pantsBagginess = 1.35f, undershirt = false,
            belt = true, beltColor = new Color(0.32f, 0.18f, 0.1f),
            wristbands = true, wristbandColor = new Color(0.95f, 0.75f, 0.2f),
            boots = true, bootColor = new Color(0.25f, 0.17f, 0.12f), bootTrimColor = new Color(0.95f, 0.75f, 0.2f),
            accessory = Accessory.None,
            auraColor = new Color(0.6f, 2.2f, 0.3f),
        };

        /// <summary>Long spiky silver-to-lavender hair, purple long coat over a black shirt, white scarf, red eyes.</summary>
        public static CharacterRecipe Rival() => new()
        {
            displayName = "Veyl",
            toonHead = ToonHeadPresets.Get(6, ToonEyeStyle.HandMade),
            seed = 404,
            height = 2.34f, muscle = 0.75f, bulk = 0.92f, shoulderWidth = 1f, waist = 0.92f, headSize = 1.25f,
            skinTone = new Color(1f, 0.88f, 0.8f),
            hairStyle = HairStyle.LongSpiky, hairColor = new Color(0.9f, 0.9f, 0.97f), hairTipColor = new Color(0.68f, 0.55f, 0.95f), spikeCount = 38, spikeLength = 1.1f, spikeCurl = 0.3f,
            eyeShape = EyeShape.Narrow, eyeColor = new Color(0.85f, 0.12f, 0.2f), browAngle = 0.5f, browThickness = 0.8f,
            outfit = OutfitType.JacketPants, outfitPrimary = new Color(0.4f, 0.16f, 0.62f), outfitSecondary = new Color(0.12f, 0.1f, 0.16f),
            outfitTrim = new Color(0.85f, 0.75f, 0.95f), sleeveLength = 1f, pantsBagginess = 0.8f, undershirt = true,
            belt = true, beltColor = new Color(0.12f, 0.1f, 0.16f),
            wristbands = false, wristbandColor = new Color(0.12f, 0.1f, 0.16f),
            boots = true, bootColor = new Color(0.14f, 0.11f, 0.18f), bootTrimColor = new Color(0.62f, 0.3f, 0.9f),
            accessory = Accessory.Scarf, accessoryColor = new Color(0.95f, 0.95f, 0.98f),
            auraColor = new Color(1.4f, 0.4f, 2.4f),
        };
    }
}
