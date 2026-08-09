# Architecture & Over-Engineering Audit

**Agent:** A2 · **Commit:** `10a2a13` · **Branch:** `dev` · **Date:** 2026-08-08
**Scope:** every first-party runtime `.cs` file (49, not 38 — see [Correction to recon](#correction-to-recon)),
all 8 `.asmdef` files, the 5 `ScriptableObject` assets, `docs/01`, `docs/02`, `docs/adr/0002`.

## Verdict

**The over-engineering suspicion is largely not borne out.** Applying the ten-point rubric file by
file across all 49 runtime files produced **5 confirmed hits**, four of them Minor or Nit, and rubric
items **#2 (factories/builders), #7 (premature perf), #8 (speculative extensibility) and #10 (leaky
abstraction over an unstable domain) have zero hits project-wide.** There is exactly one interface
with a single implementation (`IPredictedPeer`), and it is load-bearing for the assembly split rather
than decorative. `PredictedPlayer.cs` at 691 LOC is **not a god object**: git shows 713 lines added
against 22 deleted across 11 commits — 97% accretion, not rework — and every one of its three roles
(owner/server/remote) manipulates the same `_state` field, which is the definition of cohesion.

What the codebase actually suffers from is the **opposite failure in two places**: ambient global
state (9 static `Current`/`Instance`/`All` accessors acting as an undeclared service locator) and a
413-LOC `MainMenuController` that is simultaneously the composition root, the menu, the lobby and the
roster view. The real architectural defects found are a **type-level cycle** (`PlayerLife` ↔
`MatchDirector`) that the single `Snackdown.Gameplay` assembly hides, an **assembly nothing
references** (`Snackdown.Core`, 2 of its 3 references unused), and a **latent correctness bug**
surfaced by the dependency pass: `ConnectionApproval.CharacterCount` is a settable property that is
never set, so adding a 5th character skin silently makes it unselectable.

ADR 0002's reasoning **holds against the code as built** — it was *rejected*, the reuse claim was
deleted from `docs/01` instead, and the code matches that decision exactly. `Snackdown.Simulation`'s
reference to `Unity.Netcode.Runtime` is **compile-required, not a convenience**, and does not
contradict any published claim.

## Scorecard

| Dimension | Score /5 | Note |
|---|---|---|
| Abstraction budget vs. codebase size | **4.5** | 5 rubric hits / 49 files. One interface with one impl. No DI container, no event bus, no factories, no codegen. Restraint is the dominant trait. |
| Layering & dependency hygiene | **3.5** | Assembly graph is a clean DAG, but a type cycle (`PlayerLife`↔`MatchDirector`) and a namespace cycle (`Gameplay.Player`↔`Gameplay.Match`) hide inside one assembly. One dead assembly, two dead asmdef references, one broken isolation invariant. |
| SOLID / cohesion | **3.5** | `PredictedPlayer` is cohesive and defensible. `MainMenuController` is not. Global statics undercut D throughout. |
| Pattern density (restraint) | **5** | Zero DI containers, zero service-locator *classes*, zero ScriptableObject-architecture, zero custom reflection or codegen, 3 RPCs total. |
| Extensibility cost (abstraction tax) | **4.5** | MEASURED: the stun mechanic cost 4 runtime files, +43 lines, **0 deletions**. `PlayerMotor`'s "insertions not rewrites" claim is verified by git, not asserted. |
| Doc–code architectural fidelity | **3** | `docs/01` and ADR 0002 are unusually honest, but `docs/01:22` still lists "service locators" in `Core` where none exist, and the folder listing predates Phase 3. |

## Verified dependency graph

Built from the 8 `.asmdef` files and the `using` graph of all 49 runtime files — not from `docs/01`.

### Assemblies — confirmed acyclic

```
                        Snackdown.UI (4 files, 913 LOC)
                        │  refs: Connection, Netcode, Gameplay,
                        │        Unity.Netcode, Unity.InputSystem,
                        │        Unity.Multiplayer.Tools.NetworkSimulator, Unity.Collections
        ┌───────────────┼──────────────────┐
        ▼               ▼                  ▼
  Snackdown.Connection  Snackdown.Netcode  Snackdown.Gameplay (17 files, 2,326 LOC)
  (9 files, 1,253 LOC)  (9 files, 829)     │  refs: Connection, Simulation, Netcode, Input
        │                    │             └───────┬──────────┬──────────┐
        │                    ▼                     ▼          ▼          ▼
        │             Snackdown.Simulation  ◄───────┘   Snackdown.Input  Snackdown.Connection
        │             (6 files, 520 LOC)                (2 files, 154)
        │             refs: Unity.Netcode only
        │
  Snackdown.Core (2 files, 103 LOC)  ──►  Connection [UNUSED], Unity.Networking.Transport [UNUSED]
        ▲
        └── referenced by NOTHING
```

**No assembly cycles — confirmed** (`Simulation → {}`, `Input → {}`, `Connection → {}`,
`Netcode → {Simulation}`, `Core → {Connection}`, `Gameplay → {Connection, Simulation, Netcode, Input}`,
`UI → {Connection, Netcode, Gameplay}` is a strict DAG). This matches `docs/01-architecture.md:168-177`
exactly.

### Namespace-level cycle the asmdefs hide

`Snackdown.Gameplay` is one assembly containing 17 files across 5 namespaces, so the compiler cannot
see this:

```
Snackdown.Gameplay.Player  ◄──────────►  Snackdown.Gameplay.Match
        │  PredictedPlayer.cs:2  ──────────►  (MatchDirector, MatchPhase)
        │  PlayerLife.cs:3       ──────────►  (MatchConfig, MatchDirector)
        │  PlayerSpawnPoints.cs:1 ─────────►  (MatchDirector, MatchPhase)
        │  ◄───────── MatchDirector.cs:4      (PlayerLife)
        │  ◄───────── RoundReferee.cs:1       (PlayerLife)
        │  ◄───────── SpectatorCamera.cs:1    (PlayerLife)
Snackdown.Gameplay.Combat  ──►  Player, Match, Netcode   (HeadBounce.cs:2-4)
Snackdown.Gameplay.Fruits  ──►  Player (Fruit.cs:1), Match (FruitSpawner.cs:2)
Snackdown.Gameplay.Player  ──►  Snackdown.Connection (CharacterAppearance.cs:1)
```

### Type-level cycle

`PlayerLife` ↔ `MatchDirector` is a true bidirectional type dependency:
`PlayerLife.cs:137` and `:165` read `MatchDirector.Current`; `MatchDirector.cs:265` iterates
`PlayerLife.All` and calls `life.ServerReset()`. See **F-A2-2**.

`NetworkSimulationLoop` is clean — it references only `IPredictedPeer` (`NetworkSimulationLoop.cs:27,
29, 50, 55, 79, 133`) and never the concrete character. The interface does its job at that one seam.

### `Snackdown.Simulation → Unity.Netcode.Runtime`: needed, not a convenience

**Compile-required.** Three of the six types in the assembly implement `INetworkSerializable` and use
`BufferSerializer<T>`/`IReaderWriter`:

- `PlayerState.cs:16,40` — `public struct PlayerState : INetworkSerializable`
- `InputCommand.cs:14,41`
- `InputPacket.cs:15,21`

Removing the reference would require either (a) moving serialization into `Snackdown.Netcode` via
hand-written read/write helpers, adding a file and a second place that must be kept in sync with the
struct's fields — the exact failure `PlayerState`'s own remarks (`PlayerState.cs:9-15`) warn about —
or (b) a generic wire type, which **ADR 0002 proves does not compile** under NGO 2.11's IL
post-processor.

**It does not undermine any published claim.** `docs/01:170` says `Snackdown.Simulation → (nothing of
ours)`, which is true. `docs/01:16-17` calls it "the state, and the pure step over it", and `docs/02`
is explicit that purity means re-runnability, not engine-independence — `docs/02:54-60` states the
motor uses `Physics2D` casts deliberately. See **F-A2-10** for the one honest imprecision left.

## Over-engineering rubric — file-by-file results

All 49 first-party runtime files were opened and read. Hits per module:

| Assembly | Files | Runtime LOC | Confirmed hits | Rubric item |
|---|---:|---:|---:|---|
| `Snackdown.Core` | 2 | 103 | **1** | #5 (a layer that only exists) — F-A2-3 |
| `Snackdown.Connection` | 9 | 1,253 | **1** | #4 (one live value, never changed) — F-A2-5 |
| `Snackdown.Simulation` | 6 | 520 | **0** | — |
| `Snackdown.Netcode` | 9 | 829 | **2** | #1 (F-A2-1), #3 mitigated (F-A2-1a) |
| `Snackdown.Gameplay` | 17 | 2,326 | **0** | — |
| `Snackdown.Input` | 2 | 154 | **0** | — |
| `Snackdown.UI` | 4 | 913 | **1** | #9 fragmentation-inverse: one class doing 4 jobs — F-A2-6 |
| **Total** | **49** | **6,098** | **5** | **0.10 hits/file** |

**Rubric items with zero hits project-wide:**

- **#2 (generics/factories/builders where a constructor would do)** — the only static factories are
  `ConnectionRequest.Host/Join` (`ConnectionRequest.cs:24-36`, 14 lines), `ConnectionResult.Ok/Failed`
  over a private ctor (`ConnectionResult.cs:63-67`, idiomatic result type), and
  `PlayerState.AtPosition` (`PlayerState.cs:58`, one line). No generic parameter appears in any
  first-party type. No builder exists.
- **#7 (premature performance work)** — every allocation-avoidance measure is on the reconciliation
  replay path, which the ADR argues for explicitly (`adr/0002:101-108`) and which
  `PeerCollisionTests.cs:155` exercises. Total fixed cost per player: `PredictionBuffer` 1024 slots ≈
  41 KB + `WorldSnapshotBuffer` 128×8 ≈ 25 KB ≈ **66 KB/player, 264 KB for a full lobby**
  (`ESTIMATED`, formula below). Not premature; the one oversized number is called out as a Nit in
  F-A2-11.
- **#8 (speculative extensibility)** — checked and rejected. `ArenaCatalog` holds one arena, which
  looks like a hit, but `docs/03-roadmap.md:81` has "**A second arena**" as an explicit open item, and
  the type is 56 LOC. `CharacterCatalog` holds 4 real entries; `FruitTable` holds 8 with real weights.
  None of these are hooks for content that does not exist.
- **#10 (abstraction over an unstable domain)** — `IPredictedPeer` has not changed shape since it was
  created in `e99a6fb`. Two subsequent gameplay features (stun `c2a0df6`, peer collision `b46adc3`)
  added capability through `PlayerState` and concrete methods on `PredictedPlayer` and required **zero**
  edits to the interface. It is stable, not leaky.

**Rubric #6 (custom infrastructure replacing an engine feature): 3 instances, all documented, all
central to the project's stated pillar.**

| Custom infra | Replaces | Documented rationale | Verdict |
|---|---|---|---|
| `PredictedPlayer` + `SnapshotFrame` + `NetworkSimulationLoop` | `NetworkTransform` | `docs/02:8` — NGO ships no client prediction; `NetworkTransform` cannot rollback-replay | **Earned.** This *is* the project. Confirmed absent from `Player.prefab` (components are `NetworkObject`, `PredictedPlayer`, `PlayerLife`, `InputReader`, `CharacterAppearance`, `VisualSmoother`). |
| `PlayerMotor` casts | `Rigidbody2D` dynamic solver | `docs/02:54-64`, `PlayerMotor.cs:10-22` — `Physics2D.Simulate` steps every body at once and its contact ordering is not reproducible | **Earned.** |
| `InputReader` code-built actions | `.inputactions` asset | `InputReader.cs:16-18` — no rebinding yet, no asset dependency | **Defensible but stale.** See F-A2-12. |

## SOLID assessment — both directions

### `PredictedPlayer.cs` (691 LOC) — cohesive, not a god object

The brief asks for a determination. **It is a legitimately cohesive predicted-entity implementation.**
The evidence:

1. **Single mutable core.** Every one of its ~25 methods reads or writes exactly one piece of state:
   `_state` (`PredictedPlayer.cs:50`). Owner prediction (`:318`), server simulation (`:453`),
   reconciliation replay (`:571`), interpolation (`:599`), stun (`:629`), bounce (`:637`) and teleport
   (`:685`) all converge on it. A god object has several unrelated states; this has one.
2. **Churn is accretion, not rework.** `git log --numstat` over its 11 commits: **713 lines added, 22
   deleted.** A file being fought with shows large deletion counts. This file was appended to, feature
   by feature, and never restructured.
3. **The role split is inherent, not accidental.** The owner/server/remote trichotomy
   (`PredictedPlayer.cs:15-28`) is imposed by NGO's single-component-per-object model, not by the
   author. Splitting into `OwnerPredictor` / `ServerAuthority` / `RemoteInterpolator` components would
   require all three to share `_state` — reintroducing exactly the coupling the split was meant to
   remove, plus three `GetComponent` lookups.

**What can honestly be lifted (~130 LOC, and it is optional):** the debug telemetry block
(`:160-234` — `_stats`, `_recorder`, `TransportRtt`, `WriteRunMetrics`, 8 read-only properties) is a
different concern with a different consumer (`NetDebugOverlay`). Moving it to a `PredictionTelemetry`
component would take the file to ~560 LOC. This is a Minor cleanup, not a defect.

### Speculative generality — one instance

`IPredictedPeer` (F-A2-1) is the only interface in the project with a single implementation. There is
no test double: **no test file references it** (verified across all 8 `Assets/Tests/EditMode/*.cs`).

`IConnectionProvider` is **not** speculative — two real implementations
(`DirectConnectionProvider.cs:23`, `RelayConnectionProvider.cs:30`), both constructed and both
selectable at runtime via `MainMenuController.cs:139-141`. The abstraction's own stated payoff test
("if adding Relay had required more of this file than a label and a constructor call, it would not
have been worth having", `MainMenuController.cs:152-155`) is **verifiably met**: the entire
provider-specific surface in the UI is `MainMenuController.cs:163-173`, 11 lines.

### Concrete coupling / DIP violations — the real weakness

Nine pieces of ambient global state serve as the project's undeclared service locator:

| Static | File:line | Consumers |
|---|---|---|
| `MatchDirector.Current` | `MatchDirector.cs:96` | **14 call sites** across 8 files |
| `RoundReferee.Current` | `RoundReferee.cs:68` | `EndScreenController.cs:60` |
| `ArenaBounds.Current` | `ArenaBounds.cs:28` | `SpectatorCamera.cs:107` |
| `ConnectionApproval.Current` | `ConnectionApproval.cs:73` | `SessionRoster.cs:86` |
| `NetworkSimulationLoop.Instance` | `NetworkSimulationLoop.cs:43` | `NetDebugOverlay.cs:138` |
| `NetworkSimulationLoop.Players` (static list) | `NetworkSimulationLoop.cs:27` | 4 files |
| `NetworkSimulationLoop.AfterServerSimulation` (static event) | `NetworkSimulationLoop.cs:42` | `HeadBounce.cs:47` |
| `PlayerLife.All` (static list) | `PlayerLife.cs:82` | `RoundReferee.cs`, `MatchDirector.cs`, `SpectatorCamera.cs` |
| `PredictedPlayer.PredictionEnabled` / `VisualSmoother.SmoothingEnabled` | `:158` / `VisualSmoother.cs:30` | `NetDebugOverlay.cs:44-45` |

Each is individually justified in a `<remarks>` block, and the justifications are sound (spawn-order
races, per-process isolation under Multiplayer Play Mode). Collectively they are the reason
`docs/01:22` can list "service locators" under `Core` while `Core` contains none — the pattern is
real, it is just distributed and undeclared. See **F-A2-7**.

## Abstraction tax — MEASURED, from real commits

The brief asks what it costs to add one gameplay ability. Two of the three mechanics in the project
were added *after* the assembly split, so this is measurable rather than estimated.

### The stun mechanic (`c2a0df6`, "Let players stomp each other without breaking prediction") — MEASURED

| File | +lines | −lines |
|---|---:|---:|
| `Gameplay/Combat/HeadBounce.cs` (new) | 95 | 0 |
| `Gameplay/Player/PredictedPlayer.cs` | 24 | 0 |
| `Simulation/PlayerState.cs` | 13 | 0 |
| `Simulation/PlayerMotor.cs` | **6** | **0** |
| `Tests/EditMode/StunTests.cs` (new) | 139 | 0 |
| **Runtime total** | **138** | **0** |

`PlayerMotor.Simulate`'s documented claim — *"ordered steps so later mechanics are insertions rather
than rewrites"* (`PlayerMotor.cs:23-26`, `docs/02:68-79`) — **is verified**: the stun step cost
6 added lines and **zero deleted lines** in the motor. One new step method
(`PlayerMotor.cs:58-63`) plus one line in the ordered list (`PlayerMotor.cs:37`).

### The peer-collision mechanic (`b46adc3`) — MEASURED

| File | +lines | −lines |
|---|---:|---:|
| `Simulation/PlayerMotor.cs` | 166 | 30 |
| `Netcode/WorldSnapshotBuffer.cs` (new) | 77 | 0 |
| `Simulation/SimulationContext.cs` (new) | 51 | 0 |
| `Gameplay/Player/PredictedPlayer.cs` | 44 | 3 |
| **Runtime total** | **338** | **33** |

Larger because it added a *capability* (the motor could not previously see the world), not a step.
Once `SimulationContext` existed, the next mechanic that needs the world costs an insertion again.

### Projected: adding a dash — ESTIMATED

Traced through the actual code path:

| # | File | Change |
|---|---|---|
| 1 | `Simulation/MovementConfig.cs` | 3 fields (`DashSpeed`, `DashDuration`, `DashCooldown`) |
| 2 | `Settings/MovementConfig.asset` | 3 values (Unity YAML, via Inspector) |
| 3 | `Simulation/PlayerState.cs` | 2 timer fields + 2 `SerializeValue` calls |
| 4 | `Simulation/InputCommand.cs` | 1 bit constant + `Pack` overload — **no new wire field**, the `Buttons` byte has 6 spare bits |
| 5 | `Simulation/PlayerMotor.cs` | 1 `DashStep` method + 1 line in `Simulate` |
| 6 | `Input/InputReader.cs` | 1 `InputAction` + `ConsumeDashPressed()` |
| 7 | `Gameplay/Player/PredictedPlayer.cs` | 1 line in `SampleInput` (`:364-369`) |

**7 files, ~55 lines, 0 deletions, 3 assemblies crossed.** Critically: **`IPredictedPeer` is not
touched, `NetworkSimulationLoop` is not touched, and no RPC signature changes.**

### Projected: a fruit effect that feeds the predicted `Move()` — ESTIMATED

E.g. a fruit granting temporary speed. Traced: `FruitTable.Entry` (+1 field) → `Fruit.Update`
(`Fruit.cs:81`, +1 call) → new `PredictedPlayer.ServerApplyBoost` (mirrors `ServerApplyStun` at
`:626-630`, +5 lines) → `PlayerState.BoostTimer` + serialize (+3) → `PlayerMotor.HorizontalStep`
(`:65-71`, +2) → `MovementConfig.BoostMultiplier` (+1). **6 files, ~15 lines, 2 assets.**

### How much of that is the abstraction, and how much is netcode?

| Cost driver | Files | Avoidable by deleting layers? |
|---|---:|---|
| State must be complete on the wire (rollback requirement) | 2 (`PlayerState`, `InputCommand`) | **No** — inherent to reconciliation, see `PlayerState.cs:9-15` |
| Tunables live in an asset shared by both sides | 2 (`MovementConfig` + `.asset`) | **No** — `MovementConfig.cs:9-12`, client and server must read identical values |
| Input source is separate from input consumer | 2 (`InputReader`, `PredictedPlayer`) | **No** — a naïve controller needs this split too |
| The layer/assembly split itself | **0** | — |

**The abstraction tax attributable to Snackdown's layering is approximately zero.** Every file a dev
must touch would exist in a flat single-assembly design as well. That is a strong result and worth
saying plainly.

## Findings

### F-A2-1 — `IPredictedPeer` has one implementation and no test double; 3 of its 4 consumers immediately downcast

- **Severity**: Minor
- **Type**: Over-engineering
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Netcode/IPredictedPeer.cs:20-39` (40 LOC);
  sole implementation `Gameplay/Player/PredictedPlayer.cs:31`; downcasts at
  `Gameplay/Combat/HeadBounce.cs:77` (`peer is PredictedPlayer player`),
  `UI/NetDebugOverlay.cs:148` (`peer is not PredictedPlayer player`),
  `Gameplay/Player/PredictedPlayer.cs:657` (`peer is not PredictedPlayer other`). No test file
  references the type (verified across all 8 `Assets/Tests/EditMode/*.cs`).
- **What it is**: Rubric #1. One implementation, zero test doubles, and only one of four consumers
  (`NetworkSimulationLoop.cs:27-136`) actually consumes it as an abstraction. The other three iterate
  `ActivePlayers` and cast straight back to the concrete type.
- **Why it matters**: In isolation this reads as speculative generality. **It is not** — deleting it
  puts `Gameplay` types inside `Snackdown.Netcode.asmdef`, recreating the exact cycle the interface's
  own remarks describe (`IPredictedPeer.cs:10-16`) and the one ADR 0002 was written about. The cost is
  a 40-LOC file plus three `is`-pattern casts, i.e. the price of keeping the compiler enforcing the
  layering rule. Reported for completeness and defended, not as a defect to fix.
- **Recommendation**: Keep it. Optionally document in `docs/01` that the three downcasts are the
  accepted cost of the boundary — `NetDebugOverlay.cs:143-145` already does this well; `HeadBounce`
  and `CaptureWorld` do not.
- **Effort**: S

### F-A2-1a — `AfterServerSimulation` is a static event with one publisher and one subscriber

- **Severity**: Nit
- **Type**: Over-engineering
- **Confidence**: High
- **Evidence**: publisher `Netcode/NetworkSimulationLoop.cs:42,94`; sole subscriber
  `Gameplay/Combat/HeadBounce.cs:47,50`.
- **What it is**: Rubric #3, literally satisfied.
- **Why it matters**: Mitigated by the same argument as F-A2-1 — a direct call from
  `NetworkSimulationLoop` to `HeadBounce` would require `Snackdown.Netcode` to reference
  `Snackdown.Gameplay`, which is the forbidden edge. The event is the seam, and its remarks
  (`NetworkSimulationLoop.cs:35-41`) state the ordering guarantee it provides. Counted as a hit for
  rubric completeness; no action warranted.
- **Recommendation**: None.
- **Effort**: —

### F-A2-2 — `PlayerLife` ↔ `MatchDirector` is a true type cycle, hidden by the single `Gameplay` assembly

- **Severity**: Minor
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: `Gameplay/Player/PlayerLife.cs:137` and `:165` (`MatchDirector director =
  MatchDirector.Current`); `Gameplay/Match/MatchDirector.cs:265`
  (`foreach (PlayerLife life in PlayerLife.All) life.ServerReset();`). Namespace-level:
  `Player → Match` at `PredictedPlayer.cs:2`, `PlayerLife.cs:3`, `PlayerSpawnPoints.cs:1`;
  `Match → Player` at `MatchDirector.cs:4`, `RoundReferee.cs:1`, `SpectatorCamera.cs:1`.
- **What it is**: `docs/01:3-5` states the rule the whole layering exists for — *"lower layers never
  know about higher ones"* — and `docs/01:179-182` celebrates the assembly split for catching exactly
  this class of cycle at the `Netcode`/`Gameplay` boundary. The same cycle exists one level down,
  between `Gameplay.Player` and `Gameplay.Match`, and the compiler cannot see it because both
  namespaces live in one `.asmdef`.
- **Why it matters**: Not a runtime defect — it works. It matters because the project's own thesis is
  that assemblies make layering provable, and a reader who checks will find the rule holds only at the
  granularity the author chose to enforce it. In an interview this is the follow-up question. It also
  means `Gameplay` (2,326 LOC, 17 files, 5 namespaces) is now the largest assembly and its internal
  layering is convention-only.
- **Recommendation**: Cheapest honest fix: break the direction that carries the least weight.
  `MatchDirector.ServerReturnToLobby` is the only `Match → PlayerLife` write; hoisting the
  `life.ServerReset()` loop into `RoundReferee` (which already owns round lifecycle and already
  imports `PlayerLife`) removes `MatchDirector`'s dependency on `Player` entirely. Alternatively state
  in `docs/01` that intra-`Gameplay` layering is by convention, so the doc stops implying the compiler
  checks it.
- **Effort**: S

### F-A2-3 — `Snackdown.Core` is an assembly nothing references, with 2 of its 3 references unused

- **Severity**: Minor
- **Type**: Over-engineering
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Core/Snackdown.Core.asmdef` declares
  `["Snackdown.Connection", "Unity.Netcode.Runtime", "Unity.Networking.Transport"]`. Its two files use
  only `Unity.Netcode` and `UnityEngine.SceneManagement` (`AppBootstrap.cs:1-3`) and `UnityEngine`
  (`FrameRatePolicy.cs:1`). `grep -rn "Snackdown.Core"` across all `.cs` and `.asmdef` returns only its
  own namespace declarations — **no other assembly or file imports it.**
- **What it is**: Rubric #5 in its degenerate form — a layer that forwards nothing because nothing
  calls it. 103 LOC (68 + 35) occupying 1 of 8 assemblies, 1 `.csproj`, and one node in the compile
  graph. `docs/01:22` further describes it as holding "Bootstrap, scene flow, service locators" and
  `docs/01:37` as holding a "Phase 2: app state machine" — none of which exist.
- **Why it matters**: An 8-assembly graph on 6,098 LOC averages 762 LOC per assembly; this one holds
  103 and is a graph leaf with no dependents. It is the single clearest "structure for structure's
  sake" artifact in the project, and it is the one a reviewer will spot first because `docs/01`
  advertises it as a layer.
- **Recommendation**: Delete `Snackdown.Core.asmdef`; move `AppBootstrap.cs` into
  `Snackdown.Gameplay` (it already depends on nothing else) and leave `FrameRatePolicy.cs` wherever it
  lands — a `[RuntimeInitializeOnLoadMethod]` needs no assembly of its own. Update `docs/01:22,37,174`
  in the same commit. Note the CLAUDE.md rule: this deletes no Unity asset, only an `.asmdef` and its
  `.meta`, but it changes the layer diagram — worth confirming with Luca first.
- **Effort**: S

### F-A2-4 — `Snackdown.UI` reads the Input System directly, breaking the one invariant `Snackdown.Input` exists to enforce

- **Severity**: Minor
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: `Input/InputReader.cs:8-9` states the invariant — *"Nothing outside this assembly
  talks to the Input System"* — repeated at `Input/SpectatorInput.cs:16-18`. But
  `Assets/_Project/Scripts/UI/Snackdown.UI.asmdef` lists `"Unity.InputSystem"` in its references, and
  `UI/NetDebugOverlay.cs:41,44-47` reads `UnityEngine.InputSystem.Keyboard.current` and its
  `f1Key`…`f4Key` directly.
- **What it is**: A stated architectural invariant that is false in the shipped code, documented in
  two places in the very assembly it protects.
- **Why it matters**: The stated payoff of `Snackdown.Input` is that *"swapping the code-built actions
  for an `.inputactions` asset stays a single-folder change"* (`SpectatorInput.cs:17-18`). It is
  currently a two-folder change, and nobody would know until they tried. It also weakens the same
  claim's relevance to the Mobile/WebGL platform gap (F-A2-13).
- **Recommendation**: Either (a) move the F1–F4 debug bindings into a `DebugHotkeys` type inside
  `Snackdown.Input` and drop `"Unity.InputSystem"` from `Snackdown.UI.asmdef` — the compiler then
  enforces the invariant that is currently only asserted; or (b) soften the two XML remarks to say
  "nothing in the gameplay path". (a) is ~20 LOC and makes the claim true.
- **Effort**: S

### F-A2-5 — `ConnectionApproval.CharacterCount` is a settable property that is never set; a 5th skin would be silently unselectable

- **Severity**: Major
- **Type**: Correctness
- **Confidence**: High
- **Evidence**: `Connection/ConnectionApproval.cs:76` — `public int CharacterCount { get; set; } = 4;`.
  `grep -rn "CharacterCount"` across all `.cs` returns exactly three hits: the declaration and its two
  read sites (`:156`, `:183`), both `Mathf.Clamp(index, 0, CharacterCount - 1)`. **The setter is never
  called from anywhere.** The real count lives in `CharacterCatalog.Count`
  (`Gameplay/Player/CharacterCatalog.cs:31`), backed by `Settings/CharacterCatalog.asset` which today
  has 4 entries. `Snackdown.Connection` cannot reference `Snackdown.Gameplay`, so the two numbers have
  no compile-time link.
- **What it is**: Rubric #4 — a configuration switch with exactly one live value, never changed at
  runtime — that is simultaneously a duplicated constant across an assembly boundary.
- **Why it matters**: This is not stylistic. Adding a 5th entry to `CharacterCatalog.asset` makes
  index 4 valid in the catalog and clamped to 3 by approval, so a player who picks the new skin is
  admitted playing someone else's — silently, with no error, on both the payload path
  (`:183`) and the host path (`:156`). The failure appears in `CharacterAppearance.Apply`
  (`CharacterAppearance.cs:52`) as "the skin I chose isn't the one I got", which is far from its cause.
  `CharacterCatalog`'s own remarks (`:9-12`) warn that "order is the contract" — this is the other
  half of that contract, unenforced.
- **Recommendation**: Have `MainMenuController.Provider` (`MainMenuController.cs:135`, the only place
  `ConnectionApproval` is constructed) set `_approval.CharacterCount = catalog.Count` from a
  serialized `CharacterCatalog` reference — `Snackdown.UI` already references `Snackdown.Gameplay`, so
  no new edge is created. 3 lines. Alternatively make it a constructor parameter so it cannot be
  forgotten.
- **Effort**: S

### F-A2-6 — `MainMenuController` is the composition root, the menu, the lobby and the roster view in one 413-LOC MonoBehaviour

- **Severity**: Major
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/UI/MainMenuController.cs`. Four distinct responsibilities:
  (1) **composition root** — constructs `ConnectionApproval` and both providers, `:129-146`;
  (2) **connection lifecycle** — `CancellationTokenSource` ownership, busy state, `async void` handlers,
  `:180-238`; (3) **screen state machine** — `ShowMenu`/`ShowLobby` and 15 cached `VisualElement`
  fields, `:45-60, 244-276`; (4) **roster view** — builds `VisualElement` rows per player every time
  the roster changes, `:294-338`. Second-largest file in the project and 4 touches in `main..dev`.
- **What it is**: The clearest SRP break in the codebase, and the one place where **under**-engineering
  shows: there is no seam between "who owns the connection objects" and "what the menu looks like".
- **Why it matters**: The composition root is buried inside a `MonoBehaviour` in the Lobby scene, so
  the only way to construct a provider is to have that scene loaded. That is also why F-A2-5 has no
  natural fix location and why `ConnectionApproval.CharacterCount` was never wired: the object that
  should wire it is a UI screen. Concretely, a headless or automated connection test cannot be written
  without instantiating a UI document.
- **Recommendation**: Lift responsibilities (1) and (2) into a small `SessionLauncher`
  `MonoBehaviour` living in the **Bootstrap** scene (which already outlives the lobby, per
  `docs/01:71-78`), exposing `Task<ConnectionResult> HostAsync/JoinAsync` and a `CurrentProvider`
  property. `MainMenuController` then keeps (3) and (4) at ~250 LOC and stops owning objects that
  outlive it. This also gives F-A2-5 an obvious home.
- **Effort**: M

### F-A2-7 — Nine ambient statics are the project's real service locator; `docs/01` places one in `Core`, where none exists

- **Severity**: Minor
- **Type**: Maintainability
- **Confidence**: Medium
- **Evidence**: the table in [Concrete coupling](#concrete-coupling--dip-violations--the-real-weakness)
  above — 9 statics with file:line. `MatchDirector.Current` alone has **14 call sites**.
  `docs/01-architecture.md:22` lists `Core` as holding "Bootstrap, scene flow, **service locators**";
  `Core` contains `AppBootstrap.cs` (68 LOC) and `FrameRatePolicy.cs` (35 LOC) and no locator.
- **What it is**: The pattern the doc names exists — it is just distributed across 9 types in 4
  assemblies instead of being one declared thing in `Core`.
- **Why it matters**: Two consequences. First, every consumer of `MatchDirector.Current` must
  null-check it (`PlayerLife.cs:138`, `FruitSpawner.cs:71`, `RoundReferee.cs:91`,
  `SpectatorCamera.cs:86`, `PredictedPlayer.cs:143`, `LoadingScreenController.cs:56`,
  `EndScreenController.cs:55`, `MainMenuController.cs:378`) — 8 identical guards, and forgetting one
  is a `NullReferenceException` in a scene transition. Second, it is untestable: nothing that reads
  `MatchDirector.Current` can be exercised in EditMode, which is visibly why the 8 test files cover
  `Simulation`, `Netcode`, `FruitTable`, `ArenaBounds` and `ConnectionApproval` and **nothing** in
  `Gameplay/Match` or `Gameplay/Player`.
- **Recommendation**: Do **not** introduce a DI container — that would be the over-engineering this
  audit is looking for. Two cheap steps: (a) fix `docs/01:22` to say what `Core` actually holds; (b)
  where a consumer already lives on the same object or scene, prefer a serialized reference over the
  static. Accept the rest as a documented, deliberate pattern and say so in `docs/01`.
- **Effort**: S (docs) / M (partial de-statification)

### F-A2-8 — `ReconciliationStats` and `RunRecorder` duplicate the same aggregation over the same sample stream

- **Severity**: Minor
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: both are fed from the same two adjacent lines,
  `Gameplay/Player/PredictedPlayer.cs:577-578`. `ReconciliationStats.TryGetWindow`
  (`Netcode/ReconciliationStats.cs:58-95`) computes mean/max error and mean/max replayed ticks over a
  5-second window; `RunRecorder.Write` (`Netcode/RunRecorder.cs:85-111`) computes mean/max error and
  mean/max replayed ticks over the whole run. Two independent ring/list stores of the same `{Time,
  Error, ReplayedTicks}` triple (`ReconciliationStats.cs:28-33`, `RunRecorder.cs:26-33`), 246 LOC
  combined — 30% of `Snackdown.Netcode`.
- **What it is**: Not rubric over-engineering — the *capability* is justified and defended below. It is
  duplicated arithmetic across two types with different retention policies.
- **Why it matters**: Any change to what a "reconciliation sample" contains (the RTT columns were
  added to only one of them — `RunRecorder.cs:31-32` has `MeasuredRttMs`/`TransportRttMs`,
  `ReconciliationStats` does not) has to be made twice, and they have already drifted.
- **Recommendation**: One append-only sample store with two views: `Window(now, seconds)` and
  `Summary()`. Saves roughly 60–80 LOC and removes the drift. Low priority — nothing is broken.
- **Effort**: S

### F-A2-9 — A runtime assembly references the Multiplayer Tools NetworkSimulator with no define constraint

- **Severity**: Minor
- **Type**: Scope-drift
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/UI/Snackdown.UI.asmdef` lists
  `"Unity.Multiplayer.Tools.NetworkSimulator.Runtime"`; used at `UI/NetDebugOverlay.cs:5,101,106,113`.
  No `.asmdef` in the project declares `defineConstraints` or `includePlatforms` except the test
  assembly. `NetDebugOverlay` has no `#if` guard and no enable flag — `OnGUI` runs whenever
  `NetworkManager.Singleton.IsListening` (`:126`).
- **What it is**: Debug tooling wired into the release compile graph from a runtime assembly, plus
  `OnGUI` (a legacy IMGUI path with per-frame allocation characteristics) always on in a shipped
  build.
- **Why it matters**: For a portfolio build this is mostly a "what does a shipped build contain"
  question, and it is the sort of thing a reviewer notices. It also couples `Snackdown.UI` — the
  top layer of the diagram in `docs/01` — to a tooling package that the diagram does not mention.
  A5/A6/A7 will quantify the build-size and frame-cost side; the architectural point is the missing
  seam.
- **Recommendation**: Add `"defineConstraints": ["UNITY_EDITOR || DEVELOPMENT_BUILD"]` to a new
  `Snackdown.UI.Debug.asmdef` holding `NetDebugOverlay` alone, or guard the file with
  `#if UNITY_EDITOR || DEVELOPMENT_BUILD`. The first is cleaner and removes the package reference from
  the release graph entirely.
- **Effort**: S

### F-A2-10 — "The pure step" is imprecise: `PlayerMotor` reads the live physics scene

- **Severity**: Nit
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: `docs/01-architecture.md:16-17` labels the Simulation layer "the state, and the **pure**
  step over it". `PlayerMotor.cs:11-13` claims *"It never reads `Time`, a `Transform`, or a
  `Rigidbody2D`"* — carefully worded and true. But `PlayerMotor.cs:137, 162, 188` call
  `Physics2D.BoxCast` against the live scene, and `MovementConfig.GroundMask` (`:54`) selects layers in
  it.
- **What it is**: The purity claim is *re-runnability*, which the code genuinely achieves and which
  `docs/02:54-64` explains at length. The one-word label "pure" in the layer diagram invites the
  stricter reading, and the stricter reading is false: `Simulate` is only reproducible while the static
  geometry is unchanged — which holds today because arenas have no moving platforms.
- **Why it matters**: Almost nothing, until someone adds a moving platform. Then `Simulate` becomes
  genuinely non-reproducible under replay and the failure will look like random desync. `SimulationContext`'s
  remarks (`SimulationContext.cs:22-25`) already name moving platforms as the intended extension point,
  which is the right answer — but the layer diagram does not carry that caveat.
- **Recommendation**: One-line change in `docs/01:16-17`: "the *replayable* step over it — pure with
  respect to time and object state; static geometry is read through casts". No code change.
- **Effort**: S

### F-A2-11 — Residue of the assembly split: an orphaned asset, a stale `m_EditorClassIdentifier`, and a stale `<see cref>`

- **Severity**: Nit
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**:
  1. `Assets/_Project/Settings/MovementConfig.asset` carries
     `m_EditorClassIdentifier: Assembly-CSharp::Snackdown.Gameplay.Player.MovementConfig` — the
     pre-split location. The other four config assets all correctly read
     `Snackdown.Gameplay::Snackdown.Gameplay.…`. The type now lives at
     `Snackdown.Simulation::Snackdown.Simulation.MovementConfig`.
  2. `Netcode/VisualSmoother.cs:15` — `<see cref="Snackdown.Gameplay.Player.PlayerMotor"/>`. That
     namespace has no `PlayerMotor`; it is `Snackdown.Simulation.PlayerMotor`.
  3. `Assets/InputSystem_Actions.inputactions` is referenced by **zero** first-party `.cs` files
     (`grep -rn "InputSystem_Actions\|InputActionAsset" --include=*.cs Assets/` returns nothing) —
     Unity's template asset, never adopted, `InputReader` builds its actions in code instead
     (`InputReader.cs:31-47`).
  4. `PredictionBuffer.Capacity = 1024` (`Netcode/PredictionBuffer.cs:20`) is ~34 s at 30 Hz; its own
     comment says *"far more than any recoverable desync needs"*, and `Reconcile` hard-snaps rather
     than replaying past it (`PredictedPlayer.cs:542-551`).
- **What it is**: Four small artifacts of the `e99a6fb` assembly split and earlier scaffolding.
  `m_EditorClassIdentifier` is only a fallback when the script GUID fails to resolve, so nothing is
  currently broken — but it is stale metadata pointing at a type location that no longer exists.
- **Why it matters**: `CLAUDE.md` "Before every commit" item 3 forbids leftovers and orphaned files
  explicitly. Item 1 in particular is the kind of thing that turns into a null `MovementConfig` on a
  fresh clone if a GUID ever changes — and `PredictedPlayer.OnNetworkSpawn:238-246` handles that
  case by disabling the component, so the symptom would be "the character does not move" with a
  single console error.
- **Recommendation**: (1) touch `MovementConfig.asset` through the Inspector so Unity rewrites the
  identifier — **an asset edit, so ask Luca first per CLAUDE.md**; (2) one-word fix to the `cref`;
  (3) delete the orphaned `.inputactions` + `.meta`, or state in `docs/01` why it is kept;
  (4) optional: `Capacity = 256` (8.5 s) saves ~31 KB/player and matches the documented intent.
- **Effort**: S

### F-A2-12 — Under-engineering: no input seam for the stated Mobile and WebGL targets

- **Severity**: Major
- **Type**: Scalability
- **Confidence**: Medium
- **Evidence**: `Input/InputReader.cs:31-47` binds `<Keyboard>/a`, `<Keyboard>/d`,
  `<Keyboard>/leftArrow`, `<Keyboard>/rightArrow`, `<Gamepad>/leftStick/x`, `<Gamepad>/dpad/*`,
  `<Keyboard>/space`, `<Keyboard>/w`, `<Keyboard>/upArrow`, `<Gamepad>/buttonSouth`.
  `Input/SpectatorInput.cs:31-43` is keyboard + gamepad only. No `<Touchscreen>` binding, no on-screen
  control, no `.inputactions` asset in use (F-A2-11), no `defineConstraints`/`includePlatforms` on any
  runtime `.asmdef`. `Assets/_Project/Scripts/UI/*.uxml` layouts carry no touch affordances.
  Project context lists `target_platforms: ["PC (Windows)", "Mobile", "WebGL"]`.
- **What it is**: The opposite failure to over-engineering — a missing seam where the project's own
  stated targets guarantee it will be needed. `InputReader.cs:16-18` says the seam is the assembly
  boundary and swapping in a real action asset is "a one-folder change"; F-A2-4 shows that claim is
  already false, and adding touch would also require UI-layer work the assembly boundary does not
  cover.
- **Why it matters**: Two of three stated target platforms are unreachable today with no code path to
  them. This is the single largest gap between stated scope and built scope in the architecture. It
  interacts with the netcode design too: `InputCommand.MoveX` is quantized to −1/0/1
  (`InputCommand.cs:22-23`) precisely so client and server agree, which a virtual thumbstick would
  feed correctly — so the *simulation* is ready for touch and only the input layer is not. That is
  worth saying, because it means the gap is genuinely small.
- **Recommendation**: Either (a) narrow the stated targets to PC — a legitimate portfolio decision,
  and one line in `README.md`; or (b) add a `<Touchscreen>`-driven `InputSource` alongside
  `InputReader` behind the same `MoveX`/`JumpHeld`/`ConsumeJumpPressed` surface. (b) is ~1 day for the
  gameplay path; the UI Toolkit menus are a separate cost. Decide before Phase 5 closes, because the
  README claim is currently unsupported.
- **Effort**: S (a) / L (b)

## Quantified Estimates

| Metric | Value | Tag | Formula / inputs |
|---|---:|---|---|
| First-party runtime `.cs` files | **49** | MEASURED | `find Assets/_Project/Scripts -name '*.cs'`; recon's 38 was an undercount (see below) |
| First-party runtime LOC | **6,098** | MEASURED | `wc -l` summed per assembly; Connection 1,253 · Gameplay 2,326 · UI 913 · Netcode 829 · Simulation 520 · Input 154 · Core 103 |
| Test LOC | **1,195** | MEASURED | 8 files under `Assets/Tests/EditMode/`; 16.4% of total C# |
| Over-engineering rubric hits | **5** | MEASURED | Rubric applied to all 49 files; 0.10 hits/file |
| Rubric items with zero hits | **4 of 10** | MEASURED | #2, #7, #8, #10 |
| Interfaces with one implementation | **1 of 2** | MEASURED | `IPredictedPeer` (1 impl); `IConnectionProvider` (2 impls, both live) |
| Assemblies | **8** | MEASURED | Mean 762 runtime LOC each; `Core` holds 103 (13.5% of the mean) with zero dependents |
| Ambient static accessors | **9** | MEASURED | Table above; `MatchDirector.Current` alone has 14 call sites |
| Largest file | **691 LOC** (`PredictedPlayer.cs`) | MEASURED | 11.3% of runtime LOC in one file |
| `PredictedPlayer` churn ratio | **713 added / 22 deleted** | MEASURED | `git log --numstat` over 11 commits; 97% accretion |
| Abstraction tax — stun mechanic | **4 runtime files, +138 / −0** | MEASURED | commit `c2a0df6`, excluding tests, `.meta`, `.prefab`, docs |
| Abstraction tax — `PlayerMotor` insertion | **+6 / −0 lines** | MEASURED | commit `c2a0df6`, `Simulation/PlayerMotor.cs` only |
| Abstraction tax — peer collision | **4 runtime files, +338 / −33** | MEASURED | commit `b46adc3` |
| Projected tax — a dash | **7 files, ~55 lines, 0 deletions** | ESTIMATED | Traced through `MovementConfig` → `PlayerState` → `InputCommand` → `PlayerMotor` → `InputReader` → `PredictedPlayer.SampleInput` + 1 `.asset`. No wire-size change: `InputCommand.Buttons` uses 2 of 8 bits (`InputCommand.cs:16-17`). |
| Projected tax — new fruit effect into `Move()` | **6 files, ~15 lines, 2 assets** | ESTIMATED | Traced: `FruitTable.Entry` → `Fruit.cs:81` → new `ServerApplyBoost` (mirrors `ServerApplyStun`, `:626-630`) → `PlayerState` + serialize → `PlayerMotor.HorizontalStep` → `MovementConfig` |
| Tax attributable to the layer split | **~0 files** | ESTIMATED | Every file in the two traces above would also be touched in a flat single-assembly design; see table in [abstraction tax](#how-much-of-that-is-the-abstraction-and-how-much-is-netcode) |
| Fixed replay-buffer memory | **~66 KB/player, ~264 KB/lobby** | ESTIMATED | `PredictionBuffer` 1024 × (4+1+6+29 B struct, padded ≈ 40 B) ≈ 41 KB + `WorldSnapshotBuffer` 128 frames × 8 bodies × 20 B ≈ 20 KB + 128 × 12 B frame headers ≈ 1.5 KB. Assumes .NET struct layout with 4-byte alignment; not profiler-verified. |
| Profiler captures in repo | **0** | MEASURED | `git ls-files \| grep -iE '\.raw$\|profil\|metric\|\.csv$'` → only URP volume profiles. `RunRecorder` writes to `Application.persistentDataPath`, which is outside the repo, so no run artifact is version-controlled. |

### The 3 layers I would delete, and exactly what breaks

| # | Delete | LOC / structure saved | What breaks | Verdict |
|---|---|---|---|---|
| 1 | **`Snackdown.Core.asmdef`** (not its code) | 0 runtime LOC; **1 of 8 assemblies (12.5% of the graph)**, 1 `.csproj`, 2 dead asmdef references | **Nothing.** No `.cs` or `.asmdef` imports `Snackdown.Core` (verified by grep). `FrameRatePolicy` is a `[RuntimeInitializeOnLoadMethod]` and runs from any assembly; `AppBootstrap` is a scene component whose GUID reference in `Bootstrap.unity` survives an assembly move. | **Do it.** F-A2-3. |
| 2 | **`"Unity.InputSystem"` from `Snackdown.UI.asmdef`** (or `Snackdown.Input` itself) | ~1 asmdef edge; makes a documented invariant compiler-enforced instead of asserted | `NetDebugOverlay.cs:41,44-47` stops compiling until the F1–F4 hotkeys move into `Snackdown.Input` (~20 LOC). Nothing else in `Snackdown.UI` touches the Input System. | **Do it** — deleting the *reference*, not the assembly. F-A2-4. |
| 3 | **One of `ReconciliationStats` / `RunRecorder`** (merge into one store, two views) | **~60–80 of 246 LOC**, and removes the drift that already put RTT columns in one and not the other | Nothing external: `NetDebugOverlay.cs:159` reads `TryGetReconciliationWindow` and `PredictedPlayer.WriteRunMetrics:227` writes the CSV; both survive a merged type. `docs/05-validation.md`'s CSV format is preserved. | **Do it, low priority.** F-A2-8. |

**The layer I considered deleting and will not:** `IPredictedPeer` (40 LOC) plus the
`Snackdown.Netcode` assembly boundary it defends. Merging `Netcode` into `Gameplay` would delete the
interface, turn three `is`-pattern downcasts into direct access, and drop the graph to 6 assemblies —
and ADR 0002 itself argues the netcode layer is not reusable, which superficially supports it.
**Do not.** `docs/01:159-160` names the one surviving promise of that ADR — *"gameplay depends on
netcode, never the reverse"* — and describes it as *"enforced rather than promised"*. The assembly
boundary is the enforcement. Deleting it turns the project's most defensible architectural claim back
into a convention, to save 40 lines.

### Correction to recon

`00-recon.md` reports "38 first-party runtime files" and lists `Snackdown.Gameplay` as 14 files. The
actual counts are **49 runtime `.cs` files** and **17** in `Gameplay`
(`Combat` 1 · `Fruits` 3 · `Match` 8 · `Player` 5). Recon's ~6,200 runtime LOC figure is close —
measured **6,098**. Every per-file conclusion in this report is from a full read, so nothing here
depends on the recon count.

## What is genuinely good here

This section is mandatory and, in this codebase, easy to fill. Six places where complexity is
correctly earned, and one where its *absence* is:

1. **ADR 0002 is the best artifact in the repository, and it rejects its own proposal.** It compiles a
   throwaway probe against NGO 2.11, reproduces a `NullReferenceException` inside
   `Mono.Cecil.ImportGenericContext.TypeParameter` from the IL post-processor
   (`adr/0002:49-55`), narrows the boundary with a second probe (`adr/0002:66-82`), designs the
   correct refactor — and then **declines to do it** because *"the reuse was hypothetical"*
   (`adr/0002:190`). It then deletes the false sentence from `docs/01` instead. **The code matches the
   decision exactly**: `Netcode/PredictionBuffer.cs:1`, `SnapshotFrame.cs:1`,
   `SnapshotInterpolator.cs:1`, `WorldSnapshotBuffer.cs:1` all still import `Snackdown.Simulation`, as
   the ADR says they will, and `docs/01:150-160` says so out loud. An engineer who writes down the
   refactor they *didn't* do, with the measurement that decided it, is demonstrating the exact judgment
   this audit was hired to test for. The NGO codegen finding alone is publishable.
2. **`SimulationContext` / `PeerBody` — the hardest thing in the project, done right.** Predicted
   player-vs-player collision requires each replayed tick to see everyone *as they were on that tick*.
   `SimulationContext.cs:6-10` states exactly that, `WorldSnapshotBuffer.cs:9-22` implements it as a
   ring keyed by `tick % Capacity`, and `PredictedPlayer.CaptureWorld:650-672` fills it only for the
   live tick. The struct borrows an array rather than owning a list (`SimulationContext.cs:31,39-44`)
   so the replay loop at `PredictedPlayer.cs:565-573` allocates nothing. This is real netcode
   engineering, and `PeerCollisionTests.cs` (160 LOC, 8 tests) proves it without a `NetworkManager`.
3. **`PlayerState`'s completeness invariant is stated, justified, and obeyed.** `PlayerState.cs:9-15`
   explains why coyote and jump-buffer timers are on the wire even though they cost bytes, and
   `:29-34` extends the same argument to `StunTimer`. When the stun feature landed (`c2a0df6`), the
   invariant was honoured: 13 lines to `PlayerState`, including the serializer. That is a design rule
   that survived contact with a feature.
4. **`SnapshotFrame.IsTeleport` — a subtle bug found, understood, and fixed with a number attached.**
   `SnapshotFrame.cs:28-38` records that a spawn placement contributed a 3.8-unit error against a real
   correction of 0.29, and that the flag is re-announced for 3 consecutive snapshots because
   *"snapshots travel unreliably, so a flag sent once is a flag that can be lost"* — the same
   redundancy logic as `InputPacket`. This is the class of detail that separates someone who read about
   reconciliation from someone who has debugged it.
5. **The reconciler-not-listener pattern, applied consistently and for a stated reason.**
   `LoadingScreenController.cs:13-21`, `EndScreenController.cs:13-16` and `SpectatorCamera.cs:16-19`
   all poll state per frame instead of subscribing, and all three cite the same failure: a client that
   misses one notification stays wrong forever. `LoadingScreenController.cs:123-135` goes further and
   handles the in-flight-operation case that broke the first attempt. Three files, one idea, applied
   deliberately — this is coherence, not repetition.
6. **`FrameRatePolicy` — 35 lines with a measured justification.** `FrameRatePolicy.cs:8-21` explains
   that two uncapped renderers under Multiplayer Play Mode starved the tick, cites *"122 starved ticks
   and 1.66 corrections per second under 2% packet loss"*, explains why 60 rather than any other number
   (2× the tick), and notes that VSync silently overrides `targetFrameRate`. Thirty-five lines carrying
   a diagnosis, a measurement and a gotcha.

**And the restraint itself, which is the answer to the question that commissioned this audit.** Across
6,098 LOC there is: no DI container, no event bus, no service-locator class, no ScriptableObject
"architecture", no reflection, no source generators, no object pool, no custom serializer, no state
machine framework, no abstract base classes, **no generic types at all**, exactly **2 interfaces**, and
**3 RPCs total**. The 5 `ScriptableObject` configs were judged individually against rubric #4 and
**none is a hit**: `MovementConfig` must be byte-identical on both sides of the wire
(`MovementConfig.cs:9-12`) and its 12 fields are all read by `PlayerMotor`; `MatchConfig` has 5 live
tunables and 2 consumers; `FruitTable` holds 8 weighted entries with a `Validate()` that catches the
failure mode (`FruitTable.cs:76-88`); `CharacterCatalog` holds 4 real skins; `ArenaCatalog` holds one
arena today but `docs/03-roadmap.md:81` has a second as an explicit open item, and the type is 56 LOC
that also validates scene names against Build Settings. The one configuration switch that *is* a
rubric #4 hit is a plain `int` property, not a ScriptableObject (F-A2-5). **For a solo 15-day build,
this is a notably disciplined abstraction budget.**

## Open questions for the team

1. **Is `Snackdown.Core` intended to grow?** `docs/01:37` promises a "Phase 2: app state machine, scene
   flow" that never arrived. If it is coming, keep the assembly and fix the doc's tense; if it is not,
   F-A2-3 applies. Only Luca can say which.
2. **Should intra-`Gameplay` layering be enforced or explicitly declared conventional?** F-A2-2 can be
   fixed by moving three lines, or by splitting `Gameplay` into `Gameplay.Core` / `Gameplay.Match`
   assemblies (more structure, and this audit's default advice is *less*). A third option — write in
   `docs/01` that inside `Gameplay` the rule is convention — costs nothing and is honest. Which?
3. **Are Mobile and WebGL still targets?** F-A2-12 is Major only because the README says they are. If
   the answer is "PC only, the platform list is aspirational", it collapses to a one-line doc fix.
4. **`ADR 0001` does not exist.** Recon flagged the numbering gap. If 0001 was drafted and dropped, an
   `adr/0001-*.md` with `Status: Superseded` or a one-line note in `adr/README.md` closes the question;
   right now a reader assumes a file was deleted.
5. **Was the assembly split ever timed?** `adr/0002:213` justifies keeping asmdefs "by compile times
   and test isolation". Test isolation is demonstrably real (`Snackdown.Tests.EditMode` exercises
   `Simulation` and `Netcode` with no `NetworkManager`). Compile times were never measured, and at
   6,098 LOC the 8-assembly split plausibly costs *more* wall-clock than one assembly would. A single
   before/after number would settle it and would be a strong thing to have in an interview.
