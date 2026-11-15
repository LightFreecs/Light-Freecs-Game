using UnityEngine;
using UnityEngine.Rendering;

namespace LightMMORPG.Menu
{
    /// <summary>Runtime material instances for the menu scene (so switching skies never touches the material assets).</summary>
    public sealed class MenuMaterials
    {
        public Material sky, cloud, bank, rock, grass, stone, ring, outline, aura, sparkle;

        public static MenuMaterials Create(MenuTheme t)
        {
            Material Copy(Material m, string fallbackShader)
            {
                if (m != null) return new Material(m);
                var s = Shader.Find(fallbackShader);
                return s != null ? new Material(s) : null;
            }
            var mm = new MenuMaterials
            {
                sky = Copy(t.skyMaterial, "LightMMORPG/SkyGradient"),
                cloud = Copy(t.cloudMaterial, "LightMMORPG/ToonCloud"),
                bank = Copy(t.cloudMaterial, "LightMMORPG/ToonCloud"),
                rock = Copy(t.rockMaterial, "LightMMORPG/ToonLit"),
                grass = Copy(t.grassMaterial, "LightMMORPG/ToonLit"),
                stone = Copy(t.stoneMaterial, "LightMMORPG/ToonLit"),
                ring = Copy(t.ringMaterial, "LightMMORPG/ToonLit"),
                aura = Copy(t.auraMaterial, "LightMMORPG/AuraAdditive"),
                sparkle = Copy(t.auraMaterial, "LightMMORPG/AuraAdditive"),
            };
            mm.outline = Copy(t.outlineMaterial, "LightMMORPG/OutlineOnly");
            return mm;
        }
    }

    /// <summary>
    /// Applies a <see cref="SkyPreset"/>: skybox gradient + sun/moon + stars, key light, ambient, fog, cloud bands,
    /// island colours. <see cref="Current"/> is saved in PlayerPrefs (<see cref="PrefKey"/>) by the Settings sky picker.
    /// </summary>
    public sealed class MenuSky : MonoBehaviour
    {
        public const string PrefKey = "menu.sky";

        public static SkyId Saved
        {
            get
            {
                var t = MenuTheme.Load();
                int v = PlayerPrefs.GetInt(PrefKey, (int)t.defaultSky);
                return v is >= 0 and <= 2 ? (SkyId)v : t.defaultSky;
            }
            set { PlayerPrefs.SetInt(PrefKey, (int)value); PlayerPrefs.Save(); }
        }

        public SkyId Current { get; private set; }
        public SkyPreset Preset { get; private set; }
        public event System.Action<SkyPreset> Changed;

        private MenuTheme _theme;
        private MenuMaterials _mats;
        private Light _sun;

        public void Init(MenuTheme theme, MenuMaterials mats, Light sun)
        {
            _theme = theme;
            _mats = mats;
            _sun = sun;
        }

        private static Vector3 Dir(float pitch, float yaw)
        {
            // yaw 0 = straight ahead of the menu camera (+Z), positive = to the right
            return Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
        }

        public void Apply(SkyId id)
        {
            Current = id;
            var p = Preset = _theme.GetSky(id);

            var s = _mats.sky;
            if (s != null)
            {
                s.SetColor("_TopColor", p.skyTop);
                s.SetColor("_UpperColor", p.skyUpper);
                s.SetColor("_HorizonColor", p.skyHorizon);
                s.SetColor("_BelowColor", p.skyBelow);
                s.SetFloat("_UpperPos", p.upperStop);
                s.SetFloat("_HorizonPos", p.horizonStop);
                s.SetFloat("_ElevTop", p.elevTop);
                s.SetFloat("_ElevBottom", p.elevBottom);
                s.SetVector("_SunDir", Dir(p.discPitch, p.discYaw));
                s.SetColor("_SunColor", p.discColor);
                s.SetColor("_HaloColor", p.haloColor);
                s.SetFloat("_SunSize", p.discSize);
                s.SetFloat("_HaloSize", p.haloSize);
                s.SetFloat("_MoonShade", p.moonShade);
                s.SetFloat("_Stars", p.stars);
                RenderSettings.skybox = s;
            }

            if (_sun != null)
            {
                _sun.color = p.lightColor;
                _sun.intensity = p.lightIntensity;
                // light travels FROM the given direction
                _sun.transform.rotation = Quaternion.LookRotation(-Dir(p.lightPitchYaw.x, p.lightPitchYaw.y));
                RenderSettings.sun = _sun;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = p.ambientSky;
            RenderSettings.ambientEquatorColor = p.ambientEquator;
            RenderSettings.ambientGroundColor = p.ambientGround;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(Color.Lerp(p.ambientSky, p.ambientEquator, 0.5f) * 0.8f);
            sh.AddDirectionalLight(Vector3.up, p.ambientSky * 0.35f, 1f);
            RenderSettings.ambientProbe = sh;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = p.fogColor;
            RenderSettings.fogStartDistance = p.fogStart;
            RenderSettings.fogEndDistance = p.fogEnd;

            Vector3 cl = Dir(p.cloudLightPitchYaw.x, p.cloudLightPitchYaw.y);
            SetCloud(_mats.cloud, p.cloudShadow, p.cloudMid, p.cloudLit, p.cloudHighlight, p.cloudRim, cl);
            SetCloud(_mats.bank, p.bankShadow, p.bankMid, p.bankLit, p.bankLit, p.bankRim, cl);

            SetToon(_mats.rock, p.rock, p.rockShade, p.outline, 0f);
            SetToon(_mats.grass, p.grass, p.grassShade, p.outline, 1.6f);
            SetToon(_mats.stone, p.stone, p.stoneShade, p.outline, 1.8f);
            SetToon(_mats.ring, p.ringGlow, Color.white, p.outline, 0f);
            if (_mats.ring != null) _mats.ring.SetColor("_EmissionColor", p.ringGlow);
            if (_mats.outline != null)
            {
                _mats.outline.SetColor("_OutlineColor", p.outline);
                _mats.outline.SetFloat("_OutlineWidth", 1.8f);
            }
            Changed?.Invoke(p);
        }

        private static void SetCloud(Material m, Color sh, Color mid, Color lit, Color hl, Color rim, Vector3 l)
        {
            if (m == null) return;
            m.SetColor("_ShadowColor", sh);
            m.SetColor("_MidColor", mid);
            m.SetColor("_LitColor", lit);
            m.SetColor("_HighlightColor", hl);
            m.SetColor("_RimColor", rim);
            m.SetVector("_LightDir", l);
        }

        private static void SetToon(Material m, Color baseColor, Color shade, Color outline, float outlineWidth)
        {
            if (m == null) return;
            m.SetColor("_BaseColor", baseColor);
            m.SetColor("_ShadeColor", shade);
            m.SetColor("_OutlineColor", outline);
            m.SetFloat("_OutlineWidth", outlineWidth);
        }
    }
}
