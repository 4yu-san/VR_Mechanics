using System.Collections.Generic;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Levels > ... : export a scene to JSON, validate a JSON level, bake a JSON level into a scene.
public static class LevelTools
{
    const string LevelsFolder = "Assets/Levels";
    const string BakedScenesFolder = "Assets/Scenes/Levels";
    const string TemplateScene = "Assets/Scenes/New Grapple.unity";
    const string PlatformPrefab = "Assets/prefabs/Cube (7).prefab";

    // ---------------------------------------------------------------- menu items

    [MenuItem("Tools/Levels/Export Open Scene To JSON")]
    static void ExportMenu() => Debug.Log("Level exported to " + ExportOpenScene());

    [MenuItem("Tools/Levels/Validate JSON Level...")]
    static void ValidateMenu()
    {
        var path = EditorUtility.OpenFilePanel("Validate level", LevelsFolder, "json");
        if (!string.IsNullOrEmpty(path)) Debug.Log(Path.GetFileName(path) + ": " + Validate(path));
    }

    [MenuItem("Tools/Levels/Bake JSON Level To Scene...")]
    static void BakeMenu()
    {
        var path = EditorUtility.OpenFilePanel("Bake level", LevelsFolder, "json");
        if (!string.IsNullOrEmpty(path)) Debug.Log("Baked scene: " + Bake(path));
    }

    // ------------------------------------------------------------------ actions

    // Writes the open scene's platforms and Level End to Assets/Levels/<scene name>.json
    public static string ExportOpenScene()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlatformPrefab);
        var level = new LevelData { name = EditorSceneManager.GetActiveScene().name };

        var platforms = new List<PlatformData>();
        foreach (var t in FindPlatforms(prefab))
        {
            platforms.Add(new PlatformData
            {
                position = LevelData.ToArray(t.position),
                yaw = Mathf.Round(t.eulerAngles.y * 10f) / 10f,
                size = IsDefaultSize(t.localScale) ? null : LevelData.ToArray(t.localScale),
            });
        }
        level.platforms = platforms.ToArray();
        level.end = LevelData.ToArray(GameObject.Find("Level End").transform.position);

        Directory.CreateDirectory(LevelsFolder);
        var path = $"{LevelsFolder}/{level.name}.json";
        File.WriteAllText(path, level.ToJson());
        AssetDatabase.Refresh();
        return path;
    }

    // Returns the validation report for a JSON file
    public static LevelValidator.Result Validate(string jsonPath)
    {
        return LevelValidator.Validate(LevelData.FromJson(File.ReadAllText(jsonPath)));
    }

    // Duplicates New Grapple and only moves the existing Cube (7) platforms and the Level End to the
    // level's coordinates. Nothing else in the scene is touched, and nothing is added or removed.
    // Returns the new scene's path, or null if the level was rejected.
    public static string Bake(string jsonPath)
    {
        var level = LevelData.FromJson(File.ReadAllText(jsonPath));
        level?.Normalize();
        var result = LevelValidator.Validate(level);
        if (!result.Ok)
        {
            Debug.LogError("Not baked, level is invalid: " + result);
            return null;
        }

        string levelName = string.IsNullOrEmpty(level.name) ? Path.GetFileNameWithoutExtension(jsonPath) : level.name;
        Directory.CreateDirectory(BakedScenesFolder);
        string scenePath = $"{BakedScenesFolder}/{SafeFileName(levelName)}.unity";
        if (!AssetDatabase.CopyAsset(TemplateScene, scenePath))
        {
            Debug.LogError("Could not copy the template scene to " + scenePath);
            return null;
        }

        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var platforms = FindPlatforms(AssetDatabase.LoadAssetAtPath<GameObject>(PlatformPrefab));
        if (platforms.Count != level.platforms.Length)
        {
            Debug.LogError($"Not baked: the scene has {platforms.Count} Cube (7) platforms but the level has {level.platforms.Length}.");
            return null;
        }

        LevelBuilder.Apply(level, platforms, GameObject.Find("Level End").transform);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AddToBuildSettings(scenePath);
        if (result.warnings.Count > 0) Debug.LogWarning(levelName + ": " + result);
        return scenePath;
    }

    // ------------------------------------------------------------ LLM generation

    // Shown by the generator window (and readable from a script) while a generation runs
    public static bool Generating { get; private set; }
    public static string Status { get; private set; } = "";

    // Asks the local model for a level, saves it to Assets/Levels/<name>.json and optionally bakes it into a scene.
    // Must be called from the main thread.
    public static async Task<bool> GenerateAndSave(string levelName, string request, bool bake, string baseUrl, string model, int maxAttempts, CancellationToken cancel = default)
    {
        if (Generating) return false;
        Generating = true;
        var log = new StringBuilder();
        void Progress(string line) { log.AppendLine(line); Status = log.ToString(); Debug.Log("[Level generator] " + line); }

        try
        {
            string examplePath = $"{LevelsFolder}/New Grapple.json";
            if (!File.Exists(examplePath))
            {
                // The example has to be the real New Grapple level
                if (EditorSceneManager.GetActiveScene().path != TemplateScene) EditorSceneManager.OpenScene(TemplateScene);
                ExportOpenScene();
            }

            var generator = new OllamaLevelGenerator { baseUrl = baseUrl, model = model, maxAttempts = maxAttempts };
            var result = await generator.GenerateAsync(levelName, request, File.ReadAllText(examplePath), Progress, cancel);

            if (!result.success)
            {
                Progress(result.error ?? "No valid level was produced. Try again, or a more specific request.");
                return false;
            }

            var path = $"{LevelsFolder}/{SafeFileName(levelName)}.json";
            Directory.CreateDirectory(LevelsFolder);
            File.WriteAllText(path, result.level.ToJson());
            AssetDatabase.Refresh();
            Progress("Saved " + path);

            if (bake)
            {
                var scene = Bake(path);
                Progress(scene != null ? "Baked scene " + scene : "Baking failed, see the console.");
                return scene != null;
            }
            return true;
        }
        finally
        {
            Generating = false;
        }
    }

    static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "Generated Level" : name.Trim();
    }

    // ------------------------------------------------------------------ helpers

    // Hand-placed platforms = children of Map Elements that are instances of the platform prefab
    static List<Transform> FindPlatforms(GameObject prefab)
    {
        var found = new List<Transform>();
        foreach (Transform child in GameObject.Find("Map Elements").transform)
            if (PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == prefab)
                found.Add(child);
        return found;
    }

    static bool IsDefaultSize(Vector3 s) => (s - LevelData.DefaultPlatformSize).sqrMagnitude < 0.0001f;

    // Generated levels go right after New Grapple (and the earlier generated ones), so "Next Level" chains them
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
}
