using System;
using System.Collections;
using System.IO;
using LightMMORPG.Characters;
using LightMMORPG.Networking;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace LightMMORPG.Menu
{
    /// <summary>
    /// Entry point of the MainMenu scene. Command-line launches (<c>-host</c>, <c>-client ip</c>, <c>-server</c>, batch
    /// mode) skip straight to the arena. Otherwise it builds the 3D backdrop at runtime — sky, floating island, the
    /// player's fighter, cel-shaded clouds — drifts the camera slowly, and builds <see cref="MainMenuUI"/>.
    /// <c>-menuCapture &lt;dir&gt;</c> renders the screenshot set (title, menu in each sky, Play Online, Settings, Stats, Customize) at
    /// 1920×1080 and iPhone landscape 2532×1170 (with notch / home-bar safe area) and quits; <c>-menuCaptureFace json</c>
    /// shows other face choices on the Customize shots.
    /// </summary>
    public sealed class MainMenuScene : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private Light _sun;

        public MainMenuUI UI { get; private set; }
        public MenuSky Sky { get; private set; }

        private MenuTheme _t;
        private Transform _islandRoot;
        private float _t0;
        private MenuFighter _fighter;
        /// <summary>0 = normal menu framing, 1 = Customize close-up on the fighter's face.</summary>
        private float _closeUp;
        private bool _dragging;

        private void Awake()
        {
            var mode = NetLaunch.FromCommandLine(out _);
            if (mode != NetMode.None && !NetLaunch.ReturnedFromArena)
            {
                // smoke tests / dedicated server: no menu
                Debug.Log("[Menu] Command-line launch (" + mode + "): loading " + NetLaunch.ArenaScene);
                enabled = false;
                SceneManager.LoadScene(NetLaunch.ArenaScene);
                return;
            }
            Application.targetFrameRate = Application.isMobilePlatform ? 60 : -1;
            _t = MenuTheme.Load();
            if (_camera == null) _camera = Camera.main;
            var mats = MenuMaterials.Create(_t);

            Sky = gameObject.AddComponent<MenuSky>();
            Sky.Init(_t, mats, _sun);
            Sky.Apply(MenuSky.Saved);

            _islandRoot = new GameObject("IslandRoot").transform;
            _islandRoot.SetParent(transform, false);
            var island = FloatingIsland.Create(_islandRoot, mats, 7, 3.1f, 3.9f);
            _fighter = MenuFighter.Create(_islandRoot, mats, PlayerProfile.Palette, new Vector3(0f, island.TopY, -0.2f), 164f);
            ToonClouds.Create(transform, mats, _camera.transform, _t.cloudDriftSpeed);

            _camera.fieldOfView = _t.cameraFov;
            _camera.clearFlags = CameraClearFlags.Skybox;
            _t0 = Time.time;
            UpdateCamera(0f);

            UI = gameObject.AddComponent<MainMenuUI>();
            UI.Init(_t, Sky, _camera, _fighter);

            string capture = Arg("-menuCapture");
            if (capture != null) StartCoroutine(Capture(capture));
            string auto = Arg("-menuAutoplay");
            if (auto != null) StartCoroutine(AutoPlay(auto, Arg("-menuAutoplayIp")));
        }

        private void Update()
        {
            float t = Time.time - _t0;
            bool customize = UI != null && UI.Current == MainMenuUI.State.Customize;
            _closeUp = Mathf.MoveTowards(_closeUp, customize ? 1f : 0f, Time.unscaledDeltaTime / 0.7f);
            UpdateTurn(customize);
            UpdateCamera(t);
            if (_islandRoot != null)
                _islandRoot.localPosition = Vector3.up * (Mathf.Sin(t * Mathf.PI * 2f / Mathf.Max(0.1f, _t.islandBobPeriod)) * _t.islandBob);
        }

        private void UpdateCamera(float t)
        {
            float w = Mathf.PI * 2f / Mathf.Max(1f, _t.driftPeriod);
            float yaw = Mathf.Sin(t * w) * _t.driftYaw;
            float rise = Mathf.Sin(t * w * 0.7f + 1.1f) * 0.25f;
            Vector3 offset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, _t.cameraHeight + rise, -_t.cameraDistance);
            Vector3 pos = _t.lookAt + offset, look = _t.lookAt;
            if (_closeUp > 0f && _fighter != null)
            {
                // face close-up, head on the left third (the Customize panel is on the right)
                Vector3 head = _fighter.HeadPosition + Vector3.up * 0.12f;
                Vector3 fwd = _islandRoot.rotation * Quaternion.Euler(0f, _fighter.BaseYaw, 0f) * Vector3.forward;
                const float dist = 2.3f;
                Vector3 cPos = head + fwd * dist + Vector3.up * 0.08f;
                Vector3 dir = (head - cPos).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
                float halfW = dist * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Max(1f, _camera.aspect);
                Vector3 cLook = head + right * halfW * 0.5f;
                float e = _closeUp * _closeUp * (3f - 2f * _closeUp);
                pos = Vector3.Lerp(pos, cPos, e);
                look = Vector3.Lerp(look, cLook, e);
            }
            _camera.transform.position = pos;
            _camera.transform.LookAt(look);
        }

        /// <summary>Customize: drag the fighter (left part of the screen), right stick or Q / E to turn; otherwise ease back.</summary>
        private void UpdateTurn(bool customize)
        {
            if (_fighter == null) return;
            float dt = Time.unscaledDeltaTime;
            if (!customize)
            {
                _dragging = false;
                _fighter.Turn = Mathf.LerpAngle(_fighter.Turn, 0f, 1f - Mathf.Exp(-dt * 5f));
                return;
            }
            var ptr = Pointer.current;
            if (ptr != null)
            {
                if (ptr.press.wasPressedThisFrame) _dragging = ptr.position.ReadValue().x < Screen.width * 0.42f; // left of the Customize panel
                if (!ptr.press.isPressed) _dragging = false;
                if (_dragging) _fighter.Turn -= ptr.delta.ReadValue().x * 0.35f;
            }
            float axis = 0f;
            var kb = Keyboard.current;
            if (kb != null) axis += (kb.eKey.isPressed ? 1f : 0f) - (kb.qKey.isPressed ? 1f : 0f);
            var gp = Gamepad.current;
            if (gp != null) axis += gp.rightStick.ReadValue().x;
            _fighter.Turn += axis * 150f * dt;
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        private IEnumerator AutoPlay(string action, string ip)
        {
            yield return new WaitForSecondsRealtime(1.5f);
            UI.AutoPlay(action, ip);
        }

        // ---------------- screenshots ----------------

        private IEnumerator Capture(string dir)
        {
            Directory.CreateDirectory(dir);
            // let the hero mesh, shaders and the canvas settle
            for (int i = 0; i < 30; i++) yield return null;
            var sizes = new (string tag, int w, int h, Rect? safe, bool touch)[]
            {
                ("PC", 1920, 1080, null, false),
                // iPhone 14 landscape: 2532×1170 px, notch side 47 pt, home bar 21 pt (×3)
                ("iPhone", 2532, 1170, new Rect(141, 63, 2532 - 282, 1170 - 63), true),
            };
            var shots = new (string name, SkyId sky, MainMenuUI.State state)[]
            {
                ("Title", SkyId.Day, MainMenuUI.State.Title),
                ("Menu_BlueSky", SkyId.Day, MainMenuUI.State.Main),
                ("Menu_Dusk", SkyId.Dusk, MainMenuUI.State.Main),
                ("Menu_Night", SkyId.Night, MainMenuUI.State.Main),
                ("PlayOnline", SkyId.Day, MainMenuUI.State.PlayOnline),
                ("Settings", SkyId.Night, MainMenuUI.State.Settings),
                ("Stats", SkyId.Day, MainMenuUI.State.Stats),
                ("Stats_Guest", SkyId.Dusk, MainMenuUI.State.Stats),
                ("Customize", SkyId.Day, MainMenuUI.State.Customize),
                ("Customize_Side", SkyId.Day, MainMenuUI.State.Customize),
                ("Customize_Jaw", SkyId.Dusk, MainMenuUI.State.Customize),
            };
            SkyId saved = MenuSky.Saved;
            foreach (var size in sizes)
            {
                var rt = new RenderTexture(size.w, size.h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                rt.Create();
                _camera.targetTexture = rt;
                MainMenuUI.SafeAreaOverride = size.safe;
                MainMenuUI.ForceTouchLayout = size.touch;
                foreach (var shot in shots)
                {
                    Sky.Apply(shot.sky);
                    // -menuCaptureFace '<FaceCustomization json>' previews other face choices without touching the saved ones
                    string faceJson = Arg("-menuCaptureFace");
                    if (shot.state == MainMenuUI.State.Customize) UI.ShowCustomize(faceJson != null ? PlayerAppearance.FromJson(faceJson) : PlayerAppearance.Load(), shot.name.EndsWith("_Jaw") ? 2 : 0);
                    else if (shot.name == "Stats") UI.ShowStatsPreview(Core.Progression.XpForLevel(24) + 600, new Core.StatAllocation(8, 6, 3, 4, 0));
                    else if (shot.name == "Stats_Guest") UI.ShowStatsGuest();
                    else UI.SetState(shot.state, instant: true);
                    _closeUp = shot.state == MainMenuUI.State.Customize ? 1f : 0f;
                    if (_fighter != null) _fighter.Turn = shot.name.EndsWith("_Side") ? 90f : 0f;
                    for (int i = 0; i < 8; i++) yield return null;
                    while (_fighter != null && _fighter.Rebuilding) yield return null;
                    UpdateCamera(Time.time - _t0);
                    _camera.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(size.w, size.h, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, size.w, size.h), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    string file = Path.Combine(dir, "Menu_" + size.tag + "_" + shot.name + ".png");
                    File.WriteAllBytes(file, tex.EncodeToPNG());
                    Destroy(tex);
                    Debug.Log("[Menu] Capture: wrote " + file);
                }
                _camera.targetTexture = null;
                rt.Release();
                Destroy(rt);
            }
            MainMenuUI.SafeAreaOverride = null;
            MainMenuUI.ForceTouchLayout = false;
            MenuSky.Saved = saved;
            Debug.Log("[Menu] Capture done");
            Application.Quit();
        }
    }
}
