using UnityEngine;
using B = LightMMORPG.Characters.CharacterBone;

namespace LightMMORPG.Characters
{
    public static partial class ProceduralCharacterBuilder
    {
        /// <summary>
        /// <see cref="BodyModel.BaseMesh"/>: the baked anime base mesh (<see cref="BaseBody"/>) as the body, skinned with its
        /// baked weights to the same 42-bone rig, in trunks. The recipe's body sliders reshape it (1 = as modelled):
        /// <c>bulk</c> thickens everything around its bones, <c>muscle</c> the arms, legs and chest, <c>shoulderWidth</c>
        /// slides the arms out, <c>waist</c> pinches or widens the middle. The top of the neck is fitted inside the Toon
        /// Head's neck (whatever its width slider), and the head sits with its chin on the body's chin.
        /// </summary>
        private sealed partial class Ctx
        {
            private readonly bool _mesh;
            private BaseBody.Data _bb;
            private Vector3[] _segA, _segB;
            private float _shoulderDx;

            private const float ShoulderShift = 0.22f, NeckFitStart = 1.795f, NeckFitFull = 1.815f;
            /// <summary>The body's neck tube sits this much outside the head's collar, so it hides the collar's bottom edge.</summary>
            private const float NeckOverCollar = 1.03f;
            /// <summary>The collar's bottom edge is pulled in to this much of the neck, well inside the body's neck tube.</summary>
            private const float CollarRimTuck = 0.85f;
            // the Toon Head's neck at neckWidth 1 (ToonHead.Apply), to keep the player's neck-width slider as a ratio
            private const float ToonNeckW = 0.148f, ToonNeckF = 0.155f, ToonNeckB = 0.160f;

            /// <summary>
            /// The body's own neck is what shows; the head keeps only a short collar of its neck. Size that collar to the
            /// body's neck just under the chin, scaled by the player's neck-width slider. <see cref="FitNeck"/> then puts
            /// the body's neck tube just outside it (it carries on up inside the head), and the collar's bottom edge is
            /// tucked inside the tube (<see cref="CollarRimTuck"/>), so neither edge can show as a gap.
            /// </summary>
            private void FitHeadNeckToBody()
            {
                var bb = BaseBody.Get();
                float u = HeadUnit * _hs;
                _toon.NeckW = bb.NeckHalfWidth / u * (_toon.NeckW / ToonNeckW);
                _toon.NeckF = bb.NeckFront / u * (_toon.NeckF / ToonNeckF);
                _toon.NeckB = bb.NeckBack / u * (_toon.NeckB / ToonNeckB);
            }

            private static bool IsArm(int b)
            {
                for (int side = -1; side <= 1; side += 2)
                    if (b >= CharacterBones.Side(B.LeftUpperArm, side) && b <= CharacterBones.Side(B.LeftHand, side)) return true;
                return b >= (int)B.LeftThumbProximal;
            }

            private static bool IsLimb(int b)
            {
                for (int side = -1; side <= 1; side += 2)
                    if (b == CharacterBones.Side(B.LeftUpperArm, side) || b == CharacterBones.Side(B.LeftLowerArm, side)
                        || b == CharacterBones.Side(B.LeftUpperLeg, side) || b == CharacterBones.Side(B.LeftLowerLeg, side)) return true;
                return false;
            }

            private void MeshBones()
            {
                _bb = BaseBody.Get();
                for (int i = 0; i < _p.Length; i++) _p[i] = _bb.Joints[i];
                _shoulderDx = (_r.shoulderWidth - 1f) * ShoulderShift;
                for (int i = 0; i < _p.Length; i++)
                    if (IsArm(i)) _p[i].x += Mathf.Sign(_p[i].x) * _shoulderDx;
                // the Toon Head: chin on the body's chin, its neck centred on the body's neck
                _p[(int)B.Head] = new Vector3(0f, _bb.ChinY - ChinHL * _hs, _bb.NeckZ - (AxisZ + _toon.NeckZ * HeadUnit) * _hs);
                float a = 40f * Mathf.Deg2Rad;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 dir = new(side * Mathf.Sin(a), -Mathf.Cos(a), 0f);
                    Vector3 palm = new(-side * Mathf.Cos(a), -Mathf.Sin(a), 0f);
                    if (side < 0) { _armDirL = dir; _palmL = palm; } else { _armDirR = dir; _palmR = palm; }
                }
                _segA = new Vector3[_p.Length];
                _segB = new Vector3[_p.Length];
                for (int i = 0; i < _p.Length; i++) { _segA[i] = _p[i]; _segB[i] = SegmentEnd(i); }
            }

