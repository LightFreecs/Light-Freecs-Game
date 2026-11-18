using LightMMORPG.Characters;
using UnityEngine;

namespace LightMMORPG.Tests
{
    /// <summary>
    /// Test-only recipe variations (other hair, outfits, gear and head shapes) so the generator is exercised beyond the
    /// one in-game hero (<see cref="CharacterPresets.Hero"/>). Not playable characters.
    /// </summary>
    internal static class TestRecipes
    {
        public static CharacterRecipe Variant(int index) => (((index % 4) + 4) % 4) switch
        {
            0 => CharacterPresets.Hero(),
            1 => Armored(),
            2 => Vest(),
            _ => Coat(),
        };

        private static CharacterRecipe Armored() => new()
        {
            displayName = "TestArmored",
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

        private static CharacterRecipe Vest() => new()
        {
            displayName = "TestVest",
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

        private static CharacterRecipe Coat() => new()
        {
            displayName = "TestCoat",
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
