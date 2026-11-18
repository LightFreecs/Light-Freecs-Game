using UnityEngine;

namespace LightMMORPG.Characters
{
    /// <summary>
    /// What the <c>LightMMORPG/ToonFace</c> shader needs to draw a face's moving parts every frame (Brow Bench port), made by
    /// <see cref="ToonFacePainter.PaintLive"/> next to the static face texture. Thread-safe data, no Unity objects.
    /// Head units throughout; eye space as in <see cref="ToonFacePainter.SketchEye"/> (u outward from the nose, v up, eye widths).
    /// </summary>
    public sealed class ToonFaceRig
    {
        /// <summary>False for the hand-made eye (painted whole into the face texture): only the brows move.</summary>
        public bool LiveEyes;
        /// <summary>1024² eye mask, bottom row first: R = eye white minus lid lines, G = height in the white, B = white height / 1.5.</summary>
        public Color32[] EyeMask;
        /// <summary>(EyeX, EyeY, eye width W, Shift).</summary>
        public Vector4 EyeGeo;
        /// <summary>(IrisU, IrisV, IrisRx, IrisRy) and (IrisRing, DarkTop, 0, 0).</summary>
        public Vector4 Iris0, Iris1;
        /// <summary>Shines: (u, v, radius, 1 = on).</summary>
        public Vector4 Shine0, Shine1;
        public Color IrisColor;
        /// <summary>How far the iris may travel (u, v) in eye widths: the look direction (−1..1) times this.</summary>
        public Vector2 Travel;
        /// <summary>Right eye's iris centre on the face (head units, z on the surface); the left one is mirrored.</summary>
        public Vector3 EyeCentre;

        public Color BrowColor;
        /// <summary>The player's brow sliders (size ×, angle in degrees, spacing in head units).</summary>
        public float BrowSize = 1f, BrowAngle, BrowSpacing;
        /// <summary>(mean x, mean y, left end x, length) of the right brow at rest.</summary>
        public Vector4 BrowRest;
        /// <summary>(s of the first column, s span, half height of the rows in head units, 0).</summary>
        public Vector4 BrowMapRange;
        /// <summary>RGHalf texels, bottom row first: R = coverage, G = middle line − mean y.</summary>
        public ushort[] BrowMap;
        public int BrowMapW, BrowMapH;

        /// <summary>The brow transform at rest: (spacing, height offset, cos(angle) / size, sin(angle) / size).</summary>
        public Vector4 BrowXform(float angleAdd = 0f, float sizeAdd = 0f, float xAdd = 0f, float hAdd = 0f)
        {
            float a = (BrowAngle + angleAdd) * Mathf.Deg2Rad, k = Mathf.Max(0.05f, BrowSize * (1f + sizeAdd));
            return new Vector4(BrowSpacing + xAdd, hAdd, Mathf.Cos(a) / k, Mathf.Sin(a) / k);
        }

        /// <summary>Writes the rest pose and all static values into a ToonFace material.</summary>
        public void ApplyTo(Material m, Texture eyeMask, Texture browMap)
        {
            m.SetFloat(ToonFaceIds.EyeLive, LiveEyes && eyeMask != null ? 1f : 0f);
            m.SetFloat(ToonFaceIds.BrowLive, browMap != null ? 1f : 0f);
            if (eyeMask != null) m.SetTexture(ToonFaceIds.EyeMask, eyeMask);
            if (browMap != null) m.SetTexture(ToonFaceIds.BrowTex, browMap);
            m.SetVector(ToonFaceIds.EyeGeo, EyeGeo);
            m.SetVector(ToonFaceIds.Iris0, Iris0);
            m.SetVector(ToonFaceIds.Iris1, Iris1);
            m.SetVector(ToonFaceIds.Shine0, Shine0);
            m.SetVector(ToonFaceIds.Shine1, Shine1);
            m.SetColor(ToonFaceIds.IrisColor, IrisColor);
            m.SetVector(ToonFaceIds.Look, Vector4.zero);
            m.SetVector(ToonFaceIds.EyeAnim, new Vector4(1f, 0f, 0f, 0f));
            m.SetColor(ToonFaceIds.BrowColor, BrowColor);
            m.SetVector(ToonFaceIds.BrowRest, BrowRest);
            m.SetVector(ToonFaceIds.BrowMapRange, BrowMapRange);
            m.SetVector(ToonFaceIds.BrowXform, BrowXform());
            m.SetVector(ToonFaceIds.BrowBend0, Vector4.zero);
            m.SetVector(ToonFaceIds.BrowBend1, Vector4.zero);
        }

