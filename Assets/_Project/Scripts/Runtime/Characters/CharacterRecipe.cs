using System;
using UnityEngine;

namespace LightMMORPG.Characters
{
    public enum HairStyle { SpikyUp, SweptBack, Wild, LongSpiky, Short }
    public enum EyeShape { Sharp, Round, Narrow, Fierce, Big }
    public enum Expression { Serious, Grin, Smirk }
    public enum OutfitType { Gi, ArmoredSuit, JacketPants, CroppedJacket }
    public enum LegWear { Pants, Shorts }
    public enum Accessory { None, Scarf, ShoulderPads }

    /// <summary>
    /// Serializable description of a procedural fighter. <see cref="ProceduralCharacterBuilder"/> turns it into a
    /// skinned, rigged, toon-shaded GameObject. Same recipe (incl. <see cref="seed"/>) = identical mesh.
    /// Lengths are meters; the body is designed at 2.3 m and scaled uniformly to <see cref="height"/>.
    /// </summary>
    [Serializable]
    public sealed class CharacterRecipe
    {
        public string displayName = "Fighter";
        public int seed = 1;

        [Header("Body")]
        [Range(1.6f, 3f)] public float height = 2.3f;
        [Tooltip("Muscle definition / size (pecs, abs, deltoids, arms, calves).")]
        [Range(0.6f, 1.5f)] public float muscle = 1f;
        [Tooltip("Overall width of torso and limbs.")]
        [Range(0.8f, 1.3f)] public float bulk = 1f;
        [Range(0.85f, 1.25f)] public float shoulderWidth = 1f;
        [Range(0.8f, 1.2f)] public float waist = 1f;
        [Tooltip("Head scale; ~1.3 gives youthful ~6-heads-tall shonen proportions.")]
        [Range(0.85f, 1.45f)] public float headSize = 1f;
        public Color skinTone = new(1f, 0.84f, 0.72f);

        [Header("Hair")]
        public HairStyle hairStyle = HairStyle.SpikyUp;
        public Color hairColor = new(0.12f, 0.1f, 0.12f);
        [Tooltip("Colour the spikes fade to at their tips (two-tone hair).")]
        public Color hairTipColor = new(0.12f, 0.1f, 0.12f);
        [Range(8, 90)] public int spikeCount = 34;
        [Range(0.3f, 2f)] public float spikeLength = 1f;
        [Range(0f, 1f)] public float spikeCurl = 0.3f;

        [Header("Face")]
        public EyeShape eyeShape = EyeShape.Sharp;
        public Color eyeColor = new(0.2f, 0.25f, 0.35f);
        [Tooltip("Positive = angry (inner end lower).")]
        [Range(-1f, 1.5f)] public float browAngle = 0.6f;
        [Range(0.5f, 1.8f)] public float browThickness = 1f;
        public Expression expression = Expression.Serious;

        [Header("Outfit")]
        public OutfitType outfit = OutfitType.Gi;
        public Color outfitPrimary = new(1f, 0.5f, 0.1f);
        public Color outfitSecondary = new(0.15f, 0.2f, 0.45f);
        public Color outfitTrim = new(0.95f, 0.8f, 0.3f);
        [Tooltip("0 = sleeveless, 1 = full sleeves to the wrist.")]
        [Range(0f, 1f)] public float sleeveLength = 0.5f;
        [Range(0.5f, 1.6f)] public float pantsBagginess = 1f;
        public bool undershirt;
        public LegWear legwear = LegWear.Pants;

        [Header("Gear")]
        public bool belt = true;
        public Color beltColor = new(0.15f, 0.2f, 0.45f);
        public bool wristbands = true;
        public Color wristbandColor = new(0.15f, 0.2f, 0.45f);
        public bool boots = true;
        public Color bootColor = new(0.15f, 0.2f, 0.45f);
        public Color bootTrimColor = new(0.8f, 0.15f, 0.12f);
        public bool bootLaces;
        public Color laceColor = new(0.85f, 0.88f, 0.8f);
        public Accessory accessory = Accessory.None;
        public Color accessoryColor = Color.white;

        [Header("Powers")]
        public Color auraColor = new(1.8f, 1.1f, 0.15f);

        public CharacterRecipe Clone() => (CharacterRecipe)MemberwiseClone();

        /// <summary>Stable key for mesh/material caches (same recipe → same key).</summary>
        public string CacheKey() => JsonUtility.ToJson(this);
    }
}
