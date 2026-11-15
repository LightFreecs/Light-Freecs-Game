using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using LightMMORPG.Cameras;
using LightMMORPG.Config;
using LightMMORPG.Input;
using LightMMORPG.Networking;
using LightMMORPG.Player;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LightMMORPG.EditorTools
{
    /// <summary>Session 3: player prefab, Arena_01 scene, build settings, Windows player build.</summary>
    public static partial class ProjectBuilder
    {
        public const string PrefabsDir = ProjectRoot + "/Prefabs";
        public const string PlayerPrefabPath = PrefabsDir + "/Player.prefab";
        public const string ArenaScenePath = ScenesDir + "/Arena_01.unity";
        public const string DefaultPrefabObjectsPath = "Assets/DefaultPrefabObjects.asset";
        public const string SkyMaterialPath = MaterialsDir + "/Sky_Dusk.mat";
        public const float ArenaRadius = 30f;

        // ---------------- prefabs ----------------

        [MenuItem("LightMMORPG/Build Prefabs")]
        public static void BuildPrefabs()
        {
            EnsureFolder(PrefabsDir);
            var config = GameConfig.Load();

            // Player → Graphics (FishNet-smoothed) → Model (animated pivot) → procedural hero generated per palette at runtime
            var root = new GameObject("Player");
            var gfx = new GameObject("Graphics").transform;
            gfx.SetParent(root.transform, false);
            var model = new GameObject("Model").transform;
            model.SetParent(gfx, false);
            var character = model.gameObject.AddComponent<LightMMORPG.Characters.ProceduralCharacter>();
            character.Configure(0, false);
            float heroHeight = LightMMORPG.Characters.CharacterPresets.ForPalette(0).height;

            var cc = root.AddComponent<CharacterController>();
            cc.height = heroHeight;
            cc.radius = 0.45f;
            cc.center = new Vector3(0f, heroHeight * 0.5f, 0f);
            cc.stepOffset = 0.4f;
            cc.skinWidth = 0.05f;
            cc.slopeLimit = 50f;
            cc.minMoveDistance = 0f;

            var nob = root.AddComponent<NetworkObject>();
            root.AddComponent<PlayerInputReader>();
            root.AddComponent<PlayerMotor>();
            var identity = root.AddComponent<PlayerIdentity>();
            var anim = root.AddComponent<PlayerProceduralAnimator>();
            // session 4: combat
            root.AddComponent<LightMMORPG.Combat.PlayerStats>();
            root.AddComponent<LightMMORPG.Combat.PlayerCombat>();
            root.AddComponent<LightMMORPG.VFX.PlayerAura>();

            var so = new SerializedObject(nob);
            SetProp(so, "_enablePrediction", p => p.boolValue = true);
            SetProp(so, "_predictionType", p => p.intValue = 0); // PredictionType.Other
            SetProp(so, "_graphicalObject", p => p.objectReferenceValue = gfx);
            SetProp(so, "_enableTeleport", p => p.boolValue = true);
            SetProp(so, "_teleportThreshold", p => p.floatValue = 5f);
            so.ApplyModifiedPropertiesWithoutUndo();

            var idSo = new SerializedObject(identity);
            SetProp(idSo, "_character", p => p.objectReferenceValue = character);
            idSo.ApplyModifiedPropertiesWithoutUndo();
            var animSo = new SerializedObject(anim);
            SetProp(animSo, "_graphics", p => p.objectReferenceValue = gfx);
            SetProp(animSo, "_model", p => p.objectReferenceValue = model);
            animSo.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.ImportAsset(PlayerPrefabPath, ImportAssetOptions.ForceUpdate);
            RegisterNetworkPrefabs(prefab.GetComponent<NetworkObject>());
            Debug.Log("[ProjectBuilder] BuildPrefabs: wrote " + PlayerPrefabPath);
        }

        /// <summary>Makes sure FishNet's DefaultPrefabObjects contains the given prefab (uses FishNet's internal generator).</summary>
        private static void RegisterNetworkPrefabs(NetworkObject required)
        {
            var genType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("FishNet.Editing.PrefabCollectionGenerator.Generator", false))
                .FirstOrDefault(t => t != null);
            var full = genType?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GenerateFull");
            if (full != null)
            {
                var ps = full.GetParameters();
                var args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                    args[i] = ps[i].ParameterType == typeof(bool) ? (object)true : (ps[i].HasDefaultValue ? ps[i].DefaultValue : null);
                full.Invoke(null, args);
            }
            else Debug.LogWarning("[ProjectBuilder] FishNet Generator.GenerateFull not found; adding prefab manually.");

            var dpo = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(DefaultPrefabObjectsPath);
            if (dpo == null)
            {
                Debug.LogError("[ProjectBuilder] " + DefaultPrefabObjectsPath + " missing");
                return;
            }
            if (!dpo.Prefabs.Contains(required))
            {
                dpo.AddObject(required, true);
                var setHashes = typeof(DefaultPrefabObjects).GetMethod("SetAssetPathHashes", BindingFlags.NonPublic | BindingFlags.Instance);
                setHashes?.Invoke(dpo, new object[] { 0 });
                EditorUtility.SetDirty(dpo);
                AssetDatabase.SaveAssets();
            }
            Debug.Log("[ProjectBuilder] DefaultPrefabObjects: " + dpo.GetObjectCount() + " prefab(s), player registered = " + dpo.Prefabs.Contains(required)
                      + ", hash=" + required.AssetPathHash);
        }

        private static void SetProp(SerializedObject so, string name, Action<SerializedProperty> set)
        {
            var p = so.FindProperty(name);
            if (p == null) throw new Exception("[ProjectBuilder] Serialized property '" + name + "' not found on " + so.targetObject.GetType().Name);
            set(p);
        }

        // ---------------- arena scene ----------------

        [MenuItem("LightMMORPG/Build Arena Scene")]
        public static void BuildArenaScene()
        {
            EnsureFolder(ScenesDir);
            var config = GameConfig.Load();
            var playerPrefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(PlayerPrefabPath);
            if (playerPrefab == null) throw new Exception("[ProjectBuilder] Player prefab missing — run BuildPrefabs first");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- dusk environment ----
            SetupToonEnvironment(new Color(0.45f, 0.38f, 0.62f));
            RenderSettings.skybox = BuildDuskSky();
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.36f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.38f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.13f, 0.2f);
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(new Color(0.36f, 0.31f, 0.46f));
            RenderSettings.ambientProbe = sh;

            var sunGo = new GameObject("Sun (dusk)");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.6f, 0.38f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sunGo.transform.rotation = Quaternion.Euler(16f, -40f, 0f);
            RenderSettings.sun = sun;

            CreateGlobalVolume(config.arenaVolumeProfile);

            // ---- geometry ----
            var arena = new GameObject("Arena").transform;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ground.name = "Ground";
            ground.transform.SetParent(arena, false);
            ground.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(ArenaRadius * 2f + 2f, 0.5f, ArenaRadius * 2f + 2f);
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.AddComponent<MeshCollider>();
            ground.GetComponent<Renderer>().sharedMaterial = config.arenaGroundMaterial;

            // Center ring decal (slightly raised thin disc).
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "CenterCircle";
            ring.transform.SetParent(arena, false);
            ring.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            ring.transform.localScale = new Vector3(8f, 0.01f, 8f);
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.GetComponent<Renderer>().sharedMaterial = config.arenaPropMaterial;

            const int wallSegments = 36;
            float segLen = 2f * Mathf.PI * (ArenaRadius + 0.5f) / wallSegments + 0.3f;
            for (int i = 0; i < wallSegments; i++)
            {
                float a = i * Mathf.PI * 2f / wallSegments;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Wall_" + i;
                wall.transform.SetParent(arena, false);
                float h = (i % 3 == 0) ? 7f : 5f;
                wall.transform.localPosition = dir * (ArenaRadius + 0.5f) + Vector3.up * (h * 0.5f);
                wall.transform.localRotation = Quaternion.LookRotation(dir);
                wall.transform.localScale = new Vector3(segLen, h, 1f);
                wall.GetComponent<Renderer>().sharedMaterial = config.arenaWallMaterial;
                wall.isStatic = true;
            }

            // Invisible boundary so flight cannot leave the arena.
            var bounds = new GameObject("Boundary").transform;
            bounds.SetParent(arena, false);
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2f / 24;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var b = new GameObject("Barrier_" + i);
                b.transform.SetParent(bounds, false);
                b.transform.localPosition = dir * (ArenaRadius + 1f) + Vector3.up * 20f;
                b.transform.localRotation = Quaternion.LookRotation(dir);
                b.AddComponent<BoxCollider>().size = new Vector3(2f * Mathf.PI * (ArenaRadius + 1f) / 24 + 1f, 40f, 1f);
            }
            var ceiling = new GameObject("Ceiling");
            ceiling.transform.SetParent(bounds, false);
            ceiling.transform.localPosition = Vector3.up * 30f;
            ceiling.AddComponent<BoxCollider>().size = new Vector3(ArenaRadius * 2.5f, 1f, ArenaRadius * 2.5f);

            // Pillars (cover) on an inner ring, with glowing crystal caps + coloured point lights.
            var props = new GameObject("Cover").transform;
            props.SetParent(arena, false);
            Color[] glow = { new Color(0.3f, 0.8f, 1f), new Color(1f, 0.45f, 0.9f), new Color(0.5f, 1f, 0.6f) };
            for (int i = 0; i < 6; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / 6;
                var pos = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 14f;
                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = "Pillar_" + i;
                pillar.transform.SetParent(props, false);
                pillar.transform.localPosition = pos + Vector3.up * 4f;
                pillar.transform.localScale = new Vector3(2.4f, 4f, 2.4f);
                pillar.GetComponent<Renderer>().sharedMaterial = config.arenaPropMaterial;
                pillar.isStatic = true;

                var crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crystal.name = "Crystal_" + i;
                crystal.transform.SetParent(pillar.transform, false);
                crystal.transform.localPosition = new Vector3(0f, 1.25f, 0f);
                crystal.transform.localRotation = Quaternion.Euler(45f, 30f * i, 45f);
                crystal.transform.localScale = new Vector3(0.45f, 0.25f, 0.45f);
                Object.DestroyImmediate(crystal.GetComponent<Collider>());
                var cr = crystal.GetComponent<Renderer>();
                cr.sharedMaterial = config.auraMaterial;
                cr.shadowCastingMode = ShadowCastingMode.Off;

                var lightGo = new GameObject("Glow_" + i);
                lightGo.transform.SetParent(props, false);
                lightGo.transform.localPosition = pos + Vector3.up * 9f;
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = glow[i % glow.Length];
                l.intensity = 6f;
                l.range = 14f;
                l.shadows = LightShadows.None;
            }

            // Rocks (seeded, deterministic) between pillars and walls.
            var rng = new System.Random(1234);
            for (int i = 0; i < 10; i++)
            {
                float a = (float)(rng.NextDouble() * Math.PI * 2);
                float r = 7f + (float)rng.NextDouble() * 18f;
                var pos = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                if (Mathf.Abs(new Vector2(pos.x, pos.z).magnitude - 18f) < 2.5f) pos *= 0.82f; // keep spawn ring clear
                var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rock.name = "Rock_" + i;
                rock.transform.SetParent(props, false);
                var scale = new Vector3(1.5f + (float)rng.NextDouble() * 2.5f, 0.8f + (float)rng.NextDouble() * 1.8f, 1.5f + (float)rng.NextDouble() * 2.5f);
                rock.transform.localPosition = pos + Vector3.up * (scale.y * 0.4f);
                rock.transform.localRotation = Quaternion.Euler((float)rng.NextDouble() * 20f - 10f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 20f - 10f);
                rock.transform.localScale = scale;
                rock.GetComponent<Renderer>().sharedMaterial = config.arenaWallMaterial;
                rock.isStatic = true;
            }

            // ---- spawns ----
            var spawnRoot = new GameObject("SpawnPoints").transform;
            var spawns = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + 90f * i) * Mathf.Deg2Rad;
                var s = new GameObject("Spawn_" + i).transform;
                s.SetParent(spawnRoot, false);
                s.position = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 18f + Vector3.up * 0.1f;
                s.rotation = Quaternion.LookRotation(-new Vector3(s.position.x, 0f, s.position.z));
                spawns[i] = s;
            }

            // ---- camera ----
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<UnityEngine.Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 400f;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camGo.transform.position = new Vector3(0f, 14f, -34f);
            camGo.transform.rotation = Quaternion.Euler(18f, 0f, 0f);
            camGo.AddComponent<ThirdPersonCamera>();

            // ---- networking ----
            var nmGo = new GameObject("NetworkManager");
            var nm = nmGo.AddComponent<NetworkManager>();
            nm.SpawnablePrefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(DefaultPrefabObjectsPath);
            var tug = nmGo.AddComponent<Tugboat>();
            var tugSo = new SerializedObject(tug);
            SetProp(tugSo, "_port", p => p.intValue = NetworkBootstrap.DefaultPort);
            tugSo.ApplyModifiedPropertiesWithoutUndo();
            var spawner = nmGo.AddComponent<PlayerSpawner>();
            spawner.SetPlayerPrefab(playerPrefab);
            spawner.Spawns = spawns;
            var spSo = new SerializedObject(spawner);
            SetProp(spSo, "_playerPrefab", p => p.objectReferenceValue = playerPrefab);
            spSo.ApplyModifiedPropertiesWithoutUndo();
            nmGo.AddComponent<NetworkBootstrap>();

            var menuGo = new GameObject("ConnectMenu");
            menuGo.AddComponent<ConnectMenu>();

            var hudGo = new GameObject("HUD");
            hudGo.AddComponent<LightMMORPG.UI.GameHud>();
            hudGo.AddComponent<LightMMORPG.UI.TouchControls>();

            EditorSceneManager.MarkSceneDirty(scene);
            SaveSceneStable(scene, ArenaScenePath);
            Debug.Log("[ProjectBuilder] BuildArenaScene: wrote " + ArenaScenePath + " (spawnable prefabs: " + (nm.SpawnablePrefabs != null ? nm.SpawnablePrefabs.GetObjectCount() : -1) + ")");
        }

        private static Material BuildDuskSky()
        {
            var shader = Shader.Find("Skybox/Procedural");
            var mat = LoadOrCreateMaterial("Sky_Dusk", shader);
            mat.SetFloat("_SunDisk", 2f);
            mat.SetFloat("_SunSize", 0.05f);
            mat.SetFloat("_SunSizeConvergence", 4f);
            mat.SetFloat("_AtmosphereThickness", 1.6f);
            mat.SetColor("_SkyTint", new Color(0.75f, 0.42f, 0.6f));
            mat.SetColor("_GroundColor", new Color(0.12f, 0.1f, 0.16f));
            mat.SetFloat("_Exposure", 0.75f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------------- build settings / player build ----------------

        [MenuItem("LightMMORPG/Configure Build Settings")]
        public static void ConfigureBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ArenaScenePath && File.Exists(s.path)).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ArenaScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;
            Debug.Log("[ProjectBuilder] ConfigureBuildSettings: " + string.Join(", ", EditorBuildSettings.scenes.Select(s => s.path)));
        }

        /// <summary>Batch: -buildTarget Win64 -executeMethod LightMMORPG.EditorTools.ProjectBuilder.BuildWindowsPlayer → Builds/Windows/LightMMORPG.exe</summary>
        [MenuItem("LightMMORPG/Build Windows Player")]
        public static void BuildWindowsPlayer()
        {
            ConfigureBuildSettings();
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = "Builds/Windows/LightMMORPG.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log("[ProjectBuilder] BuildWindowsPlayer: " + report.summary.result + ", " + report.summary.totalSize / (1024 * 1024) + " MB, errors " + report.summary.totalErrors);
            if (report.summary.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
