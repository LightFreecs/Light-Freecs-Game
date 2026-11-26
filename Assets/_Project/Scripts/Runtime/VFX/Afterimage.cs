using LightMMORPG.Characters;
using LightMMORPG.Config;
using LightMMORPG.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightMMORPG.VFX
{
    /// <summary>
    /// Anime afterimage: a glowing copy of a fighter's pose left where it was. The body's skinned mesh (the whole hero: body,
    /// head and hair are one renderer) is baked once and drawn with the aura glow in the fighter's aura colour, fading out
    /// over a few tenths of a second. Visual only (nothing on a headless server).
    /// </summary>
    public sealed class Afterimage : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color"), IntensityId = Shader.PropertyToID("_Intensity");
        private static bool Headless => Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
        private static Material[] _mats = new Material[0];

        private Mesh _mesh;
        private MeshRenderer _mr;
        private MaterialPropertyBlock _mpb;
        private Color _color;
        private float _life, _t, _intensity;

        /// <summary>The generated hero's rig of a fighter (its Graphics/Model), or null.</summary>
        public static CharacterRig RigOf(Component fighter)
        {
            if (fighter == null) return null;
            var model = fighter.transform.Find("Graphics/Model");
            var proc = model != null ? model.GetComponent<ProceduralCharacter>() : null;
            return proc != null ? proc.Rig : null;
        }

        /// <summary>
        /// Leaves an afterimage of the rig's current pose, <paramref name="offset"/> metres from where the body is now (a
        /// flash step leaves it where the fighter vanished), glowing <paramref name="intensity"/> and gone after
        /// <paramref name="life"/> seconds.
        /// </summary>
        public static void Spawn(CharacterRig rig, float life, float intensity, Vector3 offset = default)
        {
            if (Headless || rig == null || rig.Body == null || !rig.Body.gameObject.activeInHierarchy) return;
            var mat = GameConfig.Load().auraMaterial;
            if (mat == null) return;
            var mesh = new Mesh { name = "Afterimage" };
            rig.Body.BakeMesh(mesh, true);   // with the body's scale (a dash stretch stays in the copy)
            var t = rig.Body.transform;
            var go = new GameObject("Afterimage");
            go.transform.SetPositionAndRotation(t.position + offset, t.rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            int subs = Mathf.Max(1, mesh.subMeshCount);
            if (_mats.Length != subs || (_mats.Length > 0 && _mats[0] != mat))
            {
                _mats = new Material[subs];
                for (int i = 0; i < subs; i++) _mats[i] = mat;
            }
            mr.sharedMaterials = _mats;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var a = go.AddComponent<Afterimage>();
            a._mesh = mesh;
            a._mr = mr;
            a._mpb = new MaterialPropertyBlock();
            Color c = rig.AuraColor;
            a._color = new Color(Mathf.Max(c.r, 0.05f), Mathf.Max(c.g, 0.05f), Mathf.Max(c.b, 0.05f), 1f) * 1.6f;
            a._life = Mathf.Max(0.05f, life);
            a._intensity = intensity;
            a.Apply(1f);
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float k = _t / _life;
            if (k >= 1f) { Destroy(gameObject); return; }
            Apply(1f - k);
        }

        private void Apply(float fade)
        {
            _mr.GetPropertyBlock(_mpb);
            _mpb.SetColor(ColorId, _color);
            _mpb.SetFloat(IntensityId, _intensity * fade);
            _mr.SetPropertyBlock(_mpb);
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }

    /// <summary>
    /// Per fighter, every client: a trail of afterimages while it dashes or rushes, and one where it stood when it flash-steps
    /// away (the body jumps several metres in a frame). Driven by the predicted / synced motor state, so it looks the same
    /// everywhere without messages. Runs after the procedural animator has posed the body.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class AfterimageTrail : MonoBehaviour
    {
        private const float TrailEvery = 0.045f, TrailLife = 0.24f, VanishLife = 0.5f, VanishJump = 4f;
        private PlayerMotor _motor;
        private Transform _graphics;
        private CharacterRig _rig;
        private Vector3 _lastPos;
        private bool _hasLast;
        private float _next, _rigLookup;

        public static void Attach(GameObject fighter)
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            if (fighter.GetComponent<AfterimageTrail>() == null) fighter.AddComponent<AfterimageTrail>();
        }

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _graphics = transform.Find("Graphics");
        }

        private void LateUpdate()
        {
            if (_motor == null || _graphics == null) return;
            if (_rig == null && Time.time >= _rigLookup)
            {
                _rig = Afterimage.RigOf(this);
                _rigLookup = Time.time + 0.5f;
            }
            Vector3 pos = _graphics.position;
            if (_rig != null)
            {
                // a flash step (or any vanish: the body jumps several metres in one frame) leaves a copy where it stood
                if (_hasLast && (pos - _lastPos).sqrMagnitude > VanishJump * VanishJump) Afterimage.Spawn(_rig, VanishLife, 1.4f, _lastPos - pos);
                else if (_motor.IsDashing && Time.time >= _next)
                {
                    _next = Time.time + TrailEvery;
                    Afterimage.Spawn(_rig, TrailLife, _motor.IsRushing ? 0.9f : 0.7f);
                }
            }
            _lastPos = pos;
            _hasLast = true;
        }
    }
}
