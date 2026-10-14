using System.Threading;
using UnityEditor;
using UnityEngine;

// Tools > Levels > Generate Level With Ollama...
public class LevelGeneratorWindow : EditorWindow
{
    string baseUrl = "http://localhost:11434";
    string model = "qwen2.5:14b";
    string levelName = "Generated Level 01";
    string request = "";
    int maxAttempts = 4;
    bool bake = true;

    CancellationTokenSource cancel;
    Vector2 scroll;

    [MenuItem("Tools/Levels/Generate Level With Ollama...")]
    static void Open() => GetWindow<LevelGeneratorWindow>("Level Generator");

    void OnGUI()
    {
        EditorGUILayout.LabelField("Local LLM (Ollama)", EditorStyles.boldLabel);
        baseUrl = EditorGUILayout.TextField("Server", baseUrl);
        model = EditorGUILayout.TextField("Model", model);
        maxAttempts = EditorGUILayout.IntSlider("Max attempts", maxAttempts, 1, 8);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Level", EditorStyles.boldLabel);
        levelName = EditorGUILayout.TextField("Name", levelName);
        EditorGUILayout.LabelField("What should it be like? (optional)");
        request = EditorGUILayout.TextArea(request, GUILayout.Height(60));
        bake = EditorGUILayout.Toggle("Bake into a scene", bake);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(LevelTools.Generating))
            if (GUILayout.Button("Generate", GUILayout.Height(28))) Generate();
        if (LevelTools.Generating && GUILayout.Button("Cancel")) cancel?.Cancel();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.HelpBox(string.IsNullOrEmpty(LevelTools.Status) ? "Idle." : LevelTools.Status, MessageType.None);
        EditorGUILayout.EndScrollView();
    }

    async void Generate()
    {
        cancel = new CancellationTokenSource();
        await LevelTools.GenerateAndSave(levelName, request, bake, baseUrl, model, maxAttempts, cancel.Token);
        Repaint();
    }

    void OnInspectorUpdate()
    {
        if (LevelTools.Generating) Repaint();
    }
}
