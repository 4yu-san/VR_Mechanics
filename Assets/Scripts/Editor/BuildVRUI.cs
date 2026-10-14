using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.UI;

// Tools > VR UI > ... builds the world-space menus as a reusable prefab
// (Assets/prefabs/VR Menus.prefab) and wires them into the scenes.
// Re-running is safe: the prefab is rebuilt and scene objects are reused.
public static class BuildVRUI
{
    const string PrefabPath = "Assets/prefabs/VR Menus.prefab";
    const string LevelScenePath = "Assets/Scenes/New Grapple.unity";
    const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
    const string XRIActionsGuid = "c348712bda248c246b8c49b3db54643f";
    const string PhysicsRigPrefab = "Assets/prefabs/Physics Rig.prefab";
    const string SimulatorPrefab = "Assets/Samples/XR Interaction Toolkit/3.5.1/XR Interaction Simulator/XR Interaction Simulator.prefab";

    // ---------------------------------------------------------------- menu items

    [MenuItem("Tools/VR UI/1 - Build Menus Prefab")]
    static void BuildPrefabMenu() => BuildPrefab();

    [MenuItem("Tools/VR UI/2 - Setup Level Scene (New Grapple)")]
    static void SetupLevel()
    {
        var prefab = BuildPrefab();
        var scene = EditorSceneManager.OpenScene(LevelScenePath, OpenSceneMode.Single);

        // Menus
        var menus = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var ui = menus.GetComponent<UIManager>();

        // HUD = the existing canvas under the Main Camera
        var cam = Camera.main;
        var hudCanvas = cam.GetComponentInChildren<Canvas>(true);
        ui.gameUIPanel = hudCanvas.gameObject;
        EnsureStopwatchText(hudCanvas);

        // Input: XR UI module + rays on the hands
        SetupEventSystem();
        var actions = LoadXRIActions();
        AddRay(GameObject.Find("Left Hand"), "Left", actions);
        AddRay(GameObject.Find("Right Hand"), "Right", actions);

        // Level complete screen needs the stopwatch texts
        WireStopwatch(ui);

        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("VR UI: New Grapple scene set up.");
    }

    [MenuItem("Tools/VR UI/3 - Create Main Menu Scene")]
    static void CreateMainMenu()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Remove the default camera, the player rig brings its own
        foreach (var c in Object.FindObjectsByType<Camera>())
            Object.DestroyImmediate(c.gameObject);

