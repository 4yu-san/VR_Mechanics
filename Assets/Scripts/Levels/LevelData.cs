using System;
using System.Globalization;
using System.Text;
using UnityEngine;

// A level as plain data (this is what the LLM writes, as JSON).
// Positions and sizes are [x, y, z] arrays so the JSON stays short and easy for a model to produce.
// The level runs from the start slab toward -x, like "New Grapple".
//
// Example:
// {
//   "name": "Level 02",
//   "platforms": [ { "position": [-9.6, 1.6, -2.7] }, { "position": [-13.3, 1.6, 2.3], "yaw": 30, "size": [2, 0.86, 2.28] } ],
//   "end": [-41.5, 4.2, 7.4]
// }
[Serializable]
public class LevelData
{
    public string name;
    public SlabData start;          // optional, defaults to the New Grapple start slab
    public PlatformData[] platforms;
    public float[] end;             // Level End position [x, y, z]

    // Defaults taken from the New Grapple scene
    public static readonly Vector3 DefaultStartPosition = new Vector3(2f, -0.8f, 1.15f);
    public static readonly Vector3 DefaultStartSize = new Vector3(10.94f, 0.09f, 11.42f);
    public static readonly Vector3 DefaultPlatformSize = new Vector3(1.3922f, 0.86f, 2.28f);
    // The Level End trigger: its capsule is about 2.4 wide and 5.1 tall, centred slightly above its pivot
    public static readonly Vector3 EndTriggerOffset = new Vector3(0f, 0.26f, 0f);
    public static readonly Vector3 EndTriggerSize = new Vector3(2.4f, 5.1f, 2.4f);
    // Where the player spawns (Physics Rig), used to keep platforms off the player
    public static readonly Vector3 SpawnPosition = new Vector3(0f, 0.4f, 0f);
    public static readonly Vector3 SpawnSize = new Vector3(1.2f, 2.4f, 1.2f);

    // Returns null if the text is not valid level JSON. Tolerates text around the JSON (e.g. ```json fences
    // or a sentence before it, which language models often add) by reading from the first { to the last }.
    public static LevelData FromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        int open = json.IndexOf('{'), close = json.LastIndexOf('}');
        if (open < 0 || close <= open) return null;
        try { return JsonUtility.FromJson<LevelData>(json.Substring(open, close - open + 1)); }
        catch (ArgumentException) { return null; }
    }
    // Compact, human-readable JSON (one platform per line, short numbers). Smaller and easier for an LLM
    // to read and imitate than JsonUtility's output, and it still parses with FromJson.
    public string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"name\": \"").Append((name ?? "").Replace("\"", "")).Append("\",\n");
        if (start != null && Ok3(start.position) && Ok3(start.size))
            sb.Append("  \"start\": { \"position\": ").Append(Arr(start.position)).Append(", \"size\": ").Append(Arr(start.size)).Append(" },\n");
        sb.Append("  \"platforms\": [\n");
        for (int i = 0; i < platforms.Length; i++)
        {
            var p = platforms[i];
            sb.Append("    { \"position\": ").Append(Arr(p.position));
            if (p.yaw != 0f) sb.Append(", \"yaw\": ").Append(Num(p.yaw));
            if (Ok3(p.size)) sb.Append(", \"size\": ").Append(Arr(p.size));
            sb.Append(i < platforms.Length - 1 ? " },\n" : " }\n");
        }
        sb.Append("  ],\n  \"end\": ").Append(Arr(end)).Append("\n}\n");
        return sb.ToString();
    }

    static string Num(float f) => f.ToString("0.##", CultureInfo.InvariantCulture);
    static string Arr(float[] a) => "[" + Num(a[0]) + ", " + Num(a[1]) + ", " + Num(a[2]) + "]";

    // Baking only moves the existing platforms and the Level End, so the start slab and every platform's
    // rotation and size stay as in New Grapple. Clear those fields so validation checks what will be built.
    public void Normalize()
    {
        start = null;
        if (platforms == null) return;
        foreach (var p in platforms)
        {
            if (p == null) continue;
            p.yaw = 0f;
            p.size = null;
        }
    }

    public static Vector3 ToVector(float[] a) => new Vector3(a[0], a[1], a[2]);
    public static float[] ToArray(Vector3 v) => new[] { Round(v.x), Round(v.y), Round(v.z) };
    static float Round(float f) => Mathf.Round(f * 100f) / 100f;

    public Vector3 StartPosition => start != null && Ok3(start.position) ? ToVector(start.position) : DefaultStartPosition;
    public Vector3 StartSize => start != null && Ok3(start.size) ? ToVector(start.size) : DefaultStartSize;

    public static bool Ok3(float[] a) => a != null && a.Length == 3;
}

[Serializable]
public class SlabData
{
    public float[] position;
    public float[] size;
}

[Serializable]
public class PlatformData
{
    public float[] position;
    public float yaw;               // rotation around the up axis, degrees (optional)
    public float[] size;            // optional, defaults to LevelData.DefaultPlatformSize

    public Vector3 Position => LevelData.ToVector(position);
    public Vector3 Size => LevelData.Ok3(size) ? LevelData.ToVector(size) : LevelData.DefaultPlatformSize;
}
