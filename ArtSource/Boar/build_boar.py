"""Builds, rigs and animates the small hostile boar for LightMMORPG in Blender (4.5), renders previews, exports the FBX.

    blender --background --python ArtSource/Boar/build_boar.py -- <preview dir>
    (env BOAR_PREVIEW=0 skips the preview renders)

Stylised toon boar sized against the 2.3 m hero: ~0.6 m at the shoulder, ~1 m nose to tail. Faces -Y (Blender's front),
Z up, metres. Built from code so it can be tweaked and rebuilt:

  * body: metaballs (barrel, shoulder hump, rump, head, snout) + tube legs, fused by a voxel remesh, smoothed and
    decimated into one clean mesh (no seams where legs meet the body, so it bends well)
  * parts on the surface (raycast): cloven hooves + dewclaws, curved tusks + small upper tusks, nose pad + nostrils,
    angry eyes + brows, cheek bristles, ears with pink insides, tufted mane, tail with a tuft
  * rig "BoarRig": root > pelvis > spine > chest > neck > head (+ ears), pelvis > tail; four bones per leg
    (front: upperarm, forearm, cannon, hoof; back: thigh, shin, cannon, hoof). Body skinned with automatic (heat)
    weights; small parts rigid on their bone.
  * actions (30 fps, in place): Idle (loop), Walk (loop, trot), Run (loop, charging gallop), Paw (warning scrape before a
    charge), Attack (tusk gore, hit on frame 9), Hit, Death (falls on its side and stays down)
  * cel materials (two flat bands) + inverted-hull outline modifier for the previews only (the game's ToonLit shader
    draws its own outline; the FBX has no outline geometry)

Outputs: ArtSource/Boar/boar.blend, Assets/_Project/Art/Models/Boar/Boar.fbx, previews in <preview dir>.
"""
import math
import os
import sys

import bpy
from mathutils import Euler, Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else HERE
PREVIEW = os.environ.get("BOAR_PREVIEW", "1") != "0"
FBX_PATH = os.path.join(REPO, "Assets", "_Project", "Art", "Models", "Boar", "Boar.fbx")
FPS = 30

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = FPS


def link(obj):
    scene.collection.objects.link(obj)
    return obj


def activate(obj, others=()):
    for o in bpy.context.selected_objects:
        o.select_set(False)
    for o in others:
        o.select_set(True)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


# ------------------------------------------------------------------ materials (cel: two flat bands)
def srgb(h):
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def toon(name, lit, shade):
    lit, shade = srgb(lit), srgb(shade)
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    diff = nt.nodes.new("ShaderNodeBsdfDiffuse")
    s2r = nt.nodes.new("ShaderNodeShaderToRGB")
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    emis = nt.nodes.new("ShaderNodeEmission")
    ramp.color_ramp.interpolation = "CONSTANT"
    ramp.color_ramp.elements[0].color = (*shade, 1)
    ramp.color_ramp.elements[1].position = 0.32
    ramp.color_ramp.elements[1].color = (*lit, 1)
    nt.links.new(diff.outputs[0], s2r.inputs[0])
    nt.links.new(s2r.outputs[0], ramp.inputs[0])
    nt.links.new(ramp.outputs[0], emis.inputs[0])
    nt.links.new(emis.outputs[0], out.inputs[0])
    m.diffuse_color = (*lit, 1)   # what the FBX / Unity sees as the base colour
    return m


def flat(name, color):
    c = srgb(color)
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    emis = nt.nodes.new("ShaderNodeEmission")
    emis.inputs[0].default_value = (*c, 1)
    nt.links.new(emis.outputs[0], out.inputs[0])
    m.diffuse_color = (*c, 1)
    return m


M_FUR = toon("Boar_Fur", "#9a6a45", "#5e3b27")
M_BELLY = toon("Boar_Belly", "#c79b72", "#8a6447")
M_SOCK = toon("Boar_Sock", "#5b3a29", "#3a251a")
M_MANE = toon("Boar_Mane", "#3b2a24", "#1f1513")
M_SNOUT = toon("Boar_Snout", "#d98f86", "#a2615c")
M_INNER = toon("Boar_InnerEar", "#d4877f", "#9c5a55")
M_TUSK = toon("Boar_Tusk", "#f3ead2", "#bdb197")
M_HOOF = toon("Boar_Hoof", "#2c2220", "#171110")
M_EYE = flat("Boar_Eye", "#1a1214")
M_GLINT = flat("Boar_EyeGlint", "#ffdf6b")
M_LINE = flat("Boar_Outline", "#2a1a18")
M_LINE.use_backface_culling = True

