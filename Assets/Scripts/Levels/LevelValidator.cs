using System.Collections.Generic;
using UnityEngine;

// Checks that a generated level is well-formed and beatable before it is accepted.
// Returns plain-text problems so they can be fed straight back to the LLM for a retry.
public static class LevelValidator
{
    public class Rules
    {
        public int platformCount = 24;                // New Grapple has 24 Cube (7) platforms, a baked level reuses exactly those
        public float maxStep = 7f;                    // biggest gap (surface to surface) between two platforms. New Grapple's hardest hop is ~5.5 m
        public float minPlatformSize = 0.5f;
        public float maxPlatformSize = 6f;
        public float maxHorizontal = 120f;            // |x| and |z| limit
        public float minY = 0f;
        public float maxY = 25f;
        public float maxBehindStart = 8f;             // platforms may not be further toward +x than this (the level runs toward -x)
        public float minEndDistance = 25f;            // the goal must be at least this far toward -x
        public float minHeightRange = 3f;             // lowest to highest platform, so the level is not flat
        public float minSideRange = 6f;               // smallest to largest z, so the level is not a straight line
        public float overlapTolerance = 0.01f;
    }

    public class Result
    {
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();
        public bool Ok => errors.Count == 0;

        public override string ToString()
        {
            var s = Ok ? "VALID" : "INVALID";
            foreach (var e in errors) s += "\n  ERROR: " + e;
            foreach (var w in warnings) s += "\n  warning: " + w;
            return s;
        }
    }

    // Axis-aligned box that contains a platform (rotated around Y if it has a yaw)
    struct Box
    {
        public Vector3 center, half;

        public static Box From(Vector3 center, Vector3 size, float yaw)
        {
            float r = yaw * Mathf.Deg2Rad;
            float c = Mathf.Abs(Mathf.Cos(r)), s = Mathf.Abs(Mathf.Sin(r));
            var half = size * 0.5f;
            return new Box { center = center, half = new Vector3(c * half.x + s * half.z, half.y, s * half.x + c * half.z) };
        }

        public bool Overlaps(Box o, float tol)
        {
            return Mathf.Abs(center.x - o.center.x) < half.x + o.half.x - tol
                && Mathf.Abs(center.y - o.center.y) < half.y + o.half.y - tol
                && Mathf.Abs(center.z - o.center.z) < half.z + o.half.z - tol;
        }

