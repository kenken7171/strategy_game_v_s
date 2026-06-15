# Visual & Juice Roadmap

> Status: **design blueprint only** (no code changes implied by this document).
> Target: the running Godot 4 / .NET 8 C# build at `generated_csharp/`.
> Goal: raise the game's *look* (Juice) and *tactical feedback* to a commercial
> bar by **adding** assets and presentation on top of the existing logic, without
> touching the 630 green xUnit tests, the delta-wedge drag & drop, the Japanese
> text layer, or the marriage gender-separation guard.

This file is intentionally written in English/ASCII so it can be localized later;
the *concepts* are named precisely so a future implementer (or localizer) can map
them 1:1 onto the codebase.

---

## 0. Guardrails (read first)

Every item below is an **add-on**. Honor the existing architecture so nothing
regresses:

- **Single SoT.** Game state lives only in the `ChronicleGlobal` autoload
  (`/root/ChronicleGlobal`). Views never cache state; they read it fresh and
  re-render on signals (`StateInitialized`, `EconomyChanged`, `TimelineChanged`,
  `RosterChanged`, `BattleChanged`, `FormationChanged`, `PhaseChanged`).
- **Stateless UI.** Presentation nodes hold only transient interaction latches,
  never game data.
- **Leak-free lifecycle.** Dynamically created nodes go into a per-view *ledger*
  (e.g. `_battleNodes`, `_treeNodes`) and are `QueueFree`'d at the start of each
  re-render and in `_ExitTree`. Tweens are **node-bound** (created via
  `node.CreateTween()` or `JuiceDirector.*`) so they auto-expire when the node is
  freed; every tween callback is guarded with `IsInstanceValid`.
- **Constitution I (ASCII).** Identifiers, node names, `data_testid` metas, asset
  paths, and core internal logs stay ASCII. Player-facing display strings may be
  Japanese (the localization exception already granted to the UI layer).
- **WarningsAsErrors.** 13 CS codes are promoted to errors
  (CS1998;CS4014;CS8618;CS8602;CS8603;CS8604;CS8509;CS8524;CS0162;CS0169;CS0414;
  CS0649;CS0067). New code must keep the static count at zero.
- **net8.0 + RollForward.** Build with `dotnet build ChronicleKnights.csproj`;
  test with `dotnet test Tests/ChronicleKnights.Tests.csproj` (RollForward is
  baked, no env var needed).
- **Pure logic stays pure.** Anything that can be a Godot-independent pure
  function (layout math, color selection, asset path building) should live in
  `Core/` so it can be unit-tested and the test total can grow.

Asset note: `JobTextureLibrary.TryLoad` already proves the safe-load pattern --
`ResourceLoader.Exists` first, then an `Image.LoadFromFile` raw-disk fallback via
`ProjectSettings.GlobalizePath` -- so illustrations appear even before Godot has
generated `.import` files. **Every new texture loader below must reuse this exact
two-stage pattern.**

---

## 1. Battlefield Backgrounds & Enemy Art -- asset logistics

### 1.1 What exists today
- Asset root is consolidated under `res://Assets/Textures/` (Jobs already live at
  `res://Assets/Textures/Jobs/{slug}/{male|female}.png`).
- Enemies are data only: `Core/Battle/EnemyState.cs` carries
  `EnemyState.Archetype` of type `EnemyArchetype`
  (`TrialGuardian`, `DawnWarden`, `UpheavalConqueror`, `DeclineTyrant`,
  `EternalSovereign`).
- Stage identity is data only: `Core/Chronicle/ChronicleTimelineConfig.cs` defines
  `EpochId` (`Dawn`, `Upheaval`, `Decline`, `Twilight`) and `Epochs` with
  `RegularArchetype` / `BossArchetype` per era. Chapter-boss years are 25/50/75/100.

### 1.2 New territory (directories)
```
res://Assets/Textures/
  Jobs/{slug}/{male|female}.png        (exists)
  Backgrounds/{epoch_slug}.png         (new -- one per EpochId)
  Enemies/{archetype_slug}.png         (new -- one per EnemyArchetype)
```
Slugs are ASCII snake_case derived from the enum names, e.g.
`EpochId.Dawn -> "dawn"`, `EnemyArchetype.UpheavalConqueror -> "upheaval_conqueror"`.