# ------------------------------------------------------------------ 1. body blob (metaballs)
mb = bpy.data.metaballs.new("BoarBlob")
mb.resolution = 0.016
mb.threshold = 0.6
RS = 1.45   # metaball radius scale: lands the blob on ~0.39 m wide, belly at 0.18 m, back at 0.63 m
blob = link(bpy.data.objects.new("BoarBlob", mb))


def ell(co, sx, sy, sz, r, rot=(0, 0, 0)):
    e = mb.elements.new(type="ELLIPSOID")
    e.co, e.radius = co, r * RS
    e.size_x, e.size_y, e.size_z = sx, sy, sz
    e.rotation = Euler([math.radians(a) for a in rot]).to_quaternion()


def cap(co, length, r, rot=(0, 0, 0)):
    e = mb.elements.new(type="CAPSULE")
    e.co, e.radius, e.size_x = co, r * RS, length
    e.rotation = Euler([math.radians(a) for a in rot]).to_quaternion()


ell((0, 0.02, 0.38), 0.95, 1.75, 0.95, 0.2)              # barrel
ell((0, -0.16, 0.45), 1.05, 1.0, 1.05, 0.21)             # shoulder hump (boars are front-heavy)
ell((0, 0.22, 0.39), 0.9, 0.85, 0.9, 0.18)               # rump
ell((0, -0.38, 0.38), 0.85, 0.95, 0.9, 0.15)             # head
cap((0, -0.52, 0.31), 0.07, 0.075, rot=(0, 0, 90))       # snout
ell((0, -0.08, 0.27), 0.8, 1.5, 0.55, 0.17)              # belly
ell((0, -0.43, 0.29), 0.7, 0.8, 0.5, 0.11)               # jowls / heavy jaw
activate(blob)
bpy.ops.object.convert(target="MESH")
body = bpy.context.active_object
body.name = "Boar"

# ------------------------------------------------------------------ 2. legs: tubes with round joints, fused to the blob
# joints (y, z, radius); x = +-0.11. Front: shoulder (in the body), elbow, knee (carpus), fetlock.
# Back: hip (in the body), stifle, hock (sits behind), fetlock.
LEGX = 0.11
FRONT = [(-0.17, 0.36, 0.075), (-0.155, 0.215, 0.052), (-0.16, 0.12, 0.036), (-0.17, 0.058, 0.029)]
BACK = [(0.19, 0.37, 0.085), (0.165, 0.225, 0.058), (0.215, 0.135, 0.035), (0.2, 0.058, 0.028)]


def tube(points):
    objs = []
    for i, (a, ra) in enumerate(points):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=12, radius=ra, location=a)
        objs.append(bpy.context.active_object)
        if i + 1 < len(points):
            b, rb = points[i + 1]
            d = Vector(b) - Vector(a)
            bpy.ops.mesh.primitive_cone_add(vertices=20, radius1=ra, radius2=rb, depth=d.length, location=(Vector(a) + Vector(b)) / 2,
                                            rotation=d.to_track_quat("Z", "Y").to_euler())
            objs.append(bpy.context.active_object)
    return objs


limbs = []
for sx in (-1, 1):
    for chain in (FRONT, BACK):
        limbs += tube([((sx * LEGX, y, z), r) for y, z, r in chain])
activate(body, limbs)
bpy.ops.object.join()
body = bpy.context.active_object
for name, kind, opts in (("Remesh", "REMESH", dict(mode="VOXEL", voxel_size=0.0085, adaptivity=0.0)),
                         ("Smooth", "SMOOTH", dict(factor=0.6, iterations=5))):
    m = body.modifiers.new(name, kind)
    for k, v in opts.items():
        setattr(m, k, v)
    bpy.ops.object.modifier_apply(modifier=name)
faces = len(body.data.polygons)
dec = body.modifiers.new("Decimate", "DECIMATE")
dec.ratio = min(1.0, 3600 / faces)   # ~6k triangles for the body, ~11k with every part
bpy.ops.object.modifier_apply(modifier="Decimate")
print("BODY faces", faces, "->", len(body.data.polygons))
bb = [Vector(c) for c in body.bound_box]
print("BODY_BOUNDS x %.3f..%.3f y %.3f..%.3f z %.3f..%.3f" % (min(v.x for v in bb), max(v.x for v in bb), min(v.y for v in bb),
                                                            max(v.y for v in bb), min(v.z for v in bb), max(v.z for v in bb)))

# body colours: lighter belly and jaw, darker lower legs ("socks")
for mat in (M_FUR, M_BELLY, M_SOCK):
    body.data.materials.append(mat)
