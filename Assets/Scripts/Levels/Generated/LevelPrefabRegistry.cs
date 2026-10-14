using UnityEngine;

namespace GeneratedLevels
{
    // Maps generated level pieces to the prefabs LevelBuilder spawns.
    // This project has one platform prefab (Cube (7)) used for every piece type
    // (platform/pillar/wall/goal) - only its scale/rotation changes per piece.
    [CreateAssetMenu(fileName = "LevelPrefabRegistry", menuName = "Grapple/Level Prefab Registry (Generated)")]
    public class LevelPrefabRegistry : ScriptableObject
    {
        [Header("Prefabs")]
        [Tooltip("Cube (7): a unit cube with a BoxCollider. Its scale is overwritten per piece.")]
        [SerializeField] private GameObject blockPrefab;
        [Tooltip("Level End prefab (has EndLvl.cs on it). Optional - falls back to a bare trigger + EndLvl if left empty.")]
        [SerializeField] private GameObject goalTriggerPrefab;

        [Header("Layer")]
        [Tooltip("Layer the blocks go on. Must be the layer Grappling.cs's Grappleable mask includes.")]
        [SerializeField] private string blockLayerName = "isGrapplable";

        public GameObject GetBlockPrefab() => blockPrefab;
        public GameObject GetGoalTriggerPrefab() => goalTriggerPrefab;
        public string GetBlockLayerName() => blockLayerName;
    }
}
