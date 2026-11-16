#!/usr/bin/env python3
"""Convert a Blockbench head (ASCII FBX export) into a head preset for the procedural fighters.

    python3 tools/blockbench_head.py Head1.fbx Assets/_Project/Resources/Heads/Head1.json --chin 7.3 --cut 5

Writes JSON read by `HeadPresetLibrary` (Scripts/Runtime/Characters/HeadPreset.cs):
  - positions/normals in head-bone space for headSize = 1, Unity axes (+Y up, +Z forward, left-handed),
  - one submesh per (part kind, palette colour); the `skin` part uses the recipe's skin tone in game.

The model is scaled so chin -> crown matches the procedural head (0.287 head units) and the chin lands where the
procedural chin is. Expects the head centred on X (face symmetric around x = 0) and facing +Z, as Blockbench shows it.
Only the standard library is needed (the embedded palette PNG is decoded here).
"""
import argparse, base64, json, math, re, struct, zlib
from collections import defaultdict

# Procedural head (ProceduralCharacterBuilder.Head, units of head size, relative to the Head bone).
PROC_CHIN_Y = -0.027
PROC_CROWN_Y = 0.26
PROC_MID_Z = -0.01


def decode_png(data):
    """Minimal PNG decoder: 8-bit greyscale/RGB/RGBA/palette, non-interlaced. Returns (w, h, rows of RGB tuples)."""
    assert data[:8] == b"\x89PNG\r\n\x1a\n", "not a PNG"
    pos, idat, plte = 8, b"", None
    while pos < len(data):
        n, typ = struct.unpack(">I4s", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + n]
        if typ == b"IHDR":
            w, h, depth, ctype, _, _, interlace = struct.unpack(">IIBBBBB", body)
        elif typ == b"PLTE":
            plte = [tuple(body[i:i + 3]) for i in range(0, len(body), 3)]
        elif typ == b"IDAT":
            idat += body
        pos += 12 + n
    assert depth == 8 and interlace == 0, "unsupported PNG (need 8-bit, non-interlaced)"
    bpp = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[ctype]
    raw, stride, prev, rows, o = zlib.decompress(idat), w * bpp, None, [], 0
    for _ in range(h):
        f, line = raw[o], bytearray(raw[o + 1:o + 1 + stride])
        o += 1 + stride
        p = prev or bytearray(stride)
        for i in range(stride):
            a = line[i - bpp] if i >= bpp else 0
            b, c = p[i], (p[i - bpp] if i >= bpp else 0)
            if f == 1: line[i] = (line[i] + a) & 255
            elif f == 2: line[i] = (line[i] + b) & 255
            elif f == 3: line[i] = (line[i] + (a + b) // 2) & 255
            elif f == 4:
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                line[i] = (line[i] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        prev = line
        px = [tuple(line[i:i + bpp]) for i in range(0, stride, bpp)]
        if ctype == 3: px = [plte[q[0]] for q in px]
        elif ctype in (0, 4): px = [(q[0],) * 3 for q in px]
        rows.append([q[:3] for q in px])
    return w, h, rows


def parse_fbx(text):
    parts = {}
    for g in re.finditer(r'Geometry: \d+, "Geometry::(\w+)".*?Vertices: \*\d+ \{\s*a: ([^}]*)\}'
                         r'.*?PolygonVertexIndex: \*\d+ \{\s*a: ([^}]*)\}.*?UV: \*\d+ \{\s*a: ([^}]*)\}', text, re.S):
        v = list(map(float, g.group(2).split(',')))
        idx = list(map(int, g.group(3).split(',')))
        uv = list(map(float, g.group(4).split(',')))
        polys, cur, k = [], [], 0
        for i in idx:
            end = i < 0
            cur.append((~i if end else i, uv[2 * k], uv[2 * k + 1]))
            k += 1
            if end:
                polys.append(cur)
                cur = []
        parts[g.group(1)] = ([v[i:i + 3] for i in range(0, len(v), 3)], polys)
    m = re.search(r'Content: "([^"]+)"', text)
    return parts, (decode_png(base64.b64decode(m.group(1))) if m else None)


def sub(a, b): return [a[0] - b[0], a[1] - b[1], a[2] - b[2]]
def cross(a, b): return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def wkey(p): return tuple(round(c, 5) for c in p)


def orient(polys, verts, centre):
    """Make face winding consistent across shared edges (Blockbench renders both sides, so a flipped face is
    invisible there but a hole in game), then turn each connected piece so it faces away from the head centre."""
    keys = [[wkey(verts[q[0]]) for q in p] for p in polys]
    edges = defaultdict(list)
    for f, k in enumerate(keys):
        for i in range(len(k)):
            edges[frozenset((k[i], k[i - 1]))].append(f)
    flip = [None] * len(polys)

    def directed(f, a, b):  # does face f (with its current flip) walk the edge a -> b?
        k = keys[f][::-1] if flip[f] else keys[f]
        return any(k[i - 1] == a and k[i] == b for i in range(len(k)))

    pieces = []
    for start in range(len(polys)):
        if flip[start] is not None: continue
        flip[start] = False
        piece, stack = [start], [start]
        while stack:
            f = stack.pop()
            k = keys[f][::-1] if flip[f] else keys[f]
            for i in range(len(k)):
                a, b = k[i - 1], k[i]
                for g in edges[frozenset((a, b))]:
                    if g == f or flip[g] is not None: continue
                    flip[g] = False
                    if directed(g, a, b): flip[g] = True  # neighbour must walk the shared edge b -> a
                    piece.append(g); stack.append(g)
        pieces.append(piece)
    flips = 0
    for piece in pieces:
        out = 0.0
        for f in piece:
            ps = [verts[q[0]] for q in (polys[f][::-1] if flip[f] else polys[f])]
            n = [0.0, 0.0, 0.0]
            for i in range(1, len(ps) - 1):
                c = cross(sub(ps[i], ps[0]), sub(ps[i + 1], ps[0]))
                n = [n[j] + c[j] for j in range(3)]
            cen = [sum(p[j] for p in ps) / len(ps) - centre[j] for j in range(3)]
            out += sum(n[j] * cen[j] for j in range(3))
        if out < 0:
            for f in piece: flip[f] = not flip[f]
    result = []
    for f, p in enumerate(polys):
        result.append(p[::-1] if flip[f] else p)
        flips += bool(flip[f])
    return result, flips


def fill_small_holes(tris, positions, max_edges=6):
    """Close tiny gaps (missing faces) left in the source mesh; big openings (eye sockets, mouth, neck cut) stay."""
    key_of = [wkey(p) for p in positions]
    rep = {}
    for i, k in enumerate(key_of): rep.setdefault(k, i)
    count, colour_at = defaultdict(int), {}
    for a, b, c, col in tris:
        for u, v in ((a, b), (b, c), (c, a)):
            count[(key_of[u], key_of[v])] += 1
            colour_at[key_of[u]] = col
    nxt = {}
    for (u, v) in count:
        if (v, u) not in count: nxt[v] = u  # boundary edge u -> v: the hole walks it v -> u
    added, seen = [], set()
    for start in list(nxt):
        if start in seen: continue
        loop, cur = [], start
        while cur in nxt and cur not in seen and len(loop) <= max_edges:
            seen.add(cur); loop.append(cur); cur = nxt[cur]
        if cur == start and 3 <= len(loop) <= max_edges:
            ids = [rep[k] for k in loop]
            added += [(ids[0], ids[i], ids[i + 1], colour_at[loop[0]]) for i in range(1, len(ids) - 1)]
    return added


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("fbx"); ap.add_argument("out")
    ap.add_argument("--chin", type=float, required=True, help="model Y of the bottom of the chin")
    ap.add_argument("--cut", type=float, default=None, help="drop skin faces below this model Y (e.g. a bust base)")
    ap.add_argument("--skin-part", default="skin", help="part tinted with the recipe skin tone")
    ap.add_argument("--drop", nargs="*", default=[], help="parts to leave out")
    args = ap.parse_args()

    parts, png = parse_fbx(open(args.fbx, encoding="utf-8").read())
    for d in args.drop: parts.pop(d, None)
    assert args.skin_part in parts, "no part named %r (found %s)" % (args.skin_part, ", ".join(parts))
    skin_v = parts[args.skin_part][0]
    crown = max(p[1] for p in skin_v)
    above = [p for p in skin_v if args.cut is None or p[1] >= args.cut]
    mid_z = (min(p[2] for p in above) + max(p[2] for p in above)) / 2
    k = (PROC_CROWN_Y - PROC_CHIN_Y) / (crown - args.chin)

    def to_unity(p):  # right-handed FBX -> Unity: mirror X (winding is reversed below)
        return [-p[0] * k, (p[1] - args.chin) * k + PROC_CHIN_Y, (p[2] - mid_z) * k + PROC_MID_Z]

    def colour(poly):
        if png is None: return "FFFFFF"
        w, h, rows = png
        u = sum(q[1] for q in poly) / len(poly); v = sum(q[2] for q in poly) / len(poly)
        return "%02X%02X%02X" % rows[min(h - 1, max(0, int((1 - v) * h)))][min(w - 1, max(0, int(u * w)))]

    head_centre = [(min(p[j] for p in above) + max(p[j] for p in above)) / 2 for j in range(3)]
    positions, normals, subs = [], [], defaultdict(list)
    total = flipped = 0
    for name, (verts, polys) in parts.items():
        is_skin = name == args.skin_part
        keep = [p for p in polys if not (is_skin and args.cut is not None and min(verts[q[0]][1] for q in p) < args.cut)]
        keep, flips = orient(keep, verts, head_centre)
        flipped += flips
        remap = {}
        for p in keep:
            for q in p:
                if q[0] not in remap:
                    remap[q[0]] = len(positions)
                    positions.append(to_unity(verts[q[0]]))
                    normals.append([0.0, 0.0, 0.0])
        # smooth normals: area-weighted, welded by position inside the part
        weld = defaultdict(list)
        for old, new in remap.items(): weld[wkey(verts[old])].append(new)
        part_tris = []
        for p in keep:
            ids = [remap[q[0]] for q in p]
            key = (is_skin, colour(p))
            part_tris += [(ids[0], ids[i + 1], ids[i], key) for i in range(1, len(ids) - 1)]  # fan, reversed winding
        part_tris += fill_small_holes(part_tris, positions)
        for a, b, c, key in part_tris:
            subs[key] += [a, b, c]
            # Unity's front face is the side of the numeric cross(b - a, c - a); its length weights by area
            n = cross(sub(positions[b], positions[a]), sub(positions[c], positions[a]))
            total += 1
            for vid in (a, b, c):
                nn = normals[vid]; nn[0] += n[0]; nn[1] += n[1]; nn[2] += n[2]
        for group in weld.values():
            s = [sum(normals[i][j] for i in group) for j in range(3)]
            for i in group: normals[i] = list(s)
    for p, n in zip(positions, normals):
        if n[0] ** 2 + n[1] ** 2 + n[2] ** 2 < 1e-20:  # only degenerate faces around it: point away from the head centre
            n[:] = [p[0], p[1] - 0.12, p[2] - PROC_MID_Z]
        l = math.sqrt(n[0] ** 2 + n[1] ** 2 + n[2] ** 2) or 1.0
        n[0] /= l; n[1] /= l; n[2] /= l

    # sanity: skin normals should mostly point away from the head centre (catches mirrored winding)
    centre = [0.0, 0.12, PROC_MID_Z]
    out_ok = sum(1 for p, n in zip(positions, normals) if sum((p[j] - centre[j]) * n[j] for j in range(3)) > 0)
    print("vertices %d, triangles %d, faces re-oriented %d, normals pointing outward %.1f%%"
          % (len(positions), total, flipped, 100.0 * out_ok / len(positions)))

    neck_bottom = min(p[1] for p in positions)
    data = {
        "name": args.out.replace("\\", "/").split("/")[-1].rsplit(".", 1)[0],
        "neckBottom": round(neck_bottom, 5),
        "neckTop": PROC_CHIN_Y,
        "positions": [round(c, 5) for p in positions for c in p],
        "normals": [round(c, 4) for n in normals for c in n],
        "submeshes": [{"color": col, "skin": skin, "triangles": tris} for (skin, col), tris in sorted(subs.items(), key=lambda kv: not kv[0][0])],
    }
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, separators=(",", ":"))
    for s in data["submeshes"]: print("  submesh %s%s: %d triangles" % (s["color"], " (skin)" if s["skin"] else "", len(s["triangles"]) // 3))
    print("scale %.5f m/unit at headSize 1, neck %.3f..%.3f" % (k, neck_bottom, PROC_CHIN_Y))


if __name__ == "__main__":
    main()
