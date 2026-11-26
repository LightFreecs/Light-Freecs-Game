"""The player's attack animations in Blender and back (2026-11-22).

The game plays a move as keyed AttackPose numbers (BasicMovesets.cs) through its own IK (AttackPoseSolver). This tool puts
those numbers on control bones of PlayerRig, one Blender action per move and one keyframe per AttackKey, poses the body from
them with a port of the game's solver, and writes a keyed action back as C# for BuildRush.

    blender --background --python tools/attack_keys_blender.py -- load heavy [--force] [--blend path]
    blender --background --python tools/attack_keys_blender.py -- export heavy [--blend path] [--out path]
    blender --background --python tools/attack_keys_blender.py -- check heavy [--blend path] [--dump out.json]

Moves: heavy (= windup + swing + coiled), windup, swing, coiled, guard, left, right, body, power, charge, surge, all.
The Sword moveset (BuildSword, actions Sword.<move>): sword (= all of them), sword.guard, sword.slash1 .. sword.slash3,
sword.thrust, sword.windup, sword.coiled, sword.fang1, sword.fang2, sword.quartet1 .. sword.quartet4, sword.comet.
Hit reactions (HitPoses.BuildHits, actions Hit.<name>, overhaul T2): hit (= all of them), hit.<name> for each name in HIT_MOVES.
`load` and `export` work on ArtSource/Player/player.blend unless --blend says otherwise; `load` saves the file and refuses to
replace an action that already exists without --force. `export` writes ArtSource/Player/export/<move>.cs.txt (outside Assets,
so Unity doesn't compile it) and changes nothing in the game. `check` changes no file except --dump.

In Blender's GUI: open player.blend, load this file in the Text Editor and press Run Script. That installs the live solver:
moving a control or the frame re-poses the body exactly as the game will.

The controls (bone collection "AttackControls"; everything else is solved, so don't key the body's own bones):
    c.Root   location = RootPos, rotation = RootPitch (X), RootYaw (Y), RootRoll (Z)
    c.Hips   location = HipsPos, rotation = HipsPitch / HipsYaw / HipsRoll
    c.Torso  rotation = TorsoPitch / TorsoYaw / TorsoRoll
    c.Head   rotation = HeadPitch / HeadYaw
    c.ArmL/R location = the fist target (ArmL / ArmR); custom properties Twist (degrees), Wrist (degrees: the wrist bent
             towards the little finger, which swings a held sword's blade forward into line with the forearm) and Open
             (0 fist .. 1 flat hand)
    c.ElbowL/R location = the elbow aim (ElbowL / ElbowR) in the chest's frame, drawn from the shoulder; its length (0..1) is how
             much it overrides the solver's own elbow, so at zero (on the shoulder) the solver decides
    c.LegL/R location = the foot target (LegL / LegR), rotation = FootPitch (X) and FootYaw (Y)

All values are the game's own numbers (metres of the 2.3 m body, Y up, +Z forward, +X = the fighter's right). PlayerRig carries
a display turn (see basebody_blender.py), so these are armature-space axes: Blender's Y is the game's up. Don't apply it.
AttackPose has no knee value: the game always points the knee from the foot's yaw, so turn the foot to turn the knee.

The ease of a key is the interpolation of the segment arriving at it, which Blender stores on the key before it:
Smooth = Sine ease in-out, Snap = Quintic ease out, In = Quadratic ease in. These are the game's exact curves.
Time: 100 frames per second, frame = T * 100.
"""
import json
import math
import os
import re
import struct
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

try:
    HERE = os.path.dirname(os.path.abspath(__file__))
    REPO = os.path.abspath(os.path.join(HERE, ".."))
    if not os.path.isfile(os.path.join(REPO, "CLAUDE.md")):
        raise NameError
except NameError:  # run from Blender's Text Editor: find the repo from the open .blend
    REPO = os.path.abspath(os.path.join(os.path.dirname(bpy.data.filepath), "..", ".."))
BYTES = os.path.join(REPO, "Assets", "_Project", "Resources", "Bodies", "BaseBody.bytes")
MOVESETS = os.path.join(REPO, "Assets", "_Project", "Scripts", "Runtime", "Characters", "BasicMovesets.cs")
BLEND = os.path.join(REPO, "ArtSource", "Player", "player.blend")
EXPORT_DIR = os.path.join(REPO, "ArtSource", "Player", "export")
FPS = 100
TOL_LINEAR, TOL_DEGREES = 1e-4, 1e-3     # the stated round-trip tolerance: metres / 0..1 values, degrees

BONES = ["Hips", "Spine", "Chest", "UpperChest", "Neck", "Head",
         "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
         "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes", "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes"]
for _s in ("Left", "Right"):
    for _f in ("Thumb", "Index", "Middle", "Ring", "Little"):
        BONES += [_s + _f + "Proximal", _s + _f + "Intermediate"]
B = {n: i for i, n in enumerate(BONES)}
NB = len(BONES)


def parent_of(i):
    n = BONES[i]
    fixed = {"Spine": "Hips", "Chest": "Spine", "UpperChest": "Chest", "Neck": "UpperChest", "Head": "Neck",
             "LeftShoulder": "UpperChest", "RightShoulder": "UpperChest", "LeftUpperLeg": "Hips", "RightUpperLeg": "Hips"}
    if n == "Hips":
        return -1
    if n in fixed:
        return B[fixed[n]]
    if "Proximal" in n:
        return B[("Left" if n.startswith("Left") else "Right") + "Hand"]
    if "Intermediate" in n:
        return B[n.replace("Intermediate", "Proximal")]
    return i - 1


PARENT = [parent_of(i) for i in range(NB)]


def side_bone(left, side):
    if side < 0:
        return left
    if left >= B["LeftThumbProximal"]:
        return left + 10
    return left + 4


# ------------------------------------------------------------------ AttackPose
VEC_FIELDS = ("RootPos", "HipsPos", "ArmL", "ArmR", "ElbowL", "ElbowR", "LegL", "LegR")
DEG_FIELDS = ("RootYaw", "RootPitch", "RootRoll", "HipsYaw", "HipsPitch", "HipsRoll", "TorsoYaw", "TorsoPitch", "TorsoRoll",
              "HeadYaw", "HeadPitch", "TwistL", "TwistR", "WristL", "WristR", "FootYawL", "FootYawR", "FootPitchL", "FootPitchR")
FRAC_FIELDS = ("OpenL", "OpenR")
# the order the C# is written in
FIELD_ORDER = ("RootPos", "RootYaw", "RootPitch", "RootRoll", "HipsPos", "HipsYaw", "HipsPitch", "HipsRoll",
               "TorsoYaw", "TorsoPitch", "TorsoRoll", "HeadYaw", "HeadPitch",
               "ArmL", "TwistL", "WristL", "OpenL", "ElbowL", "ArmR", "TwistR", "WristR", "OpenR", "ElbowR",
               "LegL", "FootYawL", "FootPitchL", "LegR", "FootYawR", "FootPitchR")
