"""The flame aura in 3D: built in Blender, baked for the game (2026-11-22, owner: "the shape is good but it looks too flat").

    blender --background --python tools/aura_blender.py -- poses     PlayerRig's charge / surge poses -> stand-in bodies in aura.blend
    blender --background --python tools/aura_blender.py -- build     (re)make the eight drawings in ArtSource/Aura/aura.blend
    blender --background --python tools/aura_blender.py -- check     each view's silhouette against the traced outline
    blender --background --python tools/aura_blender.py -- export    aura.blend -> Resources/VFX/AuraFlame.bytes + the bench's data

The aura keeps the owner's traced outline (TIP / NOTCH, the numbers the flat shader used): seen from any side its silhouette is
that flame "pine tree". What's new is that it is a real shape: rings of flame blades (one ring per traced tier, each blade's
profile is that tier's root -> tip -> notch) over an inner skin of wider tongues, wrapped round the fighter.

It rises like the reference clip: 4 keys lift every ring one tier (0.45 s), the bottom ring grows out of the base and the top one
melts into the spire. Eight keys (two tiers) make the loop, each a mesh of its own (Aura.d0 .. Aura.d7, same topology, keyed on the
timeline), and Aura.d8 closes it. The game blends smoothly from each key to the next (owner 2026-11-22: stepped drawings weren't
smooth). Scrub them, or edit a key by hand and run `export`; if you edit d0, make the same edit in d8's matching ring.

Mesh space ("aura units"): the game's axes (x right, y up, z forward), feet at 0, the outline's peak at y = 1. The game scales it
to the fighter and bends it along the spine in the vertex shader (Resources/VFX/AnimeAura.shader). Per-vertex attributes:
    edge    distance to the blade's outline (0 on it): the crisp, antialiased edge and the bright rim
    across  -1 .. 1 across the blade        along  0 root .. 1 tip
    rnd     one random value per blade      kind   0 blade, 1 skin, 2 flick
    az      the blade's direction from the axis (radians): the shader draws the far half first, the near half after
"""
import base64
import json
import math
import os
import struct
import sys

import bpy
from mathutils import Matrix, Vector

try:
    HERE = os.path.dirname(os.path.abspath(__file__))
    REPO = os.path.abspath(os.path.join(HERE, ".."))
    if not os.path.isfile(os.path.join(REPO, "CLAUDE.md")):
        raise NameError
except NameError:  # run from Blender's Text Editor: find the repo from the open .blend
    REPO = os.path.abspath(os.path.join(os.path.dirname(bpy.data.filepath), "..", ".."))
BLEND = os.path.join(REPO, "ArtSource", "Aura", "aura.blend")
PLAYER = os.path.join(REPO, "ArtSource", "Player", "player.blend")
BYTES = os.path.join(REPO, "Assets", "_Project", "Resources", "VFX", "AuraFlame.bytes")
BENCH = os.path.join(REPO, "docs", "bench", "aura-bench.html")

TAU = math.pi * 2

# the owner's traced outline, right half, in units of the aura's height (radius, height): each tier's tip and the notch above it.
# 1..6 are the traced tiers; 0 is the bottom corner where a new tier grows from, 7 the spire where the top one melts away
TIP = ((0.252, 0.000), (0.457, 0.150), (0.449, 0.346), (0.319, 0.583), (0.232, 0.732), (0.157, 0.776), (0.087, 0.913), (0.035, 0.961))
NOTCH = ((0.252, 0.000), (0.299, 0.142), (0.248, 0.268), (0.224, 0.488), (0.165, 0.583), (0.130, 0.740), (0.055, 0.850), (0.035, 0.961))
PEAK = (0.0, 1.0)