LEG_COLUMNS = [(sx * LEGX, ch[1][0]) for sx in (-1, 1) for ch in (FRONT, BACK)]
for p in body.data.polygons:
    p.use_smooth = True
    c, n = p.center, p.normal
    on_leg = c.z < 0.3 and any(math.hypot(c.x - lx, c.y - ly) < 0.095 for lx, ly in LEG_COLUMNS)
    if c.z < 0.125:
        p.material_index = 2
    elif n.z < -0.45 and abs(c.x) < 0.15 and -0.3 < c.y < 0.26 and c.z > 0.15 and not on_leg:
        p.material_index = 1
    elif c.y < -0.47 and n.z < -0.25:
        p.material_index = 1

bvh = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())


def surf(origin, direction):
    loc, nrm, _, _ = bvh.ray_cast(Vector(origin), Vector(direction).normalized())
    if loc is None:
        raise RuntimeError("no surface from %s along %s" % (origin, direction))
    return loc, nrm


# ------------------------------------------------------------------ 3. parts (each remembers the bone it rides on)
parts = []   # (object, bone name)


def finish(o, mat, bone, scale=(1, 1, 1)):
    o.scale = scale
    o.data.materials.append(mat)
    for p in o.data.polygons:
        p.use_smooth = True
    parts.append((o, bone))
    return o


def spike(mat, bone, base, direction, r1, r2, depth, verts=8, scale=(1, 1, 1), sink=0.0, up="Y"):
    d = Vector(direction).normalized()
    centre = Vector(base) + d * (depth / 2 - sink)
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=depth, location=centre,
                                    rotation=d.to_track_quat("Z", up).to_euler())
    return finish(bpy.context.active_object, mat, bone, scale)


def ball(mat, bone, centre, r, scale=(1, 1, 1), rot=(0, 0, 0), seg=16, rings=10):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=r, location=centre,
                                         rotation=[math.radians(a) for a in rot])
    return finish(bpy.context.active_object, mat, bone, scale)


def curve(mat, bone, base, dirs, radii, seg_len, verts=12, sink=0.0):
    """A bent horn / tusk: cones end to end along dirs, radii[i] -> radii[i+1]."""
    p = Vector(base)
    for i, d in enumerate(dirs):
        d = Vector(d).normalized()
        spike(mat, bone, p, d, radii[i], radii[i + 1], seg_len, verts=verts, sink=sink if i == 0 else 0.004)
        p = p + d * (seg_len - (sink if i == 0 else 0.004))


def side(sx):
    return "L" if sx > 0 else "R"   # facing -Y, the boar's left is +X


# cloven hooves (two toes, split down the middle) and dewclaws behind the fetlock
for sx in (-1, 1):
    for front, chain in ((True, FRONT), (False, BACK)):
        y = chain[-1][0]
        bone = ("hoof.F." if front else "hoof.B.") + side(sx)
        for tx in (-1, 1):
            ball(M_HOOF, bone, (sx * LEGX + tx * 0.0145, y - 0.012, 0.026), 0.026, scale=(0.62, 1.15, 1.0), seg=14, rings=8)
        for tx in (-1, 1):
            spike(M_HOOF, bone, (sx * LEGX + tx * 0.016, y + 0.024, 0.05), (tx * 0.2, 0.55, -1.0), 0.008, 0.0015, 0.028, verts=6, sink=0.004)

# nose pad and nostrils at the end of the snout
tip, _ = surf((0, -1.5, 0.315), (0, 1, 0))
ball(M_SNOUT, "head", tip + Vector((0, 0.004, 0)), 0.064, scale=(1, 0.3, 0.82), seg=24, rings=12)
for sx in (-1, 1):
    ball(M_EYE, "head", tip + Vector((sx * 0.023, -0.016, 0.0)), 0.012, scale=(0.85, 0.5, 1.25), seg=12, rings=8)

# tusks: big lower tusks curling up, out and back towards the eyes; small upper tusks (whetters) above them
for sx in (-1, 1):
    p, n = surf((sx * 1.0, -0.535, 0.288), (-sx, 0, 0))
    curve(M_TUSK, "head", p, [(sx * 0.5, -0.45, 1.0), (sx * 0.55, -0.05, 1.0), (sx * 0.3, 0.55, 1.0)],
          [0.021, 0.017, 0.011, 0.0008], 0.042, sink=0.016)
    q, _ = surf((sx * 1.0, -0.505, 0.325), (-sx, 0, 0))
    spike(M_TUSK, "head", q, (sx * 0.8, -0.2, 0.45), 0.011, 0.001, 0.035, verts=10, sink=0.01)

