using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// Asks a local Ollama model for a MultiGoalPlan (the shape of one path per goal), turns it into cube
// positions with MultiGoalLayout and validates them. Problems go back to the model, which tries again.
// If the model keeps failing (or useLLM is off), a procedural plan is used instead so a level is
// always produced. Same retry idea as OllamaLevelGenerator, kept separate so the old pipeline is untouched.
//
// Call from the main thread: the await continuations resume on Unity's main thread.
namespace MultiGoalLevels
{
    public class MultiGoalGenerator
    {
        public string baseUrl = "http://localhost:11434";
        public string model = "gemma2:9b-instruct-q4_K_M";
        public int maxAttempts = 4;
        public float temperature = 0.8f;
        public int contextSize = 8192;
        public int timeoutSeconds = 300;
        public bool useLLM = true;
        public bool fallbackToProcedural = true;
        public int seed = 0;

        public class Result
        {
            public bool success;
            public bool usedFallback;
            public MultiGoalPlan plan;
            public Layout layout;
            public int attempts;
            public List<string> log = new List<string>();
            public string error;
        }

        public async Task<Result> GenerateAsync(string request, LayoutSettings settings, SceneInfo scene,
                                                Action<string> progress = null, CancellationToken cancel = default)
        {
            var result = new Result();
            void Log(string s) { result.log.Add(s); progress?.Invoke(s); }

            // Settings problems (e.g. goal distance too small) are not the model's fault - stop early
            var settingErrors = new List<string>();
            MultiGoalLayout.GoalPositions(settings, scene, settingErrors);
            if (settingErrors.Count > 0)
            {
                result.error = string.Join("\n", settingErrors);
                Log(result.error);
                return result;
            }

            var rng = new System.Random(seed != 0 ? seed : Environment.TickCount);

            if (useLLM)
            {
                await AskModel(request, settings, scene, rng, result, Log, cancel);
                if (result.success || cancel.IsCancellationRequested) return result;
                if (!fallbackToProcedural) return result;
                Log("Falling back to the procedural planner.");
            }

            for (int attempt = 1; attempt <= 200; attempt++)
            {
                var plan = MultiGoalLayout.Procedural(settings, rng);
                if (TryLayout(plan, settings, scene, rng, out var layout, out _))
                {
                    result.success = true;
                    result.usedFallback = useLLM;
                    result.plan = plan;
                    result.layout = layout;
                    Log($"Procedural plan accepted after {attempt} tries ({layout.cubes.Count} platforms).");
                    return result;
                }
            }
            result.error = "The procedural planner could not find a valid layout either. Try fewer goals, a larger goal distance or a wider arc.";
            Log(result.error);
            return result;
        }

        static bool TryLayout(MultiGoalPlan plan, LayoutSettings settings, SceneInfo scene, System.Random rng, out Layout layout, out List<string> errors)
        {
            var warnings = new List<string>();
            errors = MultiGoalLayout.CheckPlan(plan, settings, warnings);
            layout = null;
            if (errors.Count > 0) return false;
            layout = MultiGoalLayout.Build(plan, settings, scene, rng, errors);
            if (errors.Count > 0) return false;
            errors = MultiGoalLayout.Validate(layout, settings, scene);
            return errors.Count == 0;
        }

        async Task AskModel(string request, LayoutSettings settings, SceneInfo scene, System.Random rng, Result result, Action<string> Log, CancellationToken cancel)
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage("system", BuildSystemPrompt(settings)),
                new ChatMessage("user", $"Design the routes.\nRequest: {(string.IsNullOrWhiteSpace(request) ? "a fun, varied level" : request)}\n" +
                                        $"Give every goal (0 to {settings.goalCount - 1}) {settings.minRoutes} to {settings.maxRoutes} different routes. Reply with the JSON object only."),
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
                        return;
                    }

                    var plan = MultiGoalPlan.FromJson(reply);
                    if (TryLayout(plan, settings, scene, rng, out var layout, out var errors))
                    {
                        result.success = true;
                        result.plan = plan;
                        result.layout = layout;
                        Log($"Attempt {attempt}: valid, {layout.cubes.Count} platforms, {plan.paths.Length} routes to {settings.goalCount} goals.");
                        return;
                    }

