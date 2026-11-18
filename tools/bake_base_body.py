#!/usr/bin/env python3
"""Bakes the anime base mesh (binary FBX, Z up, centimetres, facing -Y) into Assets/_Project/Resources/Bodies/BaseBody.bytes.

Usage:  python tools/bake_base_body.py "character base.fbx" Assets/_Project/Resources/Bodies/BaseBody.bytes
Needs numpy and scipy. The joint positions below were measured on that mesh; a different mesh needs new joints.
Format (little endian): "LBB1", int bones, float chinY, neckZ, trunksTop, trunksBottom, bones x float3 joints,
then 3 parts (body, trunks, waistband): int verts, int tris, verts x float3, verts x 4 byte bone ids,
verts x 4 float weights, tris x 3 ushort.
"""
import struct, zlib, sys, numpy as np
import scipy.sparse as sp, scipy.sparse.linalg as spl
from scipy.sparse.csgraph import connected_components


def read_fbx(path):
    """Minimal binary FBX reader: the first mesh's Vertices and PolygonVertexIndex."""
    data = open(path, 'rb').read()
    big = struct.unpack_from('<I', data, 23)[0] >= 7500

    def prop(o):
        t = chr(data[o]); o += 1
        fixed = {'Y': ('<h', 2), 'C': ('<B', 1), 'I': ('<i', 4), 'F': ('<f', 4), 'D': ('<d', 8), 'L': ('<q', 8)}
        if t in fixed:
            f, n = fixed[t]; return struct.unpack_from(f, data, o)[0], o + n
        if t in 'SR':
            n = struct.unpack_from('<I', data, o)[0]; return data[o + 4:o + 4 + n], o + 4 + n
        n, enc, cl = struct.unpack_from('<III', data, o); o += 12
        raw = data[o:o + cl]; o += cl
        if enc: raw = zlib.decompress(raw)
        f = {'f': 'f', 'd': 'd', 'l': 'q', 'i': 'i', 'b': 'B'}[t]
        return list(struct.unpack('<%d%s' % (n, f), raw)), o

    def node(o):
        if big: end, npr, _ = struct.unpack_from('<QQQ', data, o); o += 24
        else: end, npr, _ = struct.unpack_from('<III', data, o); o += 12
        nl = data[o]; name = data[o + 1:o + 1 + nl].decode(); o += 1 + nl
        if end == 0: return None, o
        props = []
        for _ in range(npr):
            val, o = prop(o); props.append(val)
        kids = []
        while o < end:
            k, o = node(o)
            if k is None: break
            kids.append(k)
        return (name, props, kids), end

    o = 27
    while o < len(data) - 200:
        nd, o = node(o)
        if nd is None: break
        if nd[0] != 'Objects': continue
        for c in nd[2]:
            if c[0] != 'Geometry': continue
            k = {x[0]: x[1][0] for x in c[2] if x[0] in ('Vertices', 'PolygonVertexIndex')}
            if len(k) == 2: return np.array(k['Vertices']).reshape(-1, 3), np.array(k['PolygonVertexIndex'])
    raise SystemExit('no mesh in ' + path)


# (bake) the owner's anime base mesh into the game's body asset:
# game coordinates (metres, +Y up, +Z forward, chin at 1.83), the mesh's own head + neck replaced by a clean neck tube that
# reaches up inside the Toon Head, skin weights for the 42-bone fighter rig (CharacterBone order) by heat diffusion, arms
# re-posed from the mesh's 14.6° to the rig's 40° A-pose so that the game's skinning gives back the sculpt when the arms
# hang at 14.6° again, trunks + waistband.

BONES = ("Hips Spine Chest UpperChest Neck Head "
         "LeftShoulder LeftUpperArm LeftLowerArm LeftHand RightShoulder RightUpperArm RightLowerArm RightHand "
         "LeftUpperLeg LeftLowerLeg LeftFoot LeftToes RightUpperLeg RightLowerLeg RightFoot RightToes "
         "LeftThumbProximal LeftThumbIntermediate LeftIndexProximal LeftIndexIntermediate LeftMiddleProximal LeftMiddleIntermediate "
         "LeftRingProximal LeftRingIntermediate LeftLittleProximal LeftLittleIntermediate "
         "RightThumbProximal RightThumbIntermediate RightIndexProximal RightIndexIntermediate RightMiddleProximal RightMiddleIntermediate "
         "RightRingProximal RightRingIntermediate RightLittleProximal RightLittleIntermediate").split()
