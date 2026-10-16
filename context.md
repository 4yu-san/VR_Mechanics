# VR_Mechanics: LLM level generation, project context

Unity 6 (6000.5.8f1) VR grappling game for Meta Quest (URP, OpenXR, XR Interaction Toolkit 3.5.1).
This file covers the AI level generation work only. Written 2026-10-16.

## Game facts the generators depend on
- Grapple: `Assets/Scripts/Grappling.cs`, `maxDistance = 25`, spring-joint pull, raycast against the `isGrapplable` layer (layer 3).
- No jump. Movement is thumbstick glide (`ContinuousMovementPhysics.cs`) plus XRI teleport. Physics Rig is about 0.86 m tall.
- Platform prefab: `Assets/prefabs/Cube (7).prefab` (unit BoxCollider, scale 1.3922 x 0.86 x 2.28, layer 3).
- Goal prefab: `Assets/prefabs/Level End.prefab` (CapsuleCollider trigger + `EndLvl.cs`, which calls `GameManager.LevelCompleted()`).
- Template scene: `Assets/Scenes/New Grapple.unity`. Hierarchy: `Map Elements` (24 Cube (7), `DeathFloor`, `Start Platform`), plus `Level End`, `Physics Rig`, `VR Menus`, `Game Manager`, `Stopwatch` at root.
- UI: `UIManager.cs` lives in the `VR Menus` prefab. `UIManager.NextLevel()` loads the next scene by build index.
- Python 3.12 installed at `%LocalAppData%\Programs\Python\Python312\` (PATH may need a fresh terminal). `pydantic` installed.
- LLM: local Ollama, `gemma2:9b-instruct-q4_K_M`, `http://localhost:11434`.

## There are three generators in the repo

### 1. Old editor pipeline (kept, works on its own format)
`Assets/Scripts/Levels/` (global namespace): `LevelData.cs`, `LevelValidator.cs`, `LevelBuilder.cs`, `OllamaLevelGenerator.cs`, `LevelChain.cs`; editor side `LevelTools.cs`, `LevelGeneratorWindow.cs`.
- Menu: Tools > Levels > Generate Level With Ollama... Copies New Grapple, only moves the existing 24 Cube (7) and the Level End, adds the scene to Build Settings.
- JSON format: `platforms[]` of `{position}` plus `end`. Exactly 24 platforms. Do not feed it files from the other pipelines ("Needs exactly 24 platforms, but it has 0" is that mismatch).
- Someone later changed the LLM output to a relative chain (`LevelChain.cs`, `LevelLayout.Expand`) to help the small model.

### 2. Python tool + runtime loader (ported from a teammate's desktop prototype)
- `Tools/levelgen/` (`generate.py`, `layout.py`, `schema.py`, `validate.py`, `movement_profile.json`). Output goes to `Assets/Resources/Levels/*.json` with a `pieces[]`, `start`, `killFloorY` format.
- Unity side: `Assets/Scripts/Levels/Generated/` (namespace `GeneratedLevels`): `LevelData`, `LevelPrefabRegistry`, `LevelBuilder`, `LevelProgressManager`. Registry asset: `Assets/Settings/Generated/LevelPrefabRegistry.asset`.
- `LevelProgressManager` steps through `levelFiles` in order, rebuilds geometry in place. Skip key: B on the right controller (or L on keyboard).
- State: the `LevelProgress (Generated)` object in `New Grapple.unity` is currently **disabled** (`m_IsActive: 0`) so it does not fight the multi-goal bake. Known gaps: the old static New Grapple geometry is not hidden by this loader, and the Next Level button is not wired to `Advance()`. `movement_profile.json` is retuned (grapple range 25, no jump). JSON files in `Resources/Levels` need Unity to import them (`.meta`) before `Resources.Load` works.
- Pipeline 2 is paused in favour of pipeline 3.

### 3. Multi-goal bake (current focus, newest)
Files: `Assets/Scripts/Levels/MultiGoal/MultiGoalLayout.cs`, `MultiGoalGenerator.cs`, `Assets/Scripts/Editor/MultiGoalLevelWindow.cs`. Namespace `MultiGoalLevels`.
- Menu: **Tools > Levels > Generate Multi-Goal Level...** then "Generate and Bake".
- Copies New Grapple to `Assets/Scenes/Levels/<name>.unity` (original untouched), changes only `Map Elements`: deletes all Cube (7) and Level Ends, spawns new ones, keeps `Start Platform`, moves `DeathFloor`. Adds the scene to Build Settings after New Grapple. Plan JSON saved to `Assets/Levels/MultiGoal/`.
- The LLM never writes coordinates. It returns routes: `{"name", "paths":[{"goal","spacing","width","waypoints":[{"t","side","up"}]}]}`. Code (`MultiGoalLayout`) turns each route into a Catmull-Rom corridor, places a spine of cubes along it, then scatter-fills the corridor in width and height.
- Rules enforced in code:
  - Every Level End is exactly `goalDistance` (default 45 m) from the Start Platform centre, same height, fanned over `arcDegrees` (default 120) around -x.
  - A landing Cube (7) sits directly above each Level End, with a small gap (0.3 m).
  - Every landing cube must be reachable from the Start Platform by hops of at most 15 m (BFS). Unreachable cubes are pruned.
  - 2-3 routes per goal, routes to the same goal must separate by at least 10 m somewhere.
  - Dense: cube spacing 2.5-4.5 m, corridor width 6-18 m, density 1.5, min surface gap 0.8 m, 2 m headroom over stacked cubes, cap 700.
  - Height: route `up` in +-8 m, layer thickness 5 m, jitter 1.5 m, cubes at most 10 m below the Start Platform top.
  - Death floor: moved to sit 2 x (25 + 0.86) = 51.72 m below the lowest element.
  - All cubes keep the prefab's rotation (same facing).
- Retry loop: errors from the validator go back to the model (history trimmed to system + request + last failure). Falls back to a procedural planner if the model fails or "Use LLM" is off.

## Verification status
- **Nothing in pipeline 3 has been compiled or run in Unity.** First step next session: open Unity, check the Console for compile errors, run the window.
- The layout maths was checked in Python ports (scratchpad scripts, not in the repo) with New Grapple's real dimensions: 1-3 goals, 2-3 routes each gave 29/30 and 30/30 valid layouts, about 80-185 cubes per goal, nearest-neighbour gap about 3.3 m, height spread about 20 m, no hop over 15 m, goals exactly 45 m out.

## Open items
1. Compile and run the Multi-Goal window in Unity; check the look against the reference screenshots (dense, wide, low-ish field, all cubes facing the same way).
2. Playtest that the grapple can actually cross the generated gaps (hop limit is 15 m, grapple range 25 m).
3. Level End placement is "below the cube". Confirm that is the intent (the player drops off the landing cube into the trigger).
4. Multi-goal output is a separate scene per bake. A progression flow (difficulty ramp, batches of 5, regenerate after 4) was planned but not built for this pipeline.
5. Decide the fate of pipelines 1 and 2 once pipeline 3 is proven.
6. Phase 2 idea: live background generation from Editor via a Python/Ollama subprocess (not possible on a Quest build, which stays offline).

## Git state when this was written
Uncommitted: the whole `MultiGoal` folder, `MultiGoalLevelWindow.cs`, scene edits to `New Grapple.unity`, `Tools/levelgen` changes, generated JSON files. Conversation history lives in `C:\Users\vr_project\.claude\projects\F--VR-Mechanics\` (not synced). This file plus the committed code is enough to continue on another PC.
