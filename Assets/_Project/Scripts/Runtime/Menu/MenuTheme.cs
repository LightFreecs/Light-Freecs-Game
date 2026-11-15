using System;
using UnityEngine;

namespace LightMMORPG.Menu
{
    public enum SkyId { Day = 0, Dusk = 1, Night = 2 }

    /// <summary>One sky look: skybox gradient, sun/moon, stars, key light, ambient, fog, cloud + island colours, UI accents.</summary>
    [Serializable]
    public sealed class SkyPreset
    {
        public string displayName = "Blue Sky";

        [Header("Sky gradient (top → below horizon)")]
        public Color skyTop, skyUpper, skyHorizon, skyBelow;
        [Range(0, 1)] public float upperStop = 0.38f, horizonStop = 0.72f;
        [Tooltip("Elevation (deg) where the gradient starts / ends. The menu camera sees about -19°..+11°.")]
        public float elevTop = 14f, elevBottom = -12f;

        [Header("Sun / moon (yaw: + = right of the menu view, pitch: + = up)")]
        public float discYaw = 18f, discPitch = 8f;
        [ColorUsage(false, true)] public Color discColor = Color.white;
        [ColorUsage(true, true)] public Color haloColor = Color.white;
        public float discSize = 2.4f, haloSize = 20f;
        [Range(0, 1)] public float moonShade;
        [Range(0, 1)] public float stars;

        [Header("Key light")]
        public Color lightColor = Color.white;
        public float lightIntensity = 1.2f;
        public Vector2 lightPitchYaw = new(35f, -140f);
        public Color ambientSky, ambientEquator, ambientGround;

        [Header("Fog")]
        public Color fogColor;
        public float fogStart = 45f, fogEnd = 170f;

        [Header("Clouds (cel bands)")]
        public Color cloudShadow, cloudMid, cloudLit, cloudHighlight;
        [ColorUsage(true, true)] public Color cloudRim;
        public Color bankShadow, bankMid, bankLit;
        [ColorUsage(true, true)] public Color bankRim;
        [Tooltip("Direction the cloud bands are lit from (pitch, yaw), independent of the key light.")]
        public Vector2 cloudLightPitchYaw = new(55f, -150f);

        [Header("Island")]
        public Color rock, rockShade, grass, grassShade, stone, stoneShade;
        [ColorUsage(false, true)] public Color ringGlow;
        public Color outline = new(0.07f, 0.05f, 0.09f);

        [Header("UI accents")]
        public Color accent;
        public Color textShadow;
        public Color titleGlow;
        public Color titleShadow;
        public Color panel;
        public Color panelBorder;
        public Color newsHeader;
        public Color cardSub;
        public Color sunDot;
        public Color sunDotGlow;
        public Color titleText = Color.white;
        [Tooltip("Play Online / Settings dialogs and the toast.")] public Color dialog;
    }

    [Serializable]
    public sealed class MenuItemDef
    {
        public string id;
        public string label;
        public string sub;
        public MenuItemDef(string id, string label, string sub = "") { this.id = id; this.label = label; this.sub = sub; }
    }

    /// <summary>
    /// Every tweakable value of the start menu: fonts, copy, sizes/spacing (in 1920×1080 reference pixels), timing,
    /// camera framing and the three sky presets. Lives at <c>Resources/MenuTheme.asset</c>; BuildAll creates it with
    /// <see cref="ResetToDefaults"/> only when it is missing, so edits in the inspector are kept
    /// (menu "LightMMORPG/Reset Menu Theme" restores the defaults).
    /// </summary>
    [CreateAssetMenu(menuName = "LightMMORPG/Menu Theme", fileName = "MenuTheme")]
    public sealed class MenuTheme : ScriptableObject
    {
        public const string ResourcePath = "MenuTheme";

        [Header("Fonts")]
        public Font titleFont;
        public Font lightFont;
        public Font bodyFont;
        public Font boldFont;