NB = len(BONES); BI = {n: i for i, n in enumerate(BONES)}
assert NB == 42


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)


# ---------------------------------------------------------------- mesh in game coordinates
v, p = read_fbx(sys.argv[1])
tri = np.where(p < 0, ~p, p).reshape(-1, 3)
floor = v[:, 2].min(); CHIN = 1.83
s = CHIN / (127.0 - floor)
g = np.stack([v[:, 0] * s, (v[:, 2] - floor) * s, -(v[:, 1] - 3.1) * s], 1)
# neck centre just under the jaw
sl = g[(g[:, 1] > 1.79) & (g[:, 1] < 1.80)]
zc = float((sl[:, 2].min() + sl[:, 2].max()) / 2)

# keep only the body: the mesh's head + neck is a separate piece that only overlaps the shoulders by a few millimetres
# (skinned apart, the two opened a seam at the base of the neck); the body gets a new neck below
lab = connected_components(sp.coo_matrix((np.ones(tri.size), (tri.ravel(), np.roll(tri, 1, 1).ravel())), shape=(len(g), len(g))),
                           directed=False)[1]
tri = tri[lab[tri[:, 0]] == np.bincount(lab).argmax()]
used = np.unique(tri); remap = -np.ones(len(g), int); remap[used] = np.arange(len(used)); g = g[used]; tri = remap[tri]


def boundary_loop(tri, ok):
    """The open edge loop through vertices where ok, in the triangles' order (each edge a -> b as a triangle has it)."""
    e = np.concatenate([tri[:, [0, 1]], tri[:, [1, 2]], tri[:, [2, 0]]])
    _, inv, cnt = np.unique(np.sort(e, 1), axis=0, return_inverse=True, return_counts=True)
    b = e[(cnt[inv.ravel()] == 1) & ok[e[:, 0]] & ok[e[:, 1]]]
    nxt = dict(zip(b[:, 0].tolist(), b[:, 1].tolist()))
    loop = [int(b[0, 0])]
    while nxt[loop[-1]] != loop[0]: loop.append(nxt[loop[-1]])
    assert len(loop) == len(b), 'the neck opening is not one loop'
    return np.array(loop)


# new neck: a clean tube from the body's neck opening (1.79 m at the front, 1.81 m at the back) straight up to NECK_TOP,
# well inside the Toon Head. Its first 3 cm round off from the opening's shape into an ellipse; in the game its top is
# fitted just outside the head's neck collar (FitNeck) and turns with the head, so the collar's rim and the tube's top
# both stay hidden.
NECK_TOP, NECK_ROWS = 2.03, 24
rim = boundary_loop(tri, g[:, 1] > 1.7)
Rm = g[rim]
ex, ef, eb = np.abs(Rm[:, 0]).max(), Rm[:, 2].max() - zc, zc - Rm[:, 2].min()
phi = np.arctan2(Rm[:, 0] / ex, (Rm[:, 2] - zc) / np.where(Rm[:, 2] >= zc, ef, eb))
ell = np.stack([ex * np.sin(phi), np.zeros(len(Rm)), zc + np.where(np.cos(phi) >= 0, ef, eb) * np.cos(phi)], 1)
rows = [rim]
for k in range(1, NECK_ROWS + 1):
    yk = Rm[:, 1] + (NECK_TOP - Rm[:, 1]) * k / NECK_ROWS
    b = smoothstep(0, 0.03, yk - Rm[:, 1])
    P = Rm * (1 - b)[:, None] + ell * b[:, None]; P[:, 1] = yk
    rows.append(np.arange(len(g), len(g) + len(Rm))); g = np.concatenate([g, P])
quads = []
for k in range(NECK_ROWS):
    a, c = rows[k], rows[k + 1]
    for i in range(len(rim)):
        j = (i + 1) % len(rim)      # the body has the opening's edge a[i] -> a[j]; the tube runs it the other way
        quads += [(a[j], a[i], c[i]), (a[j], c[i], c[j])]
tri = np.concatenate([tri, np.array(quads)])
n = len(g)
neck_tube = np.zeros(n, bool); neck_tube[len(g) - NECK_ROWS * len(rim):] = True
print('verts', n, 'tris', len(tri), 'neck centre z', round(zc, 4), 'neck opening', len(rim), 'verts')