        // Shortest distance between the two boxes' surfaces (0 if touching)
        public float Gap(Box o)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(center.x - o.center.x) - half.x - o.half.x);
            float dy = Mathf.Max(0f, Mathf.Abs(center.y - o.center.y) - half.y - o.half.y);
            float dz = Mathf.Max(0f, Mathf.Abs(center.z - o.center.z) - half.z - o.half.z);
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    public static Result Validate(LevelData level, Rules rules = null)
    {
        rules = rules ?? new Rules();
        var result = new Result();

        if (level == null) { result.errors.Add("The level could not be read (invalid JSON)."); return result; }
        int count = level.platforms == null ? 0 : level.platforms.Length;
        if (count != rules.platformCount)
            result.errors.Add($"Needs exactly {rules.platformCount} platforms, but it has {count}.");
        if (!LevelData.Ok3(level.end)) result.errors.Add("\"end\" must be an [x, y, z] array.");
        if (!result.Ok) return result;

        // 1. Each platform is well-formed and inside the allowed area
        var boxes = new List<Box>();
        var outside = new List<int>();
        var behind = new List<int>();
        var badSize = new List<int>();
        float lowest = float.MaxValue, highest = float.MinValue, leftmost = float.MaxValue, rightmost = float.MinValue;
        for (int i = 0; i < level.platforms.Length; i++)
        {
            var p = level.platforms[i];
            if (!LevelData.Ok3(p.position)) { result.errors.Add($"Platform {i}: \"position\" must be an [x, y, z] array."); continue; }
            if (p.size != null && p.size.Length != 0 && p.size.Length != 3) { result.errors.Add($"Platform {i}: \"size\" must be an [x, y, z] array."); continue; }

            var pos = p.Position;
            var size = p.Size;
            if (Mathf.Abs(pos.x) > rules.maxHorizontal || Mathf.Abs(pos.z) > rules.maxHorizontal || pos.y < rules.minY || pos.y > rules.maxY) outside.Add(i);
            if (pos.x > rules.maxBehindStart) behind.Add(i);
            if (size.x < rules.minPlatformSize || size.y < rules.minPlatformSize || size.z < rules.minPlatformSize ||
                size.x > rules.maxPlatformSize || size.y > rules.maxPlatformSize || size.z > rules.maxPlatformSize) badSize.Add(i);
            lowest = Mathf.Min(lowest, pos.y); highest = Mathf.Max(highest, pos.y);
            leftmost = Mathf.Min(leftmost, pos.z); rightmost = Mathf.Max(rightmost, pos.z);
            boxes.Add(Box.From(pos, size, p.yaw));
        }
        if (outside.Count > 0)
            result.errors.Add($"Platforms {Indices(outside)} are outside the allowed area. Every platform needs |x| <= {rules.maxHorizontal}, |z| <= {rules.maxHorizontal} and y between {rules.minY} and {rules.maxY}. " +
                              $"Your highest platform is at y = {highest:F1}; climb less per step so the whole level stays under y = {rules.maxY}.");
        if (behind.Count > 0)
            result.errors.Add($"Platforms {Indices(behind)} are behind the start slab (x > {rules.maxBehindStart}). The level must run toward NEGATIVE x, so every platform needs x <= {rules.maxBehindStart}.");
        if (badSize.Count > 0)
            result.errors.Add($"Platforms {Indices(badSize)} have a size outside {rules.minPlatformSize} to {rules.maxPlatformSize}.");
        if (!result.Ok) return result;

        var start = Box.From(level.StartPosition, level.StartSize, 0f);
        var spawn = Box.From(LevelData.SpawnPosition, LevelData.SpawnSize, 0f);
        var endPos = LevelData.ToVector(level.end);
        var end = Box.From(endPos + LevelData.EndTriggerOffset, LevelData.EndTriggerSize, 0f);

        if (endPos.x > -rules.minEndDistance)
            result.errors.Add($"The goal is at x = {endPos.x:F1} but the level must run toward NEGATIVE x. Put the goal at x <= {-rules.minEndDistance}.");
        if (highest - lowest < rules.minHeightRange)
            result.errors.Add($"The level is too flat: platform heights only span {highest - lowest:F1} m. Use at least {rules.minHeightRange} m of height difference (climb and drop).");
        if (rightmost - leftmost < rules.minSideRange)
            result.errors.Add($"The level is a straight line: platforms only span {rightmost - leftmost:F1} m sideways (z). Spread them over at least {rules.minSideRange} m in z.");

        // 2. Nothing may overlap the player spawn, another platform or the Level End
        for (int i = 0; i < boxes.Count; i++)
        {
            if (boxes[i].Overlaps(spawn, rules.overlapTolerance)) result.errors.Add($"Platform {i} overlaps the player spawn.");
            if (boxes[i].Overlaps(end, rules.overlapTolerance)) result.errors.Add($"Platform {i} overlaps the Level End.");
            for (int j = i + 1; j < boxes.Count; j++)
                if (boxes[i].Overlaps(boxes[j], rules.overlapTolerance)) result.errors.Add($"Platforms {i} and {j} overlap.");
        }

        // 3. The Level End must be reachable by hopping platform to platform from the start slab
        //    node 0 = start slab, 1..n = platforms, n+1 = Level End
        var nodes = new List<Box> { start };
        nodes.AddRange(boxes);
        nodes.Add(end);
        int endNode = nodes.Count - 1;

        var reached = new bool[nodes.Count];
        var queue = new Queue<int>();
        reached[0] = true;
        queue.Enqueue(0);
        while (queue.Count > 0)
        {
            int a = queue.Dequeue();
            for (int b = 0; b < nodes.Count; b++)
                if (!reached[b] && nodes[a].Gap(nodes[b]) <= rules.maxStep)
                {
                    reached[b] = true;
                    queue.Enqueue(b);
                }
        }

        if (!reached[endNode])
        {
            result.errors.Add($"The Level End at {Fmt(endPos)} cannot be reached from the start: no chain of platforms with gaps of at most {rules.maxStep} m leads to it.");
            // Help the model fix it: say how far the closest reachable platform is
            float best = float.MaxValue; int bestIndex = -1;
            for (int i = 0; i < nodes.Count - 1; i++)
                if (reached[i] && nodes[i].Gap(end) < best) { best = nodes[i].Gap(end); bestIndex = i; }
            result.errors.Add(bestIndex == 0
                ? $"The closest reachable thing to the Level End is the start slab, {best:F1} m away. Build a chain of platforms toward the goal, or move the goal closer."
                : $"The closest reachable platform to the Level End is platform {bestIndex - 1} at {Fmt(boxes[bestIndex - 1].center)}, {best:F1} m away. " +
                  $"Move the goal to within 5 m of that platform (or of the last platform in your chain), or add platforms that lead to it.");
        }
        for (int i = 1; i < endNode; i++)
            if (!reached[i]) result.warnings.Add($"Platform {i - 1} at {Fmt(boxes[i - 1].center)} cannot be reached (unused).");

        return result;
    }

    // "3, 4, 5, 6, 7, 8, 9 and 12 more" so one message does not list every platform
    static string Indices(List<int> list)
    {
        const int show = 6;
        var s = string.Join(", ", list.GetRange(0, Mathf.Min(show, list.Count)));
        return list.Count > show ? s + $" and {list.Count - show} more" : s;
    }

    static string Fmt(Vector3 v) => $"[{v.x:F1}, {v.y:F1}, {v.z:F1}]";
}
