using System;
using System.Collections.Generic;
using UnityEngine;

// Multi-goal levels: several Level Ends, all the same distance from the Start Platform, reached through a
// dense field of Cube (7) platforms.
//
// The LLM never writes coordinates. For each goal it describes a CORRIDOR: how the path bends (waypoints
// sideways / up / down), how wide the corridor is and how densely to pack it. This file fills each corridor
// with cubes:
//   1. a "spine" of cubes along the corridor's centre line, so every goal is reachable by construction,
//   2. random extra cubes scattered across the corridor's width (dart throwing with a minimum spacing),
//      so the level is a field with many routes, not a single file line.
// Corridors overlap near the start, so they merge into one shared field and split toward their goals.
//
// Guarantees: every Level End is exactly settings.goalDistance from the Start Platform's centre, sits
// right under a landing cube, and the landing cube is reachable from the Start Platform by hops of at
// most settings.maxStep (15 m). Cubes that cannot be reached that way are removed.
namespace MultiGoalLevels
{
    // ------------------------------------------------------------------ what the LLM writes

    [Serializable]
    public class Waypoint
    {
        public float t;     // 0..1 along the path, from the start (0) to the goal (1)
        public float side;  // metres sideways from the straight line, positive = right (looking toward the goal)
        public float up;    // metres above the straight line
    }

    [Serializable]
    public class PathPlan
    {
        public float spacing;        // metres between neighbouring cubes (smaller = denser)
        public float width;          // corridor width in metres
        public Waypoint[] waypoints;
    }

    [Serializable]
    public class MultiGoalPlan
    {
        public string name;
        public PathPlan[] paths;     // one per goal, ordered from the leftmost goal to the rightmost

        // Tolerates text around the JSON (fences, a sentence before it), like LevelData.FromJson.
        public static MultiGoalPlan FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int open = json.IndexOf('{'), close = json.LastIndexOf('}');
            if (open < 0 || close <= open) return null;
            try { return JsonUtility.FromJson<MultiGoalPlan>(json.Substring(open, close - open + 1)); }
            catch (ArgumentException) { return null; }
        }