# fill the waist: the mesh pinches in between the ribs and the hips (0.144 m half width at 1.37 m against 0.171 m at
# 1.24 m and ~0.167 m under the armpits), which reads as a hungry dip from the front and back. Push the sides out
# halfway towards a straight rib-to-hip line (50 % of the gap, so a soft waist curve stays); the middle of the front and back stays where it is.
def waist_fill(g, y0=1.22, y1=1.47, fill=0.5):
    out = g.copy()
    flank = (g[:, 1] > y0 - 0.01) & (g[:, 1] < y1 + 0.01) & (np.abs(g[:, 0]) < 0.2)
    ys = np.arange(y0, y1 + 1e-6, 0.01)
    half = np.array([np.abs(g[flank & (np.abs(g[:, 1] - y) < 0.005), 0]).max() for y in ys])
    line = np.interp(ys, [y0 + 0.02, y1 - 0.01], [half[2], 0.167])
    gap = np.maximum(0, line - half) * fill
    gap *= np.sin(np.clip((ys - y0) / (y1 - y0), 0, 1) * np.pi) ** 0.5     # fades out at both ends
    m = np.where(flank)[0]
    w = np.interp(g[m, 1], ys, half); d = np.interp(g[m, 1], ys, gap)
    side = np.clip(np.abs(g[m, 0]) / w, 0, 1) ** 2.5
    out[m, 0] = g[m, 0] * (1 + d / w * side)
    return out, ys, half, half + gap
g, _ys, _before, _after = waist_fill(g)
print('waist fill: half width at 1.37 m', round(float(np.interp(1.37, _ys, _before)), 3), '->', round(float(np.interp(1.37, _ys, _after)), 3))

# ---------------------------------------------------------------- joints (right side measured, left mirrored)
J = np.zeros((NB, 3))
def setj(name, x, y, z, mirror=True):
    J[BI[name]] = (x, y, z)
    if mirror and name.startswith('Right'): J[BI['Left' + name[5:]]] = (-x, y, z)
setj('Hips', 0, 1.12, -0.01); setj('Spine', 0, 1.25, -0.005); setj('Chest', 0, 1.40, 0.0); setj('UpperChest', 0, 1.55, -0.005)
setj('Neck', 0, 1.715, -0.04); setj('Head', 0, CHIN + 0.035, zc)          # Head is re-placed per recipe in the game
setj('RightShoulder', 0.04, 1.66, -0.02)
setj('RightUpperArm', 0.213, 1.60, -0.05); setj('RightLowerArm', 0.278, 1.345, -0.048); setj('RightHand', 0.346, 1.088, -0.045)
setj('RightUpperLeg', 0.095, 1.085, 0.0); setj('RightLowerLeg', 0.155, 0.685, -0.012); setj('RightFoot', 0.19, 0.105, -0.068)
setj('RightToes', 0.215, 0.03, 0.115)
FZ = {'Index': 0.009, 'Middle': -0.019, 'Ring': -0.052, 'Little': -0.080}
FK = {'Index': 0.997, 'Middle': 0.999, 'Ring': 0.995, 'Little': 0.988}       # knuckle heights
FM = {'Index': 0.957, 'Middle': 0.955, 'Ring': 0.953, 'Little': 0.952}       # middle joint heights
FT = {'Index': 0.918, 'Middle': 0.912, 'Ring': 0.915, 'Little': 0.925}       # tips
for f in FZ:
    setj('Right%sProximal' % f, 0.360, FK[f], FZ[f]); setj('Right%sIntermediate' % f, 0.360, FM[f], FZ[f])
setj('RightThumbProximal', 0.350, 1.035, 0.024); setj('RightThumbIntermediate', 0.353, 1.003, 0.035)

# bone segments (start, end) for nearest-bone assignment
END = {}
END['Hips'] = J[BI['Spine']]; END['Spine'] = J[BI['Chest']]; END['Chest'] = J[BI['UpperChest']]; END['UpperChest'] = J[BI['Neck']]
END['Neck'] = np.array([0, 1.83, zc]); END['Head'] = np.array([0, 2.0, zc])
for side, S in ((1, 'Right'), (-1, 'Left')):
    END[S + 'Shoulder'] = np.array([side * 0.13, 1.645, -0.03]); END[S + 'UpperArm'] = J[BI[S + 'LowerArm']]; END[S + 'LowerArm'] = J[BI[S + 'Hand']]
    END[S + 'Hand'] = np.array([side * 0.358, 0.998, -0.03])
    END[S + 'UpperLeg'] = J[BI[S + 'LowerLeg']]; END[S + 'LowerLeg'] = J[BI[S + 'Foot']]; END[S + 'Foot'] = J[BI[S + 'Toes']]
    END[S + 'Toes'] = np.array([side * 0.235, 0.02, 0.19])
    for f in FZ:
        END[S + f + 'Proximal'] = J[BI[S + f + 'Intermediate']]; END[S + f + 'Intermediate'] = np.array([side * 0.36, FT[f], FZ[f]])
    END[S + 'ThumbProximal'] = J[BI[S + 'ThumbIntermediate']]; END[S + 'ThumbIntermediate'] = np.array([side * 0.355, 0.978, 0.041])

