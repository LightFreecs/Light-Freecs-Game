using System;
using System.IO;
using LightMMORPG.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace LightMMORPG.EditorTools
{
    /// <summary>Session 2: toon materials, arena post-processing profile, look-test scene + headless screenshot.</summary>
    public static partial class ProjectBuilder
    {
        public const string ArtDir = ProjectRoot + "/Art";
        public const string MaterialsDir = ArtDir + "/Materials";
        public const string ScenesDir = ProjectRoot + "/Scenes";
        public const string ArenaVolumeProfilePath = ArtDir + "/ArenaVolumeProfile.asset";
        public const string LookTestScenePath = ScenesDir + "/LookTest.unity";
        public const string ToonShaderName = "LightMMORPG/ToonLit";
        public const string AuraShaderName = "LightMMORPG/AuraAdditive";

        private static readonly (string name, Color baseColor, Color shade)[] FighterPalettes =
        {
            ("Fighter_Orange", new Color(1.00f, 0.52f, 0.10f), new Color(0.80f, 0.50f, 0.62f)),
            ("Fighter_Blue",   new Color(0.16f, 0.42f, 1.00f), new Color(0.58f, 0.55f, 0.85f)),
            ("Fighter_Green",  new Color(0.22f, 0.80f, 0.32f), new Color(0.55f, 0.62f, 0.80f)),
            ("Fighter_Purple", new Color(0.62f, 0.26f, 0.95f), new Color(0.65f, 0.50f, 0.85f)),
        };

        [MenuItem("LightMMORPG/Build Art")]
        public static void BuildArt()
        {
            EnsureFolder(MaterialsDir);
            var toon = Shader.Find(ToonShaderName);
            var aura = Shader.Find(AuraShaderName);
            if (toon == null || aura == null)
            {
                Debug.LogError("[ProjectBuilder] BuildArt: shaders not found (" + ToonShaderName + ", " + AuraShaderName + ")");
                return;
            }
            ReportShaderErrors(toon);
            ReportShaderErrors(aura);

            var config = CreateOrLoad<GameConfig>(GameConfigPath);

            // --- fighters
            var palettes = new Material[FighterPalettes.Length];
            for (int i = 0; i < FighterPalettes.Length; i++)
            {
                var p = FighterPalettes[i];
                palettes[i] = ToonMaterial(p.name, toon, p.baseColor, p.shade);
            }
            var skin = ToonMaterial("Fighter_Skin", toon, new Color(1.00f, 0.83f, 0.70f), new Color(0.88f, 0.62f, 0.62f));
            var accent = ToonMaterial("Fighter_Accent", toon, new Color(0.13f, 0.13f, 0.20f), new Color(0.70f, 0.70f, 0.85f));

            // --- arena (thin/no outline on big flat surfaces)
            var ground = ToonMaterial("Arena_Ground", toon, new Color(0.78f, 0.70f, 0.54f), new Color(0.66f, 0.60f, 0.80f), outline: 0f, rim: 0f);
            var wall = ToonMaterial("Arena_Wall", toon, new Color(0.80f, 0.74f, 0.64f), new Color(0.62f, 0.58f, 0.78f), outline: 1.2f, rim: 0.15f);
            var pillar = ToonMaterial("Arena_Pillar", toon, new Color(0.90f, 0.86f, 0.76f), new Color(0.60f, 0.58f, 0.80f), outline: 1.6f, rim: 0.3f);

            // --- glow (HDR colors > 1 so Bloom picks them up)
            var auraMat = GlowMaterial("FX_Aura", aura, new Color(1.8f, 1.1f, 0.15f), intensity: 1.4f, fresnelPower: 2.0f, fresnelStrength: 1.4f, core: 0.04f, noise: 0.6f, noiseScale: 3f, scroll: new Vector4(0, 2.0f, 0, 0));
            var beam = GlowMaterial("FX_Beam", aura, new Color(0.25f, 0.85f, 2.8f), intensity: 1.8f, fresnelPower: 1.2f, fresnelStrength: 1.0f, core: 0.7f, noise: 0.25f, noiseScale: 2f, scroll: new Vector4(0, 6f, 0, 0));
            var blast = GlowMaterial("FX_Blast", aura, new Color(2.4f, 1.0f, 0.12f), intensity: 2.2f, fresnelPower: 1.5f, fresnelStrength: 1.0f, core: 0.9f, noise: 0.3f, noiseScale: 4f, scroll: new Vector4(0, 3f, 0, 0));
            var impact = GlowMaterial("FX_Impact", aura, new Color(2.2f, 1.8f, 1.1f), intensity: 1.8f, fresnelPower: 1.0f, fresnelStrength: 1.0f, core: 0.6f, noise: 0.4f, noiseScale: 5f, scroll: new Vector4(0, 0, 0, 0));

            config.fighterPaletteMaterials = palettes;
            config.fighterBodyMaterial = palettes[0];
            config.fighterSkinMaterial = skin;
            config.fighterAccentMaterial = accent;
            config.arenaGroundMaterial = ground;
            config.arenaWallMaterial = wall;
            config.arenaPropMaterial = pillar;
            config.auraMaterial = auraMat;
            config.beamMaterial = beam;
            config.projectileMaterial = blast;
            config.impactMaterial = impact;
            config.arenaVolumeProfile = BuildArenaVolumeProfile();
            EditorUtility.SetDirty(config);

            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectBuilder] BuildArt: materials in " + MaterialsDir + ", profile " + ArenaVolumeProfilePath);
        }

        private static void ReportShaderErrors(Shader shader)
        {
            var messages = ShaderUtil.GetShaderMessages(shader);
            foreach (var m in messages)
            {
                string text = "[ProjectBuilder] " + shader.name + " (" + m.platform + ") line " + m.line + ": " + m.message;
                if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) Debug.LogError(text);
                else Debug.LogWarning(text);
            }
            if (ShaderUtil.ShaderHasError(shader)) Debug.LogError("[ProjectBuilder] Shader error in " + shader.name);
        }

        private static Material LoadOrCreateMaterial(string name, Shader shader)
        {
            string path = MaterialsDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            return mat;
        }

        private static Material ToonMaterial(string name, Shader shader, Color baseColor, Color shade, float outline = 3.2f, float rim = 0.6f)
        {
            var m = LoadOrCreateMaterial(name, shader);
            m.SetColor("_BaseColor", baseColor);
            m.SetColor("_ShadeColor", shade);
            m.SetFloat("_RampThreshold", 0.05f);
            m.SetFloat("_RampSmooth", 0.02f);
            m.SetFloat("_Band2Threshold", -0.45f);
            m.SetColor("_Band2Color", new Color(0.80f, 0.76f, 0.90f));
            m.SetFloat("_AmbientStrength", 0.4f);
            m.SetColor("_RimColor", new Color(1f, 0.97f, 0.9f));
            m.SetFloat("_RimPower", 4.5f);
            m.SetFloat("_RimStrength", rim);
            m.SetColor("_EmissionColor", Color.black);
            m.SetColor("_OutlineColor", new Color(0.07f, 0.05f, 0.09f));
            m.SetFloat("_OutlineWidth", outline);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material GlowMaterial(string name, Shader shader, Color hdr, float intensity, float fresnelPower, float fresnelStrength,
            float core, float noise, float noiseScale, Vector4 scroll)
        {
            var m = LoadOrCreateMaterial(name, shader);
            m.SetColor("_Color", hdr);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_FresnelPower", fresnelPower);
            m.SetFloat("_FresnelStrength", fresnelStrength);
            m.SetFloat("_CoreStrength", core);
            m.SetFloat("_NoiseStrength", noise);
            m.SetFloat("_NoiseScale", noiseScale);
            m.SetVector("_NoiseScroll", scroll);
            m.renderQueue = (int)RenderQueue.Transparent;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static VolumeProfile BuildArenaVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ArenaVolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = Path.GetFileNameWithoutExtension(ArenaVolumeProfilePath);
                AssetDatabase.CreateAsset(profile, ArenaVolumeProfilePath);
            }
            // Idempotent: reuse existing override sub-assets (stable fileIDs -> no git churn), drop anything else.
            var keep = new[] { typeof(Bloom), typeof(Tonemapping), typeof(ColorAdjustments) };
            foreach (var c in profile.components.ToArray())
            {
                if (c != null && System.Array.IndexOf(keep, c.GetType()) >= 0) continue;
                profile.components.Remove(c);
                if (c != null) Object.DestroyImmediate(c, true);
            }

            var bloom = GetOrAdd<Bloom>(profile);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(1.2f);
            bloom.scatter.Override(0.65f);
            bloom.highQualityFiltering.Override(false); // mobile-friendly

            var tonemap = GetOrAdd<Tonemapping>(profile);
            tonemap.mode.Override(TonemappingMode.Neutral); // Neutral keeps saturated anime colors (ACES shifts hues)

            var color = GetOrAdd<ColorAdjustments>(profile);
            color.saturation.Override(12f);
            color.contrast.Override(6f);

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

        private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var existing)) { existing.active = true; return existing; }
            return profile.Add<T>(true);
        }

        // ---------------------------------------------------------------- look test

        /// <summary>Dev scene for judging the toon look: light, ground, props, 4 placeholder fighters, aura, glow FX, global volume.</summary>
        [MenuItem("LightMMORPG/Build Look Test Scene")]
        public static void BuildLookTestScene()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(GameConfigPath);
            if (config == null || config.auraMaterial == null) { BuildArt(); config = AssetDatabase.LoadAssetAtPath<GameConfig>(GameConfigPath); }
            EnsureFolder(ScenesDir);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupToonEnvironment(new Color(0.45f, 0.66f, 0.96f));

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1f;
            sunGo.transform.rotation = Quaternion.Euler(38f, 58f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            ground.GetComponent<Renderer>().sharedMaterial = config.arenaGroundMaterial;

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall";
            wall.transform.position = new Vector3(0f, 1.5f, 8f);
            wall.transform.localScale = new Vector3(16f, 3f, 0.8f);
            wall.GetComponent<Renderer>().sharedMaterial = config.arenaWallMaterial;

            foreach (float x in new[] { -6f, 6f })
            {
                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = "Pillar";
                pillar.transform.position = new Vector3(x, 2.5f, 4.5f);
                pillar.transform.localScale = new Vector3(1.1f, 2.5f, 1.1f);
                pillar.GetComponent<Renderer>().sharedMaterial = config.arenaPropMaterial;
            }

            float[] xs = { -3.3f, -1.1f, 1.1f, 3.3f };
            for (int i = 0; i < xs.Length; i++)
            {
                var f = CreatePlaceholderFighter("Fighter_" + i, config, i);
                f.transform.position = new Vector3(xs[i], 0f, 0f);
                f.transform.rotation = Quaternion.Euler(0f, 180f + (i - 1.5f) * 12f, 0f);
                foreach (var c in f.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                if (i == 0) AddAuraShell(f.transform, config.auraMaterial);
            }

            // glow samples: a beam crossing the back, an energy blast, an impact flash
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "BeamSample";
            beam.transform.position = new Vector3(0f, 3.4f, 3.5f);
            beam.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            beam.transform.localScale = new Vector3(0.45f, 5f, 0.45f);
            MakeGlow(beam, config.beamMaterial);

            var blast = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            blast.name = "BlastSample";
            blast.transform.position = new Vector3(2.2f, 1.6f, -1.2f);
            blast.transform.localScale = Vector3.one * 0.55f;
            MakeGlow(blast, config.projectileMaterial);

            var impact = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            impact.name = "ImpactSample";
            impact.transform.position = new Vector3(-2.2f, 1.4f, 1.5f);
            impact.transform.localScale = Vector3.one * 0.8f;
            MakeGlow(impact, config.impactMaterial);

            CreateGlobalVolume(config.arenaVolumeProfile);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.66f, 0.96f);
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.allowHDR = true;
            camGo.transform.position = new Vector3(0f, 2.3f, -8f);
            camGo.transform.LookAt(new Vector3(0f, 1.35f, 0f));
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            camGo.AddComponent<AudioListener>();

            SaveSceneStable(scene, LookTestScenePath);
            Debug.Log("[ProjectBuilder] BuildLookTestScene: wrote " + LookTestScenePath);
        }

        /// <summary>Flat-ish trilight ambient (feeds SH), no skybox, no fog. Shared with the arena builder.</summary>
        public static void SetupToonEnvironment(Color sky)
        {
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky * 0.9f;
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.52f, 0.58f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.28f, 0.32f);
            // Set the SH probe directly so it is valid without a lighting bake (batch mode).
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(new Color(0.50f, 0.52f, 0.60f));
            RenderSettings.ambientProbe = sh;
        }

        public static GameObject CreateGlobalVolume(VolumeProfile profile)
        {
            var go = new GameObject("Global Volume");
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 0f;
            v.sharedProfile = profile;
            return go;
        }

        /// <summary>Capsule body + sphere head + belt, in the given player palette. Root at the feet, height ~2.5 m.</summary>
        public static GameObject CreatePlaceholderFighter(string name, GameConfig config, int playerIndex)
        {
            var root = new GameObject(name);
            var gfx = new GameObject("Graphics");
            gfx.transform.SetParent(root.transform, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(gfx.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            body.transform.localScale = new Vector3(0.9f, 1f, 0.9f);
            body.GetComponent<Renderer>().sharedMaterial = config.GetFighterMaterial(playerIndex);

            var belt = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            belt.name = "Belt";
            belt.transform.SetParent(gfx.transform, false);
            belt.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            belt.transform.localScale = new Vector3(0.95f, 0.08f, 0.95f);
            belt.GetComponent<Renderer>().sharedMaterial = config.fighterAccentMaterial;

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(gfx.transform, false);
            head.transform.localPosition = new Vector3(0f, 2.28f, 0f);
            head.transform.localScale = Vector3.one * 0.62f;
            head.GetComponent<Renderer>().sharedMaterial = config.fighterSkinMaterial;
            return root;
        }

        public static GameObject AddAuraShell(Transform fighter, Material auraMaterial)
        {
            var shell = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            shell.name = "Aura";
            shell.transform.SetParent(fighter, false);
            shell.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            shell.transform.localScale = new Vector3(1.45f, 1.4f, 1.45f);
            MakeGlow(shell, auraMaterial);
            return shell;
        }

        private static void MakeGlow(GameObject go, Material mat)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        /// <summary>
        /// Batch: -executeMethod LightMMORPG.EditorTools.ProjectBuilder.CaptureLookTest -lookTestOut &lt;dir&gt;
        /// (no -nographics). Builds the look-test scene and renders LookTest_PC.png + LookTest_Mobile.png (one per quality level).
        /// </summary>
        public static void CaptureLookTest()
        {
            string outDir = GetArg("-lookTestOut") ?? Path.GetFullPath("Temp/LookTest");
            Directory.CreateDirectory(outDir);
            BuildArt();
            BuildLookTestScene();
            ShaderUtil.allowAsyncCompilation = false;

            var cam = Camera.main;
            int original = QualitySettings.GetQualityLevel();
            string[] names = QualitySettings.names;
            for (int q = 0; q < names.Length; q++)
            {
                QualitySettings.SetQualityLevel(q, true);
                var withPost = Render(cam, Path.Combine(outDir, "LookTest_" + names[q] + ".png"));
                // sanity check that the global Volume (bloom/tonemap) actually changes the image
                var volume = Object.FindFirstObjectByType<Volume>();
                volume.enabled = false;
                var noPost = Render(cam, null);
                volume.enabled = true;
                double diff = 0;
                for (int i = 0; i < withPost.Length; i++) diff += Math.Abs(withPost[i].r - noPost[i].r) + Math.Abs(withPost[i].g - noPost[i].g) + Math.Abs(withPost[i].b - noPost[i].b);
                Debug.Log("[ProjectBuilder] CaptureLookTest " + names[q] + ": mean |volume on - off| = " + (diff / (withPost.Length * 3.0)).ToString("F2") + " (0-255)");
            }
            QualitySettings.SetQualityLevel(original, true);
        }

        private static Color32[] Render(Camera cam, string file)
        {
            const int w = 1280, h = 720;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            rt.Create();
            for (int i = 0; i < 3; i++) // warm-up frames (shader compile, volume/bloom history)
            {
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var pixels = tex.GetPixels32();
            if (file != null)
            {
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Debug.Log("[ProjectBuilder] CaptureLookTest: wrote " + file + " (" + GraphicsSettings.currentRenderPipeline?.name + ")");
            }
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            return pixels;
        }

        private static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