DRAWINGS = 8            # the loop: two tiers of rise, 4 drawings each (the reference clip's cadence)
DRAWING_SECONDS = 0.1125
RINGS = 8               # rings on the conveyor (7 alive at a time, the 8th is wrapping from the spire to the base)
BLADES = 7              # blades per ring: few and big, like the outline's spikes (odd, so no blade has a twin straight across)
SWIRL = 0.22            # radians a blade drifts round the body per tier: the near side and far side cross, which reads as depth
REACH = 1.06            # a ring's blades only sit exactly side-on now and then: a little extra reach keeps the outline's width
BLADE_ROWS = (0.0, 0.22, 0.44, 0.64, 0.80, 0.92, 1.0)
SKIN_ROWS = (0.0, 0.36, 0.72, 1.0)
FLICKS = 18
KIND_BLADE, KIND_SKIN, KIND_FLICK = 0, 1, 2

PREVIEW_HEIGHT = 2.3    # the fighter
PREVIEW_SCALE = 2.3 / 0.62 * 0.952   # aura units -> metres at charging size, as AnimeAura.cs does
DISPLAY = Matrix(((-1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))   # PlayerRig's display turn: game axes -> Blender's


def hash01(*key):
    s = 0x811C9DC5
    for v in key:
        for b in struct.pack("<i", int(v)):
            s = ((s ^ b) * 0x01000193) & 0xFFFFFFFF
    s ^= s >> 13
    s = (s * 0x5BD1E995) & 0xFFFFFFFF
    s ^= s >> 15
    return s / 4294967296.0


def table(T, tau):
    tau = max(0.0, min(7.0, tau))
    i = min(int(tau), 6)
    f = tau - i
    return (T[i][0] + (T[i + 1][0] - T[i][0]) * f, T[i][1] + (T[i + 1][1] - T[i][1]) * f)


TRUNK = [(0.0, 0.252)] + [(NOTCH[i][1], NOTCH[i][0]) for i in range(1, 8)] + [(1.0, 0.0)]


def trunk_radius(y):
    if y <= 0:
        return TRUNK[0][1]
    for (y0, r0), (y1, r1) in zip(TRUNK, TRUNK[1:]):
        if y <= y1:
            return r0 + (r1 - r0) * (y - y0) / max(y1 - y0, 1e-6)
    return 0.0


def traced_polygon():
    right = [TIP[0]]
    for i in range(1, 7):
        right += [TIP[i], NOTCH[i]]
    right += [TIP[7], PEAK]
    return right + [(-x, y) for x, y in reversed(right[:-1])]


class Geo:
    def __init__(self):
        self.co, self.tris, self.across, self.along, self.edge, self.rnd, self.az, self.kind = [], [], [], [], [], [], [], []

    def vert(self, p, across, along, edge, rnd, az, kind):
        self.co.append(tuple(p))
        self.across.append(across)
        self.along.append(along)
        self.edge.append(edge)
        self.rnd.append(rnd)
        self.az.append(az)
        self.kind.append(kind)
        return len(self.co) - 1

    def strip(self, rows):
        for r0, r1 in zip(rows, rows[1:]):
            if len(r1) == 1:
                t = r1[0]
                for a, b in zip(r0, r0[1:]):
                    self.tris.append((a, t, b))
            else:
                for j in range(len(r0) - 1):
                    a, b, c, d = r0[j], r0[j + 1], r1[j + 1], r1[j]
                    self.tris.append((a, d, c))
                    self.tris.append((a, c, b))


def polar(r, y, az):
    return Vector((r * math.cos(az), y, r * math.sin(az)))


def bowed(p, q, a, bow):
    """The point a of the way along p -> q, pushed sideways by bow * |pq| at the middle (+ = left of the direction)."""
    x, y = p[0] + (q[0] - p[0]) * a, p[1] + (q[1] - p[1]) * a
    s = math.sin(math.pi * a) * bow
    return (x - (q[1] - p[1]) * s, y + (q[0] - p[0]) * s)


def add_blade(g, root, tip, notch, phi, half_width, curl, ridge, rnd, rows=BLADE_ROWS):
    """One flame blade: in the (radius, height) plane it is the tier's root -> tip -> notch, so side-on it IS the traced shape.
    Its lower edge is swept one way round the body and its upper edge the other, which turns the flat fin into a pitched blade."""
    tip3 = polar(tip[0], tip[1], phi + curl)
    grid = []
    for ia, a in enumerate(rows):
        if ia == len(rows) - 1:
            grid.append([g.vert(tip3, 0.0, 1.0, 0.0, rnd, phi, KIND_BLADE)])
            break
        low = bowed(root, tip, a, -0.07)     # the outer edge bulges a little
        up = bowed(notch, tip, a, -0.11)     # the underside of the next tier is hollowed, so the tip hooks upward
        centre = phi + curl * a * a
        w = half_width * (1.0 - a) ** 1.1
        pl, pu = polar(low[0], low[1], centre - w), polar(up[0], up[1], centre + w)
        mid = (pl + pu) * 0.5
        across = pu - pl
        hw = across.length * 0.5
        normal = across.cross(tip3 - mid)
        if normal.length > 1e-9:
            mid += normal.normalized() * ridge * hw     # a soft ridge down the blade, so it catches the shading
        grid.append([g.vert(pl, -1.0, a, 0.0, rnd, phi, KIND_BLADE),
                     g.vert(mid, 0.0, a, hw, rnd, phi, KIND_BLADE),
                     g.vert(pu, 1.0, a, 0.0, rnd, phi, KIND_BLADE)])
    g.strip(grid)


def add_skin(g, tau, amp, phi, half_width, rnd):
    """One tongue of the inner skin: a wide scale lying on the trunk (the notch line), pointed at the top."""
    yl, yh = table(NOTCH, tau - 1.25)[1], table(NOTCH, tau + 0.2)[1]
    grid = []
    for ia, a in enumerate(SKIN_ROWS):
        y = yl + (yh - yl) * a
        r = trunk_radius(y) * (1.0 + 0.03 * a)
        if ia == len(SKIN_ROWS) - 1:
            grid.append([g.vert(polar(r * 0.97, y, phi), 0.0, 1.0, 0.0, rnd, phi, KIND_SKIN)])
            break
        w = half_width * amp * (1.0 - a ** 1.7)
        pl, pu = polar(r * 0.94, y, phi - w), polar(r * 0.94, y, phi + w)
        mid = polar(r * 0.99, y, phi)
        hw = (pu - pl).length * 0.5
        grid.append([g.vert(pl, -1.0, a, 0.0, rnd, phi, KIND_SKIN),
                     g.vert(mid, 0.0, a, hw, rnd, phi, KIND_SKIN),
                     g.vert(pu, 1.0, a, 0.0, rnd, phi, KIND_SKIN)])
    g.strip(grid)


def add_flick(g, p, length, width, az, rnd):
    """A small dash of flame rising inside the aura: two crossed diamonds, so it has no edge-on side."""
    for turn in (0.0, math.pi * 0.5):
        d = Vector((math.cos(az + turn) * width, 0.0, math.sin(az + turn) * width))
        up = Vector((0.0, length, 0.0))
        c = g.vert(p, 0.0, 0.5, width, rnd, az, KIND_FLICK)
        ring = [g.vert(p - up * 0.6, 0.0, 0.0, 0.0, rnd, az, KIND_FLICK), g.vert(p + d, 1.0, 0.45, 0.0, rnd, az, KIND_FLICK),
                g.vert(p + up, 0.0, 1.0, 0.0, rnd, az, KIND_FLICK), g.vert(p - d, -1.0, 0.45, 0.0, rnd, az, KIND_FLICK)]
        for i in range(4):
            g.tris.append((c, ring[i], ring[(i + 1) % 4]))


def ring_place(ring, drawing):
    """Where a ring is on the conveyor in a drawing: tiers 0..7, past 7 it has melted and is on its way back to the base."""
    return (ring + drawing / 4.0) % RINGS


def amp_of(tau):
    if tau >= 7:
        return 0.0
    return min(tau, 1.0) if tau < 1 else (7.0 - tau if tau > 6 else 1.0)


def boil(drawing, ident, i, size=0.014):
    return ((hash01(drawing, ident, i, 11) - 0.5) * size, (hash01(drawing, ident, i, 12) - 0.5) * size)


def collapse(g, start):
    """A blade that isn't alive in this key becomes one point, so blending to its next place can't draw a sliver across the aura."""
    for i in range(start, len(g.co)):
        g.co[i] = g.co[start]
        g.edge[i] = 0.0


def build_drawing(d):
    """Key d of the loop. Key 8 is key 0 with every ring two places on: the same picture, but each vertex carries on from key 7."""
    g = Geo()
    look = d % DRAWINGS                          # the boil and flicker repeat with the loop
    pitch = TAU / BLADES
    order = list(reversed(range(RINGS)))         # the higher rings first: each lower ring's tips lie over the one above
    # inner skin
    for ring in order:
        tau = ring_place(ring, d)
        amp = amp_of(tau)
        cls = ring % 2
        for k in range(BLADES):
            rnd = hash01(5, cls, k)
            phi = pitch * (k + 0.5 * cls + 0.5) + SWIRL * tau + (hash01(6, cls, k) - 0.5) * 0.16
            start = len(g.co)
            add_skin(g, min(tau, 7.0), amp, phi, pitch * (0.60 + 0.14 * rnd), rnd)
            if amp <= 0.0:
                collapse(g, start)
    # the flame blades: the traced tiers
    for ring in order:
        tau = ring_place(ring, d)
        amp = amp_of(tau)
        cls = ring % 2
        for k in range(BLADES):
            rnd = hash01(1, cls, k)
            ident = cls * 100 + k
            phi = pitch * (k + 0.5 * cls) + SWIRL * tau + (hash01(2, cls, k) - 0.5) * 0.22
            root, tip, notch = table(NOTCH, tau - 1.0), table(TIP, tau), table(NOTCH, tau)
            if tau >= 7:
                root = tip = notch = TIP[7]
            reach = (1.0 + (hash01(3, cls, k) - 0.5) * 0.2 * amp) * (1.0 + (REACH - 1.0) * amp)
            tip = (root[0] + (tip[0] - root[0]) * reach, root[1] + (tip[1] - root[1]) * reach)
            bt, bn = boil(look, ident, 1), boil(look, ident, 2)
            tip = (tip[0] + bt[0] * amp, tip[1] + bt[1] * amp)
            notch = (notch[0] + bn[0] * amp, notch[1] + bn[1] * amp)
            curl = (hash01(4, cls, k) - 0.3) * 0.5
            start = len(g.co)
            add_blade(g, root, tip, notch, phi, pitch * (0.34 + 0.14 * rnd) * max(amp, 0.0), curl, 0.3, rnd)
            if amp <= 0.0:
                collapse(g, start)
    # the spire: three thin blades meeting at the one peak, which flickers (+-3 %) from drawing to drawing
    peak = 1.0 + (hash01(look, 99) - 0.5) * 0.06
    for i in range(3):
        rnd = hash01(20, i)
        b = boil(look, 300 + i, 1)
        phi = TAU * i / 3.0 + 0.4 + 0.12 * math.sin(TAU * d / DRAWINGS)
        add_blade(g, (0.088, 0.800), (0.006 + b[0] * 0.4, peak), (0.046, 0.872), phi, 0.75, 0.3, 0.25, rnd)
    # flicks rising inside: each is born (size 0), climbs and dies (size 0) over the loop's keys, so it never visibly jumps back
    for i in range(FLICKS):
        u = ((look + int(hash01(30, i) * DRAWINGS)) % DRAWINGS) / (DRAWINGS - 1.0)
        size = math.sin(math.pi * u)
        az = TAU * hash01(31, i)
        r = 0.05 + 0.17 * hash01(32, i)
        y = 0.06 + 0.42 * hash01(33, i) + 0.26 * u
        add_flick(g, polar(r, y, az), 0.04 * size + 1e-4, 0.005 * size + 1e-5, az, hash01(34, i))
    return g


# ------------------------------------------------------------------ Blender objects
def collection(name):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    return c


def preview_material():
    m = bpy.data.materials.get("AuraPreview")
    if m is not None:
        return m
    m = bpy.data.materials.new("AuraPreview")
    m.use_nodes = True
    m.surface_render_method = "BLENDED"
    m.use_backface_culling = False
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    edge = nt.nodes.new("ShaderNodeAttribute")
    edge.attribute_name = "edge"
    ramp = nt.nodes.new("ShaderNodeMapRange")
    ramp.inputs[1].default_value, ramp.inputs[2].default_value = 0.002, 0.03
    ramp.inputs[3].default_value, ramp.inputs[4].default_value = 1.0, 0.0
    mix = nt.nodes.new("ShaderNodeMixRGB")
    mix.inputs[1].default_value = (1.0, 0.62, 0.03, 1)
    mix.inputs[2].default_value = (1.0, 0.95, 0.55, 1)
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs[1].default_value = 1.6
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    sh = nt.nodes.new("ShaderNodeMixShader")
    sh.inputs[0].default_value = 0.72
    nt.links.new(edge.outputs[2], ramp.inputs[0])
    nt.links.new(ramp.outputs[0], mix.inputs[0])
    nt.links.new(mix.outputs[0], em.inputs[0])
    nt.links.new(tr.outputs[0], sh.inputs[1])
    nt.links.new(em.outputs[0], sh.inputs[2])
    nt.links.new(sh.outputs[0], out.inputs[0])
    return m


def fill_mesh(me, g):
    me.clear_geometry()
    me.from_pydata(g.co, [], g.tris)
    for name, values in (("edge", g.edge), ("across", g.across), ("along", g.along), ("rnd", g.rnd), ("az", g.az), ("kind", g.kind)):
        a = me.attributes.get(name) or me.attributes.new(name, "FLOAT", "POINT")
        a.data.foreach_set("value", [float(v) for v in values])
    me.update()


def key_visibility(ob, d):
    """Each drawing is shown for one drawing's worth of frames (80 fps: 9 frames = 0.1125 s)."""
    ob.animation_data_clear()
    hold = 9
    for f in (d * hold, (d + 1) * hold):
        hidden = f != d * hold
        for prop in ("hide_viewport", "hide_render"):
            setattr(ob, prop, hidden)
            ob.keyframe_insert(prop, frame=f)
    if d > 0:
        for prop in ("hide_viewport", "hide_render"):
            setattr(ob, prop, True)
            ob.keyframe_insert(prop, frame=0)
    act = ob.animation_data.action
    for fc in act.layers[0].strips[0].channelbag(act.slots[0]).fcurves:
        for kp in fc.keyframe_points:
            kp.interpolation = "CONSTANT"
    for prop in ("hide_viewport", "hide_render"):
        setattr(ob, prop, d != 0)


def build():
    coll = collection("AuraFlame")
    mat = preview_material()
    root = bpy.data.objects.get("AuraRoot")
    if root is None:
        root = bpy.data.objects.new("AuraRoot", None)
        coll.objects.link(root)
    root.matrix_world = DISPLAY @ Matrix.Scale(PREVIEW_SCALE, 4)
    root.empty_display_size = 0.1
    counts = set()
    for d in range(DRAWINGS + 1):        # + the closing key (Aura.d8, never shown: where each vertex of d7 is heading)
        name = "Aura.d%d" % d
        g = build_drawing(d)
        counts.add((len(g.co), len(g.tris)))
        ob = bpy.data.objects.get(name)
        if ob is None:
            me = bpy.data.meshes.new(name)
            ob = bpy.data.objects.new(name, me)
            coll.objects.link(ob)
            ob.parent = root
        fill_mesh(ob.data, g)
        if mat.name not in [m.name for m in ob.data.materials if m]:
            ob.data.materials.append(mat)
        if d < DRAWINGS:
            key_visibility(ob, d)
        else:
            ob.animation_data_clear()
            ob.hide_viewport = ob.hide_render = True
    if len(counts) != 1:
        sys.exit("the drawings don't share a topology: %s" % counts)
    scene = bpy.context.scene
    for ob in bpy.data.objects:      # one stand-in body at a time; a dark world, so the flame reads in a rendered view
        if ob.name.startswith("Body."):
            ob.hide_viewport = ob.hide_render = ob.name != "Body.charge"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("AuraDark")
        scene.world.use_nodes = True
        bg = next(n for n in scene.world.node_tree.nodes if n.type == "BACKGROUND")
        bg.inputs[0].default_value = (0.012, 0.014, 0.022, 1)
    scene.render.fps, scene.render.fps_base = 80, 1.0
    scene.frame_start, scene.frame_end = 0, DRAWINGS * 9 - 1
    scene.frame_set(0)
    v, t = counts.pop()
    print("AURA built %d drawings: %d vertices, %d triangles each" % (DRAWINGS, v, t))


# ------------------------------------------------------------------ silhouette check
def raster(points, tris, size=320):
    import numpy as np
    img = np.zeros((size, size), dtype=bool)
    sx, sy = size / 1.2, size / 1.1
    px = (points[:, 0] + 0.6) * sx
    py = (points[:, 1] + 0.03) * sy
    for a, b, c in tris:
        x0, y0, x1, y1, x2, y2 = px[a], py[a], px[b], py[b], px[c], py[c]
        area = (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0)
        if abs(area) < 1e-6:
            continue
        lo_x, hi_x = int(max(min(x0, x1, x2), 0)), int(min(max(x0, x1, x2) + 1, size - 1))
        lo_y, hi_y = int(max(min(y0, y1, y2), 0)), int(min(max(y0, y1, y2) + 1, size - 1))
        if hi_x < lo_x or hi_y < lo_y:
            continue
        xs, ys = np.meshgrid(np.arange(lo_x, hi_x + 1) + 0.5, np.arange(lo_y, hi_y + 1) + 0.5)
        w0 = ((x1 - xs) * (y2 - ys) - (y1 - ys) * (x2 - xs)) / area
        w1 = ((x2 - xs) * (y0 - ys) - (y2 - ys) * (x0 - xs)) / area
        w2 = 1.0 - w0 - w1
        img[lo_y:hi_y + 1, lo_x:hi_x + 1] |= (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
    return img


def polygon_raster(poly, size=320):
    import numpy as np
    sx, sy = size / 1.2, size / 1.1
    xs, ys = np.meshgrid((np.arange(size) + 0.5) / sx - 0.6, (np.arange(size) + 0.5) / sy - 0.03)
    inside = np.zeros((size, size), dtype=bool)
    n = len(poly)
    for i in range(n):
        x0, y0 = poly[i]
        x1, y1 = poly[(i + 1) % n]
        cond = (y0 > ys) != (y1 > ys)
        with np.errstate(divide="ignore", invalid="ignore"):
            hit = xs < (x1 - x0) * (ys - y0) / (y1 - y0) + x0
        inside ^= cond & hit
    return inside


def check():
    import numpy as np
    target = polygon_raster(traced_polygon())
    results = []
    for d in (0, 4):
        ob = bpy.data.objects["Aura.d%d" % d]
        me = ob.data
        co = np.zeros(len(me.vertices) * 3)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        tris = [tuple(p.vertices) for p in me.polygons]
        for yaw in range(0, 360, 20):
            a = math.radians(yaw)
            pts = np.stack((co[:, 0] * math.cos(a) + co[:, 2] * math.sin(a), co[:, 1]), axis=1)
            img = raster(pts, tris)
            iou = (img & target).sum() / max((img | target).sum(), 1)
            half = img.shape[1] // 2
            width = np.flatnonzero(img.any(axis=0))
            results.append((iou, d, yaw, (width.max() - width.min() + 1) / (img.shape[1] / 1.2) if len(width) else 0.0))
    ious = [r[0] for r in results]
    print("AURA silhouette vs the traced outline (IoU), %d views: mean %.3f, worst %.3f" % (len(ious), sum(ious) / len(ious), min(ious)))
    worst = min(results)
    print("AURA worst view: drawing %d, yaw %d, full width %.3f (traced %.3f)" % (worst[1], worst[2], worst[3], TIP[1][0] * 2))
    return results


# ------------------------------------------------------------------ the fighter's poses, as stand-in bodies
POSES = (("charge", 20), ("surge", 60))


def poses():
    import importlib.util
    sys.dont_write_bytecode = True
    spec = importlib.util.spec_from_file_location("atk", os.path.join(REPO, "tools", "attack_keys_blender.py"))
    atk = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(atk)
    bpy.ops.wm.open_mainfile(filepath=PLAYER)
    arm, body = bpy.data.objects["PlayerRig"], bpy.data.objects["PlayerBody"]
    scene = bpy.context.scene
    out = []
    for move, frame in POSES:
        atk.assign(arm, bpy.data.actions["Rush." + move])
        scene.frame_set(frame)
        root, pos = atk.pose_body(arm, atk.read_controls(arm))
        rig = atk.game_rig()
        at = lambda n: root @ Vector(pos[atk.B[n]])
        up = (root.to_3x3() @ (rig.model[atk.B["Head"]] @ Vector((0, 1, 0)))).normalized()
        head = at("Head")
        feet = (at("LeftFoot") + at("RightFoot")) * 0.5
        dg = bpy.context.evaluated_depsgraph_get()
        dg.update()
        temp = bpy.data.objects.new("temp", bpy.data.meshes.new_from_object(body.evaluated_get(dg)))
        scene.collection.objects.link(temp)
        mod = temp.modifiers.new("d", "DECIMATE")
        mod.ratio = 0.07
        dg = bpy.context.evaluated_depsgraph_get()
        dg.update()
        low = bpy.data.meshes.new_from_object(temp.evaluated_get(dg))
        low.calc_loop_triangles()
        out.append({"name": move, "co": [tuple(v.co) for v in low.vertices], "tris": [tuple(t.vertices) for t in low.loop_triangles],
                    "hips": tuple(at("Hips")), "feet": tuple(feet), "head": tuple(head), "headUp": tuple(up)})
        bpy.data.objects.remove(temp)
    open_blend()
    coll = collection("Poses")
    for p in out:
        name = "Body." + p["name"]
        old = bpy.data.objects.get(name)
        if old is not None:
            bpy.data.objects.remove(old)
        me = bpy.data.meshes.new(name)
        me.from_pydata(p["co"], [], p["tris"])
        me.update()
        ob = bpy.data.objects.new(name, me)
        coll.objects.link(ob)
        ob.matrix_world = DISPLAY
        for key in ("hips", "feet", "head", "headUp"):
            ob[key] = list(p[key])
        print("AURA pose", p["name"], len(p["co"]), "verts", len(p["tris"]), "tris", "hips", [round(v, 3) for v in p["hips"]])
    save()


# ------------------------------------------------------------------ out to the game
def vertex_normals(co, tris):
    n = [Vector((0, 0, 0)) for _ in co]
    for a, b, c in tris:
        f = (Vector(co[b]) - Vector(co[a])).cross(Vector(co[c]) - Vector(co[a]))
        n[a] += f
        n[b] += f
        n[c] += f
    out = []
    for i, v in enumerate(n):
        if v.length > 1e-12:
            out.append(v.normalized())
        else:   # a collapsed blade: anything sensible
            r = Vector((co[i][0], 0.0, co[i][2]))
            out.append(r.normalized() if r.length > 1e-9 else Vector((0, 1, 0)))
    return out


def sn16(v):
    return max(-32767, min(32767, int(round(v * 32767))))


def sn8(v):
    return max(-127, min(127, int(round(v * 127))))


def un8(v):
    return max(0, min(255, int(round(v * 255))))


def attr(me, name):
    a = me.attributes[name]
    values = [0.0] * len(a.data)
    a.data.foreach_get("value", values)
    return values


def export():
    """File (little endian): 'AUR2', int drawings, vertices, indices; ushort indices (padded to 4 bytes); per vertex 4 bytes shared
    by all drawings (across, along, rnd as unorm8, kind); then per key (drawings + 1: the last closes the loop), per vertex 12 bytes: x, y, z, edge (snorm16, /2 = aura
    units doubled so the range fits), nx, ny, nz (snorm8), az (snorm8, x pi)."""
    first = bpy.data.objects["Aura.d0"].data
    n_v = len(first.vertices)
    tris = [tuple(p.vertices) for p in first.polygons]
    indices = [i for t in tris for i in t]
    if n_v > 65535:
        sys.exit("too many vertices")
    across, along, rnd, kind = attr(first, "across"), attr(first, "along"), attr(first, "rnd"), attr(first, "kind")
    blob = bytearray(b"AUR2")
    blob += struct.pack("<iii", DRAWINGS, n_v, len(indices))
    blob += struct.pack("<%dH" % len(indices), *indices)
    while len(blob) % 4:
        blob += b"\0"
    for i in range(n_v):
        blob += bytes((un8(across[i] * 0.5 + 0.5), un8(along[i]), un8(rnd[i]), int(kind[i])))
    for d in range(DRAWINGS + 1):
        me = bpy.data.objects["Aura.d%d" % d].data
        if len(me.vertices) != n_v or [tuple(p.vertices) for p in me.polygons] != tris:
            sys.exit("Aura.d%d no longer has the same topology as Aura.d0: move vertices only" % d)
        co = [tuple(v.co) for v in me.vertices]
        normals = vertex_normals(co, tris)
        edge, az = attr(me, "edge"), attr(me, "az")
        for i in range(n_v):
            a = math.atan2(math.sin(az[i]), math.cos(az[i])) / math.pi
            blob += struct.pack("<hhhhbbbb", sn16(co[i][0] * 0.5), sn16(co[i][1] * 0.5), sn16(co[i][2] * 0.5), sn16(edge[i] * 0.5),
                                sn8(normals[i].x), sn8(normals[i].y), sn8(normals[i].z), sn8(a))
    with open(BYTES, "wb") as f:
        f.write(blob)
    print("AURA exported %s: %d bytes, %d vertices, %d triangles, %d drawings" % (BYTES, len(blob), n_v, len(tris), DRAWINGS))

    poses_out = []
    for move, _ in POSES:
        ob = bpy.data.objects.get("Body." + move)
        if ob is None:
            continue
        me = ob.data
        vb = bytearray()
        for v in me.vertices:
            vb += struct.pack("<fff", *v.co)
        me.calc_loop_triangles()
        ib = bytearray()
        for t in me.loop_triangles:
            ib += struct.pack("<HHH", *t.vertices)
        poses_out.append({"name": move, "pos": base64.b64encode(bytes(vb)).decode(), "idx": base64.b64encode(bytes(ib)).decode(),
                          "hips": list(ob["hips"]), "feet": list(ob["feet"]), "head": list(ob["head"]), "headUp": list(ob["headUp"])})
    # into the bench page itself, so it opens anywhere as a single file
    with open(BENCH, encoding="utf-8") as f:
        html = f.read()
    tag = '<script id="aura-data">'
    start = html.index(tag) + len(tag)
    end = html.index("</script>", start)
    payload = json.dumps({"flame": base64.b64encode(bytes(blob)).decode(), "poses": poses_out}, separators=(",", ":"))
    with open(BENCH, "w", encoding="utf-8", newline="\n") as f:
        f.write(html[:start] + "window.AURA_DATA = " + payload + ";" + html[end:])
    print("AURA bench data ->", BENCH, os.path.getsize(BENCH), "bytes")


def open_blend():
    if os.path.isfile(BLEND):
        bpy.ops.wm.open_mainfile(filepath=BLEND)
    else:
        bpy.ops.wm.read_factory_settings(use_empty=True)


def save():
    os.makedirs(os.path.dirname(BLEND), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)
    print("AURA saved ->", BLEND)


def main():
    if "--" not in sys.argv:
        return
    args = sys.argv[sys.argv.index("--") + 1:]
    if not args or args[0] not in ("poses", "build", "check", "export"):
        sys.exit("poses | build | check | export")
    cmd = args[0]
    if cmd == "poses":
        poses()
        return
    open_blend()
    if cmd == "build":
        build()
        save()
    elif cmd == "check":
        check()
    else:
        export()


if __name__ == "__main__":
    main()
