using System.Collections.Generic;
using System.IO;
using LightMMORPG.Characters;
using LightMMORPG.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace LightMMORPG.EditorTools
{
    /// <summary>Procedural characters: showcase scene (4 presets, dusk light) + headless capture of full-body/face shots.</summary>
    public static partial class ProjectBuilder
    {
        public const string CharacterShowcaseScenePath = ScenesDir + "/CharacterShowcase.unity";
        private static readonly float[] ShowcaseX = { 2.55f, 0.85f, -0.85f, -2.55f };

        [MenuItem("LightMMORPG/Build Character Showcase")]
        public static void BuildCharacterShowcase()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(GameConfigPath);
            if (config == null || config.arenaVolumeProfile == null) { BuildArt(); config = AssetDatabase.LoadAssetAtPath<GameConfig>(GameConfigPath); }
            EnsureFolder(ScenesDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            SetupToonEnvironment(new Color(0.45f, 0.38f, 0.62f));
            RenderSettings.skybox = BuildDuskSky();
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.36f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.38f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.13f, 0.2f);
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(new Color(0.4f, 0.35f, 0.5f));
            RenderSettings.ambientProbe = sh;

            var sunGo = new GameObject("Sun (dusk)");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.72f, 0.52f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            sunGo.transform.rotation = Quaternion.Euler(28f, 150f, 0f);

            var rimGo = new GameObject("Rim light");
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(0.55f, 0.5f, 1f);
            rim.intensity = 0.55f;
            rim.shadows = LightShadows.None;
            rimGo.transform.rotation = Quaternion.Euler(20f, -20f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ground.name = "Stage";
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.transform.position = new Vector3(0f, -0.05f, 0f);
            ground.transform.localScale = new Vector3(12f, 0.05f, 7f);
            ground.GetComponent<Renderer>().sharedMaterial = config.arenaGroundMaterial;

            for (int i = 0; i < CharacterPresets.Count; i++)
            {
                var go = new GameObject("Hero_" + i + "_" + CharacterPresets.ForPalette(i).displayName);
                go.transform.position = new Vector3(ShowcaseX[i], 0f, 0f);
                go.transform.rotation = Quaternion.Euler(0f, (i - 1.5f) * -8f, 0f);
                var pc = go.AddComponent<ProceduralCharacter>();
                pc.Configure(i, true);
                if (pc.Rig == null) pc.GenerateForPalette(i);
            }

            CreateGlobalVolume(config.arenaVolumeProfile);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.allowHDR = true;
            camGo.transform.position = new Vector3(0f, 1.55f, 9.6f);
            camGo.transform.LookAt(new Vector3(0f, 1.2f, 0f));
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            SaveSceneStable(scene, CharacterShowcaseScenePath);
            Debug.Log("[ProjectBuilder] BuildCharacterShowcase: wrote " + CharacterShowcaseScenePath);
        }

        /// <summary>
        /// Batch (no -nographics): -executeMethod LightMMORPG.EditorTools.ProjectBuilder.CaptureCharacterShowcase -showcaseOut &lt;dir&gt;
        /// Writes Showcase_Full.png (lineup), Showcase_Faces.png (2x2 face close-ups), Showcase_Hero.png (3/4 views).
        /// </summary>
        public static void CaptureCharacterShowcase()
        {
            string outDir = GetArg("-showcaseOut") ?? Path.GetFullPath("Temp/Showcase");
            Directory.CreateDirectory(outDir);
            ProceduralCharacterBuilder.ClearCache();
            SdfMesher.LogStats = GetArg("-partStats") != null;
            for (int pass = 0; pass < 2; pass++) // pass 1 = warm (JIT done)
                for (int i = 0; i < CharacterPresets.Count; i++)
                {
                    var gen = ProceduralCharacterBuilder.Generate(CharacterPresets.ForPalette(i));
                    Debug.Log("[ProjectBuilder] Character " + i + " " + gen.Name + " pass " + pass + ": " + gen.VertexCount + " verts, "
                              + (gen.Mesh.triangles.Length / 3) + " tris, " + gen.Slots.Count + " slots, data " + gen.Milliseconds.ToString("F1")
                              + " ms, upload " + gen.UploadMilliseconds.ToString("F1") + " ms");
                    Object.DestroyImmediate(gen.Mesh);
                    Object.DestroyImmediate(gen.Face);
                    SdfMesher.LogStats = false;
                }
            SdfMesher.LogStats = false;
            BuildCharacterShowcase();
            ShaderUtil.allowAsyncCompilation = false;
            var cam = Camera.main;
            var heroes = Object.FindObjectsByType<ProceduralCharacter>(FindObjectsSortMode.None);
            System.Array.Sort(heroes, (a, b) => b.transform.position.x.CompareTo(a.transform.position.x));

            SavePng(RenderCam(cam, 1920, 1080), Path.Combine(outDir, "Showcase_Full.png"));

            // faces 2x2
            const int fs = 640;
            var faces = new Texture2D(fs * 2, fs * 2, TextureFormat.RGB24, false);
            var orig = cam.transform.position;
            var origRot = cam.transform.rotation;
            float fov = cam.fieldOfView;
            for (int i = 0; i < heroes.Length && i < 4; i++)
            {
                var head = heroes[i].Rig[CharacterBone.Head];
                Vector3 target = head.position + Vector3.up * 0.1f * heroes[i].Rig.Height / 2.3f;
                Vector3 fwd = heroes[i].transform.forward;
                cam.transform.position = target + fwd * 0.85f + heroes[i].transform.right * 0.18f + Vector3.up * 0.03f;
                cam.transform.LookAt(target);
                cam.fieldOfView = 30f;
                var t = RenderCam(cam, fs, fs);
                faces.SetPixels((i % 2) * fs, (1 - i / 2) * fs, fs, fs, t.GetPixels());
                Object.DestroyImmediate(t);
            }
            faces.Apply();
            SavePng(faces, Path.Combine(outDir, "Showcase_Faces.png"));

            // side profile close-ups (face looking left), characters 0..3 side by side
            var side = new Texture2D(fs * 2, fs * 2, TextureFormat.RGB24, false);
            for (int i = 0; i < heroes.Length && i < 4; i++)
            {
                var head = heroes[i].Rig[CharacterBone.Head];
                Vector3 target = head.position + Vector3.down * 0.03f * heroes[i].Rig.Height / 2.3f;
                cam.transform.position = target - heroes[i].transform.right * 1.5f;
                cam.transform.LookAt(target);
                cam.fieldOfView = 30f;
                var t = RenderCam(cam, fs, fs);
                side.SetPixels((i % 2) * fs, (1 - i / 2) * fs, fs, fs, t.GetPixels());
                Object.DestroyImmediate(t);
            }
            side.Apply();
            SavePng(side, Path.Combine(outDir, "Showcase_Side.png"));

            // 3/4 hero shots
            const int hw = 480, hh = 860;
            var hero = new Texture2D(hw * 4, hh, TextureFormat.RGB24, false);
            for (int i = 0; i < heroes.Length && i < 4; i++)
            {
                var tr = heroes[i].transform;
                float hgt = heroes[i].Rig.Height;
                Vector3 target = tr.position + Vector3.up * hgt * 0.52f;
                Vector3 dir = Quaternion.Euler(0f, 35f, 0f) * tr.forward;
                cam.transform.position = target + dir * 4.2f + Vector3.up * 0.35f;
                cam.transform.LookAt(target);
                cam.fieldOfView = 34f;
                var t = RenderCam(cam, hw, hh);
                hero.SetPixels(i * hw, 0, hw, hh, t.GetPixels());
                Object.DestroyImmediate(t);
            }
            hero.Apply();
            SavePng(hero, Path.Combine(outDir, "Showcase_Hero.png"));

            // back view of the lineup (hair, scarf, armor)
            cam.transform.position = new Vector3(0f, 1.9f, -9.6f);
            cam.transform.LookAt(new Vector3(0f, 1.25f, 0f));
            cam.fieldOfView = 30f;
            SavePng(RenderCam(cam, 1600, 900), Path.Combine(outDir, "Showcase_Back.png"));
            cam.transform.SetPositionAndRotation(orig, origRot);
            cam.fieldOfView = fov;
        }

        /// <summary>Debug: -showcaseOut dir -view "px,py,pz,lx,ly,lz,fov" renders Showcase_View.png of the showcase scene.</summary>
        public static void CaptureShowcaseView()
        {
            string outDir = GetArg("-showcaseOut") ?? Path.GetFullPath("Temp/Showcase");
            Directory.CreateDirectory(outDir);
            var f = System.Array.ConvertAll((GetArg("-view") ?? "0,1.5,3,0,1.5,0,30").Split(','), float.Parse);
            BuildCharacterShowcase();
            ShaderUtil.allowAsyncCompilation = false;
            var cam = Camera.main;
            cam.transform.position = new Vector3(f[0], f[1], f[2]);
            cam.transform.LookAt(new Vector3(f[3], f[4], f[5]));
            cam.fieldOfView = f[6];
            SavePng(RenderCam(cam, 1000, 1000), Path.Combine(outDir, "Showcase_View.png"));
        }

        private static Texture2D RenderCam(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            rt.Create();
            for (int i = 0; i < 3; i++)
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
            rt.Release();
            Object.DestroyImmediate(rt);
            return tex;
        }

        private static void SavePng(Texture2D tex, string file)
        {
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("[ProjectBuilder] Capture: wrote " + file);
        }
    }
}
