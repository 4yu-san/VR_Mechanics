using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Handles the in-game VR menus (pause, game over, level complete).
// The main menu has its own scene and script (MainMenu.cs).
// The menus are world-space panels that appear in front of the player when opened.
// The HUD (gameUIPanel) is separate and stays attached to the camera.
public class UIManager : MonoBehaviour
{
    [Header("HUD (head-locked, lives under the camera)")]
    public GameObject gameUIPanel;

    [Header("Menu panels (all children of the world-space menu canvas)")]
    public GameObject menuRoot;
    public GameObject pauseMenuScreen;
    public GameObject gameOverPanel;
    public GameObject levelCompleteScreen;
    public GameObject nextLevelButton;
    public TMPro.TextMeshProUGUI levelCompleteTimer;

    [Header("Scenes")]
    public string mainMenuScene = "MainMenu";

    [Header("Placement")]
    public float menuDistance = 1.5f;

    Transform head;
    XRRayInteractor[] rays;
    InputAction pauseAction;

    // Singleton instance
    private static UIManager _instance;
    public static UIManager instance
    {
        get
        {
            if (_instance == null)
                _instance = UnityEngine.Object.FindAnyObjectByType<UIManager>();
            return _instance;
        }
    }

    void Awake()
    {
        // Pause = left controller menu/options button, Escape for the simulator.
        pauseAction = new InputAction("Pause", InputActionType.Button);
        pauseAction.AddBinding("<XRController>{LeftHand}/{Menu}");
        pauseAction.AddBinding("<XRController>{LeftHand}/menuButton");
        pauseAction.AddBinding("<Keyboard>/escape");
        pauseAction.performed += OnPausePressed;
    }

    void OnEnable() => pauseAction.Enable();
    void OnDisable() => pauseAction.Disable();
    void OnDestroy() => pauseAction.Dispose();

    void Start()
    {
        rays = FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        HideAllPanels();
    }

    void OnPausePressed(InputAction.CallbackContext ctx)
    {
        if (GameManager.instance.State == GameManager.GameState.Paused)
            Resume();
        else if (GameManager.instance.State == GameManager.GameState.Playing)
            Pause();
    }

    // --- Pause ---
    public void Pause()
    {
        GameManager.instance.SetState(GameManager.GameState.Paused);
        ShowPanel(pauseMenuScreen);
    }

    public void Resume()
    {
        GameManager.instance.SetState(GameManager.GameState.Playing);
        HideAllPanels();
    }

    // --- Level Complete ---
    public void HandleLevelCompleteUI()
    {
        GameManager.instance.SetState(GameManager.GameState.GameOver);
        levelCompleteTimer.text = "Time : " + FormatTime(GameManager.instance.finalTime);
        // Generated-levels mode rebuilds the same scene in place, so there is no "next build index" -
        // show the button whenever a LevelProgressManager is driving the loop, scene-chaining otherwise.
        bool hasNextScene = SceneManager.GetActiveScene().buildIndex + 1 < SceneManager.sceneCountInBuildSettings;
        bool hasGeneratedProgress = GeneratedLevels.LevelProgressManager.instance != null;
        nextLevelButton.SetActive(hasNextScene || hasGeneratedProgress);
        ShowPanel(levelCompleteScreen);
    }

    // --- Game Over ---
    public void HandleGameOverUI()
    {
        GameManager.instance.SetState(GameManager.GameState.GameOver);
        ShowPanel(gameOverPanel);
    }

    public string FormatTime(float elapsedTime)
    {
        int minutes = (int)(elapsedTime / 60);
        int seconds = (int)(elapsedTime % 60);
        int milliseconds = (int)((elapsedTime * 100) % 100);
        return string.Format("{0:00}:{1:00}.{2:00}", minutes, seconds, milliseconds);
    }

    // --- Navigation ---
    public void BacktoMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuScene);
    }

    public void ReloadScene()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void NextLevel()
    {
        Time.timeScale = 1f;
        int next = SceneManager.GetActiveScene().buildIndex + 1;
        if (next < SceneManager.sceneCountInBuildSettings)
            SceneManager.LoadScene(next);
        else
            BacktoMenu();
    }

    // --- Helpers ---
    // Opens one panel, hides the others and the HUD, and puts the menu in front of the player.
    void ShowPanel(GameObject panel)
    {
        HideAllPanels();
        menuRoot.SetActive(true);
        panel.SetActive(true);
        if (gameUIPanel != null) gameUIPanel.SetActive(false);
        SetRays(true);
        PlaceInFrontOfPlayer();
    }

    void HideAllPanels()
    {
        pauseMenuScreen.SetActive(false);
        gameOverPanel.SetActive(false);
        levelCompleteScreen.SetActive(false);
        menuRoot.SetActive(false);
        if (gameUIPanel != null) gameUIPanel.SetActive(true);
        SetRays(false);
    }

    void SetRays(bool on)
    {
        if (rays == null) return;
        foreach (var ray in rays)
            if (ray != null) ray.gameObject.SetActive(on);
    }

    // Level with the horizon, facing the player, ignoring head tilt.
    void PlaceInFrontOfPlayer()
    {
        if (head == null && Camera.main != null) head = Camera.main.transform;
        if (head == null) return;

        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        Transform t = menuRoot.transform;
        t.position = head.position + forward * menuDistance;
        t.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }
}
