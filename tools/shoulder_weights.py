"""Shoulder skin → collarbone (2026-11-18, after the owner found the shoulders "weird" in the basic attack).

The bake gave the whole top of the shoulder (trapezius slope and the cap over the joint) 60-95 % to the upper arm, and the
collarbone (CharacterBone.Left/RightShoulder) never moved. Any raised arm (a straight punch, the guard) then swung that
skin backwards round the shoulder joint: the cap caved in and the arm looked like it came out of the chest.

Here the upper arm keeps only part of its weight on the shoulder, by how far a point lies along the arm from the shoulder
joint (t, in the sculpt's own arm direction): above the joint (t <= -0.13 m: trapezius slope, top of the cap) the weight
goes to the collarbone, from t = +0.05 m on (the deltoid down the arm, the armpit) it stays on the arm, smoothstep between
(18 cm, more than a limb's thickness, so nothing creases). Away from the neck the cap keeps a quarter of its arm weight. The game turns the collarbone part of the way with the arm
(CharacterRig.FollowArm), so the cap rises and rolls forward with a punch instead of folding.

The bind positions are re-solved with the bake's inverse skinning (B = c + M^-1 (g - c), M = (1 - w) I + w R_down) so the
game's skinning still lands exactly on the sculpt with the arms down at the mesh's 14.6 degrees.

    python tools/shoulder_weights.py Assets/_Project/Resources/Bodies/BaseBody.bytes [out.bytes]

Refuses a body that already has it. bake_base_body.py applies the same step (move_to_collarbone) when baking from the FBX.
"""
import struct
import sys

import numpy as np

T0, T1 = -0.13, 0.05          # along the arm from the shoulder joint: all collarbone .. all upper arm
CAP_KEEP = 0.25               # the cap (away from the neck) keeps this much of its arm weight, so it still rounds over a raised arm
ARM_NATIVE = np.degrees(np.arctan2(0.346 - 0.213, 1.60 - 1.088))   # the sculpt's arm angle (same as the bake)
ROT = 40.0 - ARM_NATIVE

BONES = ["Hips", "Spine", "Chest", "UpperChest", "Neck", "Head",
         "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
         "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes", "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes"]
FINGERS = ["Thumb", "Index", "Middle", "Ring", "Little"]
for S in ("Left", "Right"):
    for f in FINGERS:
        BONES += [S + f + "Proximal", S + f + "Intermediate"]
BI = {b: i for i, b in enumerate(BONES)}
NB = len(BONES)


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def arm_set(S):
    return [BI[b] for b in BONES if b.startswith(S) and ("Arm" in b or "Hand" in b or any(f in b for f in FINGERS))]


def move_to_collarbone(g, W, J):
    """g: sculpt positions (n x 3, arms at ARM_NATIVE), W: dense weights (n x NB), J: sculpt joints. Returns new W."""
    W = W.copy()
    for sd, S in ((1, "Right"), (-1, "Left")):
        ua, sh = BI[S + "UpperArm"], BI[S + "Shoulder"]
        a = np.radians(ARM_NATIVE)
        d = np.array([sd * np.sin(a), -np.cos(a), 0.0])
        t = (g - J[ua]) @ d
        cap = CAP_KEEP * smoothstep(0.13, 0.20, np.abs(g[:, 0]))      # none on the trapezius by the neck
        keep = cap + (1 - cap) * smoothstep(T0, T1, t)
        mine = (np.sign(g[:, 0]) == sd) | (np.abs(g[:, 0]) < 1e-6)
        moved = np.where(mine, W[:, ua] * (1 - keep), 0)
        W[:, ua] -= moved
        W[:, sh] += moved
    return W


def top4(W):
    idx = np.argsort(-W, 1)[:, :4]
    w = np.take_along_axis(W, idx, 1)
    w = np.where(w < 0.01, 0, w)
    w /= w.sum(1, keepdims=True)
    return idx.astype(np.uint8), w.astype(np.float32)


def arm_weight(WI, WW, S):
    s = arm_set(S)
    wa = np.zeros(len(WI))
    for k in range(4):
        wa += np.where(np.isin(WI[:, k], s), WW[:, k], 0)
    return wa