        [Header("Title")]
        public string titleSmall = "PROJECT";
        public string titleBig = "LIGHT";
        public string titleTag = "LIGHTMMORPG";
        public int titleSmallSize = 28, titleBigSize = 132, titleTagSize = 22;
        [Tooltip("Letter spacing in em (like CSS).")] public float titleSmallSpacing = 1f, titleBigSpacing = 0.32f, titleTagSpacing = 0.4f;
        [Tooltip("Top of the title block, fraction of the screen height.")] public float titleTop = 0.05f;
        public float titleTagLineWidth = 100f;
        public string pressAnyKey = "PRESS ANY KEY";
        public string tapToStart = "TAP TO START";
        public int pressSize = 32;
        public float pressSpacing = 0.6f;
        [Tooltip("Fraction of the screen height from the bottom.")] public float pressBottom = 0.10f;

        [Header("Horizon ribbon")]
        public int itemSize = 26;
        public float itemSpacing = 0.28f;
        public int primarySize = 42;
        public float primarySpacing = 0.22f;
        public int primarySubSize = 18;
        public float itemGap = 60f;
        [Tooltip("Fraction of the screen height from the bottom (safe area).")] public float ribbonBottom = 0.06f;
        [Tooltip("Horizontal inset of the line, fraction of the width.")] public float ribbonInset = 0.08f;
        public float lineThickness = 2f;
        public float sunDotSize = 24f;
        public float sunDotRise = 60f;
        public float underlineThickness = 4f;
        [Range(0, 1)] public float idleItemAlpha = 0.9f;
        public float lineAlpha = 0.55f;
        [Tooltip("Soft sky-tinted shade behind the ribbon (fraction of the screen height, alpha).")] public float scrimHeight = 0.3f;
        public float scrimAlpha = 0.6f;

        [Header("Layout (reference px)")]
        [Tooltip("Margin from the safe-area edges, fraction of the width.")] public float edgeMargin = 0.03f;
        public float cornerTop = 0.05f;
        public float newsTop = 0.37f;
        public float newsWidth = 380f;
        public float panelRadius = 32f;
        public float subPanelWidth = 760f;
        public float iconButtonSize = 80f;

        [Header("Copy (placeholders)")]
        public string playerName = "Rookie_07";
        public int playerLevel = 24;
        public string playerRank = "Gold III";
        public string shards = "12,450";
        public string crystals = "320";
        public string newsTitle = "PATCH 0.3";
        public string newsBody = "Charged Beam rebalanced · New arena: Dusk Ruins";
        public string playOnlineBlurb = "Arena brawls for up to 4 fighters.";
        public string comingSoon = "coming soon";
        public MenuItemDef[] ribbonItems =
        {
            new("story", "Story", "Adventure"),
            new("training", "Training"),
            new("play", "Play Online", "Quick Match · Host · Join by IP"),
            new("fighters", "Fighters", "Select · Customize"),
            new("skills", "Abilities", "Skill tree"),
            new("shop", "Shop"),
        };

        [Header("Motion")]
        public float fadeTime = 0.35f;
        public float glowSpeed = 10f;
        public float toastSeconds = 1.8f;
        public float pressBlinkPeriod = 1.4f;

        [Header("Scene framing")]
        public float cameraDistance = 14.8f;
        public float cameraHeight = 2.0f;
        public float cameraFov = 30f;
        public Vector3 lookAt = new(0f, 1.25f, 0f);
        public float driftYaw = 9f;
        public float driftPeriod = 46f;
        public float islandBob = 0.18f;
        public float islandBobPeriod = 6f;
        public float cloudDriftSpeed = 0.6f;

        [Header("Skies")]
        public SkyId defaultSky = SkyId.Day;
        public SkyPreset day = new();
        public SkyPreset dusk = new();
        public SkyPreset night = new();

        [Header("Materials (generated by the builder)")]
        public Material skyMaterial;
        public Material cloudMaterial;
        public Material rockMaterial;
        public Material grassMaterial;
        public Material stoneMaterial;
        public Material ringMaterial;
        public Material auraMaterial;
        public Material outlineMaterial;

        public SkyPreset GetSky(SkyId id) => id switch { SkyId.Dusk => dusk, SkyId.Night => night, _ => day };

        private static MenuTheme _cached;

        public static MenuTheme Load()
        {
            if (_cached != null) return _cached;
            _cached = Resources.Load<MenuTheme>(ResourcePath);
            if (_cached == null)
            {
                _cached = CreateInstance<MenuTheme>();
                _cached.ResetToDefaults();
            }
            return _cached;
        }