### 1.3 New loader classes (mirror JobTextureLibrary)
- `UserInterface/BackgroundTextureLibrary.cs`
  - `static Texture2D? TryLoad(EpochId epoch)` -> `res://Assets/Textures/Backgrounds/{slug}.png`.
- `UserInterface/EnemyTextureLibrary.cs`
  - `static Texture2D? TryLoad(EnemyArchetype archetype)` -> `res://Assets/Textures/Enemies/{slug}.png`.
- Both reuse the `ResourceLoader` -> `Image.LoadFromFile` two-stage resolution and
  return `null` on a missing asset (caller renders empty -- never crash).
- The enum->slug map is a pure `switch` expression (exhaustive, no CS8509). Put the
  slug maps in a tiny pure helper (e.g. `Core/Assets/AssetSlugs.cs`) so they are
  **unit-testable** (assert every enum value maps to a non-empty ASCII slug).

### 1.4 Wiring (where to add nodes)
- **Background**: in `UI/BattleUI.cs` add a full-rect `TextureRect` as the FIRST
  child of the screen (behind `_rootShakeTarget` and `_popupLayer`),
  `StretchMode = KeepAspectCovered`, `MouseFilter = Ignore`. Choose the epoch from
  the current year via the existing timeline config. Re-pick on `BattleChanged`.
  The `TimelineUI` / Chronicle screen can show the same background for cohesion.
- **Enemy art**: in `UI/BattleUI.cs` the enemy card (`_enemyCard`) currently shows
  name + HP bar; add a `TextureRect` fed by
  `EnemyTextureLibrary.TryLoad(CurrentBattle.Enemy.Archetype)`.
- Both nodes go into the existing `_battleNodes` ledger so they are freed on
  re-render / `_ExitTree` (leak-free).

### 1.5 Do-not-break
Backgrounds/enemy art are pure presentation. No change to `EnemyScaler`,
`EnemyState`, `ChronicleTimelineConfig`, or battle resolution. Golden balance
untouched.

---

## 2. Juice -- camera shake & particle hit effects

### 2.1 What exists today (reuse, do not rebuild)
- `UI/JuiceDirector.cs` is the stateless animation toolbox:
  `Flash`, `Shake`, `SlideTo`, `Punch`, `FadeToDeath`, `DrainBar`, `CountUp`,
  `Typewriter`, `GrowLine`, `RisingPopup`. All return node-bound `Tween`s.
- `UI/BattleUI.cs` already wires **camera shake**: `_rootShakeTarget` (the board
  VBox), `ShakeCamera()` / `KillCameraShake()`, constants
  `CameraShakeAmplitude = 14`, `CameraShakeStepSeconds = 0.05`. It also flashes the
  targeted row (`FlashRow`) and the enemy card on `AllyOffenseEvent`.
- Battle drives presentation off the pure event stream from
  `Core/Battle/BattleEvent.cs`: `AllyOffenseEvent`, `EnemyOffenseEvent`,
  `UnitDamagedEvent`, `UnitDefeatedEvent`, `UnitHealedEvent`,
  `LastHitResolvedEvent`, `BattleConcludedEvent`, `RotationPerformedEvent`.

### 2.2 Add-on: tie shake intensity to event magnitude
- Today shake is a fixed amplitude. Add a pure helper
  `Core/Juice/ShakeProfile.cs` -> `float AmplitudeFor(int damage, bool isCrit, bool isFrontGuard)`
  so the *feel* is data-driven and **unit-tested** (small hit = gentle, big hit /
  iron-wall block = strong). `BattleUI.ShakeCamera` reads it from the event being
  rendered. Logic-free, additive.
- Trigger an extra shake on the front-guard mitigation moment (when a
  `BattalionDefense` / `SquadDefense` reduction is meaningful) so "the shield held"
  has weight.

### 2.3 Add-on: particle hit effects (Particle2D layer)
- New layer: in `UI/BattleUI.cs` add `_effectLayer` (a full-rect `Control`,
  `MouseFilter = Ignore`) placed **above** the board but **below** `_popupLayer`
  so numbers always read on top. Ledger it in `_battleNodes`.
