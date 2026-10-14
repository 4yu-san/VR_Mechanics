"""Checks that a generated level is well-formed and completable.

Run on files:  python validate.py path/to/level.json [more.json]
"""
import json
import math
import sys
from pathlib import Path

from layout import half_extents

EPS = 0.01


def load_profile(path=None):
    path = Path(path) if path else Path(__file__).with_name("movement_profile.json")
    return json.loads(path.read_text())


# ---------- geometry helpers ----------

def aabb(piece):
    w, h, d = piece["size"]
    hx, hy, hz = half_extents(w, h, d, piece["yaw"])
    x, y, z = piece["position"]
    return (x - hx, y - hy, z - hz), (x + hx, y + hy, z + hz)


def top_y(piece):
    return aabb(piece)[1][1]


def top_center(piece):
    lo, hi = aabb(piece)
    return ((lo[0] + hi[0]) / 2, hi[1], (lo[2] + hi[2]) / 2)


def standable(piece):
    return piece["type"] != "wall"


def boxes_overlap(a, b):
    (alo, ahi), (blo, bhi) = a, b
    return all(min(ahi[k], bhi[k]) - max(alo[k], blo[k]) > EPS for k in range(3))


def horizontal_gap(a, b):
    (alo, ahi), (blo, bhi) = aabb(a), aabb(b)
    gx = max(0.0, max(alo[0] - bhi[0], blo[0] - ahi[0]))
    gz = max(0.0, max(alo[2] - bhi[2], blo[2] - ahi[2]))
    return math.hypot(gx, gz)


def nearest_point(box, p):
    lo, hi = box
    return tuple(min(max(p[k], lo[k]), hi[k]) for k in range(3))


def segment_hits_box(p0, p1, box):
    lo, hi = box
    t0, t1 = 0.0, 1.0
    for k in range(3):
        d = p1[k] - p0[k]
        if abs(d) < 1e-9:
            if p0[k] < lo[k] or p0[k] > hi[k]:
                return False
        else:
            ta, tb = (lo[k] - p0[k]) / d, (hi[k] - p0[k]) / d
            if ta > tb:
                ta, tb = tb, ta
            t0, t1 = max(t0, ta), min(t1, tb)
            if t0 > t1:
                return False
    return t1 - t0 > 1e-6 and t0 < 1.0 - 1e-6


# ---------- movement checks ----------

def jump_ok(a, b, prof):
    rise = top_y(b) - top_y(a)
    gap = horizontal_gap(a, b)
    allowed = prof["jumpGapMax"] + max(0.0, -rise) * prof["dropGapBonusPerMetre"]
    return rise <= prof["jumpUpMax"] and gap <= allowed


def grapple_hop(a, b, pieces, prof):
    """Return (ok, reason). Rope starts at a's top (plus eye height if a is standable)."""
    ox, oy, oz = top_center(a)
    if standable(a):
        oy += prof["eyeHeight"]
    origin = (ox, oy, oz)
    target = nearest_point(aabb(b), origin)
    dist = math.dist(origin, target)
    if dist > prof["grappleRange"]:
        return False, f"{dist:.1f} m to {b['id']} is beyond grapple range {prof['grappleRange']:.0f} m"
    if top_y(b) - top_y(a) > prof["grappleRiseMax"]:
        return False, f"{b['id']} is {top_y(b) - top_y(a):.1f} m higher, limit {prof['grappleRiseMax']:.0f} m"
    for other in pieces:
        if other["id"] in (a["id"], b["id"]):
            continue
        if segment_hits_box(origin, target, aabb(other)):
            return False, f"line of sight from {a['id']} to {b['id']} is blocked by {other['id']}"
    return True, ""


# ---------- main validation ----------

