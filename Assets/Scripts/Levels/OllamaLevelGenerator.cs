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
    public string model = "qwen2.5-coder:7b";
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
                                    "Reply with the JSON object only."),
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

                // The model outputs a chain of relative steps (gap/rise/turn/shift), not absolute
                // coordinates - LevelLayout.Expand() turns that into the same absolute LevelData the
                // validator/builder always expected. See LevelChain.cs for why.
                var chain = LevelChain.FromJson(reply);
                var level = LevelLayout.Expand(chain);
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
                // Keep the conversation bounded to system + original request + the single most recent
                // failed reply/feedback pair. Appending every attempt forever (instead of replacing)
                // used to grow unbounded and eventually push the system prompt (the rules + example)
                // out of the model's context window, after which it just echoed back the same invalid
                // JSON every remaining attempt with no way to see what to fix.
                if (messages.Count > 2) messages.RemoveRange(2, messages.Count - 2);
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

    // exampleJson is no longer shown to the model - see CountCopies(), which still compares the final
    // EXPANDED positions against it to reject a level that's just a re-skin of New Grapple. Showing it
    // here would be actively misleading now, since the model's own output format is a relative chain,
    // not absolute coordinates.
    static string BuildSystemPrompt(string exampleJson)
    {
        return
"You design levels for a VR grappling-hook game. You reply with ONE JSON object and nothing else.\n" +
"\n" +
"THE GAME: the player stands on a flat start slab and must reach the goal by hopping from platform to platform with a grappling hook.\n" +
"\n" +
"FORMAT: you do NOT give absolute coordinates. You describe a CHAIN: each platform is placed relative to the one\n" +
"before it (how big a gap, how much higher/lower, whether to turn, how far sideways). Code turns this into the\n" +
"real level - you never have to compute or keep track of any x/y/z position yourself.\n" +
"\n" +
"{\"name\": \"...\",\n" +
" \"platforms\": [ {\"gap\": 3, \"rise\": 0.5, \"turn\": 0, \"shift\": 0}, ... exactly 24 of these ... ],\n" +
" \"goal\": {\"gap\": 4, \"rise\": 1, \"turn\": 0, \"shift\": 0} }\n" +
"\n" +
"Each step object has exactly these 4 numbers:\n" +
"- \"gap\": empty space (metres) between this platform and the one before it, along the current heading. 0 or more.\n" +
"- \"rise\": change in height (metres) versus the platform before it. Negative = drop down.\n" +
"- \"turn\": -90, 0 or 90. Turns the heading before moving (left, straight, right). Start heading already points\n" +
"  away from the start slab, so platform 1 normally uses turn 0.\n" +
"- \"shift\": sideways offset (metres) after turning, positive = right of the heading. Independent of turn - you\n" +
"  can shift without turning for a gentle sideways drift, or turn without shifting for a sharp corner.\n" +
"\n" +
"The LAST thing in the JSON is \"goal\": one more step, placed the same way after the 24th platform - this becomes\n" +
"the win trigger the player must reach.\n" +
"\n" +
"NUMBERS TO USE (the level is rejected if you go outside these per step):\n" +
"- gap: usually 2 to 5. Occasionally up to 7 for a hard grapple-only hop (the grapple reaches 25 m, so even a few\n" +
"  big gaps in a row are fine - just don't make every single one huge).\n" +
"- rise: usually -3 to 4. Mix ups and downs; don't climb every single step or you'll run out of height budget.\n" +
"- turn: mostly 0. Use -90/90 only occasionally (roughly 1 in 5 steps at most) - turning often makes the path\n" +
"  double back into platforms you already placed, which is rejected as an overlap.\n" +
"- shift: usually -3 to 3, occasionally a bit more for a wide sideways gap.\n" +
"\n" +
"DESIGN: make it interesting and varied, not a straight line of identical steps.\n" +
"- Vary gap and rise together: mix easy short hops, a few long grapple-only gaps, a climbing section, a drop.\n" +
"- Use shift sometimes so the path isn't perfectly straight, without turning so often that it loops back on itself.\n" +
"- Across the whole chain, make sure total height varies by at least a few metres (don't keep rise near 0 every\n" +
"  step) and total sideways shift varies too (don't keep shift at 0 every step).\n" +
"- Be creative with the overall shape: a tower/stair section, a long straight run, a zig-zag, a cluster of short\n" +
"  hops followed by one big one.\n" +
"\n" +
"Reply with the JSON object only - no \"size\", \"yaw\" or \"position\" fields, no commentary.";
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