        /// <summary>Main thread: the eye mask texture (null for the hand-made eye).</summary>
        public Texture2D CreateEyeMaskTexture(string name)
        {
            if (!LiveEyes || EyeMask == null) return null;
            var t = new Texture2D(ToonFacePainter.Size, ToonFacePainter.Size, TextureFormat.RGBA32, false, true)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            t.SetPixels32(EyeMask);
            t.Apply(false, true);
            return t;
        }

        /// <summary>Main thread: the brow map texture.</summary>
        public Texture2D CreateBrowTexture(string name)
        {
            if (BrowMap == null) return null;
            var t = new Texture2D(BrowMapW, BrowMapH, TextureFormat.RGHalf, false, true)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            t.SetPixelData(BrowMap, 0);
            t.Apply(false, true);
            return t;
        }
    }

    /// <summary>Shader property ids of the moving face.</summary>
    public static class ToonFaceIds
    {
        public static readonly int EyeLive = Shader.PropertyToID("_EyeLive"), BrowLive = Shader.PropertyToID("_BrowLive");
        public static readonly int EyeMask = Shader.PropertyToID("_EyeMask"), BrowTex = Shader.PropertyToID("_BrowTex");
        public static readonly int EyeGeo = Shader.PropertyToID("_EyeGeo"), Iris0 = Shader.PropertyToID("_Iris0"), Iris1 = Shader.PropertyToID("_Iris1");
        public static readonly int Shine0 = Shader.PropertyToID("_Shine0"), Shine1 = Shader.PropertyToID("_Shine1"), IrisColor = Shader.PropertyToID("_IrisColor");
        public static readonly int Look = Shader.PropertyToID("_Look"), EyeAnim = Shader.PropertyToID("_EyeAnim");
        public static readonly int BrowColor = Shader.PropertyToID("_BrowColor"), BrowRest = Shader.PropertyToID("_BrowRest"), BrowMapRange = Shader.PropertyToID("_BrowMapRange");
        public static readonly int BrowXform = Shader.PropertyToID("_BrowXform"), BrowBend0 = Shader.PropertyToID("_BrowBend0"), BrowBend1 = Shader.PropertyToID("_BrowBend1");
    }

    /// <summary>
    /// The moving toon face (Brow Bench port): the irises follow a look target (or wander), the eyes blink, and the seven
    /// expressions bend the brows and move the lids on top of the player's sliders, pop in with a small overshoot, play
    /// ~2.6 s and relax back to Neutral. Each frame only material values change (a property block on the face material);
    /// the face texture is never repainted.
    /// </summary>
    [DefaultExecutionOrder(300)]   // after the poses (MenuFighter 200), so the head bone is where it's drawn
    public sealed class ToonFaceAnimator : MonoBehaviour
    {
        // value sets, in brow lengths unless noted: angle (°, > 0 = outer end up / inner end down), inner / outer (lift the nose end /
        // outer end), arch (bow the middle), pinch (pull the nose end toward the nose), thick / thickIn (thicken all / the nose end),
        // h (brow height, head units), x (brow spacing), size (× 1 + size), lidTop / lidBot (upper lid down / lower lid up, 0..1),
        // iris (iris size, × 1 + iris), lookY (gaze nudge, −1 down … 1 up)
        private const int Angle = 0, Inner = 1, Outer = 2, ArchK = 3, Pinch = 4, Thick = 5, ThickIn = 6, Hh = 7, Xx = 8, SizeK = 9, LidTop = 10, LidBot = 11, IrisK = 12, LookY = 13, N = 14;