            /// <summary>Far end of a bone (for the sliders: vertices move towards / away from the bone's segment).</summary>
            private Vector3 SegmentEnd(int i)
            {
                switch ((B)i)
                {
                    case B.Hips: return _p[(int)B.Spine];
                    case B.Spine: return _p[(int)B.Chest];
                    case B.Chest: return _p[(int)B.UpperChest];
                    case B.UpperChest: return _p[(int)B.Neck];
                    case B.Neck: return new Vector3(0f, _bb.ChinY, _bb.NeckZ);
                    case B.Head: return _p[i] + Vector3.up * 0.2f;
                }
                if (i >= (int)B.LeftThumbProximal)
                {
                    bool proximal = (i - (int)B.LeftThumbProximal) % 2 == 0;
                    return proximal ? _p[i + 1] : _p[i] + (_p[i] - _p[i - 1]) * 0.9f;
                }
                for (int side = -1; side <= 1; side += 2)
                {
                    if (i == CharacterBones.Side(B.LeftShoulder, side)) return _p[CharacterBones.Side(B.LeftUpperArm, side)];
                    if (i == CharacterBones.Side(B.LeftHand, side)) return _p[CharacterBones.Side(B.LeftMiddleProximal, side)];
                    if (i == CharacterBones.Side(B.LeftToes, side)) return _p[i] + new Vector3(0f, -0.01f, 0.075f);
                }
                return _p[i + 1];   // upper / lower arm, upper / lower leg, foot
            }

