"""Sharper elbows (and optionally knees) on the baked body (2026-11-19, owner: "the elbow bend looks too rubbery ... the elbow
flattens when it gets bent").

The heat-diffused weights blend upper arm -> forearm over +-8 cm of a ~9 cm thick arm (forearm share 0.09 -> 0.93), the
same on every side. Bent, the back of the elbow became a long soft U: skin halfway between the bones cuts the corner.
Here the split between the two limb bones is redone by where a vertex sits along the limb (t, from the joint) and round it
(how far towards the OUTSIDE of the bend: elbow = back, knee = front):

  forearm (shin) share = smoothstep(c - w, c + w, t),  w = 1.2 cm on the outside (a real point, riding with the forearm
  like the olecranon: c = -0.8 cm) blending to 4.5 cm on the inside of the bend (soft, so the fold doesn't pinch)

Only the split between the two bones changes (their sum stays), so the bind shape is untouched: the bake turned the
whole arm chain rigidly, so its inverse skinning doesn't depend on the split. Hand / finger / torso weights stay.

    python tools/joint_weights.py Assets/_Project/Resources/Bodies/BaseBody.bytes [out.bytes] [--knees]
"""
import math
import struct
import sys

import numpy as np

# (upper bone, lower bone, joint bone, limb direction in the bind pose, outside-of-bend direction, inner / outer half width,
# centre shift, limb radius for the region)
SIN, COS = math.sin(math.radians(40)), math.cos(math.radians(40))
ELBOWS = [(7, 8, 8, (-SIN, -COS, 0), (0, 0, -1)), (11, 12, 12, (SIN, -COS, 0), (0, 0, -1))]
KNEES = [(14, 15, 15, (0, -1, 0), (0, 0, 1)), (18, 19, 19, (0, -1, 0), (0, 0, 1))]
SHAPE = {"elbow": dict(w_in=0.045, w_out=0.012, c=-0.008, reach=0.11, rad=0.09),
         "knee": dict(w_in=0.05, w_out=0.018, c=-0.01, reach=0.13, rad=0.12)}


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    knees = "--knees" in sys.argv
    src = args[0]
    dst = args[1] if len(args) > 1 else src
    b = bytearray(open(src, "rb").read())
    assert b[:4] == b"LBB1"
    nb = struct.unpack_from("<i", b, 4)[0]
    J = np.frombuffer(bytes(b), "<f4", nb * 3, 24).reshape(-1, 3).astype(np.float64)
    o = 24 + nb * 12
    nv, nt = struct.unpack_from("<ii", b, o)
    p0 = o + 8
    pos = np.frombuffer(bytes(b), "<f4", nv * 3, p0).reshape(-1, 3).astype(np.float64)
    bi = np.frombuffer(bytes(b), "u1", nv * 4, p0 + nv * 12).reshape(-1, 4).copy()
    bw = np.frombuffer(bytes(b), "<f4", nv * 4, p0 + nv * 16).reshape(-1, 4).astype(np.float64).copy()
    W = np.zeros((nv, nb))
    for j in range(4):
        np.add.at(W, (np.arange(nv), bi[:, j]), bw[:, j])

    jobs = [("elbow", e) for e in ELBOWS] + ([("knee", k) for k in KNEES] if knees else [])
    for kind, (ub, lb, jb, d, out_dir) in jobs:
        s = SHAPE[kind]
        d, out_dir = np.array(d), np.array(out_dir, float)
        rel = pos - J[jb]
        t = rel @ d
        radial = rel - np.outer(t, d)
        r = np.linalg.norm(radial, axis=1)
        pair = W[:, ub] + W[:, lb]
        sel = (np.abs(t) < s["reach"]) & (r < s["rad"]) & (pair > 1e-4) & ((W[:, ub] > 0) | (W[:, lb] > 0))
        outer = (radial @ out_dir) / np.maximum(r, 1e-6)
        w = s["w_in"] + (s["w_out"] - s["w_in"]) * smoothstep(-0.2, 0.8, outer)
        share = smoothstep(s["c"] - w, s["c"] + w, t)
        # fade the new split into the old one at the edge of the region, so nothing jumps
        old = W[:, lb] / np.maximum(pair, 1e-9)
        edge = smoothstep(s["reach"], s["reach"] * 0.7, np.abs(t))
        new = old + (share - old) * edge
        W[sel, lb] = pair[sel] * new[sel]
        W[sel, ub] = pair[sel] * (1 - new[sel])
        print("%s %d/%d: %d vertices re-split" % (kind, ub, lb, sel.sum()))

    idx = np.argsort(-W, 1)[:, :4]
    ww = np.take_along_axis(W, idx, 1)
    ww = np.where(ww < 0.01, 0, ww)
    ww /= ww.sum(1, keepdims=True)
    b[p0 + nv * 12: p0 + nv * 16] = idx.astype(np.uint8).tobytes()
    b[p0 + nv * 16: p0 + nv * 32] = ww.astype("<f4").tobytes()
    open(dst, "wb").write(bytes(b))
    print("wrote", dst)


if __name__ == "__main__":
    main()
