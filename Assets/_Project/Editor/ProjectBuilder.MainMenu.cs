using System.IO;
using LightMMORPG.Config;
using LightMMORPG.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace LightMMORPG.EditorTools
{
    /// <summary>
    /// Start menu ("Dusk Isle" + Horizon Ribbon from the design bench): menu materials, <c>Resources/MenuTheme.asset</c>
    /// (created with defaults only when missing, so inspector tweaks survive BuildAll), the menu post-processing profile
    /// and <c>Scenes/MainMenu.unity</c>. The island, clouds, fighter and UI are built at runtime by
    /// <see cref="MainMenuScene"/> from the theme.
    /// </summary>
    public static partial class ProjectBuilder
    {
        public const string MainMenuScenePath = ScenesDir + "/MainMenu.unity";
        public const string MenuThemePath = ResourcesDir + "/MenuTheme.asset";
        public const string MenuVolumeProfilePath = ArtDir + "/MenuVolumeProfile.asset";
        public const string FontsDir = ArtDir + "/Fonts";

        [MenuItem("LightMMORPG/Build Main Menu Scene")]
        public static void BuildMainMenuScene()
        {
            EnsureFolder(ScenesDir);
            EnsureFolder(MaterialsDir);
            var theme = BuildMenuTheme();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var day = theme.GetSky(theme.defaultSky);
            RenderSettings.skybox = theme.skyMaterial;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = day.ambientSky;
            RenderSettings.ambientEquatorColor = day.ambientEquator;
            RenderSettings.ambientGroundColor = day.ambientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = day.fogColor;
            RenderSettings.fogStartDistance = day.fogStart;
            RenderSettings.fogEndDistance = day.fogEnd;

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = day.lightColor;
            sun.intensity = day.lightIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;
            sunGo.transform.rotation = Quaternion.Euler(day.lightPitchYaw.x, day.lightPitchYaw.y + 180f, 0f);
            RenderSettings.sun = sun;

            CreateGlobalVolume(BuildMenuVolumeProfile());

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = theme.cameraFov;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 400f;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            camGo.transform.position = theme.lookAt + new Vector3(0f, theme.cameraHeight, -theme.cameraDistance);
            camGo.transform.LookAt(theme.lookAt);

            var menuGo = new GameObject("MainMenu");
            var menu = menuGo.AddComponent<MainMenuScene>();
            var so = new SerializedObject(menu);
            SetProp(so, "_camera", p => p.objectReferenceValue = cam);
            SetProp(so, "_sun", p => p.objectReferenceValue = sun);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            SaveSceneStable(scene, MainMenuScenePath);
            Debug.Log("[ProjectBuilder] BuildMainMenuScene: wrote " + MainMenuScenePath);
        }

        [MenuItem("LightMMORPG/Reset Menu Theme")]
        public static void ResetMenuTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<MenuTheme>(MenuThemePath);
            if (theme == null) { BuildMenuTheme(); return; }
            theme.ResetToDefaults();
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectBuilder] ResetMenuTheme: defaults restored in " + MenuThemePath);
        }

        private static MenuTheme BuildMenuTheme()
        {
            EnsureFolder(ResourcesDir);
            bool isNew = AssetDatabase.LoadAssetAtPath<MenuTheme>(MenuThemePath) == null;
            var theme = CreateOrLoad<MenuTheme>(MenuThemePath);
            if (isNew || GetArg("-resetMenuTheme") != null) theme.ResetToDefaults();

            // references are always (re)assigned; values are only written on creation
            theme.titleFont = LoadFont("Roboto-Black.ttf");
            theme.lightFont = LoadFont("Roboto-Light.ttf");
            theme.bodyFont = LoadFont("Roboto-Medium.ttf");
            theme.boldFont = LoadFont("Roboto-Bold.ttf");

            var toon = Shader.Find(ToonShaderName);
            var day = theme.GetSky(theme.defaultSky);
            theme.skyMaterial = LoadOrCreateMaterial("Menu_Sky", Shader.Find("LightMMORPG/SkyGradient"));
            theme.cloudMaterial = LoadOrCreateMaterial("Menu_Cloud", Shader.Find("LightMMORPG/ToonCloud"));
            theme.rockMaterial = ToonMaterial("Menu_Rock", toon, day.rock, day.rockShade, outline: 0f, rim: 0.25f);
            theme.grassMaterial = ToonMaterial("Menu_Grass", toon, day.grass, day.grassShade, outline: 1.6f, rim: 0.3f);
            theme.stoneMaterial = ToonMaterial("Menu_Stone", toon, day.stone, day.stoneShade, outline: 1.8f, rim: 0.3f);
            theme.ringMaterial = ToonMaterial("Menu_Ring", toon, day.ringGlow, Color.white, outline: 0f, rim: 0f);
            theme.ringMaterial.SetColor("_EmissionColor", day.ringGlow);
            theme.outlineMaterial = LoadOrCreateMaterial("Menu_Outline", Shader.Find("LightMMORPG/OutlineOnly"));
            theme.outlineMaterial.SetFloat("_OutlineWidth", 1.8f);
            theme.auraMaterial = GameConfig.Load() != null ? GameConfig.Load().auraMaterial : null;
            foreach (var m in new[] { theme.skyMaterial, theme.cloudMaterial, theme.outlineMaterial, theme.ringMaterial }) EditorUtility.SetDirty(m);
            foreach (var s in new[] { "LightMMORPG/SkyGradient", "LightMMORPG/ToonCloud", "LightMMORPG/OutlineOnly" })
            {
                var sh = Shader.Find(s);
                if (sh == null) Debug.LogError("[ProjectBuilder] Shader missing: " + s);
                else ReportShaderErrors(sh);
            }
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            return theme;
        }

        private static Font LoadFont(string file)
        {
            var f = AssetDatabase.LoadAssetAtPath<Font>(FontsDir + "/" + file);
            if (f == null) Debug.LogError("[ProjectBuilder] Font missing: " + FontsDir + "/" + file);
            return f;
        }

        private static VolumeProfile BuildMenuVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(MenuVolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = Path.GetFileNameWithoutExtension(MenuVolumeProfilePath);
                AssetDatabase.CreateAsset(profile, MenuVolumeProfilePath);
            }
            var keep = new[] { typeof(Bloom), typeof(ColorAdjustments), typeof(Tonemapping) };
            foreach (var c in profile.components.ToArray())
            {
                if (c != null && System.Array.IndexOf(keep, c.GetType()) >= 0) continue;
                profile.components.Remove(c);
                if (c != null) Object.DestroyImmediate(c, true);
            }
            // Tonemapping explicitly off (URP's default profile turns it on): the UI is drawn by this camera too,
            // and its colours must stay exactly as themed.
            var tonemap = GetOrAdd<Tonemapping>(profile);
            tonemap.mode.Override(TonemappingMode.None);
            var bloom = GetOrAdd<Bloom>(profile);
            bloom.threshold.Override(1.25f);
            bloom.intensity.Override(0.7f);
            bloom.scatter.Override(0.7f);
            bloom.highQualityFiltering.Override(false);
            var color = GetOrAdd<ColorAdjustments>(profile);
            color.saturation.Override(6f);
            color.contrast.Override(0f);
            foreach (var c in profile.components)
            {
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                if (!AssetDatabase.Contains(c)) AssetDatabase.AddObjectToAsset(c, profile);
                EditorUtility.SetDirty(c);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
