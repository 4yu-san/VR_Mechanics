using UnityEngine;
using UnityEngine.InputSystem;

// Drives the generated-levels game loop: plays through pre-generated batches of 5 levels,
// in a fixed, increasing-difficulty order, rebuilding New Grapple.unity in place for each one
// (no per-level scenes, no Build Settings bookkeeping).
//
// Phase 1 (current): batches are generated ahead of time by hand with Tools/levelgen and placed
// in Resources/Levels/ as batch001_level1..5, batch002_level1..5, etc. Advance() just walks that
// fixed list. Phase 2 (later) replaces the fixed list with a live background generate.py call
// kicked off after level 4 of a batch.
namespace GeneratedLevels
{
    public class LevelProgressManager : MonoBehaviour
    {
        [SerializeField] private LevelBuilder levelBuilder;

        [Tooltip("File names (without .json) under Resources/Levels, in play order.")]
        [SerializeField]
        private string[] levelFiles =
        {
            "batch001_level1", "batch001_level2", "batch001_level3", "batch001_level4", "batch001_level5",
            "batch002_level1", "batch002_level2", "batch002_level3", "batch002_level4", "batch002_level5",
        };

        [Tooltip("If true, Advance() past the last level loops back to the first instead of stopping.")]
        [SerializeField] private bool loopAtEnd = true;

        private int currentIndex = -1;
        private InputAction skipAction;

        public static LevelProgressManager instance { get; private set; }

        void Awake()
        {
            instance = this;

            // Debug shortcut: B on the right Touch controller (secondary button), with L on the
            // keyboard kept as a fallback for Editor/Simulator testing without a headset.
            skipAction = new InputAction("SkipLevel", InputActionType.Button);
            skipAction.AddBinding("<XRController>{RightHand}/{SecondaryButton}");
            skipAction.AddBinding("<XRController>{RightHand}/secondaryButton");
            skipAction.AddBinding("<Keyboard>/l");
            skipAction.performed += OnSkipPressed;
        }

        void OnEnable() => skipAction.Enable();
        void OnDisable() => skipAction.Disable();
        void OnDestroy() => skipAction.Dispose();

        void Start()
        {
            Advance();
        }

        void OnSkipPressed(InputAction.CallbackContext ctx)
        {
            Advance();
        }

        public void Advance()
        {
            if (levelFiles == null || levelFiles.Length == 0)
            {
                Debug.LogWarning("LevelProgressManager: no level files configured.");
                return;
            }

            currentIndex++;
            if (currentIndex >= levelFiles.Length)
            {
                if (!loopAtEnd)
                {
                    Debug.Log("LevelProgressManager: no more levels.");
                    currentIndex = levelFiles.Length - 1;
                    return;
                }
                currentIndex = 0;
            }

            levelBuilder.BuildFromFile(levelFiles[currentIndex]);
            GameManager.instance?.SetState(GameManager.GameState.Playing);
        }
    }
}
