using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Main menu buttons. Point at a button with the hand ray and pull the trigger.
public class MainMenu : MonoBehaviour
{
    public string firstLevelScene = "New Grapple";

    void Start()
    {
        Time.timeScale = 1f;

        // The hands carry hidden UI rays (used by the in-game menus), so turn them on here.
        foreach (var ray in FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Include))
            ray.gameObject.SetActive(true);
    }

    public void StartGame() => SceneManager.LoadScene("New Grapple");

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