# channels added after actions were already keyed: an action without the curve reads the default
LATE_FIELDS = ("WristL", "WristR")


def new_pose():
    return {f: ((0.0, 0.0, 0.0) if f in VEC_FIELDS else 0.0) for f in FIELD_ORDER}


def lerp_pose(a, b, t):
    o = {}
    for f in FIELD_ORDER:
        o[f] = tuple(x + (y - x) * t for x, y in zip(a[f], b[f])) if f in VEC_FIELDS else a[f] + (b[f] - a[f]) * t
    return o


# ------------------------------------------------------------------ reading BuildRush (the subset of C# it is written in)
TOKEN = re.compile(r'\s+|//[^\n]*|/\*.*?\*/|(?P<str>"[^"]*")|(?P<num>\d+\.\d*f?|\.\d+f?|\d+f?)|(?P<id>[A-Za-z_]\w*)|(?P<op>=>|[{}()\[\],;=.<>+\-*/?!:])', re.S)


def tokenize(text):
    out, i = [], 0
    while i < len(text):
        m = TOKEN.match(text, i)
        if not m:
            raise SyntaxError("can't read C# at: " + text[i:i + 40])
        i = m.end()
        if m.lastgroup:
            out.append((m.lastgroup, m.group(m.lastgroup)))
    return out


class Reader:
    """Evaluates BuildRush's statements: poses, the P / K helpers, local pose functions, AttackPose.Lerp and the move tables."""

    def __init__(self, tokens, env=None):
        self.t, self.i, self.env, self.funcs = tokens, 0, env if env is not None else {}, {}

    def peek(self, k=0):
        j = self.i + k
        return self.t[j][1] if j < len(self.t) else None

    def next(self):
        self.i += 1
        return self.t[self.i - 1][1]

    def expect(self, s):
        got = self.next()
        if got != s:
            raise SyntaxError("expected %r, got %r near %r" % (s, got, [x[1] for x in self.t[max(0, self.i - 6):self.i + 3]]))

    def accept(self, s):
        if self.peek() == s:
            self.i += 1
            return True
        return False

    def skip_balanced(self, opener, closer):
        self.expect(opener)
        depth = 1
        while depth:
            s = self.next()
            depth += (s == opener) - (s == closer)

    def args(self):
        self.expect("(")
        out = []
        while not self.accept(")"):
            out.append(self.expr())
            self.accept(",")
        return out

    def initializer(self):
        self.expect("{")
        o = {}
        while not self.accept("}"):
            name = self.next()
            self.expect("=")
            o[name] = self.expr()
            self.accept(",")
        return o

    def expr(self):
        s = self.peek()
        kind = self.t[self.i][0]
        if s == "-":
            self.i += 1
            return -self.expr()
        if kind == "num":
            self.i += 1
            return float(s.rstrip("f"))
        if kind == "str":
            self.i += 1
            return s[1:-1]
        if s in ("true", "false"):
            self.i += 1
            return s == "true"
        if s == "new":
            return self.new_expr()
        if s in ("KeyEase", "StrikeLimb"):
            self.i += 1
            self.expect(".")
            return self.next()
        if s == "P":
            return self.pose_call()
        if s == "K":
            self.i += 1
            a = self.args()
            return {"T": a[0], "Ease": a[1], "Pose": a[2]}
        if s in self.funcs:
            return self.call_func()
        if s in self.env:
            self.i += 1
            return self.env[s]
        raise SyntaxError("unknown expression %r" % s)

    def new_expr(self):
        self.expect("new")
        if self.accept("["):
            self.expect("]")
            self.expect("{")
            out = []
            while not self.accept("}"):
                out.append(self.expr())
                self.accept(",")
            return out
        ty = self.next()
        if ty in ("Vector3", "Vector2"):
            return tuple(self.args())
        if ty == "AttackPose":
            p = new_pose()
            if self.peek() == "(":
                self.args()
            if self.peek() == "{":
                p.update(self.initializer())
            return p
        if self.peek() == "(":
            self.args()
        return self.initializer()

    def pose_call(self):
        self.expect("P")
        self.expect("(")
        p = dict(self.env["guard"])
        if self.accept(")"):
            return p
        var = self.next()
        self.expect("=>")
        self.expect("{")
        while not self.accept("}"):
            self.expect(var)
            self.expect(".")
            field = self.next()
            self.expect("=")
            if field not in p:
                raise SyntaxError("unknown pose field " + field)
            p[field] = self.expr()
            self.expect(";")
        self.expect(")")
        return p

    def call_func(self):
        params, body = self.funcs[self.next()]
        values = self.args()
        saved_i, saved = self.i, {n: self.env.get(n) for n in params}
        self.env.update(zip(params, values))
        self.i = body
        out = self.expr()
        self.i = saved_i
        for n, v in saved.items():
            if v is None:
                self.env.pop(n, None)
            else:
                self.env[n] = v
        return out

    def local_function(self):
        """`AttackPose Name(float a, Vector3 b) => expr;` is remembered; the P / K helpers are built in."""
        self.expect("AttackPose") if self.peek() == "AttackPose" else self.expect("AttackKey")
        name = self.next()
        if name in ("P", "K"):
            self.skip_balanced("(", ")")
            if self.peek() == "{":
                self.skip_balanced("{", "}")
            else:
                while self.next() != ";":
                    pass
            return
        self.expect("(")
        params = []
        while not self.accept(")"):
            self.next()             # the type
            params.append(self.next())
            self.accept(",")
        self.expect("=>")
        body = self.i
        depth = 0
        while True:                 # to the end of the statement
            s = self.next()
            depth += s in "({" and 1 or 0
            depth -= s in ")}" and 1 or 0
            if s == ";" and depth == 0:
                break
        self.funcs[name] = (params, body)

    def statements(self):
        while self.i < len(self.t):
            s = self.peek()
            if s == "return":
                return
            if s == "var":
                self.i += 1
                name = self.next()
                self.expect("=")
                self.env[name] = self.expr()
                self.expect(";")
            elif s in ("AttackPose", "AttackKey") and self.peek(1) != ".":
                self.local_function()
            elif s == "AttackPose":     # AttackPose.Lerp(a, b, t, o);
                self.i += 1
                self.expect(".")
                self.expect("Lerp")
                a, b, t, o = self.args()
                o.update(lerp_pose(a, b, t))
                self.expect(";")
            elif s in self.env and self.peek(1) == ".":     # drop.ArmR = ...;
                target = self.env[self.next()]
                self.expect(".")
                field = self.next()
                self.expect("=")
                target[field] = self.expr()
                self.expect(";")
            else:
                raise SyntaxError("can't read the statement at %r" % [x[1] for x in self.t[self.i:self.i + 6]])


def read_builder(name, path=MOVESETS):
    """The local variables a moveset builder (BuildRush, BuildSword) leaves before its return."""
    text = open(path, encoding="utf-8").read()
    start = text.index("{", re.search(r"private static [\w\[\]<>, ]+ %s\(" % name, text).start())
    depth, i = 0, start
    while True:
        depth += (text[i] == "{") - (text[i] == "}")
        i += 1
        if depth == 0:
            break
    r = Reader(tokenize(text[start + 1: i - 1]))
    r.statements()
    return r.env