# angry eyes: dark almond, yellow glint, heavy brow; cheek bristles behind the jaw
for sx in (-1, 1):
    p, n = surf((sx * 1.0, -0.47, 0.43), (-sx, 0, 0))
    ball(M_EYE, "head", p - n * 0.006, 0.027, scale=(0.5, 1, 0.72), rot=(0, 0, sx * -18))
    ball(M_GLINT, "head", p + n * 0.006 + Vector((0, -0.006, 0.004)), 0.007, seg=10, rings=6)
    brow = p + Vector((0, 0.035, 0.03)) - n * 0.004
    spike(M_MANE, "head", brow, (-sx * 0.25, -1, -0.45), 0.024, 0.005, 0.08, verts=4, sink=0.012)
    for k, (y, z, h) in enumerate(((-0.39, 0.33, 0.06), (-0.36, 0.30, 0.07), (-0.33, 0.34, 0.055))):
        c, cn = surf((sx * 1.0, y, z), (-sx, 0, 0))
        spike(M_MANE, "head", c, cn + Vector((0, 0.9, -0.5)), 0.016, 0.0, h, verts=5, sink=0.008)

# ears: pointed, angled out and back, pink inside, a dark tuft on the tip
for sx in (-1, 1):
    p, n = surf((sx * 0.075, -0.31, 1.5), (0, 0, -1))
    d = Vector((sx * 0.55, 0.35, 1.0)).normalized()
    bone = "ear." + side(sx)
    spike(M_FUR, bone, p, d, 0.05, 0.0, 0.115, verts=4, scale=(1, 0.42, 1), sink=0.02)
    spike(M_INNER, bone, p + Vector((0, -0.011, 0.004)), d, 0.033, 0.0, 0.082, verts=4, scale=(1, 0.3, 1), sink=0.012)
    spike(M_MANE, bone, p + d * 0.085, d + Vector((0, 0.3, 0)), 0.012, 0.0, 0.04, verts=5)

# mane: tufts of three raked spikes from between the ears down the spine, tallest over the shoulders


def spine_bone(y):
    return "neck" if y < -0.27 else "chest" if y < -0.1 else "spine" if y < 0.1 else "pelvis"


for i in range(12):
    t = i / 11
    y = -0.3 + t * 0.56
    p, n = surf((0, y, 1.5), (0, 0, -1))
    h = 0.06 + 0.085 * math.sin(math.pi * min(1.0, t * 1.3))
    rake = Vector((0, 0.55 + 0.35 * t, 1.0))
    spike(M_MANE, spine_bone(y), p, rake, 0.03, 0.0, h, verts=6, sink=0.012)
    for sx in (-1, 1):
        spike(M_MANE, spine_bone(y), p + Vector((sx * 0.02, 0.012, -0.004)), rake + Vector((sx * 0.45, 0, 0)), 0.022, 0.0, h * 0.7, verts=5, sink=0.012)

# tail: a thin curl with a dark tuft
p, n = surf((0, 1.5, 0.43), (0, -1, 0))
spike(M_FUR, "tail.001", p, (0, 1.0, 0.45), 0.016, 0.011, 0.07, verts=8, sink=0.012)
p2 = p + Vector((0, 1.0, 0.45)).normalized() * 0.058
spike(M_FUR, "tail.002", p2, (0, 0.6, -0.8), 0.011, 0.007, 0.06, verts=8, sink=0.004)
p3 = p2 + Vector((0, 0.6, -0.8)).normalized() * 0.056
spike(M_MANE, "tail.002", p3, (0, 0.35, -1.0), 0.02, 0.0, 0.05, verts=6)

# ------------------------------------------------------------------ 4. rig
arm_data = bpy.data.armatures.new("BoarRig")
arm = link(bpy.data.objects.new("BoarRig", arm_data))
arm_data.display_type = "STICK"
activate(arm)
bpy.ops.object.mode_set(mode="EDIT")
eb = arm_data.edit_bones


def bone(name, head, tail, parent=None, deform=True):
    b = eb.new(name)
    b.head, b.tail = head, tail
    b.parent = eb[parent] if parent else None
    b.use_deform = deform
    b.use_connect = False
    return b