def skin_down(B, WI, WW, J):
    """Forward: bind positions -> sculpt (the arms turned down by ROT about the shoulder joint, by arm weight)."""
    g = B.copy()
    for sd, S in ((1, "Right"), (-1, "Left")):
        c = J[BI[S + "UpperArm"]]
        wa = arm_weight(WI, WW, S)
        m = wa > 0
        a = np.radians(-sd * ROT)
        A = (1 - wa[m]) + wa[m] * np.cos(a)
        Bs = wa[m] * np.sin(a)
        dx, dy = B[m, 0] - c[0], B[m, 1] - c[1]
        g[m, 0] = c[0] + A * dx - Bs * dy
        g[m, 1] = c[1] + Bs * dx + A * dy
    return g


def repose(g, WI, WW, J):
    """Inverse of skin_down (the bake's repose): sculpt -> bind positions for these weights."""
    out = g.copy()
    for sd, S in ((1, "Right"), (-1, "Left")):
        c = J[BI[S + "UpperArm"]]
        wa = arm_weight(WI, WW, S)
        m = wa > 0
        a = np.radians(-sd * ROT)
        A = (1 - wa[m]) + wa[m] * np.cos(a)
        Bs = wa[m] * np.sin(a)
        d = g[m] - c
        det = A * A + Bs * Bs
        out[m, 0] = c[0] + (A * d[:, 0] + Bs * d[:, 1]) / det
        out[m, 1] = c[1] + (-Bs * d[:, 0] + A * d[:, 1]) / det
    return out


def main():
    src = sys.argv[1]
    dst = sys.argv[2] if len(sys.argv) > 2 else src
    b = open(src, "rb").read()
    assert b[:4] == b"LBB1", "not a baked base body"
    nb = struct.unpack_from("<i", b, 4)[0]
    assert nb == NB
    head = 4 + 4 + 16
    Jb = np.frombuffer(b, "<f4", nb * 3, head).reshape(-1, 3).astype(np.float64)
    o = head + nb * 12
    nv, nt = struct.unpack_from("<ii", b, o)
    p0 = o + 8
    B = np.frombuffer(b, "<f4", nv * 3, p0).reshape(-1, 3).astype(np.float64)
    WI = np.frombuffer(b, "u1", nv * 4, p0 + nv * 12).reshape(-1, 4)
    WW = np.frombuffer(b, "<f4", nv * 4, p0 + nv * 16).reshape(-1, 4).astype(np.float64)
    rest = b[p0 + nv * 32:]   # triangles of the body, then the trunks and the waistband: unchanged

    W = np.zeros((nv, NB))
    for k in range(4):
        np.add.at(W, (np.arange(nv), WI[:, k]), WW[:, k])
    # already done? the top of the shoulder would belong to the collarbone
    top = (np.abs(B[:, 0] - 0.16) < 0.01) & (B[:, 1] > 1.7)
    if W[top][:, BI["RightShoulder"]].mean() > W[top][:, BI["RightUpperArm"]].mean():
        sys.exit("this body already has the collarbone shoulders")

    # the shoulder joints are the turning centres and stay where they are, so the bind joints serve for the sculpt too
    g = skin_down(B, WI, WW, Jb)
    W2 = move_to_collarbone(g, W, Jb)
    WI2, WW2 = top4(W2)
    B2 = repose(g, WI2, WW2.astype(np.float64), Jb)
    check = skin_down(B2, WI2, WW2.astype(np.float64), Jb)
    print("vertices moved", int((np.abs(B2 - B).max(1) > 1e-5).sum()), "max", round(float(np.abs(B2 - B).max()), 4),
          "sculpt error", float(np.abs(check - g).max()))
    with open(dst, "wb") as f:
        f.write(b[:p0])
        f.write(B2.astype("<f4").tobytes())
        f.write(WI2.tobytes())
        f.write(WW2.astype("<f4").tobytes())
        f.write(rest)
    print("wrote", dst)


if __name__ == "__main__":
    main()
