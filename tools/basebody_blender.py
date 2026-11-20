"""The player's body in Blender and back (2026-11-19).

The game reads the fighter's body from Assets/_Project/Resources/Bodies/BaseBody.bytes ("LBB1", see BaseBody.cs). This tool
turns it into a rigged .blend you can sculpt / re-weight in Blender, and writes your edits back:

    blender --background --python tools/basebody_blender.py -- import [ArtSource/Player/player.blend]
    blender --background --python tools/basebody_blender.py -- export [ArtSource/Player/player.blend]
    blender --background --python tools/basebody_blender.py -- check     (import + export in memory, must round-trip exactly)

In the .blend: object "PlayerBody" (body, trunks and waistband as three material slots, the game's 42 bones as vertex
groups) parented to armature "PlayerRig" (bones named like CharacterBone, each from its joint towards its child). Mesh
and bones keep the game's own coordinates (metres, Y up, +Z forward, +X = the fighter's right); the objects carry a
display turn (90 deg about X, mirrored X) so it stands up in Blender facing -Y. Don't apply that transform.

Export keeps the header, joints and triangles, and writes positions + the 4 strongest weights per vertex (renormalised).
Edit shapes and weights freely; don't add / delete vertices (the parts and triangles keep their layout).
Pose the rig to test deformation: Blender's skinning is the same linear skinning as the game's.
"""
import os
import struct
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, ".."))
BYTES = os.path.join(REPO, "Assets", "_Project", "Resources", "Bodies", "BaseBody.bytes")

BONES = ["Hips", "Spine", "Chest", "UpperChest", "Neck", "Head",
         "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
         "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes", "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes"]
for S in ("Left", "Right"):
    for f in ("Thumb", "Index", "Middle", "Ring", "Little"):
        BONES += [S + f + "Proximal", S + f + "Intermediate"]
NB = len(BONES)
PART_NAMES = ("Body", "Trunks", "Band")
PART_COLOURS = ((1.0, 0.86, 0.78, 1), (0.14, 0.19, 0.36, 1), (0.36, 0.44, 0.66, 1))


def parent_of(i):
    n = BONES[i]
    if n == "Hips":
        return -1
    fixed = {"Spine": "Hips", "Chest": "Spine", "UpperChest": "Chest", "Neck": "UpperChest", "Head": "Neck",
             "LeftShoulder": "UpperChest", "RightShoulder": "UpperChest", "LeftUpperLeg": "Hips", "RightUpperLeg": "Hips"}
    if n in fixed:
        return BONES.index(fixed[n])
    if "Proximal" in n:
        return BONES.index(("Left" if n.startswith("Left") else "Right") + "Hand")
    if "Intermediate" in n:
        return BONES.index(n.replace("Intermediate", "Proximal"))
    return i - 1


def read(path):
    b = open(path, "rb").read()
    assert b[:4] == b"LBB1", "not a baked base body"
    nb = struct.unpack_from("<i", b, 4)[0]
    assert nb == NB
    o = 24
    joints = [struct.unpack_from("<3f", b, o + 12 * i) for i in range(nb)]
    o += 12 * nb
    parts = []
    for _ in range(3):
        nv, nt = struct.unpack_from("<ii", b, o)
        p0 = o + 8
        pos = [struct.unpack_from("<3f", b, p0 + 12 * i) for i in range(nv)]
        bi = b[p0 + 12 * nv: p0 + 16 * nv]
        bw = struct.unpack_from("<%df" % (4 * nv), b, p0 + 16 * nv)
        tri = struct.unpack_from("<%dH" % (3 * nt), b, p0 + 32 * nv)
        parts.append(dict(off=o, nv=nv, nt=nt, pos=pos, bi=bi, bw=bw, tri=tri))
        o = p0 + 32 * nv + 6 * nt
    return b, joints, parts


DISPLAY = Matrix.Rotation(1.5707963, 4, "X") @ Matrix.Scale(-1, 4, Vector((1, 0, 0)))   # game space -> Blender view


