"""Builds the small hostile boar for LightMMORPG in Blender (4.5) and renders preview shots.

    blender --background --python ArtSource/Boar/build_boar.py -- <render dir>

Stylised toon boar sized against the 2.3 m hero: ~0.6 m at the shoulder, ~0.95 m nose to tail. Body, shoulder hump,
rump, head and snout are metaballs (one smooth blob), converted to a mesh; legs, hooves, tusks, ears, mane spikes, eyes and
nose are simple primitives joined into the same object. Cel materials (two flat bands) plus an inverted-hull outline like
the game's ToonLit shader. Faces -Y (Blender's front). Saves ArtSource/Boar/boar.blend next to this script.
"""
import math
import os
import sys

import bpy
from mathutils import Euler, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else HERE

# ------------------------------------------------------------------ scene reset
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def link(obj):
    scene.collection.objects.link(obj)
    return obj


# ------------------------------------------------------------------ materials (cel: two flat bands + a soft rim)
def toon(name, lit, shade):
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
    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = (*shade, 1)
    ramp.color_ramp.elements[1].position = 0.32
    ramp.color_ramp.elements[1].color = (*lit, 1)
    nt.links.new(diff.outputs[0], s2r.inputs[0])
    nt.links.new(s2r.outputs[0], ramp.inputs[0])
    nt.links.new(ramp.outputs[0], emis.inputs[0])
    nt.links.new(emis.outputs[0], out.inputs[0])
    m.diffuse_color = (*lit, 1)
    return m


def flat(name, color):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    emis = nt.nodes.new("ShaderNodeEmission")
    emis.inputs[0].default_value = (*color, 1)
    nt.links.new(emis.outputs[0], out.inputs[0])
    m.diffuse_color = (*color, 1)
    return m


def srgb(h):
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


M_FUR = toon("Boar_Fur", srgb("#9a6a45"), srgb("#5e3b27"))
M_BELLY = toon("Boar_Belly", srgb("#c79b72"), srgb("#8a6447"))
M_MANE = toon("Boar_Mane", srgb("#3b2a24"), srgb("#1f1513"))
M_SNOUT = toon("Boar_Snout", srgb("#d98f86"), srgb("#a2615c"))
M_TUSK = toon("Boar_Tusk", srgb("#f3ead2"), srgb("#bdb197"))
M_HOOF = toon("Boar_Hoof", srgb("#2c2220"), srgb("#171110"))
M_EYE = flat("Boar_Eye", srgb("#1a1214"))
M_EYELIGHT = flat("Boar_EyeLight", srgb("#fff3e0"))
M_ANGRY = flat("Boar_EyeGlow", srgb("#ffdf6b"))
M_LINE = flat("Boar_Outline", srgb("#2a1a18"))
M_LINE.use_backface_culling = True

# ------------------------------------------------------------------ body: metaballs
mb = bpy.data.metaballs.new("BoarBody")
mb.resolution = 0.018
mb.render_resolution = 0.012
mb.threshold = 0.6
RS = float(os.environ.get("BOAR_RS", "1.45"))   # metaball radius scale (tuned so the blob lands on the planned sizes)
body = link(bpy.data.objects.new("BoarBody", mb))


def ell(co, sx, sy, sz, r=0.2, rot=(0, 0, 0)):
    e = mb.elements.new(type="ELLIPSOID")
    e.co = co
    e.radius = r * RS
    e.size_x, e.size_y, e.size_z = sx, sy, sz
    e.rotation = Euler([math.radians(a) for a in rot]).to_quaternion()
    return e


def cap(co, length, r, rot=(0, 0, 0)):
    e = mb.elements.new(type="CAPSULE")
    e.co = co
    e.radius = r * RS
    e.size_x = length
    e.rotation = Euler([math.radians(a) for a in rot]).to_quaternion()
    return e



# barrel body, high shoulders (boars are front-heavy), smaller rump, low heavy head, wedge snout
ell((0, 0.02, 0.38), 0.95, 1.75, 0.95, r=0.2)            # barrel
ell((0, -0.16, 0.45), 1.05, 1.0, 1.05, r=0.21)           # shoulder hump
ell((0, 0.22, 0.39), 0.9, 0.85, 0.9, r=0.18)             # rump
ell((0, -0.38, 0.38), 0.85, 0.95, 0.9, r=0.15)           # head
cap((0, -0.52, 0.31), 0.07, 0.075, rot=(0, 0, 90))       # snout (capsule along y)
ell((0, -0.08, 0.27), 0.8, 1.5, 0.55, r=0.17)            # belly fill

