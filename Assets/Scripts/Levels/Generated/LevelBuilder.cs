using UnityEngine;

// Builds a level from a JSON file made by Tools/levelgen.
// Levels live in Assets/Resources/Levels/*.json, which Resources loads on every platform
// (including Android/Quest, where StreamingAssets is not a normal folder).
//
// One prefab (Cube (7)) is used for every piece type - platform, pillar, wall and goal all
// reuse it, only scale/rotation/layer change. "Wall"/"pillar" are layout concepts the Python
// validator uses for reachability (can this be stood on?), not separate Unity prefabs.
namespace GeneratedLevels
{
    public class LevelBuilder : MonoBehaviour
    {
        [SerializeField] private LevelPrefabRegistry registry;
        [Tooltip("The Physics Rig root (has the Rigidbody). Moved to the level's start point after building.")]
        [SerializeField] private Transform player;

        private const string ResourcesFolder = "Levels";
        private const float GoalTriggerHeightOffset = 1.5f; // sits above the goal platform's top surface

        private GameObject levelRoot;
        private LevelData currentLevel;

        [ContextMenu("Build Level")]
        public void BuildFromFile(string fileName)
        {
            TextAsset json = Resources.Load<TextAsset>(ResourcesFolder + "/" + fileName);
            if (json == null)
            {
                Debug.LogError($"LevelBuilder: no level JSON found at Resources/{ResourcesFolder}/{fileName}");
                return;
            }

            LevelData data = JsonUtility.FromJson<LevelData>(json.text);
            if (data == null || data.pieces == null || data.pieces.Length == 0)
            {
                Debug.LogError("LevelBuilder: " + fileName + " is empty or malformed.");
                return;
            }

            Build(data);
        }

        public void Build(LevelData data)
        {
            if (registry == null || registry.GetBlockPrefab() == null)
            {
                Debug.LogError("LevelBuilder: assign a LevelPrefabRegistry with a block prefab.");
                return;
            }

            // A level transition can happen mid-grapple (right at the goal trigger); don't carry
            // a stale SpringJoint from Grappling.cs into the next level's physics.
            if (player != null)
            {
                var joint = player.GetComponent<SpringJoint>();
                if (joint != null) Destroy(joint);
            }

            Clear();
            currentLevel = data;
            levelRoot = new GameObject("GeneratedLevel_" + data.name);
            levelRoot.transform.SetParent(transform, false);

            int layer = LayerMask.NameToLayer(registry.GetBlockLayerName());
            if (layer < 0)
                Debug.LogWarning($"LevelBuilder: layer '{registry.GetBlockLayerName()}' does not exist, pieces stay on Default and cannot be grappled.");

            foreach (LevelPieceData piece in data.pieces)
                SpawnPiece(piece, layer);

            SpawnKillFloor(data);
            PlacePlayer(data);
            Debug.Log($"LevelBuilder: built '{data.name}' ({data.pieces.Length} pieces)");
        }

        public void Clear()
        {
            if (levelRoot != null)
                Destroy(levelRoot);
            levelRoot = null;
            currentLevel = null;
        }

        public LevelData GetCurrentLevel() => currentLevel;

        private void SpawnPiece(LevelPieceData piece, int layer)
        {
            Vector3 position = ToVector3(piece.position);
            Vector3 size = ToVector3(piece.size);
            Quaternion rotation = Quaternion.Euler(0f, piece.yaw, 0f);

            GameObject block = Instantiate(registry.GetBlockPrefab(), position, rotation, levelRoot.transform);
            block.name = piece.id + "_" + piece.type;
            block.transform.localScale = size;
            if (layer >= 0)
                block.layer = layer;

            if (piece.type == "goal")
                SpawnGoalTrigger(piece, position, size);
        }

        private void SpawnGoalTrigger(LevelPieceData piece, Vector3 blockCentre, Vector3 blockSize)
        {
            if (registry.GetGoalTriggerPrefab() == null)
            {
                Debug.LogWarning("LevelBuilder: no goal trigger prefab assigned, goal piece " + piece.id + " has no win trigger.");
                return;
            }

            Vector3 triggerCentre = blockCentre + Vector3.up * (blockSize.y / 2f + GoalTriggerHeightOffset);
            GameObject trigger = Instantiate(registry.GetGoalTriggerPrefab(), triggerCentre, Quaternion.identity, levelRoot.transform);
            trigger.name = piece.id + "_goalTrigger";
        }

        private void SpawnKillFloor(LevelData data)
        {
            Vector3 centre = Vector3.zero;
            foreach (LevelPieceData piece in data.pieces)
                centre += ToVector3(piece.position);
            centre /= data.pieces.Length;
            centre.y = data.killFloorY;

            GameObject floor = new GameObject("KillFloor");
            floor.transform.SetParent(levelRoot.transform, false);
            floor.transform.position = centre;
            BoxCollider box = floor.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(2000f, 2f, 2000f);
            floor.AddComponent<DeathFloor>();
        }

        private void PlacePlayer(LevelData data)
        {
            if (player == null || data.start == null || data.start.position == null)
                return;

            player.position = ToVector3(data.start.position);
            player.rotation = Quaternion.Euler(0f, data.start.yaw, 0f);
            Rigidbody body = player.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        private static Vector3 ToVector3(float[] values) => new Vector3(values[0], values[1], values[2]);
    }
}
