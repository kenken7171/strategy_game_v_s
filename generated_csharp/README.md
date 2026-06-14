# Chronicle Knights

**A Deterministic 100-Year Chronicle RPG** (Godot 4 / .NET 8 / C# 12).

Lead a knight brigade across a single, seed-deterministic century. Each year is one
full lap of the grand corridor:

```
Title  ->  Hub  ->  Battle  ->  Settlement  ->  Hub  ->  ...
(seed)   (economy/    (enemy     (last hit +     (next
         prophecy/    intent +   spoils reflow + year)
         roster)      resolve)   chronicle)
```

The whole run is reproducible from a single integer seed: identical seed, identical century.

---

## Prerequisites

- **Godot Engine 4.x** with .NET / C# support (Mono build). Tested against Godot.NET.Sdk 4.3.0.
- **.NET 8 SDK** (the project targets `net8.0`, C# language version 12).

No other toolchain is required. All gameplay logic lives in pure C# under `Core/`.

---

## Layout

```
generated_csharp/
  ChronicleKnights.csproj      Main Godot project (Godot.NET.Sdk/4.3.0, net8.0).
  project.godot                Godot project file. Autoload: /root/ChronicleGlobal.
  Main.tscn                    Entry scene (run/main_scene).
  Autoload/                    ChronicleGlobal.cs -- the single Source of Truth (SoT) singleton.
  Core/                        Pure C# game logic (no Godot types): Battle, Chronicle,
                               Job, Units, Formation, GameFlow, Managers, Shop.
  UI/                          First-wave UI layer + JuiceDirector (juice/tween factory).
  UserInterface/               Stateless view layer: Title -> Hub -> Battle -> Settlement.
  Config/                      External data (localization, jobs).
  Tests/                       xUnit contract tests (ChronicleKnights.Tests.csproj).
```

---

## How to Build & Run (CLI)

All commands are run from this directory (`generated_csharp/`).

**Build (Debug):**

```sh
dotnet build ChronicleKnights.csproj --configuration Debug
```

**Run the game (Godot editor / windowed):**

```sh
godot --path .
```

This opens the project and runs `Main.tscn`, which boots the stateless view router
(`UserInterfaceRoot`) at the Title screen.

**Run headless (CI / smoke check):**

```sh
godot --headless --path . --quit
```

---

## How to Run Tests

The xUnit suite wraps the pure `Core/` layer as executable contracts (HP bind sources,
spoils reward math, phase-flow cycle, reset/economy invariants, equipment correction, etc.).
Godot-only types (views, autoload) are covered by logical verification instead.

```sh
dotnet test Tests/ChronicleKnights.Tests.csproj
```

---

## Architectural Pillars

1. **Single Source of Truth (`ChronicleGlobal`)**
   One autoload singleton at `/root/ChronicleGlobal` owns all game state (economy, timeline,
   roster, battle snapshot, chronicle log, ancestral archive). Every mutation flows through it
   and is broadcast via signals (StateInitialized, EconomyChanged, TimelineChanged,
   RosterChanged, BattleChanged, PhaseChanged).

2. **Stateless UI**
   Views cache no game variables. On every render they read the SoT fresh and push values
   one-way into labels/bars. SoT signals trigger re-render. The only retained fields are
   interaction latches (e.g. a double-submit guard), never gameplay data.

3. **Leak-Free Lifecycle**
   Dynamically created nodes are tracked in per-view **ledger registries** and cleared with
   `QueueFree()` at the start of every re-render and on `_ExitTree`:
   `_timelineNodes`, `_rosterNodes`, `_battleNodes`, `_settlementNodes`.
   All juice tweens (Flash / CountUp / Typewriter) are **node-bound** via `CreateTween()` on
   the target node, so they auto-expire when the node is freed; callbacks are `IsInstanceValid`
   guarded. Signal subscriptions are taken in `_Ready` and fully released in `_ExitTree`
   (and on view swap by the router).

4. **Deterministic PRNG Seeding**
   A new game injects one integer seed (`StartNewGame(seed)`); battles draw from an isolated
   battle RNG re-seeded per fight. Same seed reproduces the same 100-year chronicle. Logic is
   side-effect free and externally seeded, so outcomes are environment-independent.

---

## Development Constitution

- **Article I -- Strict ASCII.** All identifiers, component names, test ids, status text, and
  source comments in the `Core/` and `UserInterface/` layers use ASCII only. No non-ASCII
  characters or hardcoded localized strings in machine-readable structures; display strings
  for the `UserInterface/` layer are ASCII (e.g. `"BATTLEFIELD:"`, `"ACQUIRED POINTS:"`,
  `"ACCEPT HISTORY"`). Localized labels are resolved from `Config/` keys.
- **Strict code categorization.** Pure logic lives in `Core/` (no Godot dependency and
  unit-testable); Godot `Node`/`Control` views live in `UI/` and `UserInterface/`; the SoT
  singleton lives in `Autoload/`. Tests target only the pure `Core/` slice.
- **Warnings as errors.** The build treats a fixed set of nullable / control-flow / unused
  warning codes as errors; they must remain statically zero.
- **No skipped git hooks; commit messages are formal (no abbreviations).**
