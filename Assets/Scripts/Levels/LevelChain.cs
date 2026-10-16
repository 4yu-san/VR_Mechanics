using System;
using UnityEngine;

// The LLM's raw output for the editor-only Ollama pipeline: a chain of RELATIVE steps instead of
// absolute [x,y,z] coordinates. Mirrors the idea in Tools/levelgen's schema.py + layout.py (ported for
// a smaller/weaker local model), adapted to this pipeline's fixed platform size/rotation (Normalize()
// always strips size and yaw anyway - Bake only ever moves the scene's existing Cube (7) objects) and
// its "runs toward -x" convention.
//
// Why: asking for 24 independent absolute coordinates forces the model to keep all of them globally
// consistent (no two platforms touching, every one within hop range of another) with nothing but raw
// numbers and no room to "think" (format=json forces structured output). A chain makes every
// consecutive platform reachable BY CONSTRUCTION - the model only ever has to pick one small, bounded
// relative number at a time, which is a much easier task for a small model.
[Serializable]
public class ChainStep
{
    public float gap;    // empty space before this platform, along the current heading, metres (>= 0)
    public float rise;   // change in height versus the previous platform, metres (can be negative)
    public float turn;   // -90, 0 or 90: turn applied before moving
    public float shift;  // sideways offset after turning, metres (can be negative)
}

[Serializable]
public class LevelChain
{
    public string name;
    public ChainStep[] platforms;  // exactly LevelValidator.Rules.platformCount (24)
    public ChainStep goal;         // one more relative step, placed after the last platform

    // Tolerates text around the JSON (fences, a sentence before it), same approach as LevelData.FromJson.
    public static LevelChain FromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        int open = json.IndexOf('{'), close = json.LastIndexOf('}');
        if (open < 0 || close <= open) return null;
        try { return JsonUtility.FromJson<LevelChain>(json.Substring(open, close - open + 1)); }
        catch (ArgumentException) { return null; }
    }
}

// Deterministically expands a LevelChain into the absolute LevelData the rest of the pipeline
// (LevelValidator, LevelBuilder, LevelTools.Bake) already expects, unchanged. Heading 0 faces -x
// (this game runs toward negative x); turn +90 is a right turn, matching layout.py's convention.
public static class LevelLayout
{
    static (float dx, float dz) Heading(float angle)
    {
        switch (NormalizeAngle(angle))
        {
            case 0: return (-1f, 0f);
            case 90: return (0f, 1f);
            case 180: return (1f, 0f);
            default: return (0f, -1f); // 270
        }
    }

    // Half-extent of the fixed platform box along the world X/Z axes at a given heading
    static (float hx, float hz) HalfExtentsXZ(float angle)
    {
        Vector3 size = LevelData.DefaultPlatformSize;
        int a = (int)NormalizeAngle(angle);
        return (a == 90 || a == 270) ? (size.z * 0.5f, size.x * 0.5f) : (size.x * 0.5f, size.z * 0.5f);
    }

    public static LevelData Expand(LevelChain chain)
    {
        if (chain?.platforms == null || chain.platforms.Length == 0) return null;

        var platforms = new PlatformData[chain.platforms.Length];
        float angle = 0f;
        // Anchor: the start slab's near edge (toward -x) and top surface, on the spawn line (z = 0)
        float cx = LevelData.DefaultStartPosition.x - LevelData.DefaultStartSize.x * 0.5f;
        float top = LevelData.DefaultStartPosition.y + LevelData.DefaultStartSize.y * 0.5f;
        float cz = 0f;
        float prevAlongHalf = 0f;

        for (int i = 0; i < chain.platforms.Length; i++)
        {
            var step = chain.platforms[i] ?? new ChainStep();
            angle = NormalizeAngle(angle + step.turn);
            var (dx, dz) = Heading(angle);
            var (hx, hz) = HalfExtentsXZ(angle);
            float curAlongHalf = Mathf.Abs(dx) > 0.5f ? hx : hz;

            float advance = prevAlongHalf + Mathf.Max(0f, step.gap) + curAlongHalf;
            float rx = dz, rz = -dx; // right-hand vector, for "shift"

            cx += dx * advance + rx * step.shift;
            cz += dz * advance + rz * step.shift;
            top += step.rise;

            platforms[i] = new PlatformData { position = new[] { Round(cx), Round(top), Round(cz) } };
            prevAlongHalf = curAlongHalf;
        }

        var level = new LevelData { name = chain.name, platforms = platforms };

        if (chain.goal != null)
        {
            var g = chain.goal;
            angle = NormalizeAngle(angle + g.turn);
            var (dx, dz) = Heading(angle);
            float advance = prevAlongHalf + Mathf.Max(0f, g.gap); // the goal trigger has no platform box of its own here
            float rx = dz, rz = -dx;
            cx += dx * advance + rx * g.shift;
            cz += dz * advance + rz * g.shift;
            top += g.rise;
            level.end = new[] { Round(cx), Round(top), Round(cz) };
        }

        return level;
    }

    static float NormalizeAngle(float a) => ((Mathf.Round(a) % 360) + 360) % 360;
    static float Round(float f) => Mathf.Round(f * 100f) / 100f;
}
