using UnityEngine;

namespace LightMMORPG.Characters
{
    /// <summary>Bones of the procedural fighter rig (GameObject names = enum names). Order = parents before children.</summary>
    public enum CharacterBone
    {
        Hips, Spine, Chest, UpperChest, Neck, Head,
        LeftShoulder, LeftUpperArm, LeftLowerArm, LeftHand,
        RightShoulder, RightUpperArm, RightLowerArm, RightHand,
        LeftUpperLeg, LeftLowerLeg, LeftFoot, LeftToes,
        RightUpperLeg, RightLowerLeg, RightFoot, RightToes,
        LeftThumbProximal, LeftThumbIntermediate, LeftIndexProximal, LeftIndexIntermediate, LeftMiddleProximal, LeftMiddleIntermediate,
        LeftRingProximal, LeftRingIntermediate, LeftLittleProximal, LeftLittleIntermediate,
        RightThumbProximal, RightThumbIntermediate, RightIndexProximal, RightIndexIntermediate, RightMiddleProximal, RightMiddleIntermediate,
        RightRingProximal, RightRingIntermediate, RightLittleProximal, RightLittleIntermediate,
        Count
    }

    public static class CharacterBones
    {
        public const int Count = (int)CharacterBone.Count;
        public const int ArmSideOffset = CharacterBone.RightShoulder - CharacterBone.LeftShoulder;   // 4
        public const int LegSideOffset = CharacterBone.RightUpperLeg - CharacterBone.LeftUpperLeg;   // 4
        public const int FingerSideOffset = CharacterBone.RightThumbProximal - CharacterBone.LeftThumbProximal; // 10

        /// <summary>side = -1 left, +1 right; pass the Left* bone.</summary>
        public static int Side(CharacterBone leftBone, int side)
        {
            int b = (int)leftBone;
            if (side < 0) return b;
            if (leftBone >= CharacterBone.LeftThumbProximal) return b + FingerSideOffset;
            if (leftBone >= CharacterBone.LeftUpperLeg) return b + LegSideOffset;
            return b + ArmSideOffset;
        }

        public static int Parent(int bone)
        {
            var b = (CharacterBone)bone;
            switch (b)
            {
                case CharacterBone.Hips: return -1;
                case CharacterBone.Spine: return (int)CharacterBone.Hips;
                case CharacterBone.Chest: return (int)CharacterBone.Spine;
                case CharacterBone.UpperChest: return (int)CharacterBone.Chest;
                case CharacterBone.Neck: return (int)CharacterBone.UpperChest;
                case CharacterBone.Head: return (int)CharacterBone.Neck;
                case CharacterBone.LeftShoulder:
                case CharacterBone.RightShoulder: return (int)CharacterBone.UpperChest;
                case CharacterBone.LeftUpperLeg:
                case CharacterBone.RightUpperLeg: return (int)CharacterBone.Hips;
            }
            if (b >= CharacterBone.LeftThumbProximal)
            {
                int local = (bone - (int)CharacterBone.LeftThumbProximal) % FingerSideOffset;
                bool right = bone >= (int)CharacterBone.RightThumbProximal;
                if (local % 2 == 1) return bone - 1; // intermediate → proximal
                return right ? (int)CharacterBone.RightHand : (int)CharacterBone.LeftHand;
            }
            return bone - 1; // arm / leg chains
        }
    }

    /// <summary>
    /// Runtime handle to a generated fighter's skeleton. Bind pose: every bone has identity rotation (axes = model axes:
    /// +Y up, +Z forward, +X = character's right), arms hang in an A-pose 40° from vertical, palms facing the thighs.
    /// Poses are set as model-space rotations per bone via <see cref="SetModelRotation"/> (parents first).
    /// </summary>
    public sealed class CharacterRig : MonoBehaviour
    {
        [SerializeField] private Transform[] _bones;
        [SerializeField] private Vector3[] _bindLocal;
        [SerializeField] private float _height = 2.3f;
        [SerializeField] private Vector3 _curlAxisL, _curlAxisR, _armDirL, _armDirR, _palmL, _palmR;
        [SerializeField] private Color _auraColor = Color.white;

        public float Height => _height;
        public Color AuraColor => _auraColor;
        public Transform this[CharacterBone b] => _bones[(int)b];
        public Transform[] Bones => _bones;

        private readonly Quaternion[] _model = new Quaternion[CharacterBones.Count];

        internal void Init(Transform[] bones, float height, Vector3 armDirL, Vector3 armDirR, Vector3 palmL, Vector3 palmR, Color aura)
        {
            _bones = bones;
            _height = height;
            _bindLocal = new Vector3[bones.Length];
            for (int i = 0; i < bones.Length; i++) _bindLocal[i] = bones[i].localPosition;
            _armDirL = armDirL; _armDirR = armDirR;
            _palmL = palmL; _palmR = palmR;
            _curlAxisL = Vector3.Cross(armDirL, palmL).normalized;
            _curlAxisR = Vector3.Cross(armDirR, palmR).normalized;
            _auraColor = aura;
        }

