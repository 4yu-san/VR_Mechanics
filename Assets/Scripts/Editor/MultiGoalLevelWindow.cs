using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using MultiGoalLevels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Levels > Generate Multi-Goal Level...
//
// Duplicates New Grapple.unity and changes only what is inside "Map Elements": every Cube (7) and every
// Level End is removed, then new Cube (7) paths and Level Ends are placed from an LLM plan (see
// MultiGoalLayout.cs). Start Platform and DeathFloor are kept and used as the reference for the layout.
// The template scene itself is never modified.
public class MultiGoalLevelWindow : EditorWindow
{
    const string TemplateScene = "Assets/Scenes/New Grapple.unity";
    const string BakedScenesFolder = "Assets/Scenes/Levels";
    const string PlansFolder = "Assets/Levels/MultiGoal";
    const string CubePrefabPath = "Assets/prefabs/Cube (7).prefab";
    const string LevelEndPrefabPath = "Assets/prefabs/Level End.prefab";
    const string MapElementsName = "Map Elements";
    const string StartPlatformName = "Start Platform";
    const string DeathFloorName = "DeathFloor";

    // LLM
    string baseUrl = "http://localhost:11434";
    string model = "gemma2:9b-instruct-q4_K_M";
    int maxAttempts = 4;
    bool useLLM = true;
    bool fallbackToProcedural = true;

    // Level
    string levelName = "Multi Goal 01";
    string request = "";
    int goalCount = 3;
    float goalDistance = 45f;
    float goalHeight = 0f;
    float arcDegrees = 120f;
    float minSpacing = 2.5f, maxSpacing = 4.5f;
    float minWidth = 6f, maxWidth = 18f;
    float density = 1.5f;
    float grappleRange = 25f;
    float rigHeight = 0.86f;
    float floorClearanceMultiplier = 2f;
    int minRoutes = 2, maxRoutes = 3;
    float minRouteSeparation = 10f;
    float heightVariation = 8f;
    float verticalSpread = 5f;
    float heightJitter = 1.5f;
    float maxDrop = 10f;
    float endGap = 0.3f;
    float maxStep = 15f;
    int seed = 0;

    static bool generating;
    static string status = "";
    CancellationTokenSource cancel;
    Vector2 scroll;

    [MenuItem("Tools/Levels/Generate Multi-Goal Level...")]
    static void Open() => GetWindow<MultiGoalLevelWindow>("Multi-Goal Level");