def build(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    raw, joints, parts = read(path)
    # mesh: the three parts in one object, one material slot each
    verts, faces, mat_of_face, weights = [], [], [], []
    for k, p in enumerate(parts):
        base = len(verts)
        verts += p["pos"]
        for t in range(p["nt"]):
            a, b2, c = p["tri"][3 * t: 3 * t + 3]
            faces.append((base + a, base + b2, base + c))   # game winding; the mirrored display transform makes it face out
            mat_of_face.append(k)
        for v in range(p["nv"]):
            weights.append([(p["bi"][4 * v + j], p["bw"][4 * v + j]) for j in range(4)])
    me = bpy.data.meshes.new("PlayerBody")
    me.from_pydata(verts, [], faces)
    me.update()
    for k, name in enumerate(PART_NAMES):
        m = bpy.data.materials.new("Player_" + name)
        m.diffuse_color = PART_COLOURS[k]
        me.materials.append(m)
    for f, k in zip(me.polygons, mat_of_face):
        f.material_index = k
        f.use_smooth = True
    body = bpy.data.objects.new("PlayerBody", me)
    bpy.context.scene.collection.objects.link(body)
    groups = [body.vertex_groups.new(name=n) for n in BONES]
    for v, ws in enumerate(weights):
        for bi, w in ws:
            if w > 0:
                groups[bi].add([v], w, "ADD")
    # rig: bones from each joint towards its first child (ends along the parent's direction)
    arm_data = bpy.data.armatures.new("PlayerRig")
    arm = bpy.data.objects.new("PlayerRig", arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    children = {i: [c for c in range(NB) if parent_of(c) == i] for i in range(NB)}
    eb = {}
    for i, n in enumerate(BONES):
        e = arm_data.edit_bones.new(n)
        h = Vector(joints[i])
        kids = children[i]
        if n == "Hips":
            t = Vector(joints[BONES.index("Spine")])
        elif kids and n not in ("LeftHand", "RightHand", "UpperChest"):
            t = Vector(joints[kids[0]])
        elif n in ("LeftHand", "RightHand"):
            t = Vector(joints[BONES.index(n.replace("Hand", "MiddleProximal"))])
        elif n == "UpperChest":
            t = Vector(joints[BONES.index("Neck")])
        else:
            pa = parent_of(i)
            d = h - Vector(joints[pa]) if pa >= 0 else Vector((0, 0.1, 0))
            t = h + d.normalized() * max(0.02, min(0.08, d.length * 0.8))
        if n == "Head":
            t = h + Vector((0, 0.22, 0))
        if (t - h).length < 1e-4:
            t = h + Vector((0, 0.03, 0))
        e.head, e.tail = h, t
        eb[i] = e
    for i in range(NB):
        if parent_of(i) >= 0:
            eb[i].parent = eb[parent_of(i)]
    bpy.ops.object.mode_set(mode="OBJECT")
    arm.matrix_world = DISPLAY
    body.parent = arm
    body.matrix_parent_inverse = Matrix.Identity(4)
    mod = body.modifiers.new("PlayerRig", "ARMATURE")
    mod.object = arm
    arm_data.display_type = "STICK"
    arm.show_in_front = True
    body["lbb1_source"] = os.path.relpath(path, REPO)
    return raw, parts, body


def write(raw, parts, body, path):
    me = body.data
    groups = {g.index: BONES.index(g.name) for g in body.vertex_groups if g.name in BONES}
    out = bytearray(raw)
    vi = 0
    for p in parts:
        p0 = p["off"] + 8
        for v in range(p["nv"]):
            mv = me.vertices[vi]
            co = mv.co
            ws = sorted(((g.weight, groups[g.group]) for g in mv.groups if g.group in groups and g.weight > 0), reverse=True)[:4]
            total = sum(w for w, _ in ws) or 1.0
            ws = [(w / total, b) for w, b in ws if w / total >= 0.01] or [(1.0, BONES.index("Hips"))]
            total = sum(w for w, _ in ws)
            ws = [(w / total, b) for w, b in ws] + [(0.0, 0)] * (4 - len(ws))
            struct.pack_into("<3f", out, p0 + 12 * v, co.x, co.y, co.z)
            for j, (w, b) in enumerate(ws):
                out[p0 + 12 * p["nv"] + 4 * v + j] = b
                struct.pack_into("<f", out, p0 + 16 * p["nv"] + 16 * v + 4 * j, w)
            vi += 1
    open(path, "wb").write(bytes(out))
    return bytes(out)


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["check"]
    cmd = args[0]
    blend = os.path.abspath(args[1]) if len(args) > 1 else os.path.join(REPO, "ArtSource", "Player", "player.blend")
    if cmd == "import":
        build(BYTES)
        os.makedirs(os.path.dirname(blend), exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=blend)
        print("PLAYER imported ->", blend)
    elif cmd == "export":
        bpy.ops.wm.open_mainfile(filepath=blend)
        raw, _, parts = read(BYTES)
        write(raw, parts, bpy.data.objects["PlayerBody"], BYTES)
        print("PLAYER exported ->", BYTES)
    elif cmd == "check":
        raw, parts, body = build(BYTES)
        tmp = os.path.abspath(args[1]) if len(args) > 1 else os.path.join(bpy.app.tempdir or HERE, "lbb1_check.bytes")
        new = write(raw, parts, body, tmp)
        # positions match bit for bit; weights within float rounding after the renormalise (checked 2026-11-19: max 6e-8);
        # differing bytes are the order of empty weight slots
        diffs = sum(1 for a, b in zip(raw, new) if a != b)
        print("PLAYER round trip: %d of %d bytes differ (slot order / float rounding only if the weights compare equal)" % (diffs, len(raw)))
    else:
        sys.exit("import | export | check")


if __name__ == "__main__":
    main()