        public Vector3 BindArmDir(int side) => side < 0 ? _armDirL : _armDirR;
        public Vector3 BindPalm(int side) => side < 0 ? _palmL : _palmR;

        /// <summary>Back to the bind (A-) pose.</summary>
        public void ResetPose()
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                _bones[i].localRotation = Quaternion.identity;
                _bones[i].localPosition = _bindLocal[i];
                _model[i] = Quaternion.identity;
            }
        }

        /// <summary>Sets a bone's rotation relative to the rig root (model space). Call parents before children.</summary>
        public void SetModelRotation(CharacterBone bone, Quaternion modelRotation)
        {
            int i = (int)bone;
            int parent = CharacterBones.Parent(i);
            Quaternion parentModel = parent < 0 ? Quaternion.identity : _model[parent];
            _model[i] = modelRotation;
            _bones[i].localRotation = Quaternion.Inverse(parentModel) * modelRotation;
        }

        public Quaternion GetModelRotation(CharacterBone bone) => _model[(int)bone];

        /// <summary>Re-applies the parent's model rotation to a bone that keeps its own (e.g. after the parent moved).</summary>
        public void Inherit(CharacterBone bone)
        {
            int i = (int)bone;
            int parent = CharacterBones.Parent(i);
            SetModelRotation(bone, parent < 0 ? Quaternion.identity : _model[parent]);
        }

        /// <summary>Model-space position of a bone in the bind pose.</summary>
        public Vector3 BindPosition(CharacterBone bone)
        {
            Vector3 p = Vector3.zero;
            for (int i = (int)bone; i >= 0; i = CharacterBones.Parent(i)) p += _bindLocal[i];
            return p;
        }

        /// <summary>Model-space position of a joint in the CURRENT pose (uses the model rotations set so far; parents first).</summary>
        public Vector3 ModelPosition(CharacterBone bone)
        {
            int i = (int)bone, parent = CharacterBones.Parent(i);
            if (parent < 0) return _bones[i].localPosition;
            return ModelPosition((CharacterBone)parent) + _model[parent] * _bones[i].localPosition;
        }

        /// <summary>Bind-pose offset of a bone from its parent (bone length and direction).</summary>
        public Vector3 BindLocal(CharacterBone bone) => _bindLocal[(int)bone];

        /// <summary>
        /// Two-bone IK from the joint's POSED position, both bones aimed with one shared hinge axis taken from the pole, so the
        /// middle joint bends like a hinge and the two bones never roll against each other (shortest-arc aiming per bone wrings
        /// the skin at elbows and knees). <paramref name="bindPole"/>: where the middle joint sticks out in the bind pose
        /// (elbows: back, knees: forward). The end bone keeps its rotation relative to the middle one. Port of the Attack Bench.
        /// </summary>
        public void SolveTwoBoneHinged(CharacterBone upper, Vector3 target, Vector3 pole, float weight, Vector3 bindPole)
        {
            if (weight <= 0f) return;
            var mid = upper + 1;
            var end = upper + 2;
            Vector3 a = ModelPosition(upper);
            Vector3 bindU = _bindLocal[(int)mid], bindM = _bindLocal[(int)end];
            float l1 = bindU.magnitude, l2 = bindM.magnitude;
            Vector3 d1 = bindU / l1, d2 = bindM / l2;
            Quaternion curU = _model[(int)upper], curM = _model[(int)mid], endModel = _model[(int)end];
            Vector3 toT = target - a;
            float dist = Mathf.Clamp(toT.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
            Vector3 dir = toT.sqrMagnitude > 1e-10f ? toT.normalized : Vector3.down;
            float cosA = (l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist);
            float angA = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f));
            Vector3 poleDir = Vector3.ProjectOnPlane(pole - a, dir);
            if (poleDir.sqrMagnitude < 1e-6f) poleDir = Vector3.ProjectOnPlane(Vector3.down, dir);
            poleDir.Normalize();
            Vector3 elbowDir = dir * Mathf.Cos(angA) + poleDir * Mathf.Sin(angA);
            Vector3 elbowPos = a + elbowDir * l1, endPos = a + dir * dist;
            Vector3 hinge = Vector3.Cross(poleDir, dir).normalized;
            Vector3 hingeBind = Vector3.Cross(bindPole, d1).normalized;
            Quaternion qU = AimFrame(d1, hingeBind, elbowDir, hinge);
            Quaternion qM = AimFrame(d2, hingeBind, (endPos - elbowPos).normalized, hinge);
            SetModelRotation(upper, Quaternion.Slerp(curU, qU, weight));
            SetModelRotation(mid, Quaternion.Slerp(curM, qM, weight));
            SetModelRotation(end, _model[(int)mid] * Quaternion.Inverse(curM) * endModel);
        }

        /// <summary>Rotation taking the frame (x along the bone, y = hinge axis) of the bind pose to the target one.</summary>
        private static Quaternion AimFrame(Vector3 xBind, Vector3 yBind, Vector3 xTarget, Vector3 yTarget)
        {
            Quaternion Frame(Vector3 x, Vector3 y) => Quaternion.LookRotation(x, Vector3.ProjectOnPlane(y, x));
            return Frame(xTarget, yTarget) * Quaternion.Inverse(Frame(xBind, yBind));
        }

        /// <summary>Curls the fingers of one hand (0 open, 1 fist). Uses the hand's current model rotation.</summary>
        public void SetFingerCurl(int side, float curl)
        {
            Quaternion hand = _model[CharacterBones.Side(CharacterBone.LeftHand, side)];
            Vector3 axis = hand * (side < 0 ? _curlAxisL : _curlAxisR);
            Vector3 palm = hand * (side < 0 ? _palmL : _palmR);
            Vector3 dir = hand * (side < 0 ? _armDirL : _armDirR);
            for (int f = 1; f < 5; f++)
            {
                var prox = (CharacterBone)CharacterBones.Side(CharacterBone.LeftThumbProximal + f * 2, side);
                Quaternion q1 = Quaternion.AngleAxis(curl * 85f, axis) * hand;
                SetModelRotation(prox, q1);
                SetModelRotation(prox + 1, Quaternion.AngleAxis(curl * 100f, axis) * q1);
            }
            // thumb folds across the curled fingers
            Vector3 thumbAxis = Vector3.Cross(dir, palm).normalized;
            var t1 = (CharacterBone)CharacterBones.Side(CharacterBone.LeftThumbProximal, side);
            Quaternion qt = Quaternion.AngleAxis(curl * 35f, palm) * Quaternion.AngleAxis(curl * 25f, thumbAxis) * hand;
            SetModelRotation(t1, qt);
            SetModelRotation(t1 + 1, Quaternion.AngleAxis(curl * 45f, thumbAxis) * qt);
        }

        /// <summary>
        /// Relaxed fighting stance: arms closer to the body with bent elbows, fists, slightly bent knees.
        /// <paramref name="armDown"/> degrees towards the body from the A-pose.
        /// </summary>
        public void ApplyRestPose(float armDown = 29f, float elbow = 22f, float fist = 1f)
        {
            ResetPose();
            for (int side = -1; side <= 1; side += 2)
            {
                var ua = (CharacterBone)CharacterBones.Side(CharacterBone.LeftUpperArm, side);
                Quaternion qa = Quaternion.AngleAxis(-side * armDown, Vector3.forward) * Quaternion.AngleAxis(-8f, Vector3.right);
                SetModelRotation(ua, qa);
                Vector3 armDir = qa * BindArmDir(side);
                Vector3 bendAxis = Vector3.Cross(armDir, Vector3.forward).normalized; // forearm swings forward
                Quaternion qe = Quaternion.AngleAxis(elbow, bendAxis) * qa;
                SetModelRotation(ua + 1, qe);
                SetModelRotation(ua + 2, qe);
                SetFingerCurl(side, fist);
            }
        }

        /// <summary>
        /// Two-bone IK in model space: points the arm (or leg) chain so its end reaches <paramref name="target"/>
        /// with the middle joint bending towards <paramref name="pole"/>. Blends from the current pose by <paramref name="weight"/>.
        /// </summary>
        public void SolveTwoBone(CharacterBone upper, Vector3 target, Vector3 pole, float weight)
        {
            if (weight <= 0f) return;
            var mid = upper + 1;
            var end = upper + 2;
            Vector3 a = BindPosition(upper), bBind = BindPosition(mid), cBind = BindPosition(end);
            float l1 = (bBind - a).magnitude, l2 = (cBind - bBind).magnitude;
            Vector3 dirBind1 = (bBind - a) / l1, dirBind2 = (cBind - bBind) / l2;

            // current pose directions (for blending)
            Quaternion curU = _model[(int)upper], curM = _model[(int)mid];
            Vector3 toT = target - a;
            float dist = Mathf.Clamp(toT.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
            Vector3 dir = toT.normalized;
            float cosA = (l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist);
            float angA = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f));
            Vector3 poleDir = Vector3.ProjectOnPlane(pole - a, dir);
            if (poleDir.sqrMagnitude < 1e-6f) poleDir = Vector3.ProjectOnPlane(Vector3.down, dir);
            poleDir.Normalize();
            Vector3 elbowDir = dir * Mathf.Cos(angA) + poleDir * Mathf.Sin(angA);
            Vector3 elbowPos = a + elbowDir * l1;
            Vector3 endPos = a + dir * dist;
            Quaternion qU = Quaternion.FromToRotation(dirBind1, elbowDir);
            Quaternion qM = Quaternion.FromToRotation(dirBind2, (endPos - elbowPos).normalized);
            SetModelRotation(upper, Quaternion.Slerp(curU, qU, weight));
            SetModelRotation(mid, Quaternion.Slerp(curM, qM, weight));
            SetModelRotation(end, _model[(int)mid] * Quaternion.Inverse(curM) * _model[(int)end]);
        }
    }
}