- New helper `UI/HitEffectDirector.cs` (stateless, mirrors `JuiceDirector`):
  - `static void Slash(Control layer, Vector2 globalPos)` -> a short-lived
    `GpuParticles2D` (or `CpuParticles2D` for zero-import safety) tuned white/steel,
    `OneShot = true`, `Emitting = true`, auto-`QueueFree` via a node-bound timer/tween.
  - `static void Heal(Control layer, Vector2 globalPos)` -> green sparkle.
  - `static void Defeat(Control layer, Vector2 globalPos)` -> dark burst.
- Spawn position = the live grid cell of the affected unit (BattleUI already maps
  `unitId -> cell` for `FlashRow`; reuse that index). Emit on `UnitDamagedEvent`
  (slash), `UnitHealedEvent` (heal), `UnitDefeatedEvent` (defeat), `AllyOffenseEvent`
  (slash on the enemy card).
- **Pixel-art crispness**: set `TextureFilter = Nearest` on particle textures if
  they use sprite atlases. Prefer `CpuParticles2D` so the effect works run-from-
  source without an import step (consistent with the asset fallback philosophy).

### 2.4 Do-not-break
The event stream is already produced by pure `Core/Battle`. Particles/shake only
*read* events; they never feed back into resolution. Keep all tweens/particles
node-bound and ledgered.

---

## 3. UI feedback -- formation snap & damage popups

### 3.1 Formation "snap & bounce" on placement
- Today: `UI/FormationUI.cs` renders the wedge from `ChronicleGlobal.CurrentFormation`
  and uses `UserInterface/Hub/FormationSlotControl` (drop target / drag source) +
  `RosterDragCard` (drag source) + `FormationDragPayload` (ASCII codec). Placement
  calls `ChronicleGlobal.PlaceUnitOnFormation` / `SwapFormationSlots`; the board
  re-renders on `FormationChanged`. **There is no placement animation yet.**
- Add-on (presentation only): after a successful drop, play a satisfying snap:
  - On the just-filled slot's inner node, call `JuiceDirector.Punch(node, 1.18f, 0.18)`
    (scale overshoot -> settle) so the card "ka-chunk" seats.
  - Optionally `JuiceDirector.SlideTo` the dropped card from the cursor's release
    point to the slot center for a magnet effect.
- Where to hook: `FormationUI` re-renders the whole board on `FormationChanged`, so
  pass the "last placed coordinate" (a transient UI latch, not state) into
  `RenderBoard` and Punch only that slot. Reset the latch after consuming it.
- Keep the drag & drop semantics (`PlaceRequested` / `SwapRequested` delegates)
  exactly as they are; the animation is layered on the render, not the data path.

### 3.2 Damage / heal / crit popups
- Today: `UI/BattleUI.cs` already owns `_popupLayer`
  (`battle-damage-popup-layer`, full-rect, `MouseFilter = Ignore`) and color
  constants `DamagePopupColor` (red), `HealPopupColor` (green), `LastHitPopupColor`
  (gold), plus `JuiceDirector.RisingPopup`. Damage/heal numbers already rise.
- Add-on: make crits/heals *pop* harder:
  - Pure helper `Core/Juice/PopupStyle.cs` ->
    `(Color color, float fontScale, string prefix) For(BattleEvent e)`:
    big red + larger font for high-damage / last-hit; green for `UnitHealedEvent`;
    gold star for `LastHitResolvedEvent`. **Unit-test** the mapping (crit -> bigger
    scale than normal; heal -> green; defeat mark present).
  - `RisingPopup` gains/honors a `fontScale` so the number's size encodes weight.
    Drive it from `PopupStyle.For(event)`.
- This keeps the *numbers* authoritative (computed by the 630-tested core) and only
  styles how they fly out.

### 3.3 Do-not-break
`FormationBoard`, `DeploymentGate`, drag-drop payloads, and the battle event stream
are untouched. Snap/popup are render-time only.

---

## 4. Lineage Tree visualization (100-year bloodline)

### 4.1 What exists today (build on it -- do not rewrite)
- `Core/Pedigree/PedigreeGraph.cs` (pure, immutable): `PedigreeNode` (with
  `Generation` in [-2, +2]), `PedigreeEdge`, and a `PedigreeGraph` that resolves
  grandparents (-2), parents (-1), self/spouse/siblings (0), children (+1),
  grandchildren (+2). Already unit-tested.
