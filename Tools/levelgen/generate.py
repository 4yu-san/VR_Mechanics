"""Generate Grapple-W levels with a local LLM and write them as JSON for Unity.

Examples
  python generate.py --count 5 --difficulty 3 --length 10 --model qwen2.5:14b
  python generate.py --backend openai --host http://localhost:1234 --model local-model --count 3
  python generate.py --mock --count 3        # no LLM, procedural plan (pipeline test / fallback)
"""
import argparse
import json
import random
import re
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

from pydantic import ValidationError

from layout import expand
from schema import LevelPlan, Step, plan_json_schema
from validate import load_profile, validate

DEFAULT_OUT = Path(__file__).resolve().parents[2] / "Assets" / "Resources" / "Levels"


# ---------- prompt ----------

def build_system_prompt(prof):
    shapes = "\n".join(
        f"  - {name}: width {s['width'][0]}-{s['width'][1]}, depth {s['depth'][0]}-{s['depth'][1]}, "
        f"height {s['height'][0]}-{s['height'][1]}"
        for name, s in prof["shapes"].items())
    return f"""You design levels for a first-person grappling-hook parkour game. Output one JSON object only.

The level is a chain of steps. Step 1 is the start platform. Each later step is placed relative to the previous one:
  gap    empty space between the previous piece and this one, measured along the heading (metres, 0 or more)
  rise   change in top-surface height versus the previous piece (negative = lower)
  turn   -90, 0 or 90: turn applied before moving
  shift  sideways offset, positive to the right
The last step must be type "goal". Step 1 ignores gap, rise, turn and shift (use 0).

Piece types and allowed sizes (metres):
{shapes}
platform, pillar and goal can be stood on. wall cannot be stood on, it is only a grapple anchor.

Player limits:
  - Jump: crosses a gap up to {prof['jumpGapMax']} m and climbs up to {prof['jumpUpMax']} m. Dropping lets you cross a little further.
  - Grapple: reaches any surface within {prof['grappleRange']:.0f} m with a clear line of sight, up to {prof['grappleRiseMax']:.0f} m higher.
  - A gap or rise beyond the jump limits forces the player to grapple, which is the fun part.
  - Between two standable pieces you may place walls or pillars as anchors to swing through.

Rules:
  - Pieces must not overlap each other, and nothing may sit within {prof['headroom']} m above a platform.
  - Do not let the path double back into earlier pieces.
  - Vary the rhythm: mix easy jumps, long grapple gaps, climbs and the occasional turn.
  - Keep every number realistic. Never exceed the sizes above.

Example (4 steps):
{{"name":"First Swing","theme":"quarry","steps":[
 {{"type":"platform","width":8,"height":1,"depth":8,"gap":0,"rise":0,"turn":0,"shift":0}},
 {{"type":"platform","width":5,"height":1,"depth":5,"gap":4,"rise":0,"turn":0,"shift":0}},
 {{"type":"pillar","width":3,"height":12,"depth":3,"gap":14,"rise":8,"turn":90,"shift":0}},
 {{"type":"goal","width":10,"height":1,"depth":10,"gap":12,"rise":2,"turn":0,"shift":0}}]}}"""


def build_user_prompt(args):
    return (f"Design a level with {args.length} steps, difficulty {args.difficulty} out of 5, "
            f"theme \"{args.theme}\". Output the JSON object now.")


# ---------- backends ----------

def http_json(url, payload, timeout):
    req = urllib.request.Request(url, data=json.dumps(payload).encode(), headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read())


def call_model(args, messages, seed):
    schema = plan_json_schema()
    if args.backend == "ollama":
        data = http_json(f"{args.host}/api/chat", {
            "model": args.model, "messages": messages, "stream": False, "format": schema,
            "options": {"temperature": args.temperature, "seed": seed},
        }, args.timeout)
        return data["message"]["content"]
    data = http_json(f"{args.host}/v1/chat/completions", {
        "model": args.model, "messages": messages, "temperature": args.temperature, "seed": seed,
        "response_format": {"type": "json_schema", "json_schema": {"name": "level_plan", "strict": True, "schema": schema}},
    }, args.timeout)
    return data["choices"][0]["message"]["content"]


def extract_json(text):
    text = text.strip()
    if text.startswith("```"):
        text = re.sub(r"^```\w*\n?|```$", "", text).strip()
    return text


# ---------- mock plan (no LLM) ----------

