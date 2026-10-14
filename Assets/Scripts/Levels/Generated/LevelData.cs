using System;

// Mirrors the JSON written by Tools/levelgen. Field names must match the JSON keys (JsonUtility).
// Lives in its own namespace so it does not collide with the older LevelData/PlatformData classes
// used by the editor-only Ollama pipeline (Assets/Scripts/Levels/LevelData.cs), which is kept for now.
namespace GeneratedLevels
{
    [Serializable]
    public class LevelPieceData
    {
        public string id;
        public string type;      // platform, pillar, wall, goal
        public float[] position; // centre of the box, world space
        public float[] size;     // width, height, depth (local axes, before yaw)
        public float yaw;        // 0, 90, 180, 270
    }

    [Serializable]
    public class LevelStartData
    {
        public float[] position;
        public float yaw;
    }

    [Serializable]
    public class LevelData
    {
        public int version;
        public string name;
        public string theme;
        public int seed;
        public LevelStartData start;
        public float killFloorY;
        public LevelPieceData[] pieces;
    }
}
