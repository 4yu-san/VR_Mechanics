using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// Asks a local Ollama model to design a level, checks it with LevelValidator and, if it is invalid,
// sends the problems back to the model and asks again. Nothing here is editor-only, so the same code can
// later talk to a model on your PC from the Quest over Wi-Fi (set baseUrl to the PC's address).
//
// Call from the main thread: the await continuations resume on Unity's main thread.
public class OllamaLevelGenerator
{
    public string baseUrl = "http://localhost:11434";
    public string model = "qwen2.5:14b";
    public int maxAttempts = 4;
    public float temperature = 0.8f;
    public int contextSize = 8192;
    public int timeoutSeconds = 300;

    public class Result
    {
        public bool success;
        public LevelData level;
        public int attempts;
        public string lastReply;
        public List<string> log = new List<string>();
        public string error;    // set when it could not talk to the model at all
    }

    // exampleJson: a known-good level (New Grapple.json) shown to the model as a pattern to follow
    public async Task<Result> GenerateAsync(string levelName, string request, string exampleJson, Action<string> progress = null, CancellationToken cancel = default)
    {
        var result = new Result();
        void Log(string s) { result.log.Add(s); progress?.Invoke(s); }

        var example = LevelData.FromJson(exampleJson);
        var messages = new List<ChatMessage>
        {
            new ChatMessage("system", BuildSystemPrompt(exampleJson)),
            new ChatMessage("user", $"Design a new level.\nRequest: {(string.IsNullOrWhiteSpace(request) ? "a fun, varied level" : request)}\n" +
                                    "Make it clearly different from the example. Reply with the JSON object only."),
        };

        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) })
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                result.attempts = attempt;
                Log($"Attempt {attempt}/{maxAttempts}: asking {model}...");

                string reply;
                try { reply = await Chat(http, messages, cancel); }
                catch (Exception e)
                {
                    result.error = e is OperationCanceledException ? "Cancelled." : $"Could not get an answer from Ollama at {baseUrl}: {e.Message}";
                    Log(result.error);
                    return result;
                }
                result.lastReply = reply;

                var level = LevelData.FromJson(reply);
                level?.Normalize();
                var check = LevelValidator.Validate(level);
                if (check.Ok)
                {
                    int copies = CountCopies(level, example);
                    if (copies > MaxCopiesOfExample)
                        check.errors.Add($"{copies} of your platforms are copied from the example level. Place them somewhere new: this must be a different level, not a variation of the example.");
                }
                if (check.Ok)
                {
                    level.name = levelName;
                    result.level = level;
                    result.success = true;
                    Log($"Attempt {attempt}: valid level with {level.platforms.Length} platforms.");
                    return result;
                }

                Log($"Attempt {attempt}: invalid.\n{check}");
                messages.Add(new ChatMessage("assistant", reply));
                messages.Add(new ChatMessage("user", "That level is not valid:\n- " + string.Join("\n- ", check.errors) +
                                                     "\nFix these problems and reply with the complete corrected JSON object only."));
            }
        }

        Log($"Gave up after {maxAttempts} attempts.");
        return result;
    }

    // How many of the level's platforms sit (almost) exactly where one of the example's does
    const int MaxCopiesOfExample = 3;
    static int CountCopies(LevelData level, LevelData example)
    {
        if (example == null || example.platforms == null) return 0;
        int copies = 0;
        foreach (var p in level.platforms)
            foreach (var q in example.platforms)
                if ((p.Position - q.Position).sqrMagnitude < 1f) { copies++; break; }
        return copies;
    }

    // ---------------------------------------------------------------- prompt

    static string BuildSystemPrompt(string exampleJson)
    {
        return
"You design levels for a VR grappling-hook game. You reply with ONE JSON object and nothing else.\n" +
"\n" +
"THE GAME: the player stands on a flat start slab and must reach the goal by hopping from platform to platform with a grappling hook.\n" +
"\n" +
"COORDINATES (metres): x is the main direction, y is up, z is sideways. The level runs toward NEGATIVE x.\n" +
"- The start slab is flat, its top surface is at y = -0.75 and it covers x from -3.5 to 7.5, z from -4.5 to 6.9. The player spawns at [0, 0, 0].\n" +
"- Every platform is the same box (1.39 wide, 0.86 tall, 2.28 long) that cannot be rotated or resized. You only choose each platform's centre \"position\" [x, y, z]. Do not output \"size\" or \"yaw\".\n" +
"- \"end\" is the goal position [x, y, z], a trigger about 2.4 m wide and 5 m tall.\n" +
"\n" +
"RULES (the level is rejected if it breaks one):\n" +
"1. Use EXACTLY 24 platforms, no more and no fewer.\n" +
"2. The first platform must be near the start slab: its edge no more than 5 m from x = -3.5 (so around x = -7 to -10, y between 0 and 3).\n" +
"3. Each platform must be reachable: the gap between the edge of one platform and the edge of another, counting any horizontal and vertical distance, must be at most 5 m. A good step is 3 to 5 m. Every platform should be reachable from the previous ones.\n" +
"4. The goal \"end\" must be within 5 m of the last platform and reached by a chain of platforms from the start.\n" +
"5. Platforms must not overlap each other: keep centres at least 3 m apart horizontally, or at least 1.5 m apart vertically.\n" +
"6. Stay inside |x| <= 120, |z| <= 120 and y between 0 and 25. Nothing may be placed within 2 m of [0, 0, 0]. Never place anything at x > 8.\n" +
"7. The goal must be at x <= -25 (far toward negative x). The platforms must span at least 3 m in height and at least 6 m sideways (z), so the level is not flat and not a straight line.\n" +
"\n" +
"DESIGN: make it interesting and varied, like the example. Do NOT place platforms at even intervals or in a repeating pattern.\n" +
"- Vary the step between platforms (mostly 3 to 5 m, sometimes shorter) and mix climbing, dropping and sideways moves.\n" +
"- Use the sideways direction: spread platforms over z from about -12 to +14, and heights from y = 0.5 up to about 12.\n" +
"- Include a few features: a tower or stair section, a drop to a lower platform, a wide sideways gap, a small cluster of close platforms.\n" +
"- The platforms in the JSON list do not have to be in order, but every one must be part of a reachable chain.\n" +
"- Be creative: do not copy the example's coordinates.\n" +
"\n" +
"FORMAT: a JSON object with \"name\", \"platforms\" (array of {\"position\": [x, y, z]}) and \"end\" [x, y, z]. Here is a valid example of a level:\n" +
exampleJson;
    }

    // ---------------------------------------------------------------- Ollama API

    async Task<string> Chat(HttpClient http, List<ChatMessage> messages, CancellationToken cancel)
    {
        var request = new ChatRequest
        {
            model = model,
            messages = messages.ToArray(),
            stream = false,
            format = "json",
            options = new ChatOptions { temperature = temperature, num_ctx = contextSize, seed = UnityEngine.Random.Range(1, int.MaxValue) },
        };

        var content = new StringContent(JsonUtility.ToJson(request), Encoding.UTF8, "application/json");
        using (var response = await http.PostAsync(baseUrl.TrimEnd('/') + "/api/chat", content, cancel))
        {
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new Exception($"HTTP {(int)response.StatusCode}: {body}");
            return JsonUtility.FromJson<ChatResponse>(body).message.content;
        }
    }

    [Serializable] class ChatMessage { public string role; public string content; public ChatMessage(string r, string c) { role = r; content = c; } }
    [Serializable] class ChatOptions { public float temperature; public int num_ctx; public int seed; }
    [Serializable] class ChatRequest { public string model; public ChatMessage[] messages; public bool stream; public string format; public ChatOptions options; }
    [Serializable] class ChatResponse { public ChatMessage message; }
}