def _single(name, pose):
    return {"Name": name, "Keys": [{"T": 0.0, "Ease": "Smooth", "Pose": pose}]}


def read_rush(path=MOVESETS):
    e = read_builder("BuildRush", path)
    h = e["heavy"]
    return {"guard": _single("Guard", e["guard"]), "left": e["left"], "right": e["right"], "body": e["body"], "power": e["power"],
            "windup": h["WindUp"], "swing": h["Swing"], "coiled": _single("Charged punch coiled", h["Coiled"]),
            "charge": e["charge"], "surge": e["surge"]}


SWORD_MOVES = ("sword.guard", "sword.slash1", "sword.slash2", "sword.slash3", "sword.thrust", "sword.windup", "sword.coiled",
               "sword.fang1", "sword.fang2", "sword.quartet1", "sword.quartet2", "sword.quartet3", "sword.quartet4", "sword.comet")
SINGLES = ("guard", "coiled", "sword.guard", "sword.coiled")


def read_sword(path=MOVESETS):
    e = read_builder("BuildSword", path)
    h = e["heavy"]
    out = {"sword.guard": _single("Sword guard", e["guard"]), "sword.windup": h["WindUp"],
           "sword.coiled": _single("Sword pre-motion coiled", h["Coiled"])}
    for m in SWORD_MOVES:
        if m not in out:
            out[m] = e[m[len("sword."):]]
    return out


# hit reactions (HitPoses.BuildHits): flinches by where the blow lands, stagger, knockdown + get-up / tech, winded, spin-out
HIT_MOVES = ("hit.flinchHigh", "hit.flinchHighR", "hit.flinchBody", "hit.stagger", "hit.knockdown", "hit.getUp", "hit.tech",
             "hit.winded", "hit.spinOut")


def read_hits(path=MOVESETS):
    e = read_builder("BuildHits", path)
    return {m: e[m[len("hit."):]] for m in HIT_MOVES if m[len("hit."):] in e}


def read_all(path=MOVESETS):
    moves = read_rush(path)
    text = open(path, encoding="utf-8").read()
    if "private static BasicMoveset BuildSword(" in text:
        moves.update(read_sword(path))
    if " BuildHits(" in text:
        moves.update(read_hits(path))
    return moves


GROUPS = {"heavy": ("windup", "swing", "coiled"),
          "all": ("guard", "left", "right", "body", "power", "windup", "swing", "coiled", "charge", "surge"),
          "sword": SWORD_MOVES, "hit": HIT_MOVES}


def move_names(arg):
    names = GROUPS.get(arg, (arg,))
    for n in names:
        if n not in GROUPS["all"] and n not in SWORD_MOVES and n not in HIT_MOVES:
            sys.exit("unknown move %r (heavy, windup, swing, coiled, guard, left, right, body, power, charge, surge, all; sword, %s)"
                     % (n, ", ".join(SWORD_MOVES)))
    return names


# ------------------------------------------------------------------ the game's rig and solver (CharacterRig + AttackPoseSolver), hs = 1
def AA(deg, axis):
    return Quaternion(Vector(axis).normalized(), math.radians(deg))


UP, RIGHT, FWD = Vector((0, 1, 0)), Vector((1, 0, 0)), Vector((0, 0, 1))
TORSO_SIDE, TORSO_FRONT, TORSO_BACK, FIST_ROOM, FIST_RADIUS = 0.19, 0.135, 0.12, 0.07, 0.065
FIST_LOCAL = (0.025, -0.069, 0.017)
SETTLE_PASSES = 4


def clamp(v, a, b):
    return max(a, min(b, v))