        private static readonly float[][] Exprs =
        {
            //            angle  inner  outer  arch   pinch thick  thickIn h       x   size  lidTop lidBot iris   lookY
            new[] { 0f,    0f,    0f,    0f,    0f,   0f,    0f,     0f,     0f, 0f,    0f,   0f,    0f,    0f },      // Neutral
            new[] { 8f,   -0.04f, 0.02f, 0.02f, 0.02f, 0.04f, 0.15f, -0.004f, 0f, 0f,    0.12f, 0.04f, -0.03f, 0f },     // Determined
            new[] { 16f,  -0.08f, 0.04f, 0.04f, 0.05f, 0.08f, 0.32f, -0.008f, 0f, 0f,    0.22f, 0.1f,  -0.06f, 0.1f },   // Angry
            new[] { -12f,  0.10f,-0.04f,-0.06f, 0.03f, 0f,    0.15f,  0.004f, 0f, 0f,    0.28f, 0f,    0f,   -0.45f },   // Sad
            new[] { 0f,    0.04f,-0.02f, 0.14f, 0f,  -0.18f,  0f,     0.03f,  0f, 0.04f, 0f,    0f,   -0.22f,  0f },     // Surprised
            new[] { -3f,   0.03f,-0.01f, 0.10f, 0f,  -0.06f,  0f,     0.01f,  0f, 0f,    0.1f,  0.42f, 0f,    0.05f },   // Happy
            new[] { -4f,   0.05f,-0.01f, 0.12f, 0f,  -0.05f,  0f,     0.016f, 0f, 0f,    1f,    0.5f,  0f,    0f },      // Joy
        };

        /// <summary>How long a played expression lasts before the face relaxes to Neutral (seconds).</summary>
        public const float ExpressionHold = 2.6f;
        private const float BlendTime = 0.28f, LookDepth = 0.4f, GazeSmooth = 0.055f, BrowFollowsGaze = 0.005f;

        /// <summary>What the eyes look at; null = <see cref="LookPoint"/>, else they wander.</summary>
        public Transform LookTarget;
        /// <summary>A world point to look at when <see cref="LookTarget"/> is null.</summary>
        [System.NonSerialized] public Vector3? LookPoint;
        /// <summary>Wander when there is nothing to look at (otherwise look ahead).</summary>
        public bool Wander = true;
        public bool Blink = true;

        public ToonExpression Expression { get; private set; }
        public bool Holding { get; private set; }

        private Renderer _renderer;
        private int _materialIndex;
        private ToonFaceRig _rig;
        private Transform _head;
        private Vector3 _origin;
        private float _unit = 1f;
        private MaterialPropertyBlock _block;

        private readonly float[] _now = new float[N], _from = new float[N];
        private float _exprT0 = -1e9f, _lastT;
        private float _blinkAt, _blinkT0 = -1e9f;
        private readonly Vector2[] _gaze = new Vector2[2];   // [0] = left eye, [1] = right eye (bench order)
        private Vector2 _wFrom = new(0f, 0.38f), _wTo = new(0f, 0.38f);
        private float _wT0, _wDur = 1f, _wUntil;

        /// <summary>
        /// Wires the animator to the face material slot of a renderer. <paramref name="head"/> is the head bone;
        /// head units map to its local space as origin + h × unit.
        /// </summary>
        public void Init(Renderer renderer, int materialIndex, ToonFaceRig rig, Transform head, Vector3 origin, float unit)
        {
            _renderer = renderer; _materialIndex = materialIndex; _rig = rig; _head = head; _origin = origin; _unit = Mathf.Max(1e-5f, unit);
            _block ??= new MaterialPropertyBlock();
            float now = Time.unscaledTime;
            _lastT = now;
            _blinkAt = now + Random.Range(1.5f, 4f);
        }

        /// <summary>Plays an expression: it pops in, plays <see cref="ExpressionHold"/> seconds and relaxes (unless held).</summary>
        public void Play(ToonExpression e, bool hold = false)
        {
            int i = (int)e;
            if (i < 0 || i >= Exprs.Length) i = 0;
            System.Array.Copy(_now, _from, N);
            Expression = (ToonExpression)i;
            Holding = hold;
            _exprT0 = Time.unscaledTime;
        }