def mock_plan(rng, length, difficulty, prof):
    steps = [Step(type="platform", width=8, height=1, depth=8, gap=0, rise=0, turn=0, shift=0)]
    for i in range(1, length - 1):
        hard = rng.random() < 0.2 + 0.15 * difficulty
        kind = rng.choices(["platform", "pillar", "wall"], weights=[5, 2, 2 if hard else 1])[0]
        if kind == "platform":
            size = (rng.uniform(4, 9), rng.uniform(1, 2), rng.uniform(4, 9))
        elif kind == "pillar":
            size = (rng.uniform(2, 4), rng.uniform(6, 18), rng.uniform(2, 4))
        else:
            size = (rng.uniform(8, 20), rng.uniform(8, 20), rng.uniform(0.6, 1.2))
        if hard:
            gap, rise = rng.uniform(prof["jumpGapMax"] + 2, 10 + 4 * difficulty), rng.uniform(-2, 8)
        else:
            gap, rise = rng.uniform(1.5, prof["jumpGapMax"] - 1), rng.uniform(-3, 1.5)
        turn = rng.choice([0, 0, 0, -90, 90]) if i > 1 else 0
        steps.append(Step(type=kind, width=round(size[0], 1), height=round(size[1], 1), depth=round(size[2], 1),
                          gap=round(gap, 1), rise=round(rise, 1), turn=turn, shift=round(rng.uniform(-3, 3), 1)))
    steps.append(Step(type="goal", width=12, height=1, depth=12, gap=round(rng.uniform(4, 12), 1), rise=1, turn=0, shift=0))
    return LevelPlan(name=f"Mock {rng.randint(100, 999)}", theme="procedural", steps=steps)


# ---------- main loop ----------

def slug(text):
    return re.sub(r"[^a-z0-9]+", "_", text.lower()).strip("_") or "level"


def min_grapple_for(difficulty, length):
    return min(max(0, difficulty - 1), max(0, (length - 1) // 2))


def generate_one(args, prof, seed, index):
    want = min_grapple_for(args.difficulty, args.length)

    if args.mock:
        for attempt in range(200):
            rng = random.Random(seed * 1000 + attempt)
            plan = mock_plan(rng, args.length, args.difficulty, prof)
            level = expand(plan, prof, seed, {"backend": "mock"})
            errors, stats = validate(level, prof, want)
            if not errors:
                return level, stats, attempt + 1
        return None, None, 200

    messages = [{"role": "system", "content": build_system_prompt(prof)},
                {"role": "user", "content": build_user_prompt(args)}]
    for attempt in range(1, args.retries + 2):
        try:
            raw = call_model(args, messages, seed + attempt)
        except (urllib.error.URLError, TimeoutError) as exc:
            sys.exit(f"Could not reach the model server at {args.host}: {exc}")
        feedback = None
        try:
            plan = LevelPlan.model_validate_json(extract_json(raw))
            level = expand(plan, prof, seed, {"backend": args.backend, "model": args.model, "difficulty": args.difficulty})
            errors, stats = validate(level, prof, want)
            if not errors:
                return level, stats, attempt
            feedback = errors
        except (ValidationError, ValueError) as exc:
            feedback = [f"the JSON did not match the schema: {str(exc)[:300]}"]
        print(f"   attempt {attempt} rejected: {feedback[0]}")
        messages.append({"role": "assistant", "content": raw})
        messages.append({"role": "user", "content":
                         "That level failed validation:\n- " + "\n- ".join(feedback[:6]) +
                         "\nFix these problems and output the complete corrected JSON object."})
    return None, None, args.retries + 1


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--count", type=int, default=1)
    ap.add_argument("--difficulty", type=int, default=3, choices=range(1, 6))
    ap.add_argument("--length", type=int, default=10, help="number of steps including start and goal")
    ap.add_argument("--theme", default="ruined industrial rooftops")
    ap.add_argument("--backend", choices=["ollama", "openai"], default="ollama",
                    help="ollama, or openai for LM Studio / llama.cpp server")
    ap.add_argument("--host", default=None, help="default http://localhost:11434 (ollama) or :1234 (openai)")
    ap.add_argument("--model", default="gemma2:9b-instruct-q4_K_M")
    ap.add_argument("--temperature", type=float, default=0.8)
    ap.add_argument("--retries", type=int, default=4)
    ap.add_argument("--timeout", type=int, default=600)
    ap.add_argument("--seed", type=int, default=None)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    ap.add_argument("--profile", default=None)
    ap.add_argument("--mock", action="store_true", help="skip the LLM and use the procedural planner")
    args = ap.parse_args()

    if args.host is None:
        args.host = "http://localhost:11434" if args.backend == "ollama" else "http://localhost:1234"
    args.host = args.host.rstrip("/")
    args.length = max(4, min(args.length, 38))

    prof = load_profile(args.profile)
    args.out.mkdir(parents=True, exist_ok=True)
    base_seed = args.seed if args.seed is not None else int(time.time()) % 100000

    saved = 0
    for i in range(args.count):
        seed = base_seed + i
        print(f"[{i + 1}/{args.count}] seed {seed}")
        level, stats, attempts = generate_one(args, prof, seed, i)
        if level is None:
            print("   gave up after", attempts, "attempts")
            continue
        path = args.out / f"{slug(level['name'])}_{seed}.json"
        path.write_text(json.dumps(level, indent=2))
        saved += 1
        print(f"   saved {path.name}  attempts={attempts}  {stats}")
    print(f"done: {saved}/{args.count} levels written to {args.out}")
    return 0 if saved else 1


if __name__ == "__main__":
    sys.exit(main())