        public string ToJson() => JsonUtility.ToJson(this, true);
    }

    // ------------------------------------------------------------------ settings and scene facts

    public class LayoutSettings
    {
        public int goalCount = 3;
        public float goalDistance = 45f;       // straight-line distance from the Start Platform centre to every Level End
        public float goalHeight = 0f;          // Level End height above the Start Platform's top surface (same for all goals)
        public float arcDegrees = 120f;        // how widely the goals are spread around the main direction
        public Vector3 mainDirection = Vector3.left; // New Grapple runs toward -x
        public float maxStep = 15f;            // longest allowed hop between two cubes (or from the Start Platform)
        public float minSpacing = 2.5f;        // the LLM's "spacing" is clamped to [minSpacing, maxSpacing]
        public float maxSpacing = 4.5f;
        public float minWidth = 6f;            // the LLM's "width" is clamped to [minWidth, maxWidth]
        public float maxWidth = 18f;
        public float density = 1.5f;           // how full each corridor gets (1 = loose, 2 = as packed as spacing allows)
        public float maxSide = 15f;
        public float heightVariation = 2.5f;   // the LLM's "up" is clamped to +-heightVariation
        public float heightJitter = 0.75f;     // random per-cube height offset
        public float maxDrop = 5f;             // cubes never dip more than this below the Start Platform's top
        public float minGap = 0.8f;            // min surface-to-surface gap between any two cubes
        public float endGap = 0.3f;            // gap between the Level End's top and the landing cube above it
        public int maxCubes = 500;

        // The death floor is moved so that it sits this far below the lowest element of the level:
        // twice (grapple range + Physics Rig height), i.e. 2 x (25 + 0.86) = 51.72 m by default.
        public float grappleRange = 25f;       // Grappling.cs maxDistance
        public float rigHeight = 0.86f;        // Physics Rig body height
        public float floorClearanceMultiplier = 2f;
        public float FloorClearance => floorClearanceMultiplier * (grappleRange + rigHeight);
    }

    // Read from the template scene and prefabs at bake time, so nothing about New Grapple is hardcoded here
    public class SceneInfo
    {
        public Bounds startPlatform;
        public float floorTop;
        public Vector3 cubeSize;               // world size of one Cube (7)
        public float endTop = 2.8f;            // Level End trigger: top / bottom relative to its pivot, and radius
        public float endBottom = -2.3f;
        public float endRadius = 1.2f;
    }

    public class Cube
    {
        public Vector3 position;
        public float yaw;                      // degrees; 0 = the prefab's own orientation
        public int path;
        public bool landing;                   // the cube right above a Level End
    }

    public class Layout
    {
        public List<Vector3> goals = new List<Vector3>();     // Level End positions
        public List<Cube> cubes = new List<Cube>();
        public List<string> unreachable = new List<string>(); // filled by Reachability()
        public float lowestPoint;                             // bottom of the lowest element (Start Platform, cubes, Level Ends)
        public float deathFloorTop;                           // where the death floor's top surface must be moved to
    }

    public static class MultiGoalLayout
    {
        // ------------------------------------------------------------------ goals

        // Same horizontal radius and same height for every goal, so they are exactly equidistant in 3D.
        public static List<Vector3> GoalPositions(LayoutSettings s, SceneInfo scene, List<string> errors)
        {
            var goals = new List<Vector3>();
            Vector3 centre = scene.startPlatform.center;
            float vertical = scene.startPlatform.max.y + s.goalHeight - centre.y;
            if (s.goalDistance <= Mathf.Abs(vertical) + 1f)
            {
                errors.Add($"Goal distance {s.goalDistance} m is too small for a goal height of {s.goalHeight} m.");
                return goals;
            }
            float horizontal = Mathf.Sqrt(s.goalDistance * s.goalDistance - vertical * vertical);
            float startReach = Mathf.Max(scene.startPlatform.extents.x, scene.startPlatform.extents.z);
            if (horizontal < startReach + 2f * s.minSpacing)
                errors.Add($"Goal distance {s.goalDistance} m leaves no room for platforms beyond the Start Platform.");

            Vector3 main = new Vector3(s.mainDirection.x, 0f, s.mainDirection.z).normalized;
            for (int i = 0; i < s.goalCount; i++)
            {
                float angle = s.goalCount == 1 ? 0f : -s.arcDegrees * 0.5f + s.arcDegrees * i / (s.goalCount - 1);
                // Unity's yaw is clockwise seen from above, so increasing angle sweeps left -> right
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * main;
                goals.Add(centre + dir * horizontal + Vector3.up * vertical);
            }
            return goals;
        }

        // ------------------------------------------------------------------ plan checks

        // Sanitises the plan in place (clamps numbers into range) and returns problems the model must fix
        // itself - the ones that cannot be silently corrected.
        public static List<string> CheckPlan(MultiGoalPlan plan, LayoutSettings s, List<string> warnings)
        {
            var errors = new List<string>();
            if (plan == null) { errors.Add("The reply could not be read as JSON."); return errors; }
            int count = plan.paths == null ? 0 : plan.paths.Length;
            if (count != s.goalCount)
            {
                errors.Add($"\"paths\" must contain exactly {s.goalCount} paths (one per goal), but it has {count}.");
                return errors;
            }
            // The spine guarantee (see Build) needs 1.85 x spacing <= maxStep
            float maxSpacing = Mathf.Min(s.maxSpacing, s.maxStep / 1.85f);
            for (int i = 0; i < count; i++)
            {
                var p = plan.paths[i];
                if (p == null) { errors.Add($"Path {i} is empty."); continue; }

                float spacing = Mathf.Clamp(p.spacing, s.minSpacing, maxSpacing);
                if (!Mathf.Approximately(spacing, p.spacing)) warnings.Add($"Path {i}: spacing {p.spacing} clamped to {spacing}.");
                p.spacing = spacing;
                float width = Mathf.Clamp(p.width, s.minWidth, s.maxWidth);
                if (!Mathf.Approximately(width, p.width)) warnings.Add($"Path {i}: width {p.width} clamped to {width}.");
                p.width = width;

                var points = new List<Waypoint>(p.waypoints ?? new Waypoint[0]);
                points.RemoveAll(w => w == null);
                if (points.Count > 4) { warnings.Add($"Path {i}: only the first 4 waypoints are used."); points.RemoveRange(4, points.Count - 4); }
                points.Sort((a, b) => a.t.CompareTo(b.t));
                foreach (var w in points)
                {
                    w.t = Mathf.Clamp(w.t, 0.1f, 0.9f);
                    w.side = Mathf.Clamp(w.side, -s.maxSide, s.maxSide);
                    w.up = Mathf.Clamp(w.up, -s.heightVariation, s.heightVariation);
                }
                // Waypoints closer than 0.05 in t make the curve kink sharply - drop the later one
                for (int k = points.Count - 1; k > 0; k--)
                    if (points[k].t - points[k - 1].t < 0.05f) points.RemoveAt(k);
                p.waypoints = points.ToArray();
            }
            return errors;
        }

        // ------------------------------------------------------------------ plan -> cubes

        public static Layout Build(MultiGoalPlan plan, LayoutSettings s, SceneInfo scene, System.Random rng, List<string> errors)
        {
            var layout = new Layout { goals = GoalPositions(s, scene, errors) };
            if (errors.Count > 0) return layout;
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            float halfY = scene.cubeSize.y * 0.5f;
            float minY = scene.startPlatform.max.y - s.maxDrop + halfY;
            var startZone = scene.startPlatform;
            startZone.Expand(new Vector3(1f, 5f, 1f)); // includes headroom above the platform

            // Landing cubes first: fixed, right above each Level End
            var landings = new List<Vector3>();
            for (int i = 0; i < layout.goals.Count; i++)
            {
                Vector3 landing = layout.goals[i] + Vector3.up * (scene.endTop + s.endGap + halfY);
                landings.Add(landing);
                layout.cubes.Add(new Cube { position = landing, path = i, landing = true });
            }

            bool TryAdd(Vector3 p, int path, float spacing)
            {
                if (layout.cubes.Count >= s.maxCubes) return false;
                p.y = Mathf.Max(p.y, minY);
                var candidate = new Cube { position = p, path = path };
                var box = BoundsOf(candidate, scene.cubeSize);
                if (startZone.Intersects(box)) return false;
                foreach (var g in layout.goals)
                    if (HitsEndZone(box, g, scene)) return false;
                float minCentre = spacing * 0.85f;
                foreach (var c in layout.cubes)
                    if (Vector3.Distance(c.position, p) < minCentre || Gap(c, candidate, scene.cubeSize) < s.minGap)
                        return false;
                layout.cubes.Add(candidate);
                return true;
            }

            for (int i = 0; i < layout.goals.Count; i++)
            {
                var pathPlan = plan.paths[i];
                Vector3 origin = StartEdge(scene.startPlatform, layout.goals[i]);
                Vector3 landing = landings[i];

                // Path frame: forward along the straight line, right = horizontal perpendicular
                Vector3 forward = landing - origin;
                Vector3 flat = new Vector3(forward.x, 0f, forward.z).normalized;
                Vector3 right = new Vector3(flat.z, 0f, -flat.x);

                var control = new List<Vector3> { origin };
                foreach (var w in pathPlan.waypoints)
                    control.Add(origin + forward * w.t + right * w.side + Vector3.up * w.up);
                control.Add(landing);

                var curve = SampleCatmullRom(control, 40);
                float length = 0f;
                for (int k = 1; k < curve.Count; k++) length += Vector3.Distance(curve[k - 1], curve[k]);

                // 1. Spine: centre-line cubes at most "spacing" apart. One rejected for being too close to an
                //    existing cube is fine - that cube is within 0.85 x spacing of it, so the chain still never
                //    has a hop longer than 1.85 x spacing <= maxStep (CheckPlan clamps spacing for that).
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / pathPlan.spacing));
                for (int k = 1; k < steps; k++)
                {
                    Vector3 p = PointAt(curve, k * length / steps);
                    p.y += R(-s.heightJitter, s.heightJitter) * 0.5f;
                    TryAdd(p, i, pathPlan.spacing);
                }

                // 2. Fill: random cubes across the corridor, narrowing toward both ends so the corridor still
                //    leaves the start platform and arrives at the landing cube cleanly
                int target = Mathf.CeilToInt(length * pathPlan.width / (pathPlan.spacing * pathPlan.spacing) * 0.8f * s.density);
                int added = 0;
                for (int attempt = 0; attempt < target * 30 && added < target; attempt++)
                {
                    float along = R(0f, length);
                    float taper = Mathf.Clamp01(Mathf.Min(along, length - along) / Mathf.Max(1f, pathPlan.width));
                    float lateral = R(-0.5f, 0.5f) * pathPlan.width * taper;
                    Vector3 p = PointAt(curve, along) + right * lateral + Vector3.up * R(-s.heightJitter, s.heightJitter);
                    if (TryAdd(p, i, pathPlan.spacing)) added++;
                }
            }

            Reachability(layout, s, scene, prune: true);

            // The death floor goes FloorClearance below the lowest thing in the level
            float lowest = scene.startPlatform.min.y;
            foreach (var c in layout.cubes) lowest = Mathf.Min(lowest, c.position.y - halfY);
            foreach (var g in layout.goals) lowest = Mathf.Min(lowest, g.y + scene.endBottom);
            layout.lowestPoint = lowest;
            layout.deathFloorTop = lowest - s.FloorClearance;
            return layout;
        }

        // Where a path leaves the Start Platform: its top surface, at the edge facing the goal
        static Vector3 StartEdge(Bounds start, Vector3 goal)
        {
            Vector3 d = goal - start.center;
            d.y = 0f;
            d.Normalize();
            float tx = Mathf.Abs(d.x) > 1e-4f ? start.extents.x / Mathf.Abs(d.x) : float.MaxValue;
            float tz = Mathf.Abs(d.z) > 1e-4f ? start.extents.z / Mathf.Abs(d.z) : float.MaxValue;
            float t = Mathf.Min(tx, tz);
            return new Vector3(start.center.x + d.x * t, start.max.y, start.center.z + d.z * t);
        }

        // Breadth-first search from the Start Platform over hops of at most maxStep (centre to centre).
        // prune = true removes every cube that cannot be reached. Unreachable landing cubes are recorded
        // in layout.unreachable either way.
        public static void Reachability(Layout layout, LayoutSettings s, SceneInfo scene, bool prune)
        {
            int n = layout.cubes.Count;
            var reached = new bool[n];
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
                if (Vector3.Distance(scene.startPlatform.ClosestPoint(layout.cubes[i].position), layout.cubes[i].position) <= s.maxStep)
                {
                    reached[i] = true;
                    queue.Enqueue(i);
                }
            while (queue.Count > 0)
            {
                int a = queue.Dequeue();
                for (int b = 0; b < n; b++)
                    if (!reached[b] && Vector3.Distance(layout.cubes[a].position, layout.cubes[b].position) <= s.maxStep)
                    {
                        reached[b] = true;
                        queue.Enqueue(b);
                    }
            }

            layout.unreachable.Clear();
            var kept = new List<Cube>();
            for (int i = 0; i < n; i++)
            {
                var c = layout.cubes[i];
                if (!reached[i] && c.landing)
                    layout.unreachable.Add($"Path {c.path}: its goal cannot be reached with hops of at most {s.maxStep} m. Use a smaller spacing or gentler waypoints on that path.");
                if (reached[i] || !prune || c.landing) kept.Add(c);
            }
            layout.cubes = kept;
        }

        // ------------------------------------------------------------------ validation

        public static List<string> Validate(Layout layout, LayoutSettings s, SceneInfo scene)
        {
            var errors = new List<string>();
            if (layout.cubes.Count >= s.maxCubes)
                errors.Add($"The level hit the limit of {s.maxCubes} platforms. Use a larger spacing or a narrower width.");

            foreach (var g in layout.goals)
                if (Mathf.Abs(Vector3.Distance(g, scene.startPlatform.center) - s.goalDistance) > 0.05f)
                    errors.Add("Goals are not equidistant from the Start Platform (internal error).");

            Reachability(layout, s, scene, prune: false);
            errors.AddRange(layout.unreachable);

            for (int i = 0; i < layout.cubes.Count; i++)
            {
                var c = layout.cubes[i];
                if (c.position.y - scene.cubeSize.y * 0.5f < layout.deathFloorTop + s.FloorClearance - 0.01f)
                    errors.Add("A platform is closer to the death floor than the clearance allows (internal error).");
                for (int j = i + 1; j < layout.cubes.Count; j++)
                    if (Gap(c, layout.cubes[j], scene.cubeSize) < s.minGap - 0.01f)
                        errors.Add("Two platforms overlap (internal error).");
            }

            var unique = new List<string>();
            foreach (var e in errors) if (!unique.Contains(e)) unique.Add(e);
            return unique;
        }

        // ------------------------------------------------------------------ procedural fallback

        // A random plan in the same format the LLM writes: used when no LLM is available or it keeps failing.
        public static MultiGoalPlan Procedural(LayoutSettings s, System.Random rng)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var plan = new MultiGoalPlan { name = "Procedural " + rng.Next(100, 999), paths = new PathPlan[s.goalCount] };
            for (int i = 0; i < s.goalCount; i++)
            {
                int n = rng.Next(1, 4);
                var points = new Waypoint[n];
                for (int k = 0; k < n; k++)
                    points[k] = new Waypoint { t = (k + 1f) / (n + 1f) + R(-0.08f, 0.08f), side = R(-8f, 8f), up = R(-s.heightVariation, s.heightVariation) };
                plan.paths[i] = new PathPlan { spacing = R(s.minSpacing, s.maxSpacing), width = R(s.minWidth + 2f, s.maxWidth - 2f), waypoints = points };
            }
            return plan;
        }

        // ------------------------------------------------------------------ geometry helpers

        // Axis-aligned box around a cube rotated by its yaw
        static Bounds BoundsOf(Cube c, Vector3 size)
        {
            float r = c.yaw * Mathf.Deg2Rad, cos = Mathf.Abs(Mathf.Cos(r)), sin = Mathf.Abs(Mathf.Sin(r));
            var rotatedSize = new Vector3(cos * size.x + sin * size.z, size.y, sin * size.x + cos * size.z);
            return new Bounds(c.position, rotatedSize);
        }

        // Does a cube's box touch a Level End's trigger volume (a vertical capsule, treated as its bounding box)?
        static bool HitsEndZone(Bounds box, Vector3 goal, SceneInfo scene)
        {
            var zone = new Bounds();
            zone.SetMinMax(goal + new Vector3(-scene.endRadius, scene.endBottom, -scene.endRadius),
                           goal + new Vector3(scene.endRadius, scene.endTop, scene.endRadius));
            return zone.Intersects(box);
        }

        // Shortest surface-to-surface distance between two cubes (0 if they touch or overlap)
        static float Gap(Cube a, Cube b, Vector3 size)
        {
            Bounds A = BoundsOf(a, size), B = BoundsOf(b, size);
            float dx = Mathf.Max(0f, Mathf.Abs(A.center.x - B.center.x) - A.extents.x - B.extents.x);
            float dy = Mathf.Max(0f, Mathf.Abs(A.center.y - B.center.y) - A.extents.y - B.extents.y);
            float dz = Mathf.Max(0f, Mathf.Abs(A.center.z - B.center.z) - A.extents.z - B.extents.z);
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // Uniform Catmull-Rom through every control point, endpoints duplicated
        static List<Vector3> SampleCatmullRom(List<Vector3> pts, int samplesPerSegment)
        {
            var result = new List<Vector3>();
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 p0 = pts[Mathf.Max(i - 1, 0)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(i + 2, pts.Count - 1)];
                for (int k = 0; k < samplesPerSegment; k++)
                {
                    float t = k / (float)samplesPerSegment, t2 = t * t, t3 = t2 * t;
                    result.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            result.Add(pts[pts.Count - 1]);
            return result;
        }

        // Point at a given arc length along a polyline
        static Vector3 PointAt(List<Vector3> curve, float distance)
        {
            for (int k = 1; k < curve.Count; k++)
            {
                float d = Vector3.Distance(curve[k - 1], curve[k]);
                if (distance <= d) return Vector3.Lerp(curve[k - 1], curve[k], d > 0f ? distance / d : 0f);
                distance -= d;
            }
            return curve[curve.Count - 1];
        }
    }
}