        // Same player (hands, camera, UI rays) as the level, with the gameplay parts switched off
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PhysicsRigPrefab));
        rig.GetComponent<ContinuousMovementPhysics>().enabled = false;
        foreach (var path in new[] { "XR Origin (VR)/Camera Offset/Main Camera/Canvas", // HUD
                                     "XR Origin (VR)/Camera Offset/Left Hand/Grapple",
                                     "XR Origin (VR)/Camera Offset/Right Hand/Grapple (1)",
                                     "Locomotion/Move", "Locomotion/Turn", "Locomotion/Jump" })
            rig.transform.Find(path).gameObject.SetActive(false);

        // Something to stand on (same size and height as the level ground)
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.position = new Vector3(0f, -0.78f, 0f);
        floor.transform.localScale = new Vector3(10f, 0.09f, 10f);

        // Lets you test with the keyboard and mouse in the editor, same as the level scene
        PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SimulatorPrefab));

        SetupEventSystem();
        BuildMainMenuCanvas(rig.transform.Find("XR Origin (VR)"));

        EditorSceneManager.SaveScene(scene, MenuScenePath);
        AddToBuildSettings(MenuScenePath, 0);
        AddToBuildSettings(LevelScenePath, 1);
        Debug.Log("VR UI: MainMenu scene created and added to Build Settings.");
    }

    // The whole main menu: a title, Start and Quit. Nothing else.
    static void BuildMainMenuCanvas(Transform player)
    {
        var go = new GameObject("Main Menu", typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster), typeof(Image));
        var menu = go.AddComponent<MainMenu>();
        go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        go.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.12f, 0.92f);

        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(600, 400);
        rt.localScale = Vector3.one * 0.002f;
        // Roughly eye height, 2 m in front of the way the player faces, turned to face them
        Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;
        rt.position = new Vector3(player.position.x, 0.9f, player.position.z) + forward * 2f;
        rt.rotation = Quaternion.LookRotation(forward, Vector3.up);

        MakeText(go.transform, "Title", "VR GRAPPLE", 56, new Vector2(0, 130), new Vector2(560, 80));
        UnityEventTools.AddPersistentListener(MakeButton(go.transform, "Start Game", new Vector2(0, 20)).onClick, menu.StartGame);
        UnityEventTools.AddPersistentListener(MakeButton(go.transform, "Quit", new Vector2(0, -80)).onClick, menu.QuitGame);
    }

    // ------------------------------------------------------------------- prefab

    static GameObject BuildPrefab()
    {
        var root = new GameObject("VR Menus");
        var ui = root.AddComponent<UIManager>();

        // World-space canvas: 600x500 units * 0.002 = 1.2m x 1.0m
        var canvasGO = new GameObject("Menu Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
        canvasGO.transform.SetParent(root.transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = null; // XR UI module supplies the camera
        var rt = canvasGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(600, 500);
        rt.localScale = Vector3.one * 0.002f;

        ui.menuRoot = canvasGO;
        ui.pauseMenuScreen = BuildPanel(canvasGO, "Pause Panel", "PAUSED",
            ("Resume", ui.Resume), ("Restart", ui.ReloadScene), ("Main Menu", ui.BacktoMenu));
        ui.gameOverPanel = BuildPanel(canvasGO, "Game Over Panel", "GAME OVER",
            ("Restart", ui.ReloadScene), ("Main Menu", ui.BacktoMenu));
        ui.levelCompleteScreen = BuildPanel(canvasGO, "Level Complete Panel", "LEVEL COMPLETE",
            ("Next Level", ui.NextLevel), ("Restart", ui.ReloadScene), ("Main Menu", ui.BacktoMenu));

        // Level complete extras
        var lc = ui.levelCompleteScreen.transform;
        ui.nextLevelButton = lc.Find("Next Level Button").gameObject;
        ui.levelCompleteTimer = MakeText(lc, "Time Text", "Time : 00:00.00", 36, new Vector2(0, 90), new Vector2(500, 60));

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        return prefab;
    }

    // A panel = dark background, a title, and a vertical stack of buttons.
    static GameObject BuildPanel(GameObject canvas, string name, string title, params (string label, UnityEngine.Events.UnityAction action)[] buttons)
    {
        var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvas.transform, false);
        var rt = (RectTransform)panel.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.12f, 0.92f);

        MakeText(panel.transform, "Title", title, 56, new Vector2(0, 190), new Vector2(560, 80));

        float y = 30f;
        foreach (var (label, action) in buttons)
        {
            var b = MakeButton(panel.transform, label, new Vector2(0, y));
            UnityEventTools.AddPersistentListener(b.onClick, action);
            y -= 100f;
        }
        return panel;
    }

    static TextMeshProUGUI MakeText(Transform parent, string name, string text, float size, Vector2 pos, Vector2 dims)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = pos;
        rt.sizeDelta = dims;
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;
        return t;
    }

    static Button MakeButton(Transform parent, string label, Vector2 pos)
    {
        var go = new GameObject(label + " Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(360, 80);
        go.GetComponent<Image>().color = new Color(0.2f, 0.45f, 0.9f, 1f);
        MakeText(go.transform, "Label", label, 36, Vector2.zero, rt.sizeDelta);
        return go.GetComponent<Button>();
    }

    // -------------------------------------------------------------- scene setup

    // Swap the desktop input module for the XR one so ray interactors can click UI.
    static void SetupEventSystem()
    {
        var es = Object.FindFirstObjectByType<EventSystem>();
        if (es == null) es = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();

        var old = es.GetComponent<InputSystemUIInputModule>();
        if (old != null) Object.DestroyImmediate(old);
        if (es.GetComponent<XRUIInputModule>() == null) es.gameObject.AddComponent<XRUIInputModule>();
    }

    static InputActionAsset LoadXRIActions()
    {
        return AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetDatabase.GUIDToAssetPath(XRIActionsGuid));
    }

    // UI-only ray on a hand. Starts disabled; UIManager turns it on while a menu is open.
    static void AddRay(GameObject hand, string side, InputActionAsset actions)
    {
        var existing = hand.transform.Find("UI Ray");
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        var go = new GameObject("UI Ray", typeof(LineRenderer));
        go.transform.SetParent(hand.transform, false);

        var ray = go.AddComponent<XRRayInteractor>();
        go.AddComponent<XRInteractorLineVisual>();
        ray.enableUIInteraction = true;
        ray.maxRaycastDistance = 5f;
        ray.allowSelect = false; // UI clicks only, never grabs game objects

        // Bind UI Press (trigger) from the XRI default actions
        var press = actions.FindAction($"XRI {side} Interaction/UI Press", true);
        var so = new SerializedObject(ray);
        var reader = so.FindProperty("m_UIPressInput");
        reader.FindPropertyRelative("m_InputSourceMode").enumValueIndex = 1; // InputActionReference
        reader.FindPropertyRelative("m_InputActionReferencePerformed").objectReferenceValue = GetReference(press);
        reader.FindPropertyRelative("m_InputActionReferenceValue").objectReferenceValue = GetReference(press);
        so.ApplyModifiedPropertiesWithoutUndo();

        go.SetActive(false);
    }

    static InputActionReference GetReference(InputAction action)
    {
        var path = AssetDatabase.GetAssetPath(action.actionMap.asset);
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is InputActionReference r && r.action == action) return r;
        return null;
    }

    // HUD needs a text for the stopwatch; the end screen needs best-time text.
    static void EnsureStopwatchText(Canvas hud)
    {
        var sw = Object.FindFirstObjectByType<Stopwatch>();
        if (sw == null || sw.StopwatchText != null) return;

        var t = MakeText(hud.transform, "Stopwatch Text", "00:00.00", 36, new Vector2(0, 200), new Vector2(400, 60));
        sw.StopwatchText = t;
        EditorUtility.SetDirty(sw);
    }

    static void WireStopwatch(UIManager ui)
    {
        var sw = Object.FindFirstObjectByType<Stopwatch>();
        var gm = Object.FindFirstObjectByType<GameManager>();
        if (sw == null || gm == null) { Debug.LogWarning("VR UI: Stopwatch or GameManager not found in scene."); return; }

        var lc = ui.levelCompleteScreen.transform;
        sw.EndTimerText = ui.levelCompleteTimer;
        sw.bestTimeText = MakeText(lc, "Best Time Text", "Best Time: --", 30, new Vector2(0, 40), new Vector2(500, 50));
        gm.stopwatch = sw;
        EditorUtility.SetDirty(sw);
        EditorUtility.SetDirty(gm);
    }

    static void AddToBuildSettings(string path, int index)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == path);
        scenes.Insert(Mathf.Min(index, scenes.Count), new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