# ------------------------------------------------------------------ convert the blob to a mesh first, so parts can sit on its surface
body.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.convert(target="MESH")
body = bpy.context.active_object
body.name = "Boar"
bb = [body.matrix_world @ Vector(c) for c in body.bound_box]
print("BODY_BOUNDS x", round(min(v.x for v in bb), 3), round(max(v.x for v in bb), 3), "y", round(min(v.y for v in bb), 3),
      round(max(v.y for v in bb), 3), "z", round(min(v.z for v in bb), 3), round(max(v.z for v in bb), 3))
body.data.materials.append(M_FUR)
body.data.materials.append(M_BELLY)
for p in body.data.polygons:
    p.use_smooth = True
    c = p.center
    if p.normal.z < -0.6 and -0.3 < c.y < 0.26:
        p.material_index = 1          # lighter belly
    if c.y < -0.5 and p.normal.z < 0.1:
        p.material_index = 1          # lighter chin / jaw

from mathutils.bvhtree import BVHTree
bvh = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())


def surf(origin, direction):
    """First point of the body surface hit from origin along direction (location, normal)."""
    d = Vector(direction).normalized()
    loc, nrm, _, _ = bvh.ray_cast(Vector(origin), d)
    if loc is None:
        raise RuntimeError("no surface from %s along %s" % (origin, direction))
    return loc, nrm


# ------------------------------------------------------------------ parts on the surface
parts = []


def finish(o, mat, scale=(1, 1, 1)):
    o.scale = scale
    o.data.materials.append(mat)
    for p in o.data.polygons:
        p.use_smooth = True
    parts.append(o)
    return o


def spike(mat, base, direction, r1, r2, depth, verts=8, scale=(1, 1, 1), sink=0.0):
    """A cone standing on base, pointing along direction (sink pushes the base into the body)."""
    d = Vector(direction).normalized()
    centre = Vector(base) + d * (depth / 2 - sink)
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=depth, location=centre,
                                    rotation=d.to_track_quat("Z", "Y").to_euler())
    return finish(bpy.context.active_object, mat, scale)


def ball(mat, centre, r, scale=(1, 1, 1), rot=(0, 0, 0), seg=16, rings=10):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=r, location=centre,
                                         rotation=[math.radians(a) for a in rot])
    return finish(bpy.context.active_object, mat, scale)


# legs: short and stout, front pair under the shoulder hump, tops hidden inside the belly, split hooves
for sx in (-1, 1):
    for y, r in ((-0.17, 0.056), (0.19, 0.05)):
        under, _ = surf((sx * 0.11, y, -1.0), (0, 0, 1))
        top = under.z + 0.07
        spike(M_FUR, (sx * 0.11, y, 0.045), (0, 0, 1), r * 0.55, r * 1.05, top - 0.045, verts=16)
        spike(M_HOOF, (sx * 0.11, y - 0.004, 0.0), (0, 0, 1), r * 0.62, r * 0.55, 0.05, verts=16)

# snout tip: a flat pink nose pad with two nostrils
tip, tn = surf((0, -1.5, 0.315), (0, 1, 0))
ball(M_SNOUT, tip + Vector((0, 0.004, 0)), 0.062, scale=(1, 0.32, 0.82), rot=(0, 0, 0), seg=24, rings=12)
for sx in (-1, 1):
    ball(M_EYE, tip + Vector((sx * 0.022, -0.016, 0.0)), 0.012, scale=(0.85, 0.5, 1.2), seg=12, rings=8)

# tusks: out of the jaw at the sides of the snout, sweeping up, out and a little forward (base + curved tip)
for sx in (-1, 1):
    p, n = surf((sx * 1.0, -0.53, 0.29), (-sx, 0, 0))
    d1 = Vector((sx * 0.45, -0.35, 1.0))
    t1 = spike(M_TUSK, p, d1, 0.019, 0.012, 0.065, verts=12, sink=0.015)
    end = p + d1.normalized() * (0.065 - 0.015)
    spike(M_TUSK, end, Vector((sx * 0.25, -0.1, 1.0)), 0.012, 0.0005, 0.06, verts=12, sink=0.004)