            private static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b)
            {
                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-10f));
                return a + ab * t;
            }

            /// <summary>Applies the body sliders to one vertex (by its bone weights).</summary>
            private Vector3 Deform(Vector3 p, BaseBody.Part part, int v)
            {
                var r = _r;
                if (r.bulk == 1f && r.muscle == 1f && r.waist == 1f && _shoulderDx == 0f) return p;
                int o = v * 4;
                float wArm = 0f, wLimb = 0f, wTorso = 0f, wChest = 0f;
                for (int k = 0; k < 4; k++)
                {
                    float w = part.Weights[o + k];
                    if (w <= 0f) continue;
                    int b = part.BoneIndex[o + k];
                    if (IsArm(b)) wArm += w;
                    if (IsLimb(b)) wLimb += w;
                    if (b == (int)B.Hips || b == (int)B.Spine || b == (int)B.Chest) wTorso += w;
                    if (b == (int)B.Chest || b == (int)B.UpperChest) wChest += w;
                }
                // shoulders: the arms (and the top of the chest towards them) slide sideways
                if (_shoulderDx != 0f)
                {
                    float ramp = Smooth01(0.06f, 0.2f, Mathf.Abs(p.x)) * Smooth01(1.42f, 1.6f, p.y);
                    p.x += (p.x >= 0f ? 1f : -1f) * _shoulderDx * Mathf.Max(wArm, ramp);
                }
                Vector3 proj = Vector3.zero;
                for (int k = 0; k < 4; k++)
                {
                    float w = part.Weights[o + k];
                    if (w <= 0f) continue;
                    int b = part.BoneIndex[o + k];
                    proj += w * ClosestOnSegment(p, _segA[b], _segB[b]);
                }
                Vector3 off = p - proj;
                float waist = (p.y - 1.30f) / 0.09f;
                float scale = r.bulk + (r.muscle - 1f) * 0.6f * wLimb + (r.waist - 1f) * wTorso * Mathf.Exp(-waist * waist);
                if (off.z > 0f) scale += (r.muscle - 1f) * 0.35f * wChest * Smooth01(0f, 0.06f, off.z);
                return proj + off * scale;
            }

            /// <summary>
            /// Fits the body's neck tube just outside the head's collar (<see cref="NeckOverCollar"/>; grows or shrinks it),
            /// so the two meet whatever the neck-width slider says. The tube carries on up inside the head and turns with it
            /// (baked weights); below <see cref="NeckFitStart"/> the neck keeps the base mesh's shape.
            /// </summary>
            private Vector3 FitNeck(Vector3 p)
            {
                if (p.y <= NeckFitStart) return p;
                float t = Smooth01(NeckFitStart, NeckFitFull, p.y);
                float s = HeadUnit * _hs * NeckOverCollar, dz = p.z - _bb.NeckZ;
                float ex = p.x / (_toon.NeckW * s), ez = dz / ((dz >= 0f ? _toon.NeckF : _toon.NeckB) * s);
                float e = Mathf.Sqrt(ex * ex + ez * ez);
                if (e < 1e-4f) return p;
                float k = Mathf.Lerp(1f, 1f / e, t);
                // past the head's neck top the tube is inside the head: taper it, so no head slider (narrow jaw, wide
                // neck) can push it out through the jaw
                float a = Mathf.Abs(Mathf.Atan2(ex, ez)) / Mathf.PI;
                float neckTop = P(B.Head).y + (ChinHL + ToonHead.NeckTopHeight(_toon, a) * HeadUnit) * _hs;
                k *= Mathf.Lerp(1f, 0.6f, Smooth01(neckTop + 0.012f, neckTop + 0.05f, p.y));
                return new Vector3(p.x * k, p.y, _bb.NeckZ + dz * k);
            }

            private void EmitBasePart(BaseBody.Part part, int slot, bool neck)
            {
                int start = _buf.Positions.Count, n = part.VertexCount;
                var pos = new Vector3[n];
                for (int v = 0; v < n; v++)
                {
                    Vector3 p = Deform(part.Positions[v], part, v);
                    pos[v] = neck ? FitNeck(p) : p;
                }
                var nrm = new Vector3[n];
                var tri = part.Triangles;
                for (int i = 0; i < tri.Length; i += 3)
                {
                    int a = tri[i], b = tri[i + 1], c = tri[i + 2];
                    Vector3 f = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
                    nrm[a] += f; nrm[b] += f; nrm[c] += f;
                }
                for (int v = 0; v < n; v++)
                {
                    int o = v * 4;
                    _buf.Positions.Add(pos[v]);
                    _buf.Normals.Add(nrm[v].sqrMagnitude > 1e-20f ? nrm[v].normalized : Vector3.up);
                    _buf.Uvs.Add(Vector2.zero);
                    var w = part.Weights;
                    float sum = w[o] + w[o + 1] + w[o + 2] + w[o + 3];
                    float inv = sum > 0f ? 1f / sum : 0f;
                    _buf.Weights.Add(sum > 0f
                        ? new BoneWeight
                        {
                            boneIndex0 = part.BoneIndex[o], weight0 = w[o] * inv, boneIndex1 = part.BoneIndex[o + 1], weight1 = w[o + 1] * inv,
                            boneIndex2 = part.BoneIndex[o + 2], weight2 = w[o + 2] * inv, boneIndex3 = part.BoneIndex[o + 3], weight3 = w[o + 3] * inv,
                        }
                        : new BoneWeight { boneIndex0 = (int)B.Hips, weight0 = 1f });
                }
                var list = _buf.Slot(slot);
                for (int i = 0; i < tri.Length; i++) list.Add(start + tri[i]);
            }

            private void MeshBody() => EmitBasePart(_bb.Body, Slot("skin", _r.skinTone, SlotKind.Skin), true);

            /// <summary>Trunks (never come off) + waistband in the player slot colour.</summary>
            private void Trunks()
            {
                EmitBasePart(_bb.Trunks, Slot("trunks", _r.trunksColor, SlotKind.Cloth), false);
                EmitBasePart(_bb.Band, Slot("waistband", _r.outfitPrimary, SlotKind.Cloth), false);
            }
        }
    }
}