        /// <summary>Plays the next expression after the current one (Neutral is skipped).</summary>
        public void PlayNext()
        {
            int i = (int)Expression % (Exprs.Length - 1) + 1;
            Play((ToonExpression)i);
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        private Vector2 WanderPos(float now)
        {
            float p = Smooth(Mathf.Clamp01((now - _wT0) / _wDur));
            return Vector2.Lerp(_wFrom, _wTo, p);
        }

        private Vector2 WanderAt(float now)
        {
            if (now >= _wUntil)
            {
                _wFrom = WanderPos(now);
                _wTo = new Vector2(Random.Range(-0.55f, 0.55f), Random.Range(0.05f, 0.8f));
                _wT0 = now; _wDur = Random.Range(0.9f, 1.7f); _wUntil = now + _wDur + Random.Range(0.7f, 2.6f);
            }
            return WanderPos(now);
        }

        private void LateUpdate()
        {
            if (_rig == null || _renderer == null) return;
            float now = Time.unscaledTime, dt = Mathf.Min(0.05f, Mathf.Max(0f, now - _lastT));
            _lastT = now;

            // an expression plays, then the face relaxes back to Neutral
            if (!Holding && Expression != ToonExpression.Neutral && now - _exprT0 > ExpressionHold) Play(ToonExpression.Neutral);
            // snappy, with a small overshoot the way anime expressions pop (the lids are clamped below)
            float q = Mathf.Clamp01((now - _exprT0) / BlendTime), q1 = q - 1f, p = 1f + 2.2f * q1 * q1 * q1 + 1.2f * q1 * q1;
            var to = Exprs[(int)Expression];
            for (int k = 0; k < N; k++) _now[k] = _from[k] + (to[k] - _from[k]) * p;

            // blink: 60 ms down, 110 ms up, every 2.5–5.5 s, now and then a double blink
            float b = 0f;
            if (Blink && _rig.LiveEyes)
            {
                if (now >= _blinkAt) { _blinkT0 = now; _blinkAt = now + (Random.value < 0.15f ? 0.28f : Random.Range(2.5f, 5.5f)); }
                float t = now - _blinkT0;
                if (t < 0.17f) b = Smooth(Mathf.Clamp01(t < 0.06f ? t / 0.06f : 1f - (t - 0.06f) / 0.11f));
            }

            // where the eyes look, in head units (each eye turns toward the target, so near targets make them converge)
            Vector3 target = default;
            bool has = false;
            if (_head != null && (LookTarget != null || LookPoint.HasValue))
            {
                Vector3 w = LookTarget != null ? LookTarget.position : LookPoint.Value;
                target = (_head.InverseTransformPoint(w) - _origin) / _unit;
                has = true;
            }
            else if (Wander)
            {
                Vector2 wp = WanderAt(now);
                target = new Vector3(wp.x, wp.y, _rig.EyeCentre.z + LookDepth);
                has = true;
            }
            float f = 1f - Mathf.Exp(-dt / GazeSmooth);
            var look = new Vector4();
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                Vector2 w = Vector2.zero;
                if (has)
                {
                    Vector3 c = new(side * _rig.EyeCentre.x, _rig.EyeCentre.y, _rig.EyeCentre.z);
                    float dx = target.x - c.x, dy = target.y - c.y, dz = Mathf.Max(LookDepth, target.z - c.z);
                    float l = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                    w = new Vector2(dx / l, dy / l);
                }
                w.y += _now[LookY];
                if (w.sqrMagnitude > 1f) w.Normalize();   // never past the travel ellipse
                _gaze[i] += (w - _gaze[i]) * f;
                // u points outward on both sides
                float u = side * _gaze[i].x * _rig.Travel.x, v = _gaze[i].y * _rig.Travel.y;
                if (side > 0) { look.x = u; look.y = v; } else { look.z = u; look.w = v; }
            }
            // the brows follow the gaze a little: up when looking up, down when looking down
            float h = _now[Hh] + BrowFollowsGaze * (_gaze[0].y + _gaze[1].y) / 2f;

            if (!_renderer.isVisible) return;
            float lt = Mathf.Clamp01(_now[LidTop]), lb = Mathf.Clamp01(_now[LidBot]);
            _renderer.GetPropertyBlock(_block, _materialIndex);
            _block.SetVector(ToonFaceIds.Look, look);
            _block.SetVector(ToonFaceIds.EyeAnim, new Vector4(1f + _now[IrisK], lt + (1f - lt) * b, lb, 0f));
            _block.SetVector(ToonFaceIds.BrowXform, _rig.BrowXform(_now[Angle], _now[SizeK], _now[Xx], h));
            _block.SetVector(ToonFaceIds.BrowBend0, new Vector4(_now[Inner], _now[Outer], _now[ArchK], _now[Pinch]));
            _block.SetVector(ToonFaceIds.BrowBend1, new Vector4(_now[Thick], _now[ThickIn], 0f, 0f));
            _renderer.SetPropertyBlock(_block, _materialIndex);
        }
    }
}