def seg_dist(P, a, b):
    ab = b - a; t = np.clip(((P - a) @ ab) / max(ab @ ab, 1e-12), 0, 1)
    return np.linalg.norm(P - (a + t[:, None] * ab), axis=1)

# the upper arm's segment reaches up into the shoulder cap so the deltoid follows the arm
START = J.copy()
for S in ('Right', 'Left'):
    ua, la = J[BI[S + 'UpperArm']], J[BI[S + 'LowerArm']]
    START[BI[S + 'UpperArm']] = ua - (la - ua) / np.linalg.norm(la - ua) * 0.06
    START[BI[S + 'Foot']] = J[BI[S + 'Foot']] + np.array([0, -0.07, -0.07])   # the heel belongs to the foot
D = np.stack([seg_dist(g, START[i], END[BONES[i]]) for i in range(NB)], 1)    # n x 42

# ---------------------------------------------------------------- regions → allowed bones
ax = np.abs(g[:, 0]); y = g[:, 1]; side = np.where(g[:, 0] >= 0, 1, -1)
TY = np.array([1.60, 1.55, 1.50, 1.45, 1.40, 1.35, 1.30, 1.25, 1.20, 1.15, 1.10, 1.05, 0.90])
TE = np.array([0.205, 0.186, 0.190, 0.19, 0.19, 0.195, 0.205, 0.22, 0.237, 0.25, 0.262, 0.27, 0.28])   # torso edge / arm gap midline
arm_thr = np.interp(y, TY[::-1], TE[::-1])
arm = (y < 1.585) & (y > 0.88) & (ax > arm_thr)
leg = (~arm) & (y < 1.07)
hand = arm & (y < 1.092)
allowed = np.zeros((n, NB), bool)
def allow(mask, names_by_side):
    for sd, S in ((1, 'Right'), (-1, 'Left')):
        m = mask & (side == sd)
        for nm in names_by_side(S): allowed[m, BI[nm]] = True
allow(arm & ~hand, lambda S: [S + 'UpperArm', S + 'LowerArm', S + 'Hand'])
allow(leg, lambda S: [S + 'UpperLeg', S + 'LowerLeg', S + 'Foot', S + 'Toes'])
torso = ~arm & ~leg
for nm in ['Hips', 'Spine', 'Chest', 'UpperChest', 'Neck']: allowed[torso, BI[nm]] = True
allow(torso, lambda S: [S + 'Shoulder'])
allow(torso & (y > 1.56), lambda S: [S + 'UpperArm'])   # only the shoulder cap; the lats and ribs stay on the torso
allow(torso & (y < 1.16), lambda S: [S + 'UpperLeg'])

Dm = np.where(allowed, D, 1e9)
nearest = Dm.argmin(1)
dist = Dm[np.arange(n), nearest]

# hand: palm → Hand; fingers by their position across the hand; thumb at the front
for sd, S in ((1, 'Right'), (-1, 'Left')):
    hm = np.where(hand & (side == sd))[0]
    P = g[hm]
    thumb = (P[:, 2] > 0.019) & (P[:, 1] < 1.055)
    for k, i in enumerate(hm):
        px, py, pz = P[k]
        if thumb[k]:
            b = S + ('ThumbProximal' if py > 1.008 else 'ThumbIntermediate')
        else:
            f = min(FZ, key=lambda f: abs(pz - FZ[f]))
            if py > FK[f]: b = S + 'Hand'
            else: b = S + f + ('Proximal' if py > FM[f] else 'Intermediate')
        nearest[i] = BI[b]; dist[i] = D[i, BI[b]]