ear_p, _ = surf((0.075, -0.31, 1.5), (0, 0, -1))
tail_p, _ = surf((0, 1.5, 0.43), (0, -1, 0))
bone("root", (0, 0, 0), (0, 0.25, 0), deform=False)
bone("pelvis", (0, 0.24, 0.40), (0, 0.06, 0.42), "root")
bone("spine", (0, 0.06, 0.42), (0, -0.12, 0.45), "pelvis")
bone("chest", (0, -0.12, 0.45), (0, -0.27, 0.45), "spine")
bone("neck", (0, -0.27, 0.45), (0, -0.37, 0.41), "chest")
bone("head", (0, -0.37, 0.41), (0, -0.62, 0.31), "neck")
for sx in (-1, 1):
    s = side(sx)
    e = Vector((sx * ear_p.x if sx > 0 else -ear_p.x, ear_p.y, ear_p.z))
    bone("ear." + s, e, e + Vector((sx * 0.55, 0.35, 1.0)).normalized() * 0.11, "head")
    f = [Vector((sx * LEGX, y, z)) for y, z, _ in FRONT]
    bone("upperarm." + s, f[0], f[1], "chest")
    bone("forearm." + s, f[1], f[2], "upperarm." + s)
    bone("cannon.F." + s, f[2], f[3], "forearm." + s)
    bone("hoof.F." + s, f[3], Vector((sx * LEGX, FRONT[-1][0] - 0.05, 0.0)), "cannon.F." + s)
    k = [Vector((sx * LEGX, y, z)) for y, z, _ in BACK]
    bone("thigh." + s, k[0], k[1], "pelvis")
    bone("shin." + s, k[1], k[2], "thigh." + s)
    bone("cannon.B." + s, k[2], k[3], "shin." + s)
    bone("hoof.B." + s, k[3], Vector((sx * LEGX, BACK[-1][0] - 0.05, 0.0)), "cannon.B." + s)
bone("tail.001", tail_p, tail_p + Vector((0, 0.06, 0.03)), "pelvis")
bone("tail.002", tail_p + Vector((0, 0.06, 0.03)), tail_p + Vector((0, 0.1, -0.03)), "tail.001")
for b in eb:   # roll so every bone's local Z points up / forward the same way (tidy in Unity, easy to pose)
    b.align_roll(Vector((0, -1, 0)) if abs(b.vector.normalized().z) > 0.7 else Vector((0, 0, 1)))
bpy.ops.object.mode_set(mode="OBJECT")

# skin: heat weights on the body from the trunk and leg bones only (ears / tail / root don't pull the skin)
NO_SKIN = {"root", "ear.L", "ear.R", "tail.001", "tail.002"}
for b in arm_data.bones:
    b.use_deform = b.name not in NO_SKIN
activate(arm, [body])
bpy.ops.object.parent_set(type="ARMATURE_AUTO")
for b in arm_data.bones:
    b.use_deform = b.name != "root"

# anything the heat solve left unweighted goes to the nearest bone
groups = {g.index: g.name for g in body.vertex_groups}
segs = [(b.name, b.head_local.copy(), b.tail_local.copy()) for b in arm_data.bones if b.use_deform and b.name not in NO_SKIN]


def nearest_bone(p):
    best, bd = None, 1e9
    for name, a, b in segs:
        ab = b - a
        t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
        d = (a + ab * t - p).length
        if d < bd:
            best, bd = name, d
    return best


orphans = 0
for v in body.data.vertices:
    if sum(g.weight for g in v.groups) < 1e-4:
        name = nearest_bone(v.co)
        vg = body.vertex_groups.get(name) or body.vertex_groups.new(name=name)
        vg.add([v.index], 1.0, "REPLACE")
        orphans += 1
print("SKIN orphans fixed:", orphans)

# rigid parts: one vertex group each, then join them into the body (groups merge by name)
for o, bname in parts:
    vg = o.vertex_groups.new(name=bname)
    vg.add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
activate(body, [o for o, _ in parts])
bpy.ops.object.join()
body = bpy.context.active_object
print("MESH verts", len(body.data.vertices), "tris", sum(len(p.vertices) - 2 for p in body.data.polygons), "materials", len(body.data.materials))

# ------------------------------------------------------------------ 5. animation
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
PB = arm.pose.bones
for pb in PB:
    pb.rotation_mode = "QUATERNION"
REST = {b.name: b.matrix_local.to_3x3() for b in arm_data.bones}


def rot(name, turns):
    """Turns about armature axes (relative to the parent), applied in order: [(axis, degrees), ...]."""
    q = Quaternion()
    inv = REST[name].inverted()
    for axis, deg in turns:
        q = Quaternion((inv @ axis).normalized(), math.radians(deg)) @ q
    return q


def apply(spec):
    for pb in PB:
        s = spec.get(pb.name, {})
        pb.rotation_quaternion = rot(pb.name, s.get("r", []))
        pb.location = REST[pb.name].inverted() @ Vector(s.get("l", (0, 0, 0)))


def key(frame, spec):
    apply(spec)
    for pb in PB:
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        pb.keyframe_insert("location", frame=frame)


def merge(*specs):
    out = {}
    for s in specs:
        for b, v in s.items():
            d = out.setdefault(b, {"r": [], "l": (0, 0, 0)})
            d["r"] = d["r"] + v.get("r", [])
            d["l"] = tuple(a + b2 for a, b2 in zip(d["l"], v.get("l", (0, 0, 0))))
    return out