        private static Color Hex(string hex, float alpha = 1f)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            c.a = alpha;
            return c;
        }

        private static Color Hdr(string hex, float intensity, float alpha = 1f)
        {
            var c = Hex(hex) * intensity;
            c.a = alpha;
            return c;
        }

        /// <summary>The look picked on the design bench (Dusk Isle + Horizon Ribbon; Blue Sky default, Dusk, Night).</summary>
        public void ResetToDefaults()
        {
            var fresh = CreateInstance<MenuTheme>();
            var fonts = (titleFont, lightFont, bodyFont, boldFont);
            var mats = (skyMaterial, cloudMaterial, rockMaterial, grassMaterial, stoneMaterial, ringMaterial, auraMaterial, outlineMaterial);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(fresh), this);
            DestroyImmediate(fresh);
            (titleFont, lightFont, bodyFont, boldFont) = fonts;
            (skyMaterial, cloudMaterial, rockMaterial, grassMaterial, stoneMaterial, ringMaterial, auraMaterial, outlineMaterial) = mats;

            day = new SkyPreset
            {
                displayName = "Blue Sky",
                skyTop = Hex("#2a72d8"), skyUpper = Hex("#4f9be9"), skyHorizon = Hex("#86c2f5"), skyBelow = Hex("#b7dcfa"),
                discYaw = 17f, discPitch = 9f, discColor = Hdr("#fffef2", 2.2f), haloColor = Hdr("#fff3b0", 1f, 0.75f), discSize = 2.6f, haloSize = 18f,
                lightColor = Hex("#fff4e0"), lightIntensity = 1.35f, lightPitchYaw = new Vector2(38f, -145f),
                ambientSky = Hex("#9cc4ee"), ambientEquator = Hex("#b9cbe0"), ambientGround = Hex("#6d7f99"),
                fogColor = Hex("#a6d0f7"), fogStart = 50f, fogEnd = 190f,
                cloudShadow = Hex("#8fb0dc"), cloudMid = Hex("#c9def5"), cloudLit = Hex("#ffffff"), cloudHighlight = Hex("#ffffff"), cloudRim = Hdr("#fff2c4", 1.05f),
                bankShadow = Hex("#a9c7ea"), bankMid = Hex("#d2e5f8"), bankLit = Hex("#f4f9ff"), bankRim = Hdr("#ffffff", 1.05f),
                cloudLightPitchYaw = new Vector2(60f, -160f),
                rock = Hex("#7d8796"), rockShade = Hex("#7a86a8"), grass = Hex("#6cb04f"), grassShade = Hex("#7f9fb0"),
                stone = Hex("#d3c9b3"), stoneShade = Hex("#a7a8c4"), ringGlow = Hdr("#f2b84b", 2.2f), outline = Hex("#1d2433"),
                accent = Hex("#ffc48a"), textShadow = Hex("#0b2d5a", 0.9f), titleGlow = Hex("#ffffff", 0.55f), titleShadow = Hex("#2a72d8"),
                panel = Hex("#0b2d5a", 0.42f), panelBorder = Hex("#ffffff", 0.45f), newsHeader = Hex("#fff3b0"), cardSub = Hex("#fff6e0"),
                sunDot = Hex("#fff6d0"), sunDotGlow = Hex("#ffb36b"), titleText = Hex("#ffffff"), dialog = Hex("#173f75", 0.94f),
            };
            dusk = new SkyPreset
            {
                displayName = "Dusk",
                skyTop = Hex("#4b52b6"), skyUpper = Hex("#7468cc"), skyHorizon = Hex("#a98bdb"), skyBelow = Hex("#eba7c6"),
                upperStop = 0.34f, horizonStop = 0.64f,
                discYaw = -3f, discPitch = -9f, discColor = Hdr("#fff0f4", 2.0f), haloColor = Hdr("#ffb0c8", 1f, 0.85f), discSize = 4.2f, haloSize = 30f,
                lightColor = Hex("#ffb59a"), lightIntensity = 1.25f, lightPitchYaw = new Vector2(18f, -150f),
                ambientSky = Hex("#7a6cc4"), ambientEquator = Hex("#c58cb4"), ambientGround = Hex("#3c2b5c"),
                fogColor = Hex("#c79bd6"), fogStart = 45f, fogEnd = 170f,
                cloudShadow = Hex("#5a3c9c"), cloudMid = Hex("#7650bd"), cloudLit = Hex("#9670d4"), cloudHighlight = Hex("#b493e6"), cloudRim = Hdr("#ffc6cf", 1.15f),
                bankShadow = Hex("#e98fb4"), bankMid = Hex("#f6adc8"), bankLit = Hex("#ffc6d4"), bankRim = Hdr("#ffe3ea", 1.1f),
                cloudLightPitchYaw = new Vector2(50f, -175f),
                rock = Hex("#6b4a80"), rockShade = Hex("#7d5f9c"), grass = Hex("#7a8f62"), grassShade = Hex("#9a7fae"),
                stone = Hex("#c6a6c2"), stoneShade = Hex("#9a86b8"), ringGlow = Hdr("#ffd08a", 2.4f), outline = Hex("#1c0f2a"),
                accent = Hex("#ffc48a"), textShadow = Hex("#3d2273", 0.8f), titleGlow = Hex("#ffc8a0", 0.6f), titleShadow = Hex("#ff9a5a"),
                panel = Hex("#2a1d4a", 0.45f), panelBorder = Hex("#ffffff", 0.35f), newsHeader = Hex("#ffd08a"), cardSub = Hex("#ffe0c8"),
                sunDot = Hex("#fff6d0"), sunDotGlow = Hex("#ffb36b"), titleText = Hex("#ffe6f0"), dialog = Hex("#2a1d4a", 0.92f),
            };
            night = new SkyPreset
            {
                displayName = "Night",
                skyTop = Hex("#040817"), skyUpper = Hex("#0a1438"), skyHorizon = Hex("#17245a"), skyBelow = Hex("#25366f"),
                upperStop = 0.42f, horizonStop = 0.76f,
                discYaw = 16f, discPitch = 8f, discColor = Hdr("#f6f8ff", 1.6f), haloColor = Hdr("#a9b9ff", 1f, 0.45f), discSize = 2.8f, haloSize = 16f,
                moonShade = 1f, stars = 1f,
                lightColor = Hex("#9fb4ff"), lightIntensity = 0.95f, lightPitchYaw = new Vector2(40f, -150f),
                ambientSky = Hex("#26346e"), ambientEquator = Hex("#1f2a58"), ambientGround = Hex("#0c1230"),
                fogColor = Hex("#1b2a62"), fogStart = 45f, fogEnd = 170f,
                cloudShadow = Hex("#1b2352"), cloudMid = Hex("#2b3672"), cloudLit = Hex("#43529a"), cloudHighlight = Hex("#5a6bb8"), cloudRim = Hdr("#86d6ff", 1.3f),
                bankShadow = Hex("#18214f"), bankMid = Hex("#222e66"), bankLit = Hex("#2c3a78"), bankRim = Hdr("#4d63b0", 1.2f),
                cloudLightPitchYaw = new Vector2(55f, -160f),
                rock = Hex("#3a4470"), rockShade = Hex("#4a5590"), grass = Hex("#2f5a5a"), grassShade = Hex("#4a5a99"),
                stone = Hex("#5a6488"), stoneShade = Hex("#6a74a8"), ringGlow = Hdr("#7fe6ff", 2.8f), outline = Hex("#050816"),
                accent = Hex("#7fe6ff"), textShadow = Hex("#0a1438", 0.85f), titleGlow = Hex("#7fe6ff", 0.6f), titleShadow = Hex("#3a5cff"),
                panel = Hex("#0a1438", 0.45f), panelBorder = Hex("#7fe6ff", 0.4f), newsHeader = Hex("#9fe9ff"), cardSub = Hex("#9fe9ff"),
                sunDot = Hex("#f6f8ff"), sunDotGlow = Hex("#7fe6ff"), titleText = Hex("#cfe0ff"), dialog = Hex("#0a1438", 0.94f),
            };
        }
    }
}