# angry eyes on the sides of the head: dark almond, yellow glint, a heavy brow above
for sx in (-1, 1):
    p, n = surf((sx * 1.0, -0.47, 0.43), (-sx, 0, 0))
    ball(M_EYE, p - n * 0.006, 0.026, scale=(0.5, 1, 0.72), rot=(0, 0, sx * -18))
    ball(M_ANGRY, p + n * 0.006 + Vector((0, -0.006, 0.004)), 0.007, seg=10, rings=6)
    brow = p + Vector((0, 0.035, 0.03)) - n * 0.004
    spike(M_MANE, brow, Vector((-sx * 0.25, -1, -0.45)), 0.022, 0.005, 0.075, verts=4, sink=0.012)

# ears: pointed, up out of the top of the head, angled out and back
for sx in (-1, 1):
    p, n = surf((sx * 0.075, -0.31, 1.5), (0, 0, -1))
    spike(M_FUR, p, Vector((sx * 0.55, 0.35, 1.0)), 0.048, 0.0, 0.11, verts=4, scale=(1, 1, 1), sink=0.02)

# bristly mane: spikes from between the ears down the spine, tallest over the shoulders, raked back
for i in range(10):
    t = i / 9
    y = -0.3 + t * 0.55
    p, n = surf((0, y, 1.5), (0, 0, -1))
    h = 0.07 + 0.075 * math.sin(math.pi * min(1.0, t * 1.35))
    spike(M_MANE, p, Vector((0, 0.55 + 0.3 * t, 1.0)), 0.034, 0.0, h, verts=6, scale=(1, 1, 1), sink=0.012)

# tail: a short stub with a dark tuft
p, n = surf((0, 1.5, 0.43), (0, -1, 0))
stub = spike(M_FUR, p, Vector((0, 1.0, 0.6)), 0.017, 0.009, 0.08, verts=8, sink=0.01)
spike(M_MANE, p + Vector((0, 1.0, 0.6)).normalized() * 0.07, Vector((0, 0.6, 1.0)), 0.022, 0.0, 0.055, verts=6)

# ------------------------------------------------------------------ join into one object
for o in bpy.context.selected_objects:
    o.select_set(False)
for o in parts:
    o.select_set(True)
body.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
boar = bpy.context.active_object
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

# outline: inverted hull, like the game's ToonLit outline pass
boar.data.materials.append(M_LINE)
sol = boar.modifiers.new("Outline", "SOLIDIFY")
sol.thickness = 0.008
sol.offset = 1.0
sol.use_flip_normals = True
sol.use_rim = False
sol.material_offset = 100

# ------------------------------------------------------------------ stage: floor, light, camera
world = bpy.data.worlds.new("World")
scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (*srgb("#cfdcea"), 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 1.0

bpy.ops.mesh.primitive_circle_add(vertices=64, radius=1.2, fill_type="NGON", location=(0, 0, 0))
floor = bpy.context.active_object
floor.data.materials.append(toon("Floor", srgb("#e6dccf"), srgb("#c9bba9")))

sun = bpy.data.lights.new("Sun", "SUN")
sun.energy = 3.5
sun.angle = math.radians(3)
sun_o = link(bpy.data.objects.new("Sun", sun))
sun_o.rotation_euler = Euler((math.radians(50), math.radians(-20), math.radians(-35)))

cam = bpy.data.cameras.new("Cam")
cam.lens = 55
cam_o = link(bpy.data.objects.new("Cam", cam))
scene.camera = cam_o


def look(cam_obj, pos, target):
    cam_obj.location = pos
    d = Vector(target) - Vector(pos)
    cam_obj.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_WORKBENCH"):
    try:
        scene.render.engine = eng
        break
    except TypeError:
        continue
scene.render.resolution_x = 1200
scene.render.resolution_y = 900
scene.view_settings.view_transform = "Standard"
try:
    scene.eevee.taa_render_samples = 32
except AttributeError:
    pass

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "boar.blend"))

shots = {
    "boar_three_quarter.png": ((-1.25, -1.45, 0.85), (0, -0.08, 0.33)),
    "boar_side.png": ((-1.9, -0.05, 0.42), (0, -0.05, 0.33)),
    "boar_front.png": ((-0.25, -1.9, 0.5), (0, -0.2, 0.33)),
}
for name, (pos, tgt) in shots.items():
    look(cam_o, pos, tgt)
    scene.render.filepath = os.path.join(OUT, name)
    bpy.ops.render.render(write_still=True)
print("BOAR_DONE", scene.render.engine, len(boar.data.vertices), "verts")

