using System.Collections.Generic;
using LightMMORPG.Characters;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightMMORPG.Menu
{
    /// <summary>Placeholder player profile until accounts exist: chosen fighter palette (PlayerPrefs) + the theme's copy.</summary>
    public static class PlayerProfile
    {
        public const string PaletteKey = "profile.palette";

        public static int Palette
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(PaletteKey, 0), 0, CharacterPresets.PaletteCount - 1);
            set { PlayerPrefs.SetInt(PaletteKey, value); PlayerPrefs.Save(); }
        }
    }

    /// <summary>
    /// The player's procedural fighter on the menu island: heroic idle (planted wide stance, chest up, fists low and
    /// slightly out, slow breathing and head sway) plus a subtle aura shell and rising sparkles in the hero's aura colour;
    /// on Customize's Body tab the anime flame aura (<see cref="VFX.AnimeAura"/>) replaces the shell to preview the aura colour.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class MenuFighter : MonoBehaviour
    {
        private CharacterRig _rig;
        private GameObject _hero;
        private Transform _aura;
        private Material _auraMat, _sparkleMat;
        private VFX.AnimeAura _flame;
        private readonly List<(Transform t, float phase, float radius, float angle, float speed)> _sparkles = new();
        private float _height = 2.3f;
        private float _baseYaw;
        private int _palette;

        // live preview (Customize screen): one background generation at a time, the newest request wins
        private FaceCustomization _face, _builtFace;
        private System.Threading.Tasks.Task<ProceduralCharacterBuilder.GeneratedCharacter> _job;
        private FaceCustomization _jobFace;
        private Mesh _previewMesh;
        private Texture2D _previewFace;
        private Texture2D _previewShadow;
        private Texture2D _previewEyes, _previewBrows;

        /// <summary>Extra turn (degrees) on top of the menu pose; the Customize screen drags it, otherwise it eases back to 0.</summary>
        public float Turn { get; set; }
        /// <summary>The base facing (yaw, degrees) in the island's space.</summary>
        public float BaseYaw => _baseYaw;
        /// <summary>World position of the head bone (camera close-ups).</summary>
        public Vector3 HeadPosition => _rig != null ? _rig[CharacterBone.Head].position : transform.position + Vector3.up * _height * 0.9f;
        /// <summary>True while a face change is still being built.</summary>
        public bool Rebuilding => _job != null;
        /// <summary>The hero's rig (null until built).</summary>
        public CharacterRig Rig => _rig;

        /// <summary>Shows the anime flame aura in the chosen aura colour (Customize: while the Body tab with the aura colours is open).</summary>
        public bool ShowAura { get; set; }

        /// <summary>Plays the next face expression (Customize: tap the fighter).</summary>
        public void PlayNextExpression()
        {
            var face = _hero != null ? _hero.GetComponent<ToonFaceAnimator>() : null;
            if (face != null) face.PlayNext();
        }

        // the menu fighter looks at the camera
        private void SetupFace(GameObject hero)
        {
            var face = hero.GetComponent<ToonFaceAnimator>();
            if (face != null && Camera.main != null) face.LookTarget = Camera.main.transform;
        }

        public static MenuFighter Create(Transform parent, MenuMaterials m, int palette, Vector3 localPos, float yaw)
        {
            var go = new GameObject("MenuFighter");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var f = go.AddComponent<MenuFighter>();
            f._baseYaw = yaw;
            f.Build(m, palette);
            return f;
        }

        /// <summary>Shows these face choices: rebuilds the hero in the background and swaps it in when ready.</summary>
        public void SetFace(FaceCustomization face)
        {
            _face = face.Sanitized();
            SetAuraColor(PlayerAppearance.AuraColor(_face.auraColor));
            if (_job == null && !Same(_face, _builtFace)) StartJob();
        }

        // the aura colour needs no new hero: it only recolours the aura, shell and sparkles
        private static bool Same(FaceCustomization a, FaceCustomization b)
        {
            a.auraColor = b.auraColor = 0;
            return JsonUtility.ToJson(a) == JsonUtility.ToJson(b);
        }

        private void SetAuraColor(Color aura)
        {
            if (_flame != null) _flame.Color = aura;
            if (_auraMat != null) _auraMat.SetColor("_Color", aura * 0.5f);
            if (_sparkleMat != null) _sparkleMat.SetColor("_Color", aura * 1.2f);
        }

        private void StartJob()
        {
            _jobFace = _face;
            _job = ProceduralCharacterBuilder.GenerateAsync(PlayerAppearance.ForPalette(_palette, _jobFace));
        }

        private void Update()
        {
            if (_job == null || !_job.IsCompleted) return;
            var job = _job;
            _job = null;
            if (job.IsFaulted) { Debug.LogException(job.Exception); return; }
            var gen = job.Result;
            // previews keep one face texture of their own, repainted each time (the painted nose follows the nose sliders)
            if (_previewFace != null)
            {
                _previewFace.SetPixels(gen.FacePixels);
                _previewFace.Apply(true, false);
                gen.Face = _previewFace;
            }
            var hero = ProceduralCharacterBuilder.Build(PlayerAppearance.ForPalette(_palette, _jobFace), gen);
            hero.transform.SetParent(transform, false);
            if (_hero != null) Destroy(_hero);
            if (_previewMesh != null) Destroy(_previewMesh);
            if (_previewShadow != null) Destroy(_previewShadow);
            if (_previewEyes != null) Destroy(_previewEyes);
            if (_previewBrows != null) Destroy(_previewBrows);
            _previewMesh = gen.Mesh;
            _previewShadow = gen.ShadowMap;
            _previewEyes = gen.FaceEyeMask;
            _previewBrows = gen.FaceBrowMap;
            SetupFace(hero);
            _previewFace = gen.Face;
            _hero = hero;
            _rig = hero.GetComponent<CharacterRig>();
            _builtFace = _jobFace;
            if (!Same(_face, _builtFace)) StartJob();
        }

        private void OnDestroy()
        {
            if (_previewMesh != null) Destroy(_previewMesh);
            if (_previewFace != null) Destroy(_previewFace);
            if (_previewShadow != null) Destroy(_previewShadow);
            if (_previewEyes != null) Destroy(_previewEyes);
            if (_previewBrows != null) Destroy(_previewBrows);
        }

        private void Build(MenuMaterials m, int palette)
        {
            _palette = palette;
            _face = _builtFace = PlayerAppearance.Load();
            var recipe = PlayerAppearance.ForPalette(palette, _face);
            var hero = ProceduralCharacterBuilder.Build(recipe);
            hero.transform.SetParent(transform, false);
            _hero = hero;
            _rig = hero.GetComponent<CharacterRig>();
            SetupFace(hero);
            if (_rig != null) _height = _rig.Height;
            Color aura = _rig != null ? _rig.AuraColor : recipe.auraColor;
            _flame = VFX.AnimeAura.Create(transform);
            _flame.Height = _height;
            _flame.Color = aura;

            if (m.aura != null)
            {
                _auraMat = m.aura;
                _auraMat.SetColor("_Color", aura * 0.5f);
                _auraMat.SetFloat("_Intensity", 0.28f);
                _auraMat.SetFloat("_FresnelPower", 3.6f);
                _auraMat.SetFloat("_FresnelStrength", 1.2f);
                _auraMat.SetFloat("_CoreStrength", 0.02f);
                var shell = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                shell.name = "Aura";
                Destroy(shell.GetComponent<Collider>());
                var r = shell.GetComponent<Renderer>();
                r.sharedMaterial = _auraMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                shell.transform.SetParent(transform, false);
                shell.transform.localPosition = new Vector3(0f, _height * 0.5f, 0f);
                _aura = shell.transform;
            }
            if (m.sparkle != null)
            {
                _sparkleMat = m.sparkle;
                m.sparkle.SetColor("_Color", aura * 1.2f);
                m.sparkle.SetFloat("_Intensity", 1.6f);
                m.sparkle.SetFloat("_CoreStrength", 1.2f);
                m.sparkle.SetFloat("_NoiseStrength", 0f);
                var rng = new System.Random(5);
                for (int i = 0; i < 16; i++)
                {
                    var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    s.name = "Sparkle";
                    Destroy(s.GetComponent<Collider>());
                    var r = s.GetComponent<Renderer>();
                    r.sharedMaterial = m.sparkle;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    s.transform.SetParent(transform, false);
                    _sparkles.Add((s.transform, (float)rng.NextDouble(), 0.35f + (float)rng.NextDouble() * 0.45f,
                        (float)rng.NextDouble() * 6.28f, 0.25f + (float)rng.NextDouble() * 0.2f));
                }
            }
        }

        private void LateUpdate()
        {
            float t = Time.time;
            transform.localRotation = Quaternion.Euler(0f, _baseYaw + Turn, 0f);
            if (_rig != null) Pose(t);
            if (_flame != null)
            {
                _flame.Visible = ShowAura;
                _flame.Size = 0.85f;
                _flame.Height = _height;
                _flame.Body = _rig;     // the flame bends along the hero's spine (the rig is replaced when the looks change)
            }
            if (_aura != null)
            {
                _aura.gameObject.SetActive(!ShowAura);   // the flame replaces the soft shell
                float pulse = 1f + Mathf.Sin(t * 2.2f) * 0.035f + Mathf.Sin(t * 5.3f) * 0.012f;
                _aura.localScale = new Vector3(0.6f * _height * 0.5f, 0.56f * _height, 0.52f * _height * 0.5f) * pulse;
            }
            foreach (var s in _sparkles)
            {
                float k = Mathf.Repeat(t * s.speed + s.phase, 1f);
                float a = s.angle + k * 1.5f;
                s.t.localPosition = new Vector3(Mathf.Cos(a) * s.radius, k * _height * 1.15f, Mathf.Sin(a) * s.radius * 0.7f);
                s.t.localScale = Vector3.one * (0.055f * Mathf.Sin(k * Mathf.PI));
            }
        }

        private void Pose(float t)
        {
            var rig = _rig;
            float hs = rig.Height / 2.3f;
            float breathe = Mathf.Sin(t * 1.6f);
            rig.ApplyRestPose(20f, 24f, 1f);

            // torso: chest up, slow breathing; head slightly raised with a small sway
            rig.SetModelRotation(CharacterBone.Spine, Quaternion.Euler(-2f, 0f, 0f));
            rig.SetModelRotation(CharacterBone.Chest, Quaternion.Euler(-4f - breathe * 0.8f, 0f, 0f));
            rig.SetModelRotation(CharacterBone.UpperChest, Quaternion.Euler(-5f - breathe * 1.1f, 0f, 0f));
            rig.Inherit(CharacterBone.Neck);
            rig.SetModelRotation(CharacterBone.Head, Quaternion.Euler(-4f + Mathf.Sin(t * 0.5f) * 1.5f, Mathf.Sin(t * 0.37f) * 4f, 0f));
            rig.Inherit(CharacterBone.LeftShoulder);
            rig.Inherit(CharacterBone.RightShoulder);

            // arms: fists low beside the hips, a little forward and out (power stance)
            for (int side = -1; side <= 1; side += 2)
            {
                var ua = (CharacterBone)CharacterBones.Side(CharacterBone.LeftUpperArm, side);
                Vector3 pole = new Vector3(side * 0.7f, 1.3f, -0.6f) * hs;
                Vector3 target = new Vector3(side * (0.6f + breathe * 0.006f), 1.02f + breathe * 0.012f, 0.2f) * hs;
                rig.SolveTwoBone(ua, target, pole, 1f);
                rig.SetModelRotation(ua + 2, rig.GetModelRotation(ua + 1));
                rig.SetFingerCurl(side, 1f);

                // legs: wide planted stance, knees a touch bent, feet flat
                var ul = (CharacterBone)CharacterBones.Side(CharacterBone.LeftUpperLeg, side);
                Quaternion thigh = Quaternion.AngleAxis(side * 8f, Vector3.forward) * Quaternion.AngleAxis(-4f, Vector3.right);
                rig.SetModelRotation(ul, thigh);
                rig.SetModelRotation(ul + 1, Quaternion.AngleAxis(7f, Vector3.right) * thigh);
                rig.SetModelRotation(ul + 2, Quaternion.AngleAxis(side * 6f, Vector3.up));
                rig.Inherit(ul + 3);
            }
        }
    }
}