    void OnGUI()
    {
        EditorGUILayout.LabelField("Local LLM (Ollama)", EditorStyles.boldLabel);
        useLLM = EditorGUILayout.Toggle(new GUIContent("Use LLM", "Off = procedural planner only (no Ollama needed)"), useLLM);
        using (new EditorGUI.DisabledScope(!useLLM))
        {
            baseUrl = EditorGUILayout.TextField("Server", baseUrl);
            model = EditorGUILayout.TextField("Model", model);
            maxAttempts = EditorGUILayout.IntSlider("Max attempts", maxAttempts, 1, 8);
            fallbackToProcedural = EditorGUILayout.Toggle(new GUIContent("Procedural fallback", "Use the procedural planner if the model never produces a valid level"), fallbackToProcedural);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Level", EditorStyles.boldLabel);
        levelName = EditorGUILayout.TextField("Name", levelName);
        EditorGUILayout.LabelField("What should it be like? (optional)");
        request = EditorGUILayout.TextArea(request, GUILayout.Height(50));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Layout", EditorStyles.boldLabel);
        goalCount = EditorGUILayout.IntSlider(new GUIContent("Level Ends", "Number of goals, each with its own path"), goalCount, 1, 6);
        goalDistance = EditorGUILayout.Slider(new GUIContent("Goal distance (m)", "Every Level End is exactly this far from the Start Platform's centre"), goalDistance, 15f, 120f);
        goalHeight = EditorGUILayout.Slider(new GUIContent("Goal height (m)", "Level End height above the Start Platform's top"), goalHeight, -5f, 20f);
        arcDegrees = EditorGUILayout.Slider(new GUIContent("Spread (degrees)", "How widely the goals fan out around -x"), arcDegrees, 0f, 300f);
        EditorGUILayout.MinMaxSlider(new GUIContent($"Spacing {minSpacing:0.#}-{maxSpacing:0.#} m", "Distance between neighbouring cubes (the LLM picks within it). Smaller = denser."), ref minSpacing, ref maxSpacing, 2f, 8f);
        EditorGUILayout.MinMaxSlider(new GUIContent($"Width {minWidth:0}-{maxWidth:0} m", "How wide each corridor of the field can be (the LLM picks within it)"), ref minWidth, ref maxWidth, 2f, 30f);
        density = EditorGUILayout.Slider(new GUIContent("Density", "How full each corridor gets. 1 = loose, 2 = as packed as the spacing allows."), density, 0.5f, 2.5f);
        minRoutes = EditorGUILayout.IntSlider(new GUIContent("Min routes per end", "Every Level End gets at least this many different routes"), minRoutes, 1, 4);
        maxRoutes = Mathf.Max(minRoutes, EditorGUILayout.IntSlider(new GUIContent("Max routes per end"), maxRoutes, 1, 5));
        minRouteSeparation = EditorGUILayout.Slider(new GUIContent("Route separation (m)", "Routes to the same Level End must be at least this far apart somewhere"), minRouteSeparation, 3f, 30f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Height", EditorStyles.boldLabel);
        heightVariation = EditorGUILayout.Slider(new GUIContent("Height variation (m)", "How far routes may climb or dip (the LLM's \"up\" range). 0 = flat."), heightVariation, 0f, 20f);
        verticalSpread = EditorGUILayout.Slider(new GUIContent("Layer thickness (m)", "Cubes in each corridor are spread over this many metres vertically"), verticalSpread, 0f, 12f);
        heightJitter = EditorGUILayout.Slider(new GUIContent("Height jitter (m)", "Random up/down offset per cube"), heightJitter, 0f, 4f);
        maxDrop = EditorGUILayout.Slider(new GUIContent("Max drop (m)", "Cubes never go more than this below the Start Platform's top"), maxDrop, 0f, 30f);
        maxStep = EditorGUILayout.Slider(new GUIContent("Max hop (m)", "The Level Ends must be reachable from the Start Platform with no hop longer than this"), maxStep, 5f, 25f);
        endGap = EditorGUILayout.Slider(new GUIContent("Level End gap (m)", "Space between the top of each Level End and the Cube (7) above it"), endGap, 0f, 3f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Death floor", EditorStyles.boldLabel);
        grappleRange = EditorGUILayout.FloatField(new GUIContent("Grapple range (m)", "Grappling.cs maxDistance"), grappleRange);
        rigHeight = EditorGUILayout.FloatField(new GUIContent("Physics Rig height (m)"), rigHeight);
        floorClearanceMultiplier = EditorGUILayout.Slider(new GUIContent("Clearance multiplier", "DeathFloor is moved this many times (grapple range + rig height) below the lowest element"), floorClearanceMultiplier, 0.5f, 4f);
        EditorGUILayout.LabelField(" ", $"DeathFloor goes {floorClearanceMultiplier * (grappleRange + rigHeight):0.##} m below the lowest element");

        maxSpacing = Mathf.Min(maxSpacing, maxStep / 1.85f);
        minSpacing = Mathf.Min(minSpacing, maxSpacing);
        seed = EditorGUILayout.IntField(new GUIContent("Seed", "Procedural planner seed, 0 = random"), seed);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(generating))
            if (GUILayout.Button("Generate and Bake", GUILayout.Height(28))) Generate();
        if (generating && GUILayout.Button("Cancel")) cancel?.Cancel();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.HelpBox(string.IsNullOrEmpty(status) ? "Idle." : status, MessageType.None);
        EditorGUILayout.EndScrollView();
    }

    void OnInspectorUpdate()
    {
        if (generating) Repaint();
    }

    LayoutSettings Settings() => new LayoutSettings
    {
        goalCount = goalCount,
        goalDistance = goalDistance,
        goalHeight = goalHeight,
        arcDegrees = arcDegrees,
        minSpacing = minSpacing,
        maxSpacing = maxSpacing,
        minWidth = minWidth,
        maxWidth = maxWidth,
        density = density,
        grappleRange = grappleRange,
        rigHeight = rigHeight,
        floorClearanceMultiplier = floorClearanceMultiplier,
        minRoutes = minRoutes,
        maxRoutes = maxRoutes,
        minRouteSeparation = minRouteSeparation,
        heightVariation = heightVariation,
        verticalSpread = verticalSpread,
        heightJitter = heightJitter,
        maxDrop = maxDrop,
        endGap = endGap,
        maxStep = maxStep,
    };

    async void Generate()
    {
        if (generating) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        generating = true;
        cancel = new CancellationTokenSource();
        var log = new StringBuilder();
        void Progress(string line) { log.AppendLine(line); status = log.ToString(); Debug.Log("[Multi-goal generator] " + line); Repaint(); }

        try
        {
            var cubePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CubePrefabPath);
            var endPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LevelEndPrefabPath);
            if (cubePrefab == null || endPrefab == null)
            {
                Progress($"Missing prefab: {(cubePrefab == null ? CubePrefabPath : LevelEndPrefabPath)}");
                return;
            }

            // Read the reference geometry from the (unmodified) template
            if (EditorSceneManager.GetActiveScene().path != TemplateScene)
                EditorSceneManager.OpenScene(TemplateScene, OpenSceneMode.Single);
            var scene = ReadSceneInfo(cubePrefab, endPrefab, out string sceneError);
            if (scene == null) { Progress(sceneError); return; }
            Progress($"Start Platform at {scene.startPlatform.center}, death floor top at y = {scene.floorTop:F1}, cube size {scene.cubeSize}.");

            var generator = new MultiGoalGenerator
            {
                baseUrl = baseUrl, model = model, maxAttempts = maxAttempts,
                useLLM = useLLM, fallbackToProcedural = fallbackToProcedural, seed = seed,
            };
            var result = await generator.GenerateAsync(request, Settings(), scene, Progress, cancel.Token);
            if (!result.success)
            {
                Progress(result.error ?? "No valid level was produced. Try again, change the request, or enable the procedural fallback.");
                return;
            }

            result.plan.name = levelName;
            string scenePath = Bake(levelName, result.layout, cubePrefab, endPrefab, out string bakeError);
            if (scenePath == null) { Progress(bakeError); return; }

            Directory.CreateDirectory(PlansFolder);
            string planPath = $"{PlansFolder}/{SafeFileName(levelName)}.json";
            File.WriteAllText(planPath, result.plan.ToJson());
            AssetDatabase.Refresh();

            Progress($"Baked {scenePath}: {result.layout.cubes.Count} platforms, {result.layout.goals.Count} Level Ends, " +
                     $"each {goalDistance:0.#} m from the start{(result.usedFallback ? " (procedural fallback)" : "")}. " +
                     $"Lowest element at y = {result.layout.lowestPoint:F1}, DeathFloor moved to y = {result.layout.deathFloorTop:F1}. Plan saved to {planPath}.");
        }
        finally
        {
            generating = false;
            Repaint();
        }
    }

