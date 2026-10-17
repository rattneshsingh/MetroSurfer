# CLAUDE.md

## Project

A 3-lane endless runner for **Android**, themed around Indian states. Each state
(Delhi, Punjab, Kerala, ...) is a distinct visual + audio theme sharing one
gameplay core. Solo developer, evenings only. Currently in **Phase 1** (see
"Phase 1 scope" at the bottom; Phase 0 gameplay core is complete).

Working title: `RUNNER` — rename throughout when the real name is picked.

## Stack

- Unity 6.5 (6000.5.x), **URP** (mobile renderer). Never HDRP.
- C# — developer has 6+ years .NET/ASP.NET Core experience. Skip C# basics;
  do NOT skip Unity-specific gotchas (see Performance).
- New Input System (not legacy `Input.GetKey`)
- Addressables for state content delivery
- Later phases only: Firebase (Analytics/Remote Config/Crashlytics),
  AppLovin MAX. Do not add these until asked.

## Target device

A ~₹12,000 Android phone (4 GB RAM, mid-tier Adreno/Mali, 60 Hz).
**Not** a flagship. Every perf decision is judged against that device.
Targets: locked 60 FPS, < 150 MB initial APK/AAB, < 200 draw calls.

## Core architecture rule — a state is DATA, not a scene

This is the most important constraint in the project. One generic gameplay
scene loads a state's content at runtime.

- Each state = a `StateConfig` ScriptableObject: palette, prop set references,
  train/vehicle prefabs, skyline/landmark set, music track, obstacle table,
  difficulty curve tuning.
- Art loads via **Addressables** so states download on demand and don't bloat
  the base build.
- **Never** duplicate the gameplay scene per state. Never hardcode a state name
  in gameplay code. If adding a new state requires editing gameplay scripts,
  the design is wrong — flag it instead of writing the code.

Rationale: adding states 4–10 must stay cheap. That is the project's only moat.

## Performance rules (non-negotiable)

The developer comes from server-side .NET where allocations are cheap. In the
frame loop they are not. Enforce these and call out violations explicitly.

- **Zero allocations in `Update`/`FixedUpdate`/`LateUpdate`.** No LINQ, no
  lambdas that capture, no string concatenation, no boxing, no `params` arrays.
- **Pool everything.** Obstacles, trains, coins, VFX, audio sources. No
  `Instantiate` or `Destroy` during gameplay — ever. Pool warm-up at load.
- Cache all component lookups. No `GetComponent`, `Find`, `Camera.main`, or
  `FindObjectOfType` inside the loop.
- Prefer structs and arrays over classes and `List<T>` in hot paths.
- No `foreach` over interfaces/collections that allocate an enumerator in hot
  paths — indexed `for` loops instead.
- Physics: use layers + a minimal collision matrix. Avoid mesh colliders.
- Keep material count low; batch aggressively; atlas textures.

## Game feel (Phase 0 priority)

Feel beats content. These must be tunable via serialized fields, not constants:

- Lane-switch snap duration and easing
- Coyote time on jumps, input buffering window
- Hitboxes **smaller** than the visual mesh (forgiving, never punishing)
- Speed ramp curve over distance
- Death/near-miss feedback timing

## Code conventions

- Namespace root: `Runner.` (e.g. `Runner.Player`, `Runner.Spawning`)
- One public type per file, filename matches type name
- `[SerializeField] private` over `public` fields
- No singletons except a single explicit `GameManager`; prefer injected refs
- ScriptableObjects for all tunable data — no magic numbers in code
- XML doc comments only on non-obvious public APIs; skip narrating obvious code

## Folder structure

```
Assets/
  _Project/
    Scripts/
      Core/          GameManager, run state machine, scoring
      Player/        input, lane controller, jump/slide, collisions
      Spawning/      pooling, chunk/segment spawner, difficulty
      Config/        ScriptableObjects (StateConfig, TuningConfig)
      UI/            HUD, menus, state select
      Utils/         pooling primitives, extensions
    Art/             models, textures, materials (per state subfolders)
    Audio/
    Prefabs/
    ScriptableObjects/
    Scenes/
```

Third-party/imported assets stay at `Assets/` root, outside `_Project/`.

## What Claude Code cannot do here

Do not claim these are done — list them as manual steps for the developer:

- Unity Editor work: scene setup, prefab creation and wiring, dragging
  Inspector references, Animator state machines, Addressables group config,
  lighting/bake settings, build settings, Player Settings
- Profiling on a physical device
- Anything requiring visual judgement of game feel

Write the code, then state exactly which Editor steps are needed to hook it up.

## Working style

- Blunt and concise. No hedging, no praise padding.
- Flag design problems before implementing a bad request.
- Small focused changes over large rewrites.
- When a perf rule and a readability preference conflict in a hot path, perf
  wins — and say so in a comment.

## Phase 0 scope (done)

Grey cubes: 3 lanes, swipe/keyboard input, jump, slide, pooled obstacle and coin
spawning, procedural segment generation, collision + death, score, restart,
speed ramp.

## Phase 1 scope (current): Delhi vertical slice

Make one state look and feel like a finished game, as a playable portfolio piece.

In scope: full UI (title, HUD, pause, game over, best score), an animated runner
character, vehicle and train models, a scrolling Delhi environment, basic audio.
Art goes through the existing seams — `ObstacleDefinition.Prefab`, the player's
`_visual`, and `StateConfig` — never by hardcoding Delhi into gameplay code.

Art rules:
- Only CC0 or CC-BY assets (Kenney, Quaternius, Mixamo, Sketchfab filtered by
  licence). Record every third-party asset and its licence in `CREDITS.md`.
- Original look only. Nothing that copies or imitates another game's characters,
  logo, name or art.

Still out of scope: progression, shop, missions, IAP, ads, Firebase, state
select, Addressables content loading.
