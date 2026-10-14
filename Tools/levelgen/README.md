# levelgen

Generates VR_Mechanics levels with a local LLM and writes them as JSON that `GeneratedLevels.LevelBuilder`
loads in Unity. The game itself never talks to a model, so shipped builds (including Quest) stay offline.
Ported from a teammate's desktop prototype and retuned for this project's VR rig (see `movement_profile.json`).

## Setup

```
pip install -r requirements.txt
```

Install a local model runner. Ollama is the default:

```
ollama pull gemma2:9b-instruct-q4_K_M
```

## Generate a batch of 5, one difficulty step per level

```
python generate.py --count 5 --difficulty 1 --length 8  --out ../../Assets/Resources/Levels --model gemma2:9b-instruct-q4_K_M
```

Useful flags:

| flag | meaning |
|---|---|
| `--difficulty 1-5` | higher values require more grapple-only transitions |
| `--length N` | steps including start and goal |
| `--backend openai --host http://localhost:1234` | LM Studio or a llama.cpp server |
| `--seed N` | reproducible numbering and sampling seed |
| `--mock` | skip the LLM, use the procedural planner (pipeline test / fallback) |

Rename the output files to `batch00N_level{1..5}_<slug>_<seed>.json` (or just renumber them) so
`GeneratedLevels.LevelProgressManager`'s ordered `levelFiles` list plays them in the right order.

## How it works

1. The model returns a chain of steps (`schema.py`), each placed relative to the previous one.
   Ollama's `format` constrains the output to the schema.
2. `layout.py` converts the chain to absolute positions.
3. `validate.py` checks sizes, bounds, overlaps, headroom, and that every transition is reachable
   by jump or grapple (with line of sight), using `movement_profile.json`'s numbers for *this*
   project's rig (no real jump - almost everything is grapple-required by design). Failures go
   back to the model as a list of specific problems, up to `--retries` times.
4. Valid levels are saved to `Assets/Resources/Levels/`. Check any file later with
   `python validate.py path/to/level.json`.

## Tuning

`movement_profile.json` holds the player limits the validator and prompt use, already set from
`Grappling.cs`'s `maxDistance = 25`. Playtest a few levels, then adjust `grappleRange` and
`grappleRiseMax` further if the pull/swing feels tighter or looser than the validator assumes.

## Unity setup (done once per scene)

1. `Assets > Create > Grapple > Level Prefab Registry (Generated)`. Assign `Cube (7)` as the block
   prefab, `Level End` as the goal trigger prefab, and leave the layer as `isGrapplable`.
2. In `New Grapple.unity`, add an empty object with `GeneratedLevels.LevelBuilder` (assign the
   registry and the Physics Rig transform) and `GeneratedLevels.LevelProgressManager` (assign the
   LevelBuilder, and the ordered list of level file names).
3. Point the Level Complete panel's "Next Level" button at
   `LevelProgressManager.instance.Advance()`. Pressing **L** also advances, as a debug shortcut.