    // ------------------------------------------------------------------ scene

    static SceneInfo ReadSceneInfo(GameObject cubePrefab, GameObject endPrefab, out string error)
    {
        error = null;
        var map = GameObject.Find(MapElementsName);
        if (map == null) { error = $"No \"{MapElementsName}\" object in {TemplateScene}."; return null; }
        var start = map.transform.Find(StartPlatformName);
        if (start == null) { error = $"No \"{StartPlatformName}\" inside \"{MapElementsName}\"."; return null; }
        var floor = map.transform.Find(DeathFloorName);

        if (!TryGetBounds(start.gameObject, out var startBounds)) { error = "Start Platform has no Renderer or Collider to measure."; return null; }
        float floorTop = floor != null && TryGetBounds(floor.gameObject, out var floorBounds) ? floorBounds.max.y : startBounds.min.y - 20f;

        // Cube (7) is a unit BoxCollider scaled by its transform
        var box = cubePrefab.GetComponent<BoxCollider>();
        Vector3 cubeSize = Vector3.Scale(box != null ? box.size : Vector3.one, cubePrefab.transform.localScale);

        var info = new SceneInfo { startPlatform = startBounds, floorTop = floorTop, cubeSize = cubeSize };

        // Level End's trigger is a vertical CapsuleCollider scaled by its transform: measure it, so the landing
        // cube sits just above its top
        var capsule = endPrefab.GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            Vector3 scale = endPrefab.transform.localScale;
            info.endTop = (capsule.center.y + capsule.height * 0.5f) * scale.y;
            info.endBottom = (capsule.center.y - capsule.height * 0.5f) * scale.y;
            info.endRadius = capsule.radius * Mathf.Max(scale.x, scale.z);
        }
        return info;
    }

    static bool TryGetBounds(GameObject go, out Bounds bounds)
    {
        var renderer = go.GetComponentInChildren<Renderer>();
        if (renderer != null) { bounds = renderer.bounds; return true; }
        var collider = go.GetComponentInChildren<Collider>();
        if (collider != null) { bounds = collider.bounds; return true; }
        bounds = default;
        return false;
    }

    // Copies the template and swaps the contents of Map Elements. Returns the new scene's path, or null.
    static string Bake(string levelName, Layout layout, GameObject cubePrefab, GameObject endPrefab, out string error)
    {
        error = null;
        Directory.CreateDirectory(BakedScenesFolder);
        AssetDatabase.Refresh();
        string scenePath = AssetDatabase.GenerateUniqueAssetPath($"{BakedScenesFolder}/{SafeFileName(levelName)}.unity");
        if (!AssetDatabase.CopyAsset(TemplateScene, scenePath)) { error = "Could not copy the template scene to " + scenePath; return null; }

        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var map = GameObject.Find(MapElementsName).transform;

        // Remove every Cube (7) inside Map Elements, and every Level End (New Grapple keeps one at the scene root)
        var remove = new List<GameObject>();
        foreach (Transform child in map)
            if (IsInstanceOf(child.gameObject, cubePrefab) || IsInstanceOf(child.gameObject, endPrefab))
                remove.Add(child.gameObject);
        foreach (var root in scene.GetRootGameObjects())
            if (IsInstanceOf(root, endPrefab))
                remove.Add(root);
        foreach (var go in remove)
            Object.DestroyImmediate(go);

        const string letters = "ABCDEFGHIJ";
        // Names: "Cube (7) B2-07" = goal B, its 2nd route, 7th cube; "Cube (7) B landing" sits on Level End B
        var routeNumber = new Dictionary<int, int>();   // plan route index -> route number within its goal
        var routesPerGoal = new int[layout.goals.Count];
        var cubesPerRoute = new Dictionary<int, int>();
        foreach (var c in layout.cubes)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab, scene);
            go.transform.SetParent(map, true);
            // yaw 0 keeps the prefab's own rotation, so every cube faces the same way
            go.transform.SetPositionAndRotation(c.position, cubePrefab.transform.rotation * Quaternion.Euler(0f, c.yaw, 0f));
            if (c.landing)
            {
                go.name = $"Cube (7) {letters[c.goal]} landing";
                continue;
            }
            if (!routeNumber.ContainsKey(c.route)) routeNumber[c.route] = ++routesPerGoal[c.goal];
            cubesPerRoute[c.route] = cubesPerRoute.TryGetValue(c.route, out int n) ? n + 1 : 1;
            go.name = $"Cube (7) {letters[c.goal]}{routeNumber[c.route]}-{cubesPerRoute[c.route]:00}";
        }
        for (int i = 0; i < layout.goals.Count; i++)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(endPrefab, scene);
            go.transform.SetParent(map, true);
            go.transform.SetPositionAndRotation(layout.goals[i], Quaternion.identity);
            go.name = $"Level End {letters[i]}";
        }

        // Drop DeathFloor (it lives in Map Elements) so its top is the required clearance below the level
        var floor = map.Find(DeathFloorName);
        if (floor != null && TryGetBounds(floor.gameObject, out var floorBounds))
            floor.position += Vector3.up * (layout.deathFloorTop - floorBounds.max.y);
        else
            Debug.LogWarning($"[Multi-goal generator] No \"{DeathFloorName}\" in {MapElementsName}, the death floor was not moved.");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AddToBuildSettings(scenePath);
        return scenePath;
    }

    static bool IsInstanceOf(GameObject go, GameObject prefab) =>
        PrefabUtility.IsAnyPrefabInstanceRoot(go) && PrefabUtility.GetCorrespondingObjectFromSource(go) == prefab;

    // Baked levels go right after New Grapple (and earlier baked ones), so "Next Level" chains them
    static void AddToBuildSettings(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == path);
        int insertAt = scenes.Count;
        for (int i = scenes.Count - 1; i >= 0; i--)
            if (scenes[i].path == TemplateScene || scenes[i].path.StartsWith(BakedScenesFolder))
            {
                insertAt = i + 1;
                break;
            }
        scenes.Insert(insertAt, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "Multi Goal Level" : name.Trim();
    }
}