                    Log($"Attempt {attempt}: invalid.\n  - " + string.Join("\n  - ", errors));
                    // Keep only system + request + the latest failed reply, so the rules never fall out of
                    // a small model's context window (same fix as Tools/levelgen/generate.py)
                    if (messages.Count > 2) messages.RemoveRange(2, messages.Count - 2);
                    messages.Add(new ChatMessage("assistant", reply));
                    messages.Add(new ChatMessage("user", "That level is not valid:\n- " + string.Join("\n- ", errors.GetRange(0, Mathf.Min(6, errors.Count))) +
                                                         "\nFix these problems and reply with the complete corrected JSON object only."));
                }
            }
            Log($"The model gave no valid level after {maxAttempts} attempts.");
        }

        // ---------------------------------------------------------------- prompt

        static string BuildSystemPrompt(LayoutSettings s)
        {
            int n = s.goalCount;
            return
"You design levels for a VR grappling-hook game. You reply with ONE JSON object and nothing else.\n" +
"\n" +
$"THE LEVEL: the player starts on a platform. There are {n} goals, numbered 0 to {n - 1} from left to right, all\n" +
$"{s.goalDistance:0} m from the start. The space between the start and the goals is filled with a DENSE 3D FIELD of\n" +
"small floating platforms that the player grapples across. You describe ROUTES through that field: every goal\n" +
$"needs {s.minRoutes} to {s.maxRoutes} DIFFERENT routes leading to it. Code fills each route with platforms - you only\n" +
"describe its shape. All routes start at the start platform, so they merge into one shared field near the start\n" +
"and split apart toward the goals.\n" +
"\n" +
"A route follows a curve from the start (t = 0) to its goal (t = 1). Without waypoints it is a straight line.\n" +
"Bend it with waypoints, each with:\n" +
"- \"t\": how far along, from 0.1 to 0.9. Increasing within a route.\n" +
$"- \"side\": metres sideways from the straight line, from -{s.maxSide:0} to {s.maxSide:0}. Positive = right (looking from the start toward the goal).\n" +
$"- \"up\": metres above the straight line, from -{s.heightVariation:0.#} to {s.heightVariation:0.#}. Use the full range: climbs, towers and dips.\n" +
"Each route also has:\n" +
$"- \"goal\": which goal it leads to, 0 to {n - 1}.\n" +
$"- \"width\": how wide the field is along this route, from {s.minWidth:0} to {s.maxWidth:0} metres.\n" +
$"- \"spacing\": metres between neighbouring platforms, from {s.minSpacing:0} to {s.maxSpacing:0}. Smaller = denser.\n" +
"Give each route 1 to 4 waypoints.\n" +
"\n" +
"RULES (the level is rejected if it breaks one):\n" +
$"1. Every goal 0 to {n - 1} has {s.minRoutes} to {s.maxRoutes} routes in \"paths\".\n" +
$"2. Routes to the SAME goal must be clearly different: somewhere along the way they must be at least {s.minRouteSeparation:0} m\n" +
"   apart. Easiest: one route curves left (negative side) and another curves right (positive side), or one goes\n" +
"   high (up +5 or more) and another goes low (negative up).\n" +
"3. Every goal must stay reachable: avoid very sharp bends (big opposite \"side\" values on waypoints close together).\n" +
"\n" +
"DESIGN: make the routes feel different: a high route that climbs and drops onto the goal, a low route that weaves\n" +
"sideways, a wide dense field (width 12+, spacing near the minimum), a narrow trail (width near the minimum).\n" +
"Do not reuse the same numbers for every route.\n" +
"\n" +
$"FORMAT (example for 1 goal with 2 routes - yours must cover goals 0 to {n - 1}):\n" +
"{\"name\": \"Twin Ways\", \"paths\": [\n" +
"  {\"goal\": 0, \"spacing\": 3, \"width\": 12, \"waypoints\": [{\"t\": 0.35, \"side\": -9, \"up\": 3}, {\"t\": 0.7, \"side\": -6, \"up\": 7}]},\n" +
"  {\"goal\": 0, \"spacing\": 4, \"width\": 7, \"waypoints\": [{\"t\": 0.3, \"side\": 6, \"up\": -3}, {\"t\": 0.55, \"side\": 10, \"up\": -1}, {\"t\": 0.8, \"side\": 4, \"up\": 2}]}\n" +
"]}";
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
}
