using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LightMMORPG.Settings
{
    public enum GraphicsQuality { Low, Medium, High }

    /// <summary>
    /// Player settings, saved in PlayerPrefs (<c>settings.*</c>) and applied at startup and whenever they change:
    /// graphics quality (render scale / MSAA / shadow distance on a runtime copy of the active URP asset, so the
    /// project asset is never modified), frame-rate cap, look sensitivity, invert look, and keyboard/mouse rebinds
    /// (Input System binding overrides as JSON).
    /// </summary>
    public static class GameSettings
    {
        private const string KQuality = "settings.quality", KFps = "settings.fpsCap", KSens = "settings.lookSensitivity",
            KInvert = "settings.invertY", KBindings = "settings.bindings";

        /// <summary>Frame-rate caps offered in Settings (0 = no cap).</summary>
        public static readonly int[] FpsCaps = { 30, 60, 120, 0 };
        /// <summary>Look-sensitivity multipliers offered in Settings.</summary>
        public static readonly float[] Sensitivities = { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 2.5f, 3f };

        public static event Action Changed;

        private static bool Mobile => Application.isMobilePlatform;

        public static GraphicsQuality Quality
        {
            get => (GraphicsQuality)Mathf.Clamp(PlayerPrefs.GetInt(KQuality, (int)(Mobile ? GraphicsQuality.Medium : GraphicsQuality.High)), 0, 2);
            set { PlayerPrefs.SetInt(KQuality, (int)value); Save(); }
        }

        /// <summary>Frames per second cap; 0 = uncapped. iOS defaults to 30 fps unless set, so the default is 60.</summary>
        public static int FpsCap
        {
            get => Mathf.Max(0, PlayerPrefs.GetInt(KFps, 60));
            set { PlayerPrefs.SetInt(KFps, Mathf.Max(0, value)); Save(); }
        }

        /// <summary>Multiplier on mouse, stick and touch look speed.</summary>
        public static float LookSensitivity
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(KSens, 1f), 0.1f, 5f);
            set { PlayerPrefs.SetFloat(KSens, Mathf.Clamp(value, 0.1f, 5f)); Save(); }
        }

        public static bool InvertLookY
        {
            get => PlayerPrefs.GetInt(KInvert, 0) != 0;
            set { PlayerPrefs.SetInt(KInvert, value ? 1 : 0); Save(); }
        }

        private static void Save()
        {
            PlayerPrefs.Save();
            Apply();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplyAtStartup()
        {
            if (Application.isBatchMode) return; // servers / smoke tests keep the project defaults
            Apply();
        }

        /// <summary>Pushes the saved values into Unity (frame rate, render pipeline). Input reads its values live.</summary>
        public static void Apply()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = FpsCap > 0 ? FpsCap : -1;
            ApplyGraphics(Quality);
            Changed?.Invoke();
        }

        private static UniversalRenderPipelineAsset _source, _copy;

        private static void ApplyGraphics(GraphicsQuality q)
        {
            var active = QualitySettings.renderPipeline as UniversalRenderPipelineAsset
                         ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (active == null) return;
            if (active != _copy)
            {
                // work on a copy: changing the project asset at runtime would save into the repo in the editor
                _source = active;
                if (_copy != null) UnityEngine.Object.Destroy(_copy);
                _copy = UnityEngine.Object.Instantiate(active);
                _copy.name = active.name + " (Settings)";
                QualitySettings.renderPipeline = _copy;
            }
            switch (q)
            {
                case GraphicsQuality.Low:
                    _copy.renderScale = Mobile ? 0.65f : 0.75f;
                    _copy.msaaSampleCount = 1;
                    _copy.shadowDistance = Mathf.Min(_source.shadowDistance, 25f);
                    break;
                case GraphicsQuality.Medium:
                    _copy.renderScale = Mobile ? 0.8f : 0.9f;
                    _copy.msaaSampleCount = Mathf.Min(_source.msaaSampleCount, 2);
                    _copy.shadowDistance = Mathf.Min(_source.shadowDistance, 50f);
                    break;
                default:
                    _copy.renderScale = _source.renderScale;
                    _copy.msaaSampleCount = _source.msaaSampleCount;
                    _copy.shadowDistance = _source.shadowDistance;
                    break;
            }
        }

        // ------------------------------------------------------------------ key bindings

        /// <summary>Called by <see cref="Input.GameInputBindings"/> once the action map exists.</summary>
        public static void LoadBindings(InputActionMap map)
        {
            string json = PlayerPrefs.GetString(KBindings, "");
            if (string.IsNullOrEmpty(json)) return;
            try { map.LoadBindingOverridesFromJson(json); }
            catch (Exception e) { Debug.LogWarning("[Settings] Ignoring saved key bindings: " + e.Message); }
        }

        public static void SaveBindings(InputActionMap map)
        {
            PlayerPrefs.SetString(KBindings, map.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static void ResetBindings(InputActionMap map)
        {
            map.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(KBindings);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ labels

        public static string FpsLabel(int cap) => cap > 0 ? cap + " FPS" : "Unlimited";
        public static string SensitivityLabel(float s) => s.ToString("0.##") + "×";

        /// <summary>Next value in a list (wraps), starting from the closest entry to the current one.</summary>
        public static T Next<T>(T[] values, T current, Func<T, T, float> distance)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++)
                if (distance(values[i], current) < distance(values[best], current)) best = i;
            return values[(best + 1) % values.Length];
        }
    }
}
