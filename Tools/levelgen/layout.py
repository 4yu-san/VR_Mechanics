"""Turns a LevelPlan (relative steps) into the absolute level JSON that Unity loads."""

# yaw 0 faces +Z, yaw 90 faces +X (Unity convention, clockwise seen from above)
HEADINGS = {0: (0, 1), 90: (1, 0), 180: (0, -1), 270: (-1, 0)}


def half_extents(width, height, depth, yaw):
    if yaw % 180 == 0:
        return (width / 2, height / 2, depth / 2)
    return (depth / 2, height / 2, width / 2)


def expand(plan, profile, seed=0, meta=None):
    pieces = []
    yaw = 0
    top = 0.0
    cx = cz = 0.0
    prev_half = None

    for i, step in enumerate(plan.steps):
        if i == 0:
            hx, hy, hz = half_extents(step.width, step.height, step.depth, yaw)
            cx, cz, top = 0.0, 0.0, 0.0
        else:
            yaw = (yaw + step.turn) % 360
            dx, dz = HEADINGS[yaw]
            hx, hy, hz = half_extents(step.width, step.height, step.depth, yaw)
            prev_along = prev_half[0] if dx != 0 else prev_half[2]
            cur_along = hx if dx != 0 else hz
            advance = prev_along + step.gap + cur_along
            rx, rz = dz, -dx  # right-hand vector
            cx += dx * advance + rx * step.shift
            cz += dz * advance + rz * step.shift
            top += step.rise

        cy = top - step.height / 2
        pieces.append({
            "id": f"p{i}",
            "type": step.type,
            "position": [round(cx, 3), round(cy, 3), round(cz, 3)],
            "size": [step.width, step.height, step.depth],
            "yaw": yaw,
        })
        prev_half = (hx, hy, hz)

    first = pieces[0]
    start_top = first["position"][1] + first["size"][1] / 2
    lowest = min(p["position"][1] - p["size"][1] / 2 for p in pieces)

    level = {
        "version": 1,
        "name": plan.name,
        "theme": plan.theme,
        "seed": seed,
        "start": {
            "position": [first["position"][0], round(start_top + 1.5, 3), first["position"][2]],
            "yaw": 0,
        },
        "killFloorY": round(lowest - profile["killFloorMargin"], 3),
        "pieces": pieces,
    }
    if meta:
        level["generator"] = meta
    return level