# A downward leg bone turning +deg about X swings its lower end BACK (the boar faces -Y). Spine bones turning +deg about X
# pitch DOWN. +deg about Z turns the head to the boar's LEFT.
def front_leg(s, swing, bend, hoof=0.0):
    return {"upperarm." + s: {"r": [(X, -swing)]}, "forearm." + s: {"r": [(X, -bend * 0.25)]},
            "cannon.F." + s: {"r": [(X, bend)]}, "hoof.F." + s: {"r": [(X, hoof - bend * 0.3)]}}


def back_leg(s, swing, bend, hoof=0.0):
    return {"thigh." + s: {"r": [(X, -swing)]}, "shin." + s: {"r": [(X, bend * 0.6)]},
            "cannon.B." + s: {"r": [(X, -bend)]}, "hoof.B." + s: {"r": [(X, hoof + bend * 0.25)]}}


def legs(t, phases, front_amp, back_amp, bend_amp):
    """Four legs at cycle time t (0..1). phases: FL, FR, BL, BR. Lifts (bends) while swinging forward."""
    out = {}
    for (s, front), ph in zip((("L", True), ("R", True), ("L", False), ("R", False)), phases):
        p = 2 * math.pi * ((t + ph) % 1.0)
        swing = (front_amp if front else back_amp) * math.cos(p)
        bend = bend_amp * max(0.0, -math.sin(p)) ** 0.8
        out.update(front_leg(s, swing, bend) if front else back_leg(s, swing, bend))
    return out


ACTIONS = []


def action(name, frames, loop):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data.action = act
    ACTIONS.append((act, frames, loop))
    return act


arm.animation_data_create()

# Idle (2 s loop): breathing, sniffing, an ear flick, a lazy tail swish
action("Idle", 60, True)
for f in range(0, 61, 5):
    t = f / 60
    br = math.sin(2 * math.pi * t)
    flick = max(0.0, math.sin(math.pi * (f - 30) / 8)) if 30 <= f <= 38 else 0.0
    key(f, merge(
        {"pelvis": {"l": (0, 0, 0.004 * br)}, "chest": {"r": [(X, -1.2 * br)]},
         "neck": {"r": [(X, 3 + 2 * math.sin(2 * math.pi * 2 * t)), (Z, 4 * math.sin(2 * math.pi * t))]},
         "head": {"r": [(X, -3 * math.sin(2 * math.pi * 2 * t + 0.6))]},
         "ear.L": {"r": [(X, -30 * flick)]}, "ear.R": {"r": [(X, -5 * br)]},
         "tail.001": {"r": [(Z, 25 * math.sin(2 * math.pi * t))]}, "tail.002": {"r": [(Z, 20 * math.sin(2 * math.pi * t - 0.8))]}},
        legs(0.25, (0, 0, 0, 0), 0, 0, 0)))

# Walk (0.8 s loop): trot, diagonal pairs together; ~1.2 m/s in the game
action("Walk", 24, True)
for f in range(0, 25, 3):
    t = f / 24
    bob = math.cos(4 * math.pi * t)
    key(f, merge(
        {"pelvis": {"l": (0, 0, -0.008 * bob), "r": [(Y, 2.5 * math.sin(2 * math.pi * t))]},
         "chest": {"r": [(Y, -2 * math.sin(2 * math.pi * t))]},
         "neck": {"r": [(X, 4 + 3 * bob)]}, "head": {"r": [(Z, 3 * math.sin(2 * math.pi * t))]},
         "ear.L": {"r": [(X, -8 * bob)]}, "ear.R": {"r": [(X, -8 * bob)]},
         "tail.001": {"r": [(Z, 18 * math.sin(2 * math.pi * t))]}},
        legs(t, (0.0, 0.5, 0.5, 0.0), 22, 24, 48)))

# Run (0.47 s loop): charging gallop, head down with the tusks forward, ears pinned back, spine bunching and stretching
action("Run", 14, True)
for f in range(0, 15, 2):
    t = f / 14
    s = math.sin(2 * math.pi * t)
    key(f, merge(
        {"pelvis": {"l": (0, 0, 0.022 * math.sin(4 * math.pi * t)), "r": [(X, -6 * s)]},
         "spine": {"r": [(X, 5 * s)]}, "chest": {"r": [(X, 4 * s)]},
         "neck": {"r": [(X, 14 - 4 * s)]}, "head": {"r": [(X, 6)]},
         "ear.L": {"r": [(X, -45)]}, "ear.R": {"r": [(X, -45)]},
         "tail.001": {"r": [(X, -35 + 10 * s)]}, "tail.002": {"r": [(X, -20)]}},
        legs(t, (0.0, 0.1, 0.5, 0.6), 36, 40, 70)))

