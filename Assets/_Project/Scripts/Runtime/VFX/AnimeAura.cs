using System;
using LightMMORPG.Characters;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightMMORPG.VFX
{
    /// <summary>
    /// The anime flame aura (shader <c>LightMMORPG/AnimeAura</c>, bench <c>docs/bench/aura-bench.html</c>): a real 3D flame modelled in
    /// Blender (<c>tools/aura_blender.py</c>, <c>ArtSource/Aura/aura.blend</c>) whose silhouette is the owner's traced outline (a tiered
    /// flame "pine tree" with one peak over the head, round a see-through arch where the fighter stands). It rises in eight baked
    /// hand-drawn drawings (<c>Resources/VFX/AuraFlame.bytes</c>): the bottom ring climbs to become the middle ones and the middle the top.
    /// Set <see cref="Visible"/>, <see cref="Size"/> (0.85 charging, 1 Power Surge), <see cref="Color"/>, <see cref="Height"/> (the
    /// fighter's height) and <see cref="Body"/>: the flame bends along that fighter's spine, so a leaning body stays inside it.
    /// It flares up when it appears and shrinks away when it stops.
    /// </summary>
    [DefaultExecutionOrder(220)]   // after the poses (animators 200), so the bones are where they're drawn
    public sealed class AnimeAura : MonoBehaviour
    {
        /// <summary>How bright the aura colour is drawn (HDR, so the spikes catch Bloom).</summary>
        public const float Intensity = 1.35f;

        private const float FighterY = 0.62f;            // the fighter's height in the traced outline's space
        private const float DrawingSeconds = 0.1125f;    // one drawing; 4 lift every ring one tier (0.45 s, the reference clip)
        private const float TailPerSpeed = 0.05f, TailMax = 0.55f;   // how far the flame's tip drags behind a moving fighter (m)
        private const float TeleportDistance = 4f;       // a jump this big in one frame is a flash step: the flame doesn't whip across

        public bool Visible { get; set; }
        public float Size { get; set; } = 0.85f;
        public float Height { get; set; } = 2.3f;
        /// <summary>The aura colour (any brightness; it is normalised and drawn at <see cref="Intensity"/>).</summary>
        public Color Color { get; set; } = new(1f, 0.85f, 0.1f);
        /// <summary>The fighter whose spine the flame bends along (null = straight up).</summary>
        public CharacterRig Body { get; set; }

        private static readonly int ColorId = Shader.PropertyToID("_Color"), StrengthId = Shader.PropertyToID("_Strength"),
            FadeId = Shader.PropertyToID("_Fade"), FrontId = Shader.PropertyToID("_Front"), SeedId = Shader.PropertyToID("_Seed"),
            BendAId = Shader.PropertyToID("_BendA"), BendBId = Shader.PropertyToID("_BendB"), BendCId = Shader.PropertyToID("_BendC"),
            BendTailId = Shader.PropertyToID("_BendTail"), BendTopId = Shader.PropertyToID("_BendTop");

        private static Shader _shader;
        private static Mesh[] _drawings;

        private Material _far, _near;
        private MaterialPropertyBlock _mpb;
        private float _fade, _flare, _seed;
        private bool _tracking;
        private Vector3 _lastFeet, _velocity, _footOff, _hipsOff, _topOff;

        public static AnimeAura Create(Transform parent)
        {
            var go = new GameObject("AnimeAura");
            go.transform.SetParent(parent, false);
            return go.AddComponent<AnimeAura>();
        }

        private void Awake()
        {
            if (_shader == null) _shader = Resources.Load<Shader>("VFX/AnimeAura");
            if (_shader == null) { Debug.LogWarning("[AnimeAura] shader Resources/VFX/AnimeAura missing"); enabled = false; return; }
            if (LoadDrawings() == null) { enabled = false; return; }
            // the far side of the ring is drawn first, the near side over it
            _far = new Material(_shader) { name = "AnimeAura (far)", renderQueue = (int)RenderQueue.Transparent };
            _near = new Material(_shader) { name = "AnimeAura (near)", renderQueue = (int)RenderQueue.Transparent + 1 };
            _far.SetFloat(FrontId, 0f);
            _near.SetFloat(FrontId, 1f);
            _seed = UnityEngine.Random.value * 50f;
            _mpb = new MaterialPropertyBlock();
        }

        /// <summary>
        /// Reads the baked drawings (format: tools/aura_blender.py export): "AUR1", int drawings, vertices, indices; ushort indices
        /// (padded to 4 bytes); per vertex across, along, rnd (unorm8), kind; then per drawing and vertex x, y, z, edge (snorm16 × 2),
        /// nx, ny, nz (snorm8), az (snorm8 × π). Shared by every fighter.
        /// </summary>
        private static Mesh[] LoadDrawings()
        {
            if (_drawings != null && _drawings.Length > 0 && _drawings[0] != null) return _drawings;
            var asset = Resources.Load<TextAsset>("VFX/AuraFlame");
            if (asset == null) { Debug.LogWarning("[AnimeAura] Resources/VFX/AuraFlame.bytes missing"); return null; }
            byte[] b = asset.bytes;
            if (b.Length < 16 || b[0] != 'A' || b[1] != 'U' || b[2] != 'R' || b[3] != '1')
            {
                Debug.LogWarning("[AnimeAura] AuraFlame.bytes has an unknown format");
                return null;
            }
            int count = BitConverter.ToInt32(b, 4), nv = BitConverter.ToInt32(b, 8), ni = BitConverter.ToInt32(b, 12);
            int off = 16;
            var indices = new ushort[ni];
            Buffer.BlockCopy(b, off, indices, 0, ni * 2);
            off += ni * 2;
            while (off % 4 != 0) off++;
            int shared = off;
            off += nv * 4;

            var meshes = new Mesh[count];
            var pos = new Vector3[nv];
            var nor = new Vector3[nv];
            var data = new Vector4[nv];
            var misc = new Vector2[nv];
            for (int d = 0; d < count; d++)
            {
                for (int i = 0; i < nv; i++, off += 12)
                {
                    int s = shared + i * 4;
                    pos[i] = new Vector3(Sn16(b, off), Sn16(b, off + 2), Sn16(b, off + 4));
                    nor[i] = new Vector3((sbyte)b[off + 8] / 127f, (sbyte)b[off + 9] / 127f, (sbyte)b[off + 10] / 127f);
                    data[i] = new Vector4(b[s] / 255f * 2f - 1f, b[s + 1] / 255f, Sn16(b, off + 6), b[s + 2] / 255f);
                    misc[i] = new Vector2((sbyte)b[off + 11] / 127f * Mathf.PI, b[s + 3]);
                }
                var m = new Mesh { name = "AuraFlame." + d, hideFlags = HideFlags.HideAndDontSave };
                m.SetVertices(pos);
                m.SetNormals(nor);
                m.SetUVs(0, data);
                m.SetUVs(1, misc);
                m.SetIndices(indices, MeshTopology.Triangles, 0, false);
                // room for the spine's bend (the draw call's bounds are the ones that count)
                m.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(2.4f, 2.4f, 2.4f));
                m.UploadMeshData(true);
                meshes[d] = m;
            }
            Resources.UnloadAsset(asset);
            _drawings = meshes;
            return _drawings;
        }

        private static float Sn16(byte[] b, int at) => BitConverter.ToInt16(b, at) / 32767f * 2f;

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            _fade = Mathf.MoveTowards(_fade, Visible ? 1f : 0f, dt / (Visible ? 0.12f : 0.25f));
            // flare: bursts out a little oversized when it starts, settles in a quarter second
            _flare = Visible && _fade < 1f ? 1f : Mathf.MoveTowards(_flare, 0f, dt / 0.25f);
            if (_fade <= 0.001f) { _tracking = false; return; }
            var meshes = LoadDrawings();
            if (meshes == null) return;

            float ease = _fade * _fade * (3f - 2f * _fade);
            float size = Size * Mathf.Lerp(0.55f, 1f, ease) * (1f + 0.12f * _flare * Mathf.Sin(_fade * Mathf.PI));
            float k = Mathf.Clamp01((size - 0.55f) / 0.45f);     // 0 a weak charge .. 1 Power Surge
            // the aura's height: the arch's top sits just over the head
            float scale = Mathf.Max(0.5f, Height) / FighterY * Mathf.Lerp(0.865f, 0.996f, k) * Mathf.Lerp(0.6f, 1f, ease);
            Vector3 feet = transform.position;
            int drawing = Mathf.FloorToInt((Time.timeSinceLevelLoad + _seed) / DrawingSeconds);

            float max = Mathf.Max(Color.r, Mathf.Max(Color.g, Color.b));
            var c = max > 1e-4f ? Color / max : Color.white;
            c.a = 1f;
            _mpb.SetColor(ColorId, c * Intensity);
            _mpb.SetFloat(StrengthId, size);
            _mpb.SetFloat(FadeId, ease);
            _mpb.SetFloat(SeedId, _seed);
            FollowSpine(feet, scale, drawing * DrawingSeconds, dt);

            var mesh = meshes[drawing % meshes.Length];
            var matrix = Matrix4x4.TRS(feet, Quaternion.identity, Vector3.one * scale);
            var bounds = new Bounds(feet + Vector3.up * (scale * 0.5f), Vector3.one * (scale * 2.6f));
            Draw(_far, mesh, matrix, bounds);
            Draw(_near, mesh, matrix, bounds);
        }

        private void Draw(Material material, Mesh mesh, in Matrix4x4 matrix, in Bounds bounds)
        {
            var rp = new RenderParams(material)
            {
                worldBounds = bounds,
                matProps = _mpb,
                layer = gameObject.layer,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
            };
            Graphics.RenderMesh(rp, mesh, 0, matrix);
        }

        /// <summary>
        /// Bends the flame along the body: a parabola (sideways only, the flame keeps rising) through the feet, hips and head top,
        /// each following its bone with a touch of lag. Above the head the flame straightens up again, and its tip drags behind a
        /// moving fighter. Same fit as the bench.
        /// </summary>
        private void FollowSpine(Vector3 feet, float scale, float drawingTime, float dt)
        {
            Vector3 foot = Vector3.zero, hips = Vector3.zero, top = Vector3.zero;
            float hipsY = Height * 0.487f;
            var rig = Body;
            if (rig != null)
            {
                foot = (rig[CharacterBone.LeftFoot].position + rig[CharacterBone.RightFoot].position) * 0.5f - feet;
                hips = rig[CharacterBone.Hips].position - feet;
                var head = rig[CharacterBone.Head];
                top = head.position + head.up * Mathf.Max(0f, Height - rig.BindPosition(CharacterBone.Head).y) - feet;
                hipsY = rig.BindPosition(CharacterBone.Hips).y;
            }

            Vector3 move = feet - _lastFeet;
            _lastFeet = feet;
            if (!_tracking || move.sqrMagnitude > TeleportDistance * TeleportDistance)
            {
                _tracking = true;
                _footOff = foot;
                _hipsOff = hips;
                _topOff = top;
                _velocity = Vector3.zero;
            }
            else if (dt > 0f)
            {
                _footOff = Vector3.Lerp(_footOff, foot, 1f - Mathf.Exp(-30f * dt));
                _hipsOff = Vector3.Lerp(_hipsOff, hips, 1f - Mathf.Exp(-26f * dt));
                _topOff = Vector3.Lerp(_topOff, top, 1f - Mathf.Exp(-18f * dt));
                _velocity = Vector3.Lerp(_velocity, move / dt, 1f - Mathf.Exp(-10f * dt));
            }

            float y1 = Mathf.Max(hipsY, 0.2f) / scale, y2 = Mathf.Max(Height, hipsY + 0.2f) / scale;
            Vector2 v0 = Flat(_footOff) / scale, v1 = Flat(_hipsOff) / scale, v2 = Flat(_topOff) / scale;
            Vector2 d1 = (v1 - v0) / y1, d2 = (v2 - v1) / (y2 - y1);
            Vector2 curve = (d2 - d1) / y2, slope = d1 - curve * y1;
            // the upper part sways, on the drawings' clock
            curve += new Vector2(Mathf.Cos(drawingTime * 0.7f), Mathf.Sin(drawingTime * 0.7f)) * (0.03f * Mathf.Sin(drawingTime * 2.6f));
            Vector3 drag = Vector3.ClampMagnitude(-_velocity * TailPerSpeed, TailMax);

            _mpb.SetVector(BendAId, v0);
            _mpb.SetVector(BendBId, slope);
            _mpb.SetVector(BendCId, curve);
            _mpb.SetVector(BendTailId, new Vector4(drag.x / scale, drag.z / scale, 0f, 0f));
            _mpb.SetFloat(BendTopId, y2);
        }

        private static Vector2 Flat(Vector3 v) => new(v.x, v.z);

        private void OnDestroy()
        {
            if (_far != null) Destroy(_far);
            if (_near != null) Destroy(_near);
        }
    }
}