# ---------------------------------------------------------------- skin weights by heat diffusion (bone heat)
# Each bone's weight w solves (L + M H) w = M H p over the surface: p = 1 where the bone is the vertex's nearest allowed
# bone, H = HEAT / d² (d = distance to that bone, clamped), L = cotangent Laplacian, M = vertex areas. The weights fall off
# smoothly over about a limb's thickness at every joint (elbows, knees, shoulders, spine), instead of the sharp steps that
# folded into creases when the joints turned.
HEAT, DMIN, DMAX = 1.0, 0.006, 0.05
a_, b_, c_ = tri.T
def cot(p0, p1, p2):
    u = g[p1] - g[p0]; w = g[p2] - g[p0]
    return np.clip((u * w).sum(1) / np.maximum(np.linalg.norm(np.cross(u, w), axis=1), 1e-12), 0, None)
ca, cb, cc = cot(a_, b_, c_), cot(b_, c_, a_), cot(c_, a_, b_)
Wl = sp.coo_matrix((0.5 * np.concatenate([ca, ca, cb, cb, cc, cc]),
                    (np.concatenate([b_, c_, c_, a_, a_, b_]), np.concatenate([c_, b_, a_, c_, b_, a_]))), shape=(n, n)).tocsr()
Lap = sp.diags(np.asarray(Wl.sum(1)).ravel()) - Wl
area = np.linalg.norm(np.cross(g[b_] - g[a_], g[c_] - g[a_]), axis=1) / 2
Mass = np.bincount(tri.ravel(), np.repeat(area, 3), n) / 3
H = Mass * HEAT / np.clip(dist, DMIN, DMAX) ** 2
Pn = np.zeros((n, NB)); Pn[np.arange(n), nearest] = 1
arm_bones = [BI[b] for b in BONES if ('Arm' in b or 'Hand' in b or 'Thumb' in b or 'Index' in b or 'Middle' in b or 'Ring' in b or 'Little' in b)]
body_cols = [i for i in range(NB) if i not in arm_bones]
W = np.zeros((n, NB))
W[:, body_cols] = spl.splu((Lap + sp.diags(H)).tocsc()).solve(H[:, None] * Pn[:, body_cols])
# no arm weight on the ribs and lats below the armpit (else they bulge when the arm moves) or on the trapezius next to the
# neck (else raised arms pull the base of the neck up): those are pinned to 0 for the arm bones, the pin fading in over
# a few centimetres so the arm's weight still falls off smoothly between them and the shoulder
pin = np.where(torso, np.maximum(smoothstep(1.53, 1.47, y), smoothstep(0.12, 0.07, ax)), 0)
Hp = H + Mass * 1e5 * pin ** 2
W[:, arm_bones] = spl.splu((Lap + sp.diags(Hp)).tocsc()).solve(H[:, None] * Pn[:, arm_bones])
W = np.clip(W, 0, None); W /= W.sum(1, keepdims=True)

# neck: from the base of the neck up into the head the tube turns with the head, fully from just above the bottom of the
# Toon Head's collar (its first neck row: 1.814 m at the front, 1.907 m at the back), so the part inside the head moves
# with it whatever the pose
collar_y = 1.814 + smoothstep(0, 1, (0.02 - g[:, 2]) / 0.11) * 0.093
hw = np.where(torso & (ax < 0.12), smoothstep(collar_y - 0.035, collar_y + 0.005, y), 0)
W *= (1 - hw)[:, None]
W[:, BI['Head']] += hw

def top4(W):
    idx = np.argsort(-W, 1)[:, :4]; w = np.take_along_axis(W, idx, 1)
    w = np.where(w < 0.01, 0, w); w /= w.sum(1, keepdims=True)
    return idx.astype(np.uint8), w.astype(np.float32)
WI, WW = top4(W)

# ---------------------------------------------------------------- re-pose the arms to the rig's 40° A-pose (rotation about the shoulder joint)
# Inverse skinning: the bind-pose position B of each vertex is chosen so that the game's linear skinning, turning the arm
# bones back down to the mesh's own 14.6°, lands exactly on the sculpt (B = c + M⁻¹ (g − c), M = (1 − w) I + w R_down,
# w = the vertex's arm weight). Arms at the sides look as modelled; a plain rotation would lose the shoulder's volume
# twice (once here, once in the game) and sink the shoulders.
ARM_NATIVE = np.degrees(np.arctan2(0.346 - 0.213, 1.60 - 1.088))
ROT = 40.0 - ARM_NATIVE
def rotz(deg):
    a = np.radians(deg); c, s_ = np.cos(a), np.sin(a)
    return np.array([[c, -s_, 0], [s_, c, 0], [0, 0, 1]])