# Paw (1.2 s): the warning before a charge: head low, the right front hoof scrapes the ground twice, a snort
action("Paw", 36, False)
LOW = {"neck": {"r": [(X, 14)]}, "head": {"r": [(X, 6)]}, "ear.L": {"r": [(X, -30)]}, "ear.R": {"r": [(X, -30)]},
       "pelvis": {"l": (0, 0.01, -0.01)}, "tail.001": {"r": [(X, -30)]}}
for f, swing, bend, shake in ((0, 0, 0, 0), (6, 20, 55, 0), (11, -18, 8, 0), (15, 22, 55, 0), (21, -20, 8, 0), (26, 0, 10, 6),
                              (29, 0, 0, -6), (32, 0, 0, 4), (36, 0, 0, 0)):
    k = merge(LOW, legs(0.25, (0, 0, 0, 0), 0, 0, 0), front_leg("R", swing, bend), {"head": {"r": [(Z, shake)]}})
    key(f, k if f else merge({"neck": {"r": [(X, 4)]}}, legs(0.25, (0, 0, 0, 0), 0, 0, 0)))

# Attack (0.8 s): tusk gore: crouch and dip the head, lunge and hook the tusks up (hit on frame 9), recover
action("Attack", 24, False)
STAND = legs(0.25, (0, 0, 0, 0), 0, 0, 0)
key(0, merge({"neck": {"r": [(X, 4)]}}, STAND))
key(5, merge({"pelvis": {"l": (0, 0.05, -0.03)}, "neck": {"r": [(X, 26)]}, "head": {"r": [(X, 14)]},
              "ear.L": {"r": [(X, -40)]}, "ear.R": {"r": [(X, -40)]}, "tail.001": {"r": [(X, -40)]}},
             front_leg("L", -12, 10), front_leg("R", -12, 10), back_leg("L", 10, 15), back_leg("R", 10, 15)))
key(9, merge({"pelvis": {"l": (0, -0.12, 0.01)}, "spine": {"r": [(X, -4)]}, "neck": {"r": [(X, -18), (Z, 8)]},
              "head": {"r": [(X, -22), (Y, 10)]}, "ear.L": {"r": [(X, -45)]}, "ear.R": {"r": [(X, -45)]}, "tail.001": {"r": [(X, -45)]}},
             front_leg("L", 22, 12), front_leg("R", 16, 30), back_leg("L", -24, 5), back_leg("R", -20, 5)))
key(13, merge({"pelvis": {"l": (0, -0.1, 0.0)}, "neck": {"r": [(X, -10), (Z, 6)]}, "head": {"r": [(X, -12), (Y, 6)]},
               "ear.L": {"r": [(X, -35)]}, "ear.R": {"r": [(X, -35)]}},
              front_leg("L", 18, 5), front_leg("R", 14, 10), back_leg("L", -20, 5), back_leg("R", -18, 5)))
key(24, merge({"neck": {"r": [(X, 4)]}}, STAND))

# Hit (0.4 s): flinch back, head jerks up and away, ears flat
action("Hit", 12, False)
key(0, merge({"neck": {"r": [(X, 4)]}}, STAND))
key(3, merge({"pelvis": {"l": (0, 0.05, 0.01)}, "spine": {"r": [(X, -5), (Y, 4)]}, "neck": {"r": [(X, -16), (Z, -12)]},
              "head": {"r": [(X, -8)]}, "ear.L": {"r": [(X, -50)]}, "ear.R": {"r": [(X, -50)]}, "tail.001": {"r": [(X, 20)]}},
             front_leg("L", -10, 12), front_leg("R", -6, 6), back_leg("L", -6, 6), back_leg("R", -4, 4)))
key(12, merge({"neck": {"r": [(X, 4)]}}, STAND))

# Death (1.2 s): stagger, fall onto its right side, legs stiff, a bounce, stays down
action("Death", 36, False)
key(0, merge({"neck": {"r": [(X, 4)]}}, STAND))
key(7, merge({"pelvis": {"l": (0, 0.04, -0.02)}, "neck": {"r": [(X, -20), (Z, 10)]}, "ear.L": {"r": [(X, -40)]},
              "ear.R": {"r": [(X, -40)]}}, front_leg("L", -8, 25), front_leg("R", 6, 30), back_leg("L", 4, 20), back_leg("R", -4, 10)))