def smoothstep(a, b, x):
    t = clamp((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def project_on_plane(v, n):
    return v - n * v.dot(n)


def frame_q(x, y):
    X = x.normalized()
    Y = project_on_plane(y, X).normalized()
    Z = X.cross(Y)
    return Matrix((X, Y, Z)).transposed().to_quaternion()


def aim_frame(x_bind, y_bind, x_target, y_target):
    return frame_q(x_target, y_target) @ frame_q(x_bind, y_bind).inverted()


def read_joints(path=BYTES):
    b = open(path, "rb").read()
    assert b[:4] == b"LBB1", "not a baked base body"
    assert struct.unpack_from("<i", b, 4)[0] == NB
    return [Vector(struct.unpack_from("<3f", b, 24 + 12 * i)) for i in range(NB)]


class Rig:
    def __init__(self, joints):
        self.joints = joints
        self.bind = [joints[i] - joints[PARENT[i]] if PARENT[i] >= 0 else joints[i].copy() for i in range(NB)]
        self.pos0 = self.bind[0].copy()
        self.model = [Quaternion() for _ in range(NB)]
        a = math.radians(40)
        self.arm_dir = lambda s: Vector((s * math.sin(a), -math.cos(a), 0))
        self.palm = lambda s: Vector((-s * math.cos(a), -math.sin(a), 0))

    def set(self, i, q):
        self.model[i] = q.copy()

    def inherit(self, i):
        self.set(i, self.model[PARENT[i]] if PARENT[i] >= 0 else Quaternion())

    def pos(self, i):
        chain = []
        while i >= 0:
            chain.append(i)
            i = PARENT[i]
        p = self.pos0.copy()
        for c in reversed(chain[:-1]):
            p = p + self.model[PARENT[c]] @ self.bind[c]
        return p

    def finger_curl(self, side, curl):
        hand = self.model[side_bone(B["LeftHand"], side)]
        cu = self.arm_dir(side).cross(self.palm(side)).normalized()
        axis, palm, d = hand @ cu, hand @ self.palm(side), hand @ self.arm_dir(side)
        for f in range(1, 5):
            prox = side_bone(B["LeftThumbProximal"] + f * 2, side)
            q1 = AA(curl * 85, axis) @ hand
            self.set(prox, q1)
            self.set(prox + 1, AA(curl * 100, axis) @ q1)
        thumb = d.cross(palm).normalized()
        t1 = side_bone(B["LeftThumbProximal"], side)
        qt = AA(curl * 35, palm) @ AA(curl * 25, thumb) @ hand
        self.set(t1, qt)
        self.set(t1 + 1, AA(curl * 45, thumb) @ qt)

    def rest_arm(self, side):
        ua = side_bone(B["LeftUpperArm"], side)
        qa = AA(-side * 29, FWD) @ AA(-8, RIGHT)
        up = qa @ self.bind[ua + 1].normalized()
        bend = (qa @ self.arm_dir(side)).cross(FWD).normalized()
        fore = AA(22, bend) @ qa @ self.bind[ua + 2].normalized()
        d = (up * self.bind[ua + 1].length + fore * self.bind[ua + 2].length).normalized()
        return d, project_on_plane(up, d).normalized()

    def two_bone(self, upper, target, pole, bind_pole):
        mid, end = upper + 1, upper + 2
        a = self.pos(upper)
        l1, l2 = self.bind[mid].length, self.bind[end].length
        d1, d2 = self.bind[mid] / l1, self.bind[end] / l2
        cur_m, end_model = self.model[mid].copy(), self.model[end].copy()
        to = target - a
        dist = clamp(to.length, abs(l1 - l2) + 1e-3, l1 + l2 - 1e-3)
        d = to.normalized() if to.length_squared > 1e-10 else Vector((0, -1, 0))
        ang = math.acos(clamp((l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist), -1, 1))
        pd = project_on_plane(pole - a, d)
        if pd.length_squared < 1e-6:
            pd = project_on_plane(Vector((0, -1, 0)), d)
        pd.normalize()
        elbow_dir = d * math.cos(ang) + pd * math.sin(ang)
        elbow, end_pos = a + elbow_dir * l1, a + d * dist
        hinge = pd.cross(d).normalized()
        hinge_bind = bind_pole.cross(d1).normalized()
        self.set(upper, aim_frame(d1, hinge_bind, elbow_dir, hinge))
        self.set(mid, aim_frame(d2, hinge_bind, (end_pos - elbow).normalized(), hinge))
        self.set(end, self.model[mid] @ cur_m.inverted() @ end_model)

    def follow_arm(self, side, arm_dir):
        sh = side_bone(B["LeftShoulder"], side)
        chest = self.model[PARENT[sh]]
        if arm_dir.length_squared < 1e-8:
            self.set(sh, chest)
            return
        want = (chest.inverted() @ arm_dir).normalized()
        bind_e = math.asin(clamp(-self.arm_dir(side).y, -1, 1))
        now_e = math.asin(clamp(-want.y, -1, 1))
        up = clamp(math.degrees(bind_e - now_e) * 0.25, 0, 25)
        fwd = clamp((want.z - 0.14) * 17, -8, 14)
        self.set(sh, chest @ AA(-side * fwd, UP) @ AA(side * up, FWD))


def _girth(u):
    return smoothstep(0, 0.1, u) * (1 - smoothstep(0.85, 1, u))


def keep_out_room(rig, target, room):
    a, b = rig.pos(B["Hips"]), rig.pos(B["Neck"])
    axis = b - a
    if axis.length_squared < 1e-6:
        return target.copy()
    u = (target - a).dot(axis) / axis.length_squared
    g = _girth(u)
    if g <= 0:
        return target.copy()
    c = a + axis * u
    chest = rig.model[B["Chest"]]
    o = chest.inverted() @ (target - c)
    rx = (TORSO_SIDE + room) * g
    rz = ((TORSO_FRONT if o.z >= 0 else TORSO_BACK) + room) * g
    k = (o.x / rx) ** 2 + (o.z / rz) ** 2
    if k >= 1:
        return target.copy()
    if k < 1e-6:
        o = Vector((0, o.y, rz))
    else:
        s = 1 / math.sqrt(k)
        o = Vector((o.x * s, o.y, o.z * s))
    return c + chest @ o


def inside_torso(rig, p, room):
    a, b = rig.pos(B["Hips"]), rig.pos(B["Neck"])
    axis = b - a
    if axis.length_squared < 1e-6:
        return False
    u = (p - a).dot(axis) / axis.length_squared
    g = smoothstep(-0.05, 0.05, u) * (1 - smoothstep(0.9, 1.05, u))
    if g <= 0:
        return False
    o = rig.model[B["Chest"]].inverted() @ (p - (a + axis * u))
    rx = (TORSO_SIDE + room) * g
    rz = ((TORSO_FRONT if o.z >= 0 else TORSO_BACK) + room) * g
    return (o.x / rx) ** 2 + (o.z / rz) ** 2 < 1


def turn_towards(a, b, t, axis, outside):
    ang = math.degrees(math.atan2(axis.dot(a.cross(b)), clamp(a.dot(b), -1, 1)))
    if abs(ang) > 150:
        alt = ang - math.copysign(360, ang)
        if (AA(alt * 0.5, axis) @ a).dot(outside) > (AA(ang * 0.5, axis) @ a).dot(outside):
            ang = alt
    return (AA(ang * t, axis) @ a).normalized()


def elbow_pole(rig, side, shoulder, fist, chest, hint):
    ua = side_bone(B["LeftUpperArm"], side)
    l1, l2 = rig.bind[ua + 1].length, rig.bind[ua + 2].length
    to = fist - shoulder
    dist = clamp(to.length, abs(l1 - l2) + 1e-3, l1 + l2 - 1e-3)
    d = to.normalized() if to.length_squared > 1e-10 else Vector((0, -1, 0))

    def perp(v):
        w = project_on_plane(v, d)
        return None if w.length_squared < 1e-6 else w.normalized()

    outward = perp(chest @ Vector((side, -0.25, 0))) or perp(chest @ Vector((0, -1, 0))) or Vector((0, 0, 0))
    chest_pole = perp(chest @ Vector((side * 0.5, -0.7, -0.25))) or outward
    rest_dir, rest_elbow = rig.rest_arm(side)
    rest_dir = chest @ rest_dir
    carried = perp(rest_dir.rotation_difference(d) @ (chest @ rest_elbow)) or chest_pole
    swing = math.degrees(rest_dir.angle(d, 0.0))
    tb = clamp((swing - 70) / 50, 0, 1)
    base = carried.lerp(chest_pole, tb * tb * (3 - 2 * tb))
    base = base.normalized() if base.length_squared > 1e-12 else chest_pole
    hw = hint.length
    if hw > 1e-4:
        aim = perp(chest @ hint)
        if aim is not None:
            base = turn_towards(base, aim, min(1.0, hw), d, outward)
    ang = math.acos(clamp((l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist), -1, 1))
    along = d * (math.cos(ang) * l1)
    off = math.sin(ang) * l1
    sinks = lambda t: inside_torso(rig, shoulder + along + turn_towards(base, outward, t, d, outward) * off, 0.05)
    lo, hi = 0.0, -1.0
    for i in range(11):
        t = i / 10
        if not sinks(t):
            hi = t
            break
        lo = t
    if hi > 0:
        for _ in range(6):
            m = (lo + hi) * 0.5
            if sinks(m):
                lo = m
            else:
                hi = m
    return shoulder + turn_towards(base, outward, 1.0 if hi < 0 else hi, d, outward)


def fist_centre(rig, side):
    hand = side_bone(B["LeftHand"], side)
    return rig.pos(hand) + rig.model[hand] @ Vector((side * FIST_LOCAL[0], FIST_LOCAL[1], FIST_LOCAL[2]))


def fist_clearance(rig, c):
    chest = rig.model[B["UpperChest"]]
    mid = (rig.pos(B["LeftUpperArm"]) + rig.pos(B["RightUpperArm"])) * 0.5 + chest @ Vector((0, -0.02, 0.02))
    o = chest.inverted() @ (c - mid)
    ax, ay = 0.27 + FIST_RADIUS, 0.14 + FIST_RADIUS
    az = (0.14 if o.z >= 0 else 0.13) + FIST_RADIUS
    s = (o.x / ax) ** 2 + (o.y / ay) ** 2 + (o.z / az) ** 2
    push = Vector((0, 0, 0))
    if s < 1:
        z_front = math.sqrt(max(0, 1 - (o.x / ax) ** 2 - (o.y / ay) ** 2)) * (0.14 + FIST_RADIUS)
        front = Vector((0, 0, z_front - o.z))
        radial = o * (1 / math.sqrt(max(s, 1e-6)) - 1)
        push = chest @ radial.lerp(front, smoothstep(-0.6 * az, -0.2 * az, o.z))
    p = c + push
    return push + (keep_out_room(rig, p, FIST_RADIUS) - p)


def euler_q(yaw, pitch, roll):
    return AA(yaw, UP) @ AA(pitch, RIGHT) @ AA(roll, FWD)


def wrist_axis(rig, side):
    """AttackPoseSolver.WristAxis: in the hand's bind frame, square to the thumb and the forearm; + bends the thumb side towards
    the forearm's far end (towards the little finger)."""
    hand, thumb = side_bone(B["LeftHand"], side), side_bone(B["LeftThumbProximal"], side)
    fore = rig.bind[hand].normalized()
    up = project_on_plane(rig.bind[thumb], fore).normalized()
    return up.cross(fore).normalized()


def knee_pole(hip, foot, fy):
    d = (foot - hip).normalized()
    right = AA(fy, UP) @ RIGHT
    n = d.cross(right)
    if n.length_squared < 1e-6:
        n = AA(fy, UP) @ FWD
    return (hip + foot) * 0.5 + n.normalized() * 0.5


def solve(rig, P):
    """AttackPoseSolver.Apply: writes every bone's model rotation (the root is applied by the caller)."""
    RQ = euler_q(P["RootYaw"], P["RootPitch"], P["RootRoll"])
    RQi, RP = RQ.inverted(), Vector(P["RootPos"])
    to_model = lambda a: RQi @ (Vector(a) - RP)
    rig.pos0 = rig.bind[0] + Vector(P["HipsPos"])
    H = euler_q(P["HipsYaw"], P["HipsPitch"], P["HipsRoll"])
    rig.set(B["Hips"], H)
    T = lambda f: euler_q(P["TorsoYaw"] * f, P["TorsoPitch"] * f, P["TorsoRoll"] * f)
    rig.set(B["Spine"], H @ T(0.35))
    rig.set(B["Chest"], H @ T(0.7))
    rig.set(B["UpperChest"], H @ T(1))
    chest = rig.model[B["UpperChest"]].copy()
    chest_yaw = P["HipsYaw"] + P["TorsoYaw"]
    head_yaw = chest_yaw + clamp(P["HeadYaw"] - P["RootYaw"] - chest_yaw, -55, 55)
    head = AA(head_yaw, UP) @ AA(P["HeadPitch"], RIGHT)
    rig.set(B["Neck"], chest.slerp(head, 0.5))
    rig.set(B["Head"], head)
    for side in (-1, 1):
        s = "L" if side < 0 else "R"
        sh = side_bone(B["LeftShoulder"], side)
        ua = sh + 1
        rig.inherit(sh)
        fist = keep_out_room(rig, to_model(P["Arm" + s]), FIST_ROOM)
        for b in (ua, ua + 1, ua + 2):
            rig.set(b, chest)
        hint = Vector(P["Elbow" + s])
        for _ in range(3):
            rig.inherit(sh)
            rig.follow_arm(side, fist - rig.pos(ua))
            for b in (ua, ua + 1, ua + 2):
                rig.set(b, chest)
            shoulder = rig.pos(ua)
            rig.two_bone(ua, fist, elbow_pole(rig, side, shoulder, fist, chest, hint), Vector((0, 0, -1)))
            push = fist_clearance(rig, fist_centre(rig, side))
            if push.length < 0.002:
                break
            fist = fist + push * 1.15
        fore = (rig.pos(ua + 2) - rig.pos(ua + 1)).normalized()
        roll = AA(P["Twist" + s] * 0.5, fore)
        rig.set(ua + 1, roll @ rig.model[ua + 1])
        rig.set(ua + 2, roll @ rig.model[ua + 1])
        if P["Wrist" + s]:
            rig.set(ua + 2, rig.model[ua + 2] @ AA(P["Wrist" + s], wrist_axis(rig, side)))
        rig.finger_curl(side, 1 - clamp(P["Open" + s], 0, 1))
    for side in (-1, 1):
        s = "L" if side < 0 else "R"
        ul = side_bone(B["LeftUpperLeg"], side)
        t = P["Leg" + s]
        fy, fp = P["FootYaw" + s] - P["RootYaw"], P["FootPitch" + s]
        for b in (ul, ul + 1, ul + 2):
            rig.set(b, H)
        foot = to_model(t)
        rig.two_bone(ul, foot, knee_pole(rig.pos(ul), foot, fy), Vector((0, 0, 1)))
        planted = AA(fy, UP) @ AA(fp, RIGHT)
        lifted = rig.model[ul + 1] @ AA(fp, RIGHT)
        u = clamp((t[1] - 0.2) / 0.25, 0, 1)
        lift = 0.5 - 0.5 * math.cos(math.pi * u)
        foot_rot = planted.slerp(lifted, lift)
        rig.set(ul + 2, foot_rot)
        if fp > 0:
            rig.set(ul + 3, AA(fy, UP).slerp(foot_rot, lift))
        else:
            rig.inherit(ul + 3)


def solve_settled(rig, P):
    """
    The game's solver keeps state between frames: the left fist's clearance reads the right collarbone as the previous frame left
    it. Solving from the bind pose and repeating until that settles gives the pose a held frame reaches, whatever was on screen
    before it, so scrubbing in Blender can't change the result.
    """
    rig.model = [Quaternion() for _ in range(NB)]
    for _ in range(SETTLE_PASSES):
        solve(rig, P)


def root_matrix(P):
    return Matrix.Translation(P["RootPos"]) @ euler_q(P["RootYaw"], P["RootPitch"], P["RootRoll"]).to_matrix().to_4x4()


# ------------------------------------------------------------------ the control bones
ARM_CONTROLS = ("c.ArmL", "c.ArmR", "c.LegL", "c.LegR")
CONTROLS = ("c.Root", "c.Hips", "c.Torso", "c.Head") + ARM_CONTROLS + ("c.ElbowL", "c.ElbowR")
RAD = math.pi / 180


def _channels():
    """(pose field, component or None, bone, data property, array index or None, Blender units per game unit)"""
    out = []

    def vec(field, bone):
        out.extend((field, k, bone, "location", k, 1.0) for k in range(3))

    def ang(field, bone, axis):
        out.append((field, None, bone, "rotation_euler", axis, RAD))

    vec("RootPos", "c.Root"); ang("RootPitch", "c.Root", 0); ang("RootYaw", "c.Root", 1); ang("RootRoll", "c.Root", 2)
    vec("HipsPos", "c.Hips"); ang("HipsPitch", "c.Hips", 0); ang("HipsYaw", "c.Hips", 1); ang("HipsRoll", "c.Hips", 2)
    ang("TorsoPitch", "c.Torso", 0); ang("TorsoYaw", "c.Torso", 1); ang("TorsoRoll", "c.Torso", 2)
    ang("HeadPitch", "c.Head", 0); ang("HeadYaw", "c.Head", 1)
    for s in "LR":
        vec("Arm" + s, "c.Arm" + s)
        out.append(("Twist" + s, None, "c.Arm" + s, '["Twist"]', None, 1.0))
        out.append(("Wrist" + s, None, "c.Arm" + s, '["Wrist"]', None, 1.0))
        out.append(("Open" + s, None, "c.Arm" + s, '["Open"]', None, 1.0))
        vec("Elbow" + s, "c.Elbow" + s)
        vec("Leg" + s, "c.Leg" + s)
        ang("FootPitch" + s, "c.Leg" + s, 0)
        ang("FootYaw" + s, "c.Leg" + s, 1)
    return out


CHANNELS = _channels()


def get_rig():
    arm = bpy.data.objects.get("PlayerRig")
    if arm is None or arm.type != "ARMATURE":
        sys.exit("no armature PlayerRig in this file")
    return arm


def ensure_controls(arm):
    """Adds the control bones (no skin weights) if they're missing. Rest: identity axes, so a bone's numbers are the game's."""
    joints = read_joints()
    for i, n in enumerate(BONES):
        got = arm.data.bones[n].head_local
        if (got - joints[i]).length > 1e-3:
            sys.exit("PlayerRig's %s is not on the game's joint (%.4f m off): re-import the body first" % (n, (got - joints[i]).length))
    if all(c in arm.data.bones for c in CONTROLS):
        ensure_wrist(arm)
        return
    bpy.context.view_layer.objects.active = arm
    arm.hide_set(False)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.data.edit_bones
    place = {"c.Root": (Vector((0, 0, 0)), None), "c.Hips": (joints[B["Hips"]], "c.Root"),
             "c.Torso": (joints[B["UpperChest"]], "Chest"), "c.Head": (joints[B["Head"]], "Neck"),
             "c.ElbowL": (joints[B["LeftUpperArm"]], "UpperChest"), "c.ElbowR": (joints[B["RightUpperArm"]], "UpperChest")}
    for c in ARM_CONTROLS:
        place[c] = (Vector((0, 0, 0)), None)
    for c in CONTROLS:
        if c in eb:
            continue
        e = eb.new(c)
        head = place[c][0]
        e.head, e.tail = head, head + Vector((0, 0.1, 0))
        e.roll = 0
        e.use_deform = False
    for c in CONTROLS:
        if place[c][1]:
            eb[c].parent = eb[place[c][1]]
            eb[c].use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    coll = arm.data.collections.get("AttackControls") or arm.data.collections.new("AttackControls")
    for c in CONTROLS:
        coll.assign(arm.data.bones[c])
    for c in ("c.Torso", "c.Head"):
        arm.data.bones[c].use_inherit_rotation = False    # their rotation is the pose's number, whatever the bone they ride on does
    for c in ("c.Root", "c.Hips", "c.Torso", "c.Head", "c.LegL", "c.LegR"):
        arm.pose.bones[c].rotation_mode = "ZXY"           # = Unity's yaw * pitch * roll
    for c in ("c.ArmL", "c.ArmR", "c.ElbowL", "c.ElbowR"):
        arm.pose.bones[c].lock_rotation = (True, True, True)
    for c in CONTROLS:
        arm.pose.bones[c].lock_scale = (True, True, True)
    for c in ("c.Torso", "c.Head"):
        arm.pose.bones[c].lock_location = (True, True, True)
    arm.pose.bones["c.Head"].lock_rotation[2] = True
    for c in ("c.LegL", "c.LegR"):
        arm.pose.bones[c].lock_rotation[2] = True
    for c in ("c.ArmL", "c.ArmR"):
        pb = arm.pose.bones[c]
        pb["Twist"] = 0.0
        pb.id_properties_ui("Twist").update(min=-180.0, max=180.0, description="AttackPose.Twist: fist roll, degrees")
        pb["Open"] = 0.0
        pb.id_properties_ui("Open").update(min=0.0, max=1.0, description="AttackPose.Open: 0 fist, 1 flat hand")
    ensure_wrist(arm)
    for pb in arm.pose.bones:
        if pb.name not in CONTROLS:
            pb.rotation_mode = "QUATERNION"


def ensure_wrist(arm):
    """The Wrist property (added 2026-11-26, after the controls were first made) on both fist controls."""
    for c in ("c.ArmL", "c.ArmR"):
        pb = arm.pose.bones[c]
        if "Wrist" not in pb:
            pb["Wrist"] = 0.0
            pb.id_properties_ui("Wrist").update(min=-90.0, max=90.0,
                                                description="AttackPose.Wrist: the wrist bent towards the little finger (+) or the thumb (-), degrees")


def read_controls(arm):
    P = new_pose()
    vecs = {f: [0.0, 0.0, 0.0] for f in VEC_FIELDS}
    for field, comp, bone, prop, idx, unit in CHANNELS:
        pb = arm.pose.bones[bone]
        v = pb[prop[2:-2]] if prop.startswith('["') else getattr(pb, prop)[idx]
        if comp is None:
            P[field] = v / unit
        else:
            vecs[field][comp] = v
    for f in VEC_FIELDS:
        P[f] = tuple(vecs[f])
    return P


def write_controls(arm, P):
    for field, comp, bone, prop, idx, unit in CHANNELS:
        pb = arm.pose.bones[bone]
        v = P[field][comp] if comp is not None else P[field] * unit
        if prop.startswith('["'):
            pb[prop[2:-2]] = v
        else:
            getattr(pb, prop)[idx] = v


_RIG_CACHE = {}


def game_rig():
    if "rig" not in _RIG_CACHE:
        _RIG_CACHE["rig"] = Rig(read_joints())
    return _RIG_CACHE["rig"]


def pose_body(arm, P):
    """Solves P with the game's solver and writes it on the body's bones (the root goes on Hips, which has no parent)."""
    rig = game_rig()
    solve_settled(rig, P)
    root = root_matrix(P)
    world = [None] * NB
    positions = [rig.pos(i) for i in range(NB)]
    for i, n in enumerate(BONES):
        world[i] = root @ Matrix.Translation(positions[i]) @ rig.model[i].to_matrix().to_4x4() @ Matrix.Translation(-rig.joints[i])
    for i, n in enumerate(BONES):
        pb = arm.pose.bones[n]
        rest = pb.bone.matrix_local
        parent = world[PARENT[i]] if PARENT[i] >= 0 else Matrix.Identity(4)
        pb.matrix_basis = rest.inverted() @ parent.inverted() @ world[i] @ rest
    return root, positions


# ------------------------------------------------------------------ actions
EASE_TO_BLENDER = {"Smooth": ("SINE", "EASE_IN_OUT"), "Snap": ("QUINT", "EASE_OUT"), "In": ("QUAD", "EASE_IN")}
BLENDER_TO_EASE = {v: k for k, v in EASE_TO_BLENDER.items()}


def action_name(move):
    if move.startswith("sword."):
        return "Sword." + move[len("sword."):]
    if move.startswith("hit."):
        return "Hit." + move[len("hit."):]
    return "Rush." + move


def channelbag(action, arm):
    slot = action.slots[0]
    return action.layers[0].strips[0].channelbag(slot, ensure=True)


def data_path(bone, prop):
    return 'pose.bones["%s"]%s' % (bone, prop if prop.startswith("[") else "." + prop)


def build_action(arm, move, data, force=False):
    name = action_name(move)
    old = bpy.data.actions.get(name)
    if old is not None:
        if not force:
            sys.exit("action %s already exists (it may hold your keys): pass --force to replace it" % name)
        bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    slot = act.slots.new(id_type="OBJECT", name=arm.name)
    strip = act.layers.new("Keys").strips.new(type="KEYFRAME")
    bag = strip.channelbag(slot, ensure=True)
    keys = data["Keys"]
    for field, comp, bone, prop, idx, unit in CHANNELS:
        fc = bag.fcurves.new(data_path(bone, prop), index=idx if idx is not None else 0)
        fc.group = bag.groups.get(bone) or bag.groups.new(bone)
        fc.keyframe_points.add(len(keys))
        for k, key in enumerate(keys):
            v = key["Pose"][field][comp] if comp is not None else key["Pose"][field] * unit
            kp = fc.keyframe_points[k]
            kp.co = (key["T"] * FPS, v)
            kp.handle_left_type = kp.handle_right_type = "AUTO_CLAMPED"
            # the segment leaving this key is eased the way the game eases the arrival at the next key
            ease = keys[k + 1]["Ease"] if k + 1 < len(keys) else "Smooth"
            kp.interpolation, kp.easing = EASE_TO_BLENDER[ease]
        fc.update()
    act["attack_move"] = move
    act["attack_name"] = data.get("Name", move)
    if "Length" in data:
        act["attack_length"] = data["Length"]
    act.frame_range = (keys[0]["T"] * FPS, max(keys[-1]["T"] * FPS, keys[0]["T"] * FPS + 1))
    return act


def assign(arm, act):
    arm.animation_data_create()
    arm.animation_data.action = act
    if arm.animation_data.action_slot is None:
        arm.animation_data.action_slot = act.slots[0]


def read_action(arm, move):
    """The keyed action back to [{T, Ease, Pose}]. A key on any channel makes a pose; the others are read off their curves."""
    act = bpy.data.actions.get(action_name(move))
    if act is None:
        sys.exit("no action " + action_name(move))
    bag = channelbag(act, arm)
    curves = {}
    for ch in CHANNELS:
        field, comp, bone, prop, idx, unit = ch
        fc = bag.fcurves.find(data_path(bone, prop), index=idx if idx is not None else 0)
        if fc is None:
            if field in LATE_FIELDS:
                continue        # keyed before this channel existed: it stays at its default (0)
            sys.exit("%s has no curve for %s.%s" % (act.name, bone, prop))
        curves[ch] = fc
    frames = sorted({round(kp.co.x, 3) for fc in curves.values() for kp in fc.keyframe_points})
    extra = [fc.data_path for fc in bag.fcurves if '"c.' not in fc.data_path]
    if extra:
        print("WARNING: %s has keys on solved bones, which the game ignores: %s" % (act.name, sorted(set(extra))[:4]))
    keys = []
    for k, frame in enumerate(frames):
        P = new_pose()
        vecs = {f: [0.0, 0.0, 0.0] for f in VEC_FIELDS}
        for (field, comp, bone, prop, idx, unit), fc in curves.items():
            v = fc.evaluate(frame)
            if comp is None:
                P[field] = round(v / unit, 3 if field in DEG_FIELDS else 4)
            else:
                vecs[field][comp] = round(v, 4)
        for f in VEC_FIELDS:
            P[f] = tuple(vecs[f])
        ease = "Smooth"
        if k > 0:
            found = set()
            for fc in curves.values():
                for kp in fc.keyframe_points:
                    if abs(kp.co.x - frames[k - 1]) < 1e-3:
                        found.add((kp.interpolation, kp.easing))
            eases = set()
            for pair in found:
                if pair in BLENDER_TO_EASE:
                    eases.add(BLENDER_TO_EASE[pair])
                elif pair[0] == "BEZIER":
                    eases.add("Smooth")     # Blender's default on a new key
                else:
                    sys.exit("%s frame %g: interpolation %s / %s has no game ease (use Sine in-out, Quintic out or Quadratic in)"
                             % (act.name, frames[k - 1], pair[0], pair[1]))
            if len(eases) > 1:
                sys.exit("%s frame %g: channels disagree on the ease (%s); give the whole key one" % (act.name, frames[k - 1], sorted(eases)))
            ease = eases.pop() if eases else "Smooth"
        keys.append({"T": round(frame / FPS, 4), "Ease": ease, "Pose": P})
    return keys


# ------------------------------------------------------------------ C#
def cs_num(v):
    s = ("%.4f" % v).rstrip("0").rstrip(".")
    if s in ("-0", ""):
        s = "0"
    return s + "f"


def cs_pose(P, indent):
    parts = []
    for f in FIELD_ORDER:
        v = P[f]
        if f in VEC_FIELDS:
            if any(abs(x) > 1e-9 for x in v):
                parts.append("%s = new Vector3(%s)" % (f, ", ".join(cs_num(x) for x in v)))
        elif abs(v) > 1e-9:
            parts.append("%s = %s" % (f, cs_num(v)))
    lines, cur = [], ""
    for p in parts:
        if cur and len(cur) + len(p) > 110:
            lines.append(cur)
            cur = ""
        cur += p + ", "
    if cur:
        lines.append(cur)
    pad = " " * (indent + 4)
    return "new AttackPose\n" + " " * indent + "{\n" + "".join(pad + l.rstrip() + "\n" for l in lines) + " " * indent + "}"


def cs_move(move, keys):
    builder = "BuildSword" if move.startswith("sword.") else "BuildHits" if move.startswith("hit.") else "BuildRush"
    var = move.split(".")[-1]
    if move in SINGLES:
        return "// %s\n%s = %s;\n" % (move, var, cs_pose(keys[0]["Pose"], 0))
    body = "".join("    K(%s, KeyEase.%s, %s),\n" % (cs_num(k["T"]), k["Ease"], cs_pose(k["Pose"], 4)) for k in keys)
    return "// %s: paste as this move's Keys in %s (BasicMovesets.cs)\nKeys = new[]\n{\n%s},\n" % (move, builder, body)


def reread_cs(move, text):
    """The exported C# read back with the same reader that reads BuildRush / BuildSword."""
    code = re.sub(r"//[^\n]*", "", text)
    r = Reader(tokenize(code[code.index("=") + 1:]))
    v = r.expr()
    if move in SINGLES:
        return [{"T": 0.0, "Ease": "Smooth", "Pose": v}]
    return v


# ------------------------------------------------------------------ the round trip
def compare(a, b):
    """Worst differences between two key lists: (metres / fractions, degrees, what)."""
    if len(a) != len(b):
        return 9e9, 9e9, "key count %d vs %d" % (len(a), len(b))
    worst_l, worst_d, what = 0.0, 0.0, ""
    for i, (x, y) in enumerate(zip(a, b)):
        if abs(x["T"] - y["T"]) > 1e-4 or x["Ease"] != y["Ease"]:
            return 9e9, 9e9, "key %d time / ease: %s %s vs %s %s" % (i, x["T"], x["Ease"], y["T"], y["Ease"])
        for f in FIELD_ORDER:
            if f in VEC_FIELDS:
                d = max(abs(p - q) for p, q in zip(x["Pose"][f], y["Pose"][f]))
                if d > worst_l:
                    worst_l, what = d, "key %d %s" % (i, f)
            else:
                d = abs(x["Pose"][f] - y["Pose"][f])
                if f in DEG_FIELDS and d > worst_d:
                    worst_d = d
                elif f in FRAC_FIELDS and d > worst_l:
                    worst_l, what = d, "key %d %s" % (i, f)
    return worst_l, worst_d, what


def check(arg, dump):
    arm = get_rig()
    ensure_controls(arm)
    rush = read_all()
    scene = bpy.context.scene
    ok = True
    out = {}
    for move in move_names(arg):
        data = rush[move]
        act = build_action(arm, move, data, force=True)
        assign(arm, act)
        back = read_action(arm, move)
        text = cs_move(move, back)
        again = reread_cs(move, text)
        l1, d1, w1 = compare(data["Keys"], back)
        l2, d2, w2 = compare(data["Keys"], again)
        # every key on the real armature: the animation drives the controls, the controls drive the body
        worst_joint = 0.0
        rig_out = []
        for k, key in zip(data["Keys"], back):
            f = k["T"] * FPS                      # keys between frames (0.025 s = frame 2.5) are posed at their own time
            whole = math.floor(f + 1e-6)
            scene.frame_set(int(whole), subframe=max(0.0, f - whole))
            P = read_controls(arm)
            root, positions = pose_body(arm, P)
            bpy.context.view_layer.update()
            joints = []
            for i, n in enumerate(BONES):
                want = root @ positions[i]
                got = arm.pose.bones[n].head
                worst_joint = max(worst_joint, (want - got).length)
                joints.append(list(want))
            rig_out.append({"T": key["T"], "Ease": key["Ease"], "Pose": key["Pose"], "joints": joints})
        out[move] = rig_out
        good = l1 <= TOL_LINEAR and d1 <= TOL_DEGREES and l2 <= TOL_LINEAR and d2 <= TOL_DEGREES and worst_joint < 1e-3
        ok = ok and good
        print("KEYS %-7s %2d keys | Blender: %.6f m, %.4f deg | through the C#: %.6f m, %.4f deg | body on solver: %.6f m | %s %s"
              % (move, len(back), l1, d1, l2, d2, worst_joint, "OK" if good else "FAILED", w1 if not good else ""))
    if dump:
        with open(dump, "w", encoding="utf-8") as f:
            json.dump(out, f)
    print("KEYS round trip %s (tolerance %g m, %g deg)" % ("OK" if ok else "FAILED", TOL_LINEAR, TOL_DEGREES))
    return ok


# ------------------------------------------------------------------ live solver in the GUI
_last = {}


def _resolve(scene=None, depsgraph=None):
    arm = bpy.data.objects.get("PlayerRig")
    if arm is None or arm.type != "ARMATURE" or "c.Root" not in arm.pose.bones:
        return
    P = read_controls(arm)
    sig = tuple(tuple(v) if isinstance(v, tuple) else v for v in (P[f] for f in FIELD_ORDER))
    if _last.get("sig") == sig:
        return
    _last["sig"] = sig
    pose_body(arm, P)
    arm.update_tag()


def install_live():
    for h in (bpy.app.handlers.frame_change_post, bpy.app.handlers.depsgraph_update_post):
        for f in [f for f in h if getattr(f, "__name__", "") == "_resolve"]:
            h.remove(f)
        h.append(_resolve)
    _last.clear()
    arm = bpy.data.objects.get("PlayerRig")
    if arm is not None:
        ensure_controls(arm)
        _resolve()
    print("KEYS live solver on: moving a c.* control or the frame re-poses the body like the game")


# ------------------------------------------------------------------
def option(args, name, default=None):
    if name in args:
        i = args.index(name)
        v = args[i + 1]
        del args[i:i + 2]
        return v
    return default


def main():
    if "--" not in sys.argv:
        install_live()
        return
    args = sys.argv[sys.argv.index("--") + 1:]
    force = "--force" in args
    if force:
        args.remove("--force")
    blend = os.path.abspath(option(args, "--blend", BLEND))
    out = option(args, "--out")
    dump = option(args, "--dump")
    if len(args) < 2 or args[0] not in ("load", "export", "check"):
        sys.exit("load <move> [--force] [--blend path] | export <move> [--blend path] [--out path] | check <move> [--blend path] [--dump out.json]")
    cmd, arg = args[0], args[1]
    bpy.ops.wm.open_mainfile(filepath=blend)
    scene = bpy.context.scene
    scene.render.fps, scene.render.fps_base = FPS, 1.0
    arm = get_rig()
    if cmd == "check":
        if not check(arg, dump):
            sys.exit(1)
    elif cmd == "load":
        ensure_controls(arm)
        rush = read_all()
        for move in move_names(arg):
            if not force and bpy.data.actions.get(action_name(move)):
                sys.exit("action %s already exists (it may hold your keys): pass --force to replace it" % action_name(move))
        act = None
        for move in move_names(arg):
            act = build_action(arm, move, rush[move], force)
            print("KEYS loaded", act.name, len(rush[move]["Keys"]), "keys")
        assign(arm, bpy.data.actions[action_name(move_names(arg)[0])])
        scene.frame_start, scene.frame_end = 0, int(max(a.frame_range[1] for a in bpy.data.actions if "attack_move" in a))
        scene.frame_set(0)
        pose_body(arm, read_controls(arm))
        bpy.ops.wm.save_as_mainfile(filepath=blend)
        print("KEYS saved ->", blend)
    else:
        parts = []
        for move in move_names(arg):
            keys = read_action(arm, move)
            act = bpy.data.actions[action_name(move)]
            if "attack_length" in act and abs(act["attack_length"] - keys[-1]["T"]) > 1e-4:
                print("WARNING: %s now ends at %.3f s but its move's Length is %.3f s: update Length (and Hit / Streak) in BuildRush"
                      % (move, keys[-1]["T"], act["attack_length"]))
            parts.append(cs_move(move, keys))
        path = os.path.abspath(out) if out else os.path.join(EXPORT_DIR, "%s.cs.txt" % arg)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(parts))
        print("KEYS exported ->", path)


if __name__ == "__main__":
    main()