armset = {sd: [BI[b] for b in BONES if b.startswith(S) and ('Arm' in b or 'Hand' in b or 'Thumb' in b or 'Index' in b or 'Middle' in b or 'Ring' in b or 'Little' in b)]
          for sd, S in ((1, 'Right'), (-1, 'Left'))}
def repose(P, WI, WW):
    out = P.copy()
    for sd in (1, -1):
        c = J[armset[sd][0]]
        wa = np.zeros(len(P))
        for k in range(4): wa += np.where(np.isin(WI[:, k], armset[sd]), WW[:, k], 0)
        m = wa > 0
        a = np.radians(-sd * ROT)                         # R_down: from the 40° bind back to the mesh's angle
        A = (1 - wa[m]) + wa[m] * np.cos(a); Bs = wa[m] * np.sin(a)
        d = P[m] - c; det = A * A + Bs * Bs
        out[m, 0] = c[0] + (A * d[:, 0] + Bs * d[:, 1]) / det
        out[m, 1] = c[1] + (-Bs * d[:, 0] + A * d[:, 1]) / det
    return out
gb = repose(g, WI, WW)
Jb = J.copy()
for sd in (1, -1):
    R = rotz(sd * ROT); c = J[armset[sd][0]]
    for b in armset[sd]: Jb[b] = (J[b] - c) @ R.T + c
print('arm native', round(ARM_NATIVE, 1), 'rotated by', round(ROT, 1))

# ---------------------------------------------------------------- trunks + waistband (copied from the body surface, weights interpolated)
fn = np.cross(g[tri[:, 1]] - g[tri[:, 0]], g[tri[:, 2]] - g[tri[:, 0]])
N = np.zeros_like(g)
for k in range(3): np.add.at(N, tri[:, k], fn)
N /= np.linalg.norm(N, axis=1)[:, None] + 1e-12
CROTCH = 1.0711
def band(lo, hi, off, maxx=0.235):
    P = []; Wl = []; T = []; key = {}
    def vid(k, p, nn, w):
        if k not in key:
            key[k] = len(P); P.append(p + nn / np.linalg.norm(nn) * off); Wl.append(w)
        return key[k]
    for t in tri:
        if abs(g[t].mean(0)[0]) > maxx: continue
        ys = g[t][:, 1]
        if ys.max() < lo or ys.min() > hi: continue
        poly = [(('v', int(i)), g[i], N[i], W[i]) for i in t]
        for y0, sg in ((lo, 1), (hi, -1)):
            out = []
            for j in range(len(poly)):
                kp, a, na, wa_ = poly[j]; kq, b, nb, wb = poly[(j + 1) % len(poly)]
                da = sg * (a[1] - y0); db = sg * (b[1] - y0)
                if da >= 0: out.append((kp, a, na, wa_))
                if (da >= 0) != (db >= 0):
                    u = da / (da - db)
                    out.append((('e', y0, min(kp, kq), max(kp, kq)), a + (b - a) * u, na + (nb - na) * u, wa_ + (wb - wa_) * u))
            poly = out
            if len(poly) < 3: break
        if len(poly) < 3: continue
        ids = [vid(*q) for q in poly]
        for j in range(1, len(ids) - 1): T.append((ids[0], ids[j], ids[j + 1]))
    P = np.array(P); i4, w4 = top4(np.array(Wl, np.float32))
    return repose(P, i4, w4), i4, w4, np.array(T)
TOP, BOT = CROTCH + 0.205, CROTCH - 0.075
trunks = band(BOT, TOP, 0.006)
waist = band(TOP - 0.026, TOP + 0.002, 0.0095)
print('trunks', len(trunks[0]), len(trunks[3]), 'band', len(waist[0]), len(waist[3]))

# ---------------------------------------------------------------- write  (Resources/Bodies/BaseBody.bytes)
out = sys.argv[2] if len(sys.argv) > 2 else 'BaseBody.bytes'
with open(out, 'wb') as f:
    f.write(b'LBB1'); f.write(struct.pack('<i', NB))
    f.write(struct.pack('<4f', CHIN, zc, TOP, BOT))
    f.write(Jb.astype('<f4').tobytes())
    for P, i4, w4, T in ((gb, WI, WW, tri), trunks, waist):
        f.write(struct.pack('<ii', len(P), len(T)))
        f.write(P.astype('<f4').tobytes()); f.write(i4.astype(np.uint8).tobytes()); f.write(w4.astype('<f4').tobytes())
        f.write(T.astype('<u2').tobytes())
print('wrote', out)