FALLEN = merge({"root": {"r": [(Y, -88)], "l": (-0.02, 0, 0.19)}, "neck": {"r": [(X, -6), (Y, 15)]}, "head": {"r": [(X, -10)]},
                "ear.L": {"r": [(X, -50)]}, "ear.R": {"r": [(X, -60)]}, "tail.001": {"r": [(X, 30)]}, "tail.002": {"r": [(X, 20)]}},
               front_leg("L", 18, 5), front_leg("R", 10, 0), back_leg("L", -14, 5), back_leg("R", -6, 0))
key(20, merge(FALLEN, {"root": {"r": [(Y, 4)], "l": (0, 0, 0.03)}}))
key(25, merge(FALLEN, {"root": {"r": [(Y, -3)]}}))
key(29, FALLEN)
key(36, FALLEN)

# loops: matching first / last keys + cyclic curves in Blender; Unity loops by the clip setting
for act, frames, loop in ACTIONS:
    act.frame_range = (0, frames)
    for fc in getattr(act, "fcurves", []):
        for kp in fc.keyframe_points:
            kp.interpolation = "BEZIER"
arm.animation_data.action = bpy.data.actions["Idle"]
scene.frame_start, scene.frame_end = 0, 60

# ------------------------------------------------------------------ 6. export FBX (no outline geometry; Unity: generic rig)
os.makedirs(os.path.dirname(FBX_PATH), exist_ok=True)
activate(arm, [body])
bpy.ops.export_scene.fbx(
    filepath=FBX_PATH, use_selection=True, object_types={"ARMATURE", "MESH"}, use_mesh_modifiers=False,
    add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X", armature_nodetype="NULL",
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
    bake_anim_simplify_factor=0.0, axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_ALL",
    mesh_smooth_type="FACE", use_armature_deform_only=False)   # keep "root": Death turns it to lay the boar down
print("FBX", FBX_PATH)

# ------------------------------------------------------------------ 7. outline for the previews, stage, save, render
body.data.materials.append(M_LINE)
sol = body.modifiers.new("Outline", "SOLIDIFY")
sol.thickness, sol.offset, sol.use_flip_normals, sol.use_rim, sol.material_offset = 0.008, 1.0, True, False, 100

world = bpy.data.worlds.new("World")
scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (*srgb("#cfdcea"), 1)
bpy.ops.mesh.primitive_circle_add(vertices=64, radius=1.6, fill_type="NGON", location=(0, 0, 0))
floor = bpy.context.active_object
floor.name = "Floor"
floor.data.materials.append(toon("Floor", "#e6dccf", "#c9bba9"))
sun = bpy.data.lights.new("Sun", "SUN")
sun.energy, sun.angle = 3.5, math.radians(3)
sun_o = link(bpy.data.objects.new("Sun", sun))
sun_o.rotation_euler = Euler((math.radians(50), math.radians(-20), math.radians(-35)))
cam = bpy.data.cameras.new("Cam")
cam.lens = 55
cam_o = link(bpy.data.objects.new("Cam", cam))
scene.camera = cam_o
for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
    try:
        scene.render.engine = eng
        break
    except TypeError:
        continue
scene.view_settings.view_transform = "Standard"
scene.eevee.taa_render_samples = 16


def look(pos, target):
    cam_o.location = pos
    cam_o.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()


bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "boar.blend"))

if PREVIEW:
    scene.render.resolution_x, scene.render.resolution_y = 1200, 900
    arm.animation_data.action = bpy.data.actions["Idle"]
    scene.frame_set(0)
    for name, (pos, tgt) in {"boar_three_quarter.png": ((-1.3, -1.5, 0.85), (0, -0.1, 0.32)),
                             "boar_side.png": ((-2.1, -0.12, 0.42), (0, -0.12, 0.33)),
                             "boar_front.png": ((-0.25, -1.95, 0.5), (0, -0.2, 0.33)),
                             "boar_back.png": ((1.1, 1.7, 0.8), (0, 0.0, 0.33))}.items():
        look(pos, tgt)
        scene.render.filepath = os.path.join(OUT, name)
        bpy.ops.render.render(write_still=True)
    # animation frames: every action, side-on 3/4
    scene.render.resolution_x, scene.render.resolution_y = 480, 360
    look((-1.75, -1.25, 0.75), (0, -0.05, 0.3))
    for act, frames, loop in ACTIONS:
        arm.animation_data.action = act
        step = 2 if frames <= 24 else 3
        for f in range(0, frames + (0 if loop else 1), step):
            scene.frame_set(f)
            scene.render.filepath = os.path.join(OUT, "anim", "%s_%03d.png" % (act.name, f))
            bpy.ops.render.render(write_still=True)
    arm.animation_data.action = bpy.data.actions["Idle"]
    scene.frame_set(0)
print("BOAR_DONE")
