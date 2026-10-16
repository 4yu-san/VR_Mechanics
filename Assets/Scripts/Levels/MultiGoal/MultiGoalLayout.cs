using System;
using System.Collections.Generic;
using UnityEngine;

// Multi-goal levels: several Level Ends, all the same distance from the Start Platform, each reached by
// several different routes through a dense, layered field of Cube (7) platforms.
//
// The LLM never writes coordinates. It describes ROUTES: each route belongs to one goal and is a corridor
// with a shape (waypoints that bend it sideways / up / down), a width and a density. This file fills every
// corridor with cubes:
//   1. a "spine" of cubes along the corridor's centre line, so every route reaches its goal by construction,
//   2. random extra cubes scattered across the corridor's width and height (dart throwing with a minimum
//      spacing and headroom), so the level is a 3D field with many routes, not a single file line.
// Routes overlap near the start, so they merge into one shared field and split toward their goals.
//
// Guarantees: every Level End is exactly settings.goalDistance from the Start Platform's centre and sits
// right under a landing cube; that landing cube is reachable from the Start Platform by hops of at most
// settings.maxStep (15 m); routes to the same goal really are different (they separate by at least
// settings.minRouteSeparation somewhere). Cubes that cannot be reached are removed.
namespace MultiGoalLevels
{
    // ------------------------------------------------------------------ what the LLM writes

    [Serializable]
    public class Waypoint
    {
        public float t;     // 0..1 along the route, from the start (0) to the goal (1)
        public float side;  // metres sideways from the straight line, positive = right (looking toward the goal)
        public float up;    // metres above the straight line
    }

    [Serializable]
    public class PathPlan
    {
        public int goal;             // which Level End this route leads to (0 = leftmost)
        public float spacing;        // metres between neighbouring cubes (smaller = denser)
        public float width;          // corridor width in metres
        public Waypoint[] waypoints;
    }

    [Serializable]
    public class MultiGoalPlan
    {
        public string name;
        public PathPlan[] paths;     // every route; each goal needs between minRoutes and maxRoutes of them

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
        public int minRoutes = 2;              // routes per Level End
        public int maxRoutes = 3;
        public float minRouteSeparation = 10f; // routes to the same goal must be at least this far apart somewhere
        public float maxStep = 15f;            // longest allowed hop between two cubes (or from the Start Platform)
        public float minSpacing = 2.5f;        // the LLM's "spacing" is clamped to [minSpacing, maxSpacing]
        public float maxSpacing = 4.5f;
        public float minWidth = 6f;            // the LLM's "width" is clamped to [minWidth, maxWidth]
        public float maxWidth = 18f;
        public float density = 1.5f;           // how full each corridor gets (1 = loose, 2 = as packed as spacing allows)
        public float maxSide = 15f;
        public float heightVariation = 8f;     // the LLM's "up" is clamped to +-heightVariation
        public float verticalSpread = 5f;      // each corridor's thickness: cubes are scattered over this many metres vertically
        public float heightJitter = 1.5f;      // random per-cube height offset
        public float headroom = 2f;            // free space required above a cube's top if another cube is directly above it
        public float maxDrop = 10f;            // cubes never dip more than this below the Start Platform's top
        public float minGap = 0.8f;            // min surface-to-surface gap between any two cubes
        public float endGap = 0.3f;            // gap between the Level End's top and the landing cube above it
        public int maxCubes = 700;

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
        public int goal;
        public int route;                      // index into plan.paths, -1 for landing cubes
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