- `UI/PedigreeOverlay.cs` already draws cards per generation band and connects
  parent->child with `Godot.Line2D` grown via `JuiceDirector.GrowLine`. It ledgers
  cards + connectors (`_treeNodes`) and grow tweens (`_growTweens`), and is mounted
  front-most by `GameDirector` with the standard `CloseRequested` / `_ExitTree`
  self-collapse pattern (same as the Job Manual / Prophecy overlays).

### 4.2 Add-on visual polish (presentation only)
- **Portraits**: replace text-only nodes with the unit's job illustration via
  `JobTextureLibrary.TryLoad(job, gender)` inside each pedigree card (gender read is
  already preserved across the codebase).
- **Generation bands**: tint each band (-2..+2) with a distinct color and a
  Japanese band label ("祖父母 / 父母 / 本人世代 / 子 / 孫") for instant legibility.
- **Marriage link**: draw the self<->spouse horizontal `Line2D` in a warm hue
  (heart link), distinct from the vertical parent->child connectors.
- **Stagger + camera**: keep the `GrowLine` stagger (`ConnectorGrowStaggerSeconds`)
  so lines draw on in sequence; add a gentle `JuiceDirector.Punch` on each card as
  its incoming connector completes (chain reveal).
- **Entry points**: the overlay is already reachable from marriage; also surface a
  "blood" button on a roster/unit-detail card to open the tree rooted at that unit
  (inject `TargetUnitId` before `AddChild`, exactly like the existing mount).

### 4.3 Optional pure extension (testable)
If a wider tree is wanted, add a pure `Core/Pedigree/PedigreeLayout.cs` that, given
a `PedigreeGraph`, returns normalized (x, y) slots per node (column = sibling index,
row = generation). Unit-test the layout (deterministic positions, no overlap,
self+spouse adjacent). The overlay then just maps slots to pixels -- keeping all
math in the tested core.

### 4.4 Do-not-break
`PedigreeGraph` and `MarriageService` (incl. the opposite-gender guard) stay
exactly as they are. The tree is a read-only view of `ChronicleGlobal` bloodline
data.

---

## 5. Suggested sequencing

1. **Asset logistics (Section 1)** -- create the directories + two loader classes +
   the pure slug map and its test. Lowest risk, unblocks everything visual.
2. **Popups & formation snap (Section 3)** -- highest feel-per-line; reuses
   `RisingPopup` / `Punch` that already exist. Add the two pure style helpers + tests.
3. **Particles & magnitude shake (Section 2)** -- new `_effectLayer` +
   `HitEffectDirector`; add `ShakeProfile` + test.
4. **Lineage polish (Section 4)** -- portraits, band colors, optional
   `PedigreeLayout` + test.

Each step is a self-contained commit: build 0/0, all xUnit green (and growing),
then push.

---

## 6. Net effect on tests

Every pure helper proposed here (`AssetSlugs`, `ShakeProfile`, `PopupStyle`,
`PedigreeLayout`) is Godot-independent and **adds** xUnit coverage, so the test
total only grows from 630. The Godot view wiring (TextureRects, particles, tweens)
is validated by running the build and the game, never by mutating core logic.

---

## 7. File map (new vs touched)

New (additive):
```
res://Assets/Textures/Backgrounds/{epoch}.png      (art)
res://Assets/Textures/Enemies/{archetype}.png      (art)
Core/Assets/AssetSlugs.cs                           (+ test)
Core/Juice/ShakeProfile.cs                          (+ test)
Core/Juice/PopupStyle.cs                            (+ test)
Core/Pedigree/PedigreeLayout.cs                     (optional, + test)
UserInterface/BackgroundTextureLibrary.cs
UserInterface/EnemyTextureLibrary.cs
UI/HitEffectDirector.cs
```
Touched (render-only, no logic change):
```
UI/BattleUI.cs            (background TextureRect, enemy art, _effectLayer, popup style)
UI/FormationUI.cs         (snap Punch on last-placed slot)
UI/PedigreeOverlay.cs     (portraits, band colors, marriage link)
UI/JuiceDirector.cs       (RisingPopup honors fontScale -- backward compatible)
```

Nothing in `Core/Battle` resolution, `FormationBoard`, `DeploymentGate`,
`MarriageService`, the Japanese text layer, or the drag-drop payloads changes.
