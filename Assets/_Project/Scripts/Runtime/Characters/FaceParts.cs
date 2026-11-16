using UnityEngine;

namespace LightMMORPG.Characters
{
    // Part presets for the procedural head. A preset is a ready-made shape for one part (Classic = the original head,
    // Head1 = traced from the user's Blockbench head); the recipe's sliders then adjust it (1 / 0 = the preset as is).
    public enum JawPreset { Classic, Head1, Square, Pointed }
    public enum ChinPreset { Classic, Head1, Strong, Small }
    public enum NosePreset { Classic, Head1, Button, Long }
    public enum EarPreset { Classic, Head1, Small, Pointed }

    /// <summary>Jaw line: one rounded limb per side from below the cheekbone (fixed) to beside the chin. Head units.</summary>
    public struct JawShape
    {
        /// <summary>Half-width of the lower face (x radius of the face ellipsoid).</summary>
        public float CheekWidth;
        /// <summary>|x| where the jaw limb starts under the cheekbone, and where it ends beside the chin.</summary>
        public float TopWidth, BottomWidth;
        /// <summary>Height and forward position of the jaw limb's lower end.</summary>
        public float BottomY, Forward;
        /// <summary>Jaw limb radius at the top (the lower end is ~70 % of it).</summary>
        public float Thickness;
    }

    /// <summary>Chin ellipsoid. Head units.</summary>
    public struct ChinShape
    {
        public Vector3 Size;
        public float Y, Z;
    }

    /// <summary>Nose: a tapered limb from the bridge (Top) to the tip. Head units.</summary>
    public struct NoseShape
    {
        public Vector3 Top, Tip;
        public float Radius;
    }

    /// <summary>Ear ellipsoid (right side; mirrored for the left). Flare turns it out from the head, Tilt leans the top out.</summary>
    public struct EarShape
    {
        public Vector3 Size, Centre;
        public float Flare, Tilt;
    }

    public struct FaceShapes
    {
        public JawShape Jaw;
        public ChinShape Chin;
        public NoseShape Nose;
        public EarShape Ear;
    }

    /// <summary>Preset shapes and <see cref="Resolve"/> (preset + the recipe's sliders). Pure data, thread-safe.</summary>
    public static class FaceParts
    {
        /// <summary>Nose tip height the painted nose lines were drawn for (Classic).</summary>
        public const float ClassicNoseTipY = 0.038f;

        public static JawShape Jaw(JawPreset p) => p switch
        {
            JawPreset.Head1 => new JawShape { CheekWidth = 0.1f, TopWidth = 0.085f, BottomWidth = 0.03f, BottomY = -0.012f, Forward = 0.072f, Thickness = 0.03f },
            JawPreset.Square => new JawShape { CheekWidth = 0.092f, TopWidth = 0.08f, BottomWidth = 0.04f, BottomY = -0.012f, Forward = 0.07f, Thickness = 0.03f },
            JawPreset.Pointed => new JawShape { CheekWidth = 0.085f, TopWidth = 0.07f, BottomWidth = 0.006f, BottomY = -0.004f, Forward = 0.082f, Thickness = 0.02f },
            _ => new JawShape { CheekWidth = 0.088f, TopWidth = 0.074f, BottomWidth = 0.014f, BottomY = -0.006f, Forward = 0.077f, Thickness = 0.024f },
        };

        public static ChinShape Chin(ChinPreset p) => p switch
        {
            ChinPreset.Head1 => new ChinShape { Size = new Vector3(0.028f, 0.016f, 0.018f), Y = -0.012f, Z = 0.074f },
            ChinPreset.Strong => new ChinShape { Size = new Vector3(0.036f, 0.022f, 0.024f), Y = -0.012f, Z = 0.08f },
            ChinPreset.Small => new ChinShape { Size = new Vector3(0.018f, 0.014f, 0.016f), Y = -0.004f, Z = 0.08f },
            _ => new ChinShape { Size = new Vector3(0.026f, 0.019f, 0.02f), Y = -0.008f, Z = 0.077f },
        };

        public static NoseShape Nose(NosePreset p) => p switch
        {
            NosePreset.Head1 => new NoseShape { Top = new Vector3(0f, 0.074f, 0.113f), Tip = new Vector3(0f, 0.056f, 0.124f), Radius = 0.0075f },
            NosePreset.Button => new NoseShape { Top = new Vector3(0f, 0.054f, 0.115f), Tip = new Vector3(0f, 0.04f, 0.122f), Radius = 0.0085f },
            NosePreset.Long => new NoseShape { Top = new Vector3(0f, 0.07f, 0.11f), Tip = new Vector3(0f, 0.03f, 0.13f), Radius = 0.0055f },
            _ => new NoseShape { Top = new Vector3(0f, 0.064f, 0.112f), Tip = new Vector3(0f, 0.038f, 0.124f), Radius = 0.006f },
        };

        public static EarShape Ear(EarPreset p) => p switch
        {
            EarPreset.Head1 => new EarShape { Size = new Vector3(0.016f, 0.04f, 0.026f), Centre = new Vector3(0.112f, 0.088f, -0.03f), Flare = 32f },
            EarPreset.Small => new EarShape { Size = new Vector3(0.011f, 0.025f, 0.018f), Centre = new Vector3(0.108f, 0.066f, -0.014f), Flare = 12f },
            EarPreset.Pointed => new EarShape { Size = new Vector3(0.013f, 0.045f, 0.02f), Centre = new Vector3(0.11f, 0.085f, -0.02f), Flare = 25f, Tilt = 25f },
            _ => new EarShape { Size = new Vector3(0.014f, 0.032f, 0.022f), Centre = new Vector3(0.11f, 0.068f, -0.014f), Flare = 20f },
        };

        /// <summary>The recipe's part presets with its sliders applied (head units, before headSize).</summary>
        public static FaceShapes Resolve(CharacterRecipe r)
        {
            var jaw = Jaw(r.jawPreset);
            jaw.CheekWidth *= r.jawWidth;
            jaw.TopWidth *= r.jawWidth;
            jaw.BottomWidth *= r.jawWidth;
            jaw.BottomY -= r.jawLength;
            jaw.Forward += r.jawForward;
            jaw.Thickness *= r.jawThickness;

            var chin = Chin(r.chinPreset);
            chin.Size *= r.chinSize;
            chin.Y -= r.jawLength + r.chinLength; // the chin rides on the jaw
            chin.Z += r.jawForward + r.chinForward;

            var nose = Nose(r.nosePreset);
            nose.Tip = nose.Top + (nose.Tip - nose.Top) * r.noseLength;
            nose.Top.y += r.noseHeight;
            nose.Tip.y += r.noseHeight;
            nose.Radius *= r.noseSize;

            var ear = Ear(r.earPreset);
            ear.Size *= r.earSize;
            ear.Centre.y += r.earHeight;
            ear.Flare += r.earFlare;

            return new FaceShapes { Jaw = jaw, Chin = chin, Nose = nose, Ear = ear };
        }
    }
}