        // Sanitises the plan in place (clamps numbers into range, drops extra routes) and returns problems the
        // model must fix itself - the ones that cannot be silently corrected.
        public static List<string> CheckPlan(MultiGoalPlan plan, LayoutSettings s, List<string> warnings)
        {
            var errors = new List<string>();
            if (plan == null) { errors.Add("The reply could not be read as JSON."); return errors; }
            if (plan.paths == null || plan.paths.Length == 0) { errors.Add("\"paths\" is missing or empty."); return errors; }

            // The spine guarantee (see Build) needs 1.85 x spacing <= maxStep
            float maxSpacing = Mathf.Min(s.maxSpacing, s.maxStep / 1.85f);
            var perGoal = new int[s.goalCount];
            var kept = new List<PathPlan>();
            foreach (var p in plan.paths)
            {
                if (p == null) continue;
                if (p.goal < 0 || p.goal >= s.goalCount)
                {
                    errors.Add($"A route has \"goal\": {p.goal}, but goals are numbered 0 to {s.goalCount - 1}.");
                    continue;
                }
                if (perGoal[p.goal] >= s.maxRoutes) { warnings.Add($"Goal {p.goal}: extra routes beyond {s.maxRoutes} dropped."); continue; }
                perGoal[p.goal]++;

                p.spacing = Mathf.Clamp(p.spacing, s.minSpacing, maxSpacing);
                p.width = Mathf.Clamp(p.width, s.minWidth, s.maxWidth);

                var points = new List<Waypoint>(p.waypoints ?? new Waypoint[0]);
                points.RemoveAll(w => w == null);
                if (points.Count > 4) points.RemoveRange(4, points.Count - 4);
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
                kept.Add(p);
            }
            plan.paths = kept.ToArray();

            for (int g = 0; g < s.goalCount; g++)
                if (perGoal[g] < s.minRoutes)
                    errors.Add($"Goal {g} has {perGoal[g]} route(s), it needs at least {s.minRoutes}. Add routes with \"goal\": {g}.");
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
            for (int g = 0; g < layout.goals.Count; g++)
            {
                Vector3 landing = layout.goals[g] + Vector3.up * (scene.endTop + s.endGap + halfY);
                landings.Add(landing);
                layout.cubes.Add(new Cube { position = landing, goal = g, route = -1, landing = true });
            }

            // Centre-line curve of every route
            var curves = new List<List<Vector3>>();
            var lengths = new List<float>();
            var rights = new List<Vector3>();
            foreach (var route in plan.paths)
            {
                Vector3 origin = StartEdge(scene.startPlatform, layout.goals[route.goal]);
                Vector3 landing = landings[route.goal];
                Vector3 forward = landing - origin;
                Vector3 flat = new Vector3(forward.x, 0f, forward.z).normalized;
                Vector3 right = new Vector3(flat.z, 0f, -flat.x);

                var control = new List<Vector3> { origin };
                foreach (var w in route.waypoints)
                    control.Add(origin + forward * w.t + right * w.side + Vector3.up * w.up);
                control.Add(landing);

                var curve = SampleCatmullRom(control, 40);
                float length = 0f;
                for (int k = 1; k < curve.Count; k++) length += Vector3.Distance(curve[k - 1], curve[k]);
                curves.Add(curve);
                lengths.Add(length);
                rights.Add(right);
            }

            // Routes to the same goal must really be different
            for (int a = 0; a < plan.paths.Length; a++)
                for (int b = a + 1; b < plan.paths.Length; b++)
                {
                    if (plan.paths[a].goal != plan.paths[b].goal) continue;
                    float widest = 0f;
                    for (float t = 0.2f; t <= 0.8f; t += 0.05f)
                        widest = Mathf.Max(widest, Vector3.Distance(PointAt(curves[a], t * lengths[a]), PointAt(curves[b], t * lengths[b])));
                    if (widest < s.minRouteSeparation)
                        errors.Add($"Goal {plan.paths[a].goal}: two of its routes are too similar (they never get more than {widest:F0} m apart, " +
                                   $"they need {s.minRouteSeparation:F0} m). Make them different: e.g. one with positive \"side\" values and one with negative, " +
                                   "or one high (positive \"up\") and one low (negative \"up\").");
                }
            if (errors.Count > 0) return layout;

            bool TryAdd(Vector3 p, int routeIndex, float spacing)
            {
                if (layout.cubes.Count >= s.maxCubes) return false;
                p.y = Mathf.Max(p.y, minY);
                var candidate = new Cube { position = p, goal = plan.paths[routeIndex].goal, route = routeIndex };
                var box = BoundsOf(candidate, scene.cubeSize);
                if (startZone.Intersects(box)) return false;
                foreach (var g in layout.goals)
                    if (HitsEndZone(box, g, scene)) return false;
                float minCentre = spacing * 0.85f;
                foreach (var c in layout.cubes)
                {
                    if (Vector3.Distance(c.position, p) < minCentre || Gap(c, candidate, scene.cubeSize) < s.minGap) return false;
                    if (BlocksHeadroom(c, candidate, scene.cubeSize, s.headroom)) return false;
                }
                layout.cubes.Add(candidate);
                return true;
            }

            for (int r = 0; r < plan.paths.Length; r++)
            {
                var route = plan.paths[r];
                var curve = curves[r];
                float length = lengths[r];

                // 1. Spine: centre-line cubes at most "spacing" apart. One rejected for being too close to an
                //    existing cube is fine - that cube is within 0.85 x spacing of it, so the chain still never
                //    has a hop longer than 1.85 x spacing <= maxStep (CheckPlan clamps spacing for that).
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / route.spacing));
                for (int k = 1; k < steps; k++)
                {
                    Vector3 p = PointAt(curve, k * length / steps);
                    p.y += R(-s.heightJitter, s.heightJitter) * 0.5f;
                    TryAdd(p, r, route.spacing);
                }