def validate(level, prof, min_grapple=0):
    errors = []
    pieces = level.get("pieces", [])
    n = len(pieces)

    if n < 3:
        return ["level needs at least 3 pieces"], {}
    if n > prof["maxPieces"]:
        errors.append(f"{n} pieces, maximum is {prof['maxPieces']}")

    if pieces[0]["type"] != "platform":
        errors.append("first step must be a platform (the start)")
    if pieces[-1]["type"] != "goal":
        errors.append("last step must be type goal")
    if sum(1 for p in pieces if p["type"] == "goal") != 1:
        errors.append("exactly one goal is allowed, and it must be last")

    for p in pieces:
        shape = prof["shapes"].get(p["type"])
        w, h, d = p["size"]
        for label, val in (("width", w), ("height", h), ("depth", d)):
            lo, hi = shape[label]
            if not lo <= val <= hi:
                errors.append(f"{p['id']} ({p['type']}) {label} {val} outside {lo} to {hi}")

    bounds = prof["bounds"]
    for p in pieces:
        lo, hi = aabb(p)
        for k, axis in enumerate("xyz"):
            if lo[k] < bounds[axis][0] or hi[k] > bounds[axis][1]:
                errors.append(f"{p['id']} leaves the {axis} bounds {bounds[axis]}")
                break

    for i in range(n):
        for j in range(i + 1, n):
            if boxes_overlap(aabb(pieces[i]), aabb(pieces[j])):
                errors.append(f"{pieces[i]['id']} overlaps {pieces[j]['id']}")

    for i, p in enumerate(pieces):
        if not standable(p):
            continue
        lo, hi = aabb(p)
        column = ((lo[0], hi[1], lo[2]), (hi[0], hi[1] + prof["headroom"], hi[2]))
        for j, other in enumerate(pieces):
            if j != i and boxes_overlap(column, aabb(other)):
                errors.append(f"{other['id']} blocks the headroom above {p['id']}")

    stats = {"pieces": n, "jumps": 0, "grappleRequired": 0, "walkLength": 0.0}
    if errors:
        return errors, stats

    cur = 0
    anchors = []
    for i in range(1, n):
        p = pieces[i]
        if not standable(p):
            anchors.append(i)
            continue
        a = pieces[cur]
        label = f"{a['id']} to {p['id']}"
        if not anchors:
            if jump_ok(a, p, prof):
                stats["jumps"] += 1
            else:
                ok, why = grapple_hop(a, p, pieces, prof)
                if ok:
                    stats["grappleRequired"] += 1
                else:
                    errors.append(
                        f"{label}: cannot jump (gap {horizontal_gap(a, p):.1f} m, rise {top_y(p) - top_y(a):.1f} m, "
                        f"limits {prof['jumpGapMax']} m and {prof['jumpUpMax']} m) and cannot grapple: {why}")
        else:
            chain = [a] + [pieces[k] for k in anchors] + [p]
            failed = None
            for s, t in zip(chain, chain[1:]):
                ok, why = grapple_hop(s, t, pieces, prof)
                if not ok:
                    failed = f"hop {s['id']} to {t['id']}: {why}"
                    break
            if failed:
                errors.append(f"{label} via anchors: {failed}")
            else:
                stats["grappleRequired"] += 1
        stats["walkLength"] += math.dist(top_center(a), top_center(p))
        cur, anchors = i, []

    if not errors and stats["grappleRequired"] < min_grapple:
        errors.append(
            f"too easy: only {stats['grappleRequired']} transitions need the grapple, at least {min_grapple} wanted. "
            f"Use larger gaps (over {prof['jumpGapMax']} m), bigger rises (over {prof['jumpUpMax']} m), or wall/pillar anchors.")

    stats["walkLength"] = round(stats["walkLength"], 1)
    return errors, stats


def main(argv):
    if not argv:
        print(__doc__)
        return 2
    prof = load_profile()
    bad = 0
    for arg in argv:
        level = json.loads(Path(arg).read_text())
        errors, stats = validate(level, prof)
        if errors:
            bad += 1
            print(f"FAIL {arg}")
            for e in errors:
                print("   -", e)
        else:
            print(f"ok   {arg}  {stats}")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
