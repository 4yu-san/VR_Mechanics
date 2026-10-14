using System.Collections.Generic;
using UnityEngine;

// Applies a level to a copy of New Grapple by changing only positions:
// the existing Cube (7) platforms and the Level End. Nothing is created, deleted or resized.
public static class LevelBuilder
{
    // platforms: the Cube (7) objects already in the scene (must match the level's platform count)
    public static void Apply(LevelData level, IList<Transform> platforms, Transform levelEnd)
    {
        for (int i = 0; i < level.platforms.Length; i++)
            platforms[i].position = level.platforms[i].Position;

        levelEnd.position = LevelData.ToVector(level.end);
    }
}