                // 2. Fill: random cubes across the corridor's width AND height, narrowing toward both ends so the
                //    corridor still leaves the start platform and arrives at the landing cube cleanly
                int target = Mathf.CeilToInt(length * route.width / (route.spacing * route.spacing) * 0.8f * s.density);
                int added = 0;
                for (int attempt = 0; attempt < target * 30 && added < target; attempt++)
                {
                    float along = R(0f, length);
                    float taper = Mathf.Clamp01(Mathf.Min(along, length - along) / Mathf.Max(1f, route.width));
                    float lateral = R(-0.5f, 0.5f) * route.width * taper;
                    float vertical = R(-0.5f, 0.5f) * s.verticalSpread * taper + R(-s.heightJitter, s.heightJitter);
                    Vector3 p = PointAt(curve, along) + rights[r] * lateral + Vector3.up * vertical;
                    if (TryAdd(p, r, route.spacing)) added++;
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

        // Where a route leaves the Start Platform: its top surface, at the edge facing the goal
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
                    layout.unreachable.Add($"Goal {c.goal} cannot be reached with hops of at most {s.maxStep} m. Use a smaller spacing or gentler waypoints on its routes.");
                if (reached[i] || !prune || c.landing) kept.Add(c);
            }
            layout.cubes = kept;
        }

        // ------------------------------------------------------------------ validation

        public static List<string> Validate(Layout layout, LayoutSettings s, SceneInfo scene)
        {
            var errors = new List<string>();
            if (layout.cubes.Count >= s.maxCubes)
                errors.Add($"The level hit the limit of {s.maxCubes} platforms. Use a larger spacing, a narrower width or fewer routes.");

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
        // Routes to the same goal are fanned out sideways and alternate high / low, so they are clearly different.
        public static MultiGoalPlan Procedural(LayoutSettings s, System.Random rng)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var paths = new List<PathPlan>();
            for (int g = 0; g < s.goalCount; g++)
            {
                int routes = rng.Next(s.minRoutes, s.maxRoutes + 1);
                for (int r = 0; r < routes; r++)
                {
                    float fan = routes == 1 ? 0f : Mathf.Lerp(-1f, 1f, r / (float)(routes - 1));
                    float height = (r % 2 == 0 ? R(0.3f, 1f) : R(-0.7f, -0.1f)) * s.heightVariation;
                    var points = new List<Waypoint>
                    {
                        new Waypoint { t = R(0.4f, 0.6f), side = fan * R(8f, s.maxSide), up = height },
                    };
                    int extra = rng.Next(0, 3);
                    for (int k = 0; k < extra; k++)
                        points.Add(new Waypoint { t = k == 0 ? R(0.15f, 0.3f) : R(0.7f, 0.85f), side = fan * R(2f, 8f) + R(-3f, 3f), up = R(-0.5f, 0.5f) * s.heightVariation });
                    paths.Add(new PathPlan { goal = g, spacing = R(s.minSpacing, s.maxSpacing), width = R(s.minWidth, s.maxWidth * 0.8f), waypoints = points.ToArray() });
                }
            }
            return new MultiGoalPlan { name = "Procedural " + rng.Next(100, 999), paths = paths.ToArray() };
        }

        // ------------------------------------------------------------------ geometry helpers

        // Axis-aligned box around a cube rotated by its yaw
        static Bounds BoundsOf(Cube c, Vector3 size)
        {
            float r = c.yaw * Mathf.Deg2Rad, cos = Mathf.Abs(Mathf.Cos(r)), sin = Mathf.Abs(Mathf.Sin(r));
            var rotatedSize = new Vector3(cos * size.x + sin * size.z, size.y, sin * size.x + cos * size.z);
            return new Bounds(c.position, rotatedSize);
        }

        // True if one cube sits so close directly above the other that the player could not stand on the lower one
        static bool BlocksHeadroom(Cube a, Cube b, Vector3 size, float headroom)
        {
            Bounds A = BoundsOf(a, size), B = BoundsOf(b, size);
            bool footprintsOverlap = Mathf.Abs(A.center.x - B.center.x) < A.extents.x + B.extents.x + 0.3f
                                  && Mathf.Abs(A.center.z - B.center.z) < A.extents.z + B.extents.z + 0.3f;
            if (!footprintsOverlap) return false;
            float verticalGap = Mathf.Abs(A.center.y - B.center.y) - A.extents.y - B.extents.y;
            return verticalGap < headroom;
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
