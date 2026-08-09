# Vision & Scope Alignment Audit

**Agent:** A1 · **Branch:** `dev` @ `10a2a13` · **Date:** 2026-08-08 · **Method:** feature inventory
built from types and from what `Bootstrap.unity` / `Lobby.unity` / `Arena01.unity` / `Player.prefab` /
`Fruit.prefab` actually reference (every script GUID resolved against every scene, prefab and
`.asset`), then mapped to the three stated pillars. Docs compared to code in both directions.

## Verdict

`dev` is a **coherent vertical slice, not a pile of parallel experiments.** Every shipped type is
reachable from a scene or from a type that is, the whole pitched loop runs end to end (host over
Relay → lobby → arena load → countdown → fruit, stun, drain, death, spectate → verdict → end screen →
back to lobby), and Pillar 1 is the largest single block of code in the repo at **38.7 % of runtime
LOC and 59 % of test LOC** — the effort distribution does reflect the stated reason the project
exists. UI and connection work has **not** outgrown netcode: all four UI files together are 913 LOC
(15 % of runtime), and 216 of those are the netcode debug overlay. Scope drift is real but small —
about **530 LOC, 8.7 % of runtime**, concentrated in multi-arena and spectator-pan machinery for
content that does not exist, plus a character-select feature that is marked `[x]` and is provably
unreachable. The serious problem is the opposite of over-engineering: **`README.md` still tells a
reader to open a scene that was deleted, and describes a Phase-1 test arena, while the code is two
phases past that** — and the game's central mechanic, a life countdown, **has no on-screen readout
anywhere in the shipping scenes.** For a project whose deliverable is its legibility, those two are
the findings that matter.

**Fidelity score: 7 / 10.** Three points off: −1.5 for a README whose "Running it" section cannot be
followed at all (the first thing a reviewer does), −1 for Pillar 2 shipping without the presentation
layer that makes it a game rather than a simulation, −0.5 for the checked-but-unreachable character
select and the two stated target platforms with zero supporting evidence.

## Scorecard

| Dimension | Score /5 | Note |
|---|---|---|
| Pillar coverage — is each pillar actually built? | 4 | P1 and P3 complete and demonstrable; P2 complete server-side but has **no player-facing HUD** (F-A1-3). |
| Effort distribution vs. stated priority | 5 | P1 = 2,358 runtime + 707 test LOC (42 % of all C#). Netcode is genuinely the centre of gravity; UI has not outgrown it. |
| Scope discipline — features for a game this isn't | 4 | ~530 LOC (8.7 %) of speculative content machinery. Bounded, self-documented, no half-finished subsystems, zero dead files. |
| Docs ↔ code fidelity | 2 | README stale for 39 commits and factually unfollowable; `docs/01` folder listing omits 9 shipped types and describes a `Core/` that does not exist; ADR 0002 overtaken by the code and never amended. |
| Roadmap claim accuracy | 3 | "36 unit tests" and the 8-fruit/35 %→1 % table verify exactly. "100k rolls" and "measured at 1 write/s" are not reproducible from anything in the repo. |

---

## Feature inventory, built from code and mapped to pillars

38 first-party runtime files, 6,098 LOC (matches recon's ~6,200). Every script GUID was resolved
against the three scenes, two prefabs and five `.asset` files; the "reached from" column is that
result, not an assumption.

### Pillar 1 — correct, demonstrable netcode · **2,358 LOC (38.7 %)**

| Feature | Types | LOC | Reached from |
|---|---|---:|---|
| Fixed 30 Hz tick, ordered phases, one snapshot broadcast | `NetworkSimulationLoop` | 138 | `Bootstrap.unity` (`TickRate: 30` confirmed at `Bootstrap.unity:446`) |
| Pure re-runnable step, ordered sub-steps | `PlayerMotor`, `PlayerState`, `MovementConfig`, `SimulationContext`, `InputCommand`, `InputPacket` | 520 | `MovementConfig.asset`; rest via code |
| Prediction, reconciliation, interpolation, three roles in one component | `PredictedPlayer` | 691 | `Player.prefab` |
| Ring buffers, wire format, peer-position history | `PredictionBuffer`, `SnapshotFrame`, `WorldSnapshotBuffer`, `SnapshotInterpolator`, `IPredictedPeer` | 379 | code |
| Visual absorption of corrections | `VisualSmoother` | 66 | `Player.prefab` (child `Visual`) |
| Measurement: rolling window + CSV export | `ReconciliationStats`, `RunRecorder` | 246 | code, via `NetDebugOverlay` |
| Input sampling | `InputReader` | 102 | `Player.prefab` |
| The demo surface (F1–F4, predicted vs authoritative) | `NetDebugOverlay` | 216 | `Arena01.unity` |

Tests: `PlayerMotorTests` 262, `PeerCollisionTests` 160, `PredictionBufferTests` 144,
`SnapshotInterpolatorTests` 141 = **707 LOC (59 % of all test code)**.

### Pillar 2 — last-player-standing survival · **1,971 LOC (32.3 %)**

| Feature | Types | LOC | Reached from |
|---|---|---:|---|
| Life timer, interval replication, alive flag, static registry | `PlayerLife`, `MatchConfig` | 247 | `Player.prefab`, `MatchConfig.asset` |
| Fruit: weighted table, spawner, server-side pickup | `FruitTable`, `FruitSpawner`, `Fruit` | 327 | `FruitTable.asset`, `Arena01.unity`, `Fruit.prefab` |
| Head-bounce stun, swept, resolved on the tick | `HeadBounce` | 117 | `Arena01.unity` |
| Phase machine, networked additive arena load, countdown deadline | `MatchDirector`, `MatchPhase` | 300 | `Bootstrap.unity` |
| Win conditions, verdict replication | `RoundReferee`, `MatchOutcome` | 219 | `Bootstrap.unity` |
| Spawn placement via authoritative teleport | `PlayerSpawnPoints` | 78 | `Arena01.unity` |
| Death → spectator, arena-clamped free camera | `SpectatorCamera`, `SpectatorInput`, `ArenaBounds` | 232 | `Arena01.unity` (`SpectatorInput` added at runtime, `SpectatorCamera.cs:52`) |
| Arena catalog | `ArenaCatalog` | 56 | `ArenaCatalog.asset` (**1 entry**) |
| Character skins | `CharacterAppearance`, `CharacterCatalog` | 111 | `Player.prefab`, `CharacterCatalog.asset` (4 entries) |
| Loading screen, countdown, end screen | `LoadingScreenController`, `EndScreenController` | 284 | `Bootstrap.unity` |

Tests: `StunTests` 139, `FruitTableTests` 128, `ArenaBoundsTests` 115 = 382 LOC.

**Gap: there is no in-match HUD.** `grep` for `UIDocument` returns 2 in `Bootstrap.unity`, 1 in
`Lobby.unity`, **0 in `Arena01.unity`**. `Player.prefab` resolves to exactly six script components —
`NetworkObject`, `InputReader`, `PredictedPlayer`, `CharacterAppearance`, `PlayerLife`,
`VisualSmoother` — plus two `SpriteRenderer`s (`Visual`, `AuthoritativeGhost`). No canvas, no label,
no bar. See F-A1-3.

### Pillar 3 — one identical join flow, LAN and Relay · **1,666 LOC (27.3 %)**

| Feature | Types | LOC | Reached from |
|---|---|---:|---|
| The abstraction + two real implementations | `IConnectionProvider`, `DirectConnectionProvider`, `RelayConnectionProvider` | 602 | `MainMenuController.cs:139-141` |
| Result/failure modelling as return values | `ConnectionResult`, `ConnectionRequest` | 119 | code |
| Approval at the door: version, cap, name sanitation, skin clamp | `ConnectionApproval`, `ConnectionPayload` | 305 | code |
| Replicated roster with ready state | `SessionRoster`, `PlayerSlot` | 227 | `Bootstrap.unity` |
| Menu + lobby UI | `MainMenuController` | 413 | `Lobby.unity` |

Tests: `ConnectionApprovalTests` 106 LOC.

### Unmapped to any pillar — **103 LOC (1.7 %)**

`AppBootstrap` (68, `Bootstrap.unity`) and `FrameRatePolicy` (35, no scene reference — applied via
`[RuntimeInitializeOnLoadMethod]` at `FrameRatePolicy.cs:28`). Both are app plumbing with a
documented, measured justification; neither is drift.

### Effort distribution — the headline number

| Pillar | Runtime LOC | % | Test LOC | % | Combined | % of 7,293 |
|---|---:|---:|---:|---:|---:|---:|
| 1 — Netcode | 2,358 | 38.7 | 707 | 59.2 | **3,065** | **42.0** |
| 2 — Survival gameplay | 1,971 | 32.3 | 382 | 32.0 | 2,353 | 32.3 |
| 3 — Join flow | 1,666 | 27.3 | 106 | 8.9 | 1,772 | 24.3 |
| Unmapped plumbing | 103 | 1.7 | 0 | 0 | 103 | 1.4 |

**Answer to the brief's question:** no, UI/connection/menu work has **not** outgrown the netcode.
The four UI files total 913 LOC (15.0 % of runtime), and 216 of those are `NetDebugOverlay`, which is
netcode demonstration, not menu work. `MainMenuController` at 413 LOC is the second-largest file in
the repo but is a single screen-swapping controller with no connection logic in it
(`MainMenuController.cs:14-22` states this and the file bears it out). Pillar 1 is under-invested in
exactly one place — **it has no PlayMode or multi-instance test** (8 EditMode files, 0 PlayMode,
confirmed by `Assets/Tests/` containing only `EditMode/`) — which is A8's domain, not mine.

---

## Findings

### F-A1-1 — `README.md` cannot be followed: it points at a deleted scene and describes a game two phases behind

- **Severity**: Critical
- **Type**: Process
- **Confidence**: High
- **Evidence**: `README.md:46-48` ("Phase 1 ships a bare test arena, not a game yet… Open
  `Assets/_Project/Scenes/NetTest.unity` and press Play"); `README.md:78-80` ("Phase 1 (netcode core)
  is in… pending validation against a live remote peer"). `NetTest.unity` deleted in commit `97e7e3f`
  (2026-08-08). README's last commit is `2bcd7b5` (2026-08-06); `git rev-list --count 2bcd7b5..dev` =
  **39 commits**, of which **19 touch `Assets/`**. Actual entry scene is `Bootstrap.unity`
  (`ProjectSettings/EditorBuildSettings.asset`, first entry). Contrast `docs/03-roadmap.md:9-14`,
  where Phases 1–3 are ✅. `CLAUDE.md` § Documentation: *"`README.md` and the affected `docs/` file
  are updated **in the same commit** as the code."*
- **What it is**: The first document a reviewer opens gives instructions that fail on step 1, and
  states a project status two phases stale. Recon's contradictions #1 and #2, both verified.
- **Why it matters**: This is a portfolio project; the README *is* the product surface. A reviewer
  who follows it hits a missing-scene error and concludes the repo does not run. It also
  undersells the work — Relay, lobby, roster, full match loop and win conditions are all in and the
  README claims none of them. The netcode claims in `README.md:35-40` were each verified against the
  code and are **true**; only the "Running it" and "Status" sections lie.
- **Recommendation**: Rewrite `README.md:44-80` against `Bootstrap.unity`, the Host/Join menu flow
  and the Phase 3 state. Keep the F1–F4 table — it is accurate (`NetDebugOverlay.cs:44-47`).
- **Effort**: S

### F-A1-2 — `docs/01-architecture.md` describes a `Core` layer that does not exist and omits nine shipped types

- **Severity**: Major
- **Type**: Process
- **Confidence**: High
- **Evidence**: `docs/01-architecture.md:22` (layer diagram: *"Core — Bootstrap, scene flow, **service
  locators**"*) and `:38` (*"`Core/` FrameRatePolicy (Phase 2: app state machine, scene flow)"*).
  `Assets/_Project/Scripts/Core/` contains exactly two files: `AppBootstrap.cs` (68) and
  `FrameRatePolicy.cs` (35). Neither is a service locator or a state machine. The folder listing at
  `:34-61` omits `Match/ArenaBounds`, `Match/RoundReferee`, `Match/MatchConfig`,
  `Match/SpectatorCamera`, `Player/PlayerLife`, `Player/CharacterCatalog`, `Input/SpectatorInput`,
  `UI/LoadingScreenController`, `UI/EndScreenController` — all nine confirmed present on disk. The
  file's last commit is `925f1fb` (2026-08-08), so the prose was updated that day and the listing was
  not.
- **What it is**: Recon's contradictions #3 and #4, both verified. Worth one correction to recon:
  service locators *do* exist in this codebase — five of them (`MatchDirector.Current:96`,
  `RoundReferee.Current:68`, `ConnectionApproval.Current:73`, `ArenaBounds.Current` per
  `SpectatorCamera.cs:107`, `NetworkSimulationLoop.Instance:43`) — they are simply scattered across
  `Gameplay/`, `Connection/` and `Netcode/` rather than owned by `Core/` as the diagram claims.
- **Why it matters**: The architecture doc is the artefact an interviewer reads to judge whether the
  author can describe their own system. A diagram that names a layer responsibility with no
  implementation, next to a real ambient-singleton pattern the doc never mentions, invites exactly
  the question the project least wants: "did you write this doc before or after the code?"
- **Recommendation**: Delete "service locators" and "app state machine" from `:22` and `:38`;
  regenerate the folder listing from disk; add one paragraph naming the five `Current`/`Instance`
  statics as the deliberate ambient-lookup pattern and why (`ConnectionApproval.cs:66-72` already
  argues it well — lift that reasoning up).
- **Effort**: S

### F-A1-3 — The core mechanic has no on-screen readout: no in-match HUD exists

- **Severity**: Major
- **Type**: Scope-drift
- **Confidence**: High
- **Evidence**: `Arena01.unity` contains **zero** `UIDocument` components (GUID sweep across all three
  scenes: Bootstrap 2, Lobby 1, Arena01 0). `Player.prefab` resolves to six script components, none
  of them UI. `PlayerLife.Fraction` is declared at `PlayerLife.cs:54-57` with the doc comment *"for a
  bar"* and has **zero** call sites. `RoundReferee.RoundRemaining` (`RoundReferee.cs:58-66`) has
  **zero** call sites. `MatchConfig.asset` sets `RoundSeconds: 180`. `Assets/_Project/UI/` contains
  three `.uxml` files — MainMenu, LoadingScreen, EndScreen — and no HUD.
- **What it is**: The pitch is *"your life is a countdown timer"* and *"on round timeout, most life
  left wins"*. In the shipping build a player can see neither their own life nor the round clock nor
  anyone else's. The only in-match text on screen is `NetDebugOverlay`, an IMGUI debug panel. The two
  properties that were written to feed a HUD were never consumed.
- **Why it matters**: Player-facing. A demo of this build shows characters running around collecting
  fruit for no visible reason and then abruptly dying; the TimeUp outcome (`RoundReferee.cs:103`,
  `EndScreenController.cs:86`) is unreachable as an *observable* event because nobody can see the
  clock. The roadmap defers "live scoreboard" to Phase 4, but a scoreboard is the multi-player view —
  a player's own countdown is the mechanic itself, and no doc or roadmap line acknowledges it is
  missing. This is Pillar 2's only real hole.
- **Recommendation**: One `HUD.uxml` + one ~60-LOC controller in the bootstrap scene reading
  `PlayerLife.Fraction`/`.Remaining` for the local owner and `RoundReferee.RoundRemaining`. Both
  properties already exist and are already replicated; no netcode change. Add the line to Phase 3 of
  the roadmap rather than Phase 4 — it closes an item already marked `[x]`.
- **Effort**: S

### F-A1-4 — Character select is marked `[x]` but is unreachable: the index is hardcoded to 0 at every call site

- **Severity**: Major
- **Type**: Scope-drift
- **Confidence**: High
- **Evidence**: `docs/03-roadmap.md:56-58` marks it `[x]`. The full path exists —
  `ConnectionRequest.CharacterIndex:22`, `ConnectionPayload.CharacterIndex:32`,
  `ConnectionApproval.Approve` clamps it at `:156` and `:183`, `SessionRoster.AddPlayer` stores it at
  `:94`, `CharacterAppearance.IndexForOwner():56-65` reads it, `CharacterCatalog.asset` has 4 entries
  with 4 distinct sprite GUIDs. But the only two producers are
  `ConnectionRequest.Host(nickname, characterIndex = 0)` and `.Join(target, nickname,
  characterIndex = 0)` (`ConnectionRequest.cs:24,31`), and `MainMenuController.cs:186` and `:197`
  call both **without** the argument. `MainMenu.uxml` contains no character-picker element (all 18
  named elements enumerated: nickname, target, host, join, cancel, ready, start, leave, statuses,
  join-code, copy-code, roster-list, screens). `ConnectionApproval.CharacterCount` is a
  `public int { get; set; } = 4` with **no setter call anywhere** in `Assets/`.
- **What it is**: A networked field that is provably constant at 0 in every session, ~150 LOC of
  plumbing serving it, and four skins of which one is reachable. The roadmap footnote is honest
  ("the picker UI itself is cosmetic and lands with the lobby polish") but the item is checked.
- **Why it matters**: The observable feature — four players who look different — does not happen.
  In a 4-player lobby every character renders identically, which undercuts the roster and the end
  screen. Secondly, `CharacterCount` is a latent bug: adding a 5th entry to `CharacterCatalog.asset`
  would have approval silently clamp it away, because nothing wires the catalog's `Count` to it.
- **Recommendation**: Either (a) add four buttons to `MainMenu.uxml` and pass the index through the
  two `ConnectionRequest` factories — the rest is already built and would light up unchanged — or
  (b) uncheck the roadmap item and say the payload plumbing landed ahead of the picker. Separately,
  set `CharacterCount` from `CharacterCatalog.Count` at construction instead of defaulting to a
  literal 4.
- **Effort**: S

### F-A1-5 — Multi-arena and spectator-pan machinery built for content that does not exist

- **Severity**: Minor
- **Type**: Over-engineering
- **Confidence**: High
- **Evidence**: `ArenaCatalog.asset` contains **one** entry (`Arena 01` → `Arena01`);
  `MainMenuController.cs:385` calls `director.ServerStartMatch(0)` with a literal; `MatchDirector`
  carries `NetworkVariable<int> _arenaIndex` (`:34`), `ArenaCatalog.Validate()` (`ArenaCatalog.cs:45`)
  and `UnloadCurrentSceneThen`/`UnloadCurrentScene` (`:170-187`) — ~85 LOC of arena-switching for a
  set of one, with Phase 4's "second arena" unchecked. Separately: `ArenaBounds` (66),
  `SpectatorInput` (52), `ArenaBoundsTests` (115) and the pan/clamp path in `SpectatorCamera.cs:69-72,
  105-112` (~35) = **268 LOC**, whose observable effect the architecture doc itself quantifies at
  `docs/01-architecture.md:140-142`: *"Arena01 is the small kind: it is 26×9 against a 24.9×14 view,
  so a spectator there gets about half a unit of horizontal slack and nothing vertical."*
- **What it is**: Over-engineering rubric #8 (speculative extensibility for content that doesn't
  exist) and #4 (a switch with one live value). Total drift ≈ 530 LOC including F-A1-4's plumbing and
  F-A1-8's dead API — **8.7 % of runtime**.
- **Why it matters**: Velocity, mildly, and interview optics more. 115 lines of unit tests exercising
  a clamp that produces half a unit of movement on the only shipped map is the kind of thing a
  reviewer notices. Against that: the code is small, correct, honestly documented, and the multi-arena
  path is genuinely load-bearing for the networked additive-load design that Phase 3 needed anyway.
  This is the mildest possible form of the failure.
- **Recommendation**: Do nothing to the code. Add one line to `docs/03-roadmap.md` Phase 4 noting
  that the arena catalog and the camera clamp are already in place so a second arena is authoring-only
  — that converts speculative work into a stated, defensible position.
- **Effort**: S

### F-A1-6 — ADR 0002's decision was overtaken by the code four commits later and never amended; ADR 0001 never existed

- **Severity**: Major
- **Type**: Process
- **Confidence**: High
- **Evidence**: `docs/adr/0002-decoupling-the-netcode-layer.md:23-25` asserts *"the assembly split…
  cannot be done — an assembly definition for `Netcode` would need a reference to `Gameplay`"*; `:184`
  *"None of the options were taken"*; `:213` *"Assembly definitions stay in Phase 5."* The ADR landed
  in `fa2a289`/`d7019ed` (merged `4a106df`, PR #5). Commit **`e99a6fb` — "Give every system its own
  assembly, and break the cycle that exposed"** (PR #6) is the *next* feature commit, and it did the
  split: 8 `.asmdef` files exist, `Snackdown.Netcode` does not reference `Gameplay`, and the cycle was
  broken by extracting a `Simulation` leaf assembly plus a non-generic `IPredictedPeer`
  (`IPredictedPeer.cs`, 40 LOC) — a **fourth option** the ADR never lists. `docs/01-architecture.md:187-189`
  documents the outcome; the ADR does not. `git log --all -- docs/adr` returns two commits and one
  file; **no `0001` was ever authored or deleted** (recon's #6, verified — it is an absence, not a
  removal).
- **What it is**: The ADR's Context and Decision sections now state things the repository disproves,
  in the one document type whose entire value is being the durable record of a decision.
- **Why it matters**: This ADR is the strongest single artefact in the repo — it contains a real
  compiler probe against NGO 2.11 with the actual ILPP stack trace (`:49-55`), which is genuinely
  publishable material. Leaving it saying "the split cannot be done" next to eight asmdefs that do it
  wastes that. An interviewer who reads the ADR then the asmdefs will ask which one is true.
- **Recommendation**: Append a dated "Superseded in practice" block to ADR 0002 recording option 4
  (extract `Simulation` as a shared leaf + `IPredictedPeer` at the loop boundary), that it landed in
  `e99a6fb`, and that it cost no boxing and no generics at the wire. Either renumber to `0001` or add
  a one-line `docs/adr/README.md` saying numbering starts at 0002 deliberately — an unexplained gap
  reads as a lost document.
- **Effort**: S

### F-A1-7 — Two roadmap claims cannot be reproduced from anything in the repository

- **Severity**: Minor
- **Type**: Process
- **Confidence**: High
- **Evidence**: Verified claim by claim.
  - *"36 unit tests"* (`docs/03-roadmap.md:89-90`) — **accurate for the three files named**:
    `PlayerMotorTests` 15 + `PredictionBufferTests` 10 + `SnapshotInterpolatorTests` 11 = 36 `[Test]`
    attributes exactly. The repo-wide total is now **77** across 8 files (0 `[TestCase]`), so the
    number is stale-low rather than wrong.
  - *"8 fruit from 35% common to 1% legendary, worth 3s to 20s"* (`:64-66`) — **exact**.
    `FruitTable.asset` has 8 entries, weights 35/25/18/12/6/2/1/1 summing to 100, `LifeSeconds` 3→20.
  - *"distribution verified over 100k rolls"* — **not reproducible.** `grep` for
    `100000|100_000|100k|distribution|Chi` across `Assets/Tests` and `Assets/_Project/Scripts` returns
    one unrelated hit. `FruitTableTests` (10 tests) covers boundaries, clamping, zero-weight and
    validation — it does **not** run a distribution check.
  - *"Measured at 1 write/s where the original did ~60"* (`:61-63`) — **derivable but not measured.**
    `MatchConfig.asset` sets `LifeReplicationHz: 1` and `PlayerLife.cs:149-153` publishes on that
    interval, so 1 write/s is correct as `ESTIMATED`; there is no capture, log or test in the repo
    behind the word "measured", and it under-states the real rate, which also includes an immediate
    publish on every fruit pickup (`PlayerLife.cs:187-188`) and on death (`:148`).
  - *"40 ticks of mixed input, simulated twice… bit-identical"* (`:37-38`) — **true**.
    `PlayerMotorTests.Replay` loops `t < 40` with varying `moveX`/`jumpPressed`
    (`PlayerMotorTests.cs:84-94`) and `ReplayingASequence_LandsOnTheSameStateEveryTime` asserts exact
    `Vector2` equality on Position and Velocity. Caveat: the assertion covers Position and Velocity
    only, not `CoyoteTimer`/`JumpBufferTimer`/`StunTimer`, and `GroundMask = 0` in `SetUp` so no
    `BoxCast` path is exercised — the test file states this limitation openly at `:15-19`.
  - Same section: *"Host session in `NetTest.unity`"* (`:36`) — that scene no longer exists.
- **Why it matters**: Two unfalsifiable numbers sit beside five that check out exactly. In an
  interview the checkable ones earn credit and the two soft ones cost it, because the reviewer cannot
  tell from the doc which is which.
- **Recommendation**: Either add the 100k-roll distribution test (≈15 lines in `FruitTableTests`,
  asserting each bucket within tolerance of `ChanceOf(i)` — `ChanceOf` already exists at
  `FruitTable.cs:66` and is otherwise only used by tests) or reword to "weights are 35 %→1 % by
  construction". Change "Measured at 1 write/s" to "1 write/s by configuration, plus an immediate
  publish on pickup and on death". Update "36" to 77 and replace the `NetTest.unity` reference.
- **Effort**: S

### F-A1-8 — Public API on shipped types with zero call sites

- **Severity**: Nit
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: `FruitSpawner.ServerDespawnAll()` (`:136-145`, doc-commented *"Clears the arena
  between rounds"*) and `FruitSpawner.ActiveCount` (`:39-46`) — zero callers.
  `MatchDirector.ServerReturnToLobby` (`:254`) resets life and clears ready but never clears fruit, so
  the round-reset path the method was written for is incomplete. `RoundReferee.RoundRemaining` (`:58`)
  and `PlayerLife.Fraction` (`:54`) — zero callers (both are F-A1-3's missing HUD).
- **Why it matters**: `ServerDespawnAll` not being called from `ServerReturnToLobby` is a real
  behavioural gap, not just dead code: fruit spawned in match 1 survives into the lobby and into
  match 2. Small, but it is a second-round bug hiding behind an unused method.
- **Recommendation**: Call `ServerDespawnAll()` from `MatchDirector.ServerReturnToLobby`
  (`MatchDirector.cs:254-268`, next to the existing `PlayerLife.ServerReset` loop). Leave
  `Fraction`/`RoundRemaining` alone — F-A1-3 consumes them.
- **Effort**: S

### F-A1-9 — `docs/02` "What replicates how" states an RPC for fruit that does not exist

- **Severity**: Nit
- **Type**: Process
- **Confidence**: High
- **Evidence**: `docs/02-netcode.md:193` — *"Fruit spawn / pickup | server spawn/despawn + RPC | on
  event"*. There are exactly three `[Rpc]` declarations in the project (`SessionRoster.cs:133`,
  `PredictedPlayer.cs:380`, `NetworkSimulationLoop.cs:119`) and none is fruit-related. Fruit uses
  `NetworkObject.Spawn()`/`.Despawn()` plus `NetworkVariable<int> _kind` (`Fruit.cs:35, 85`).
- **Why it matters**: `docs/02` is the centrepiece document and its wire-format table is the thing a
  netcode reviewer will read most closely. Every other row in that table checks out — input carries
  3 commands (`InputPacket`, confirmed `PredictedPlayer.cs:324-329`), snapshot is one unreliable RPC
  for all players (`NetworkSimulationLoop.cs:110`), life is `NetworkVariable` at ~1 Hz. One wrong row
  in an otherwise exact table is a cheap thing to lose credibility on.
- **Recommendation**: Change the mechanism cell to "server spawn/despawn + `NetworkVariable<int>`
  (kind, set before `Spawn` so it ships in initial state)".
- **Effort**: S

### F-A1-10 — Mobile and WebGL are stated target platforms with zero supporting evidence in the repo

- **Severity**: Major
- **Type**: Scope-drift
- **Confidence**: High
- **Evidence**: `grep` across `Assets/_Project` and `Assets/Tests` for
  `UNITY_ANDROID|UNITY_IOS|UNITY_WEBGL|WebSocket|UseWebSockets|Touchscreen|OnScreenStick` returns
  **zero hits**. No `.asmdef` declares `includePlatforms` or `defineConstraints` except the test
  assembly. Input is keyboard-only: `NetDebugOverlay.cs:41` reads `Keyboard.current`;
  `InputReader`/`SpectatorInput` drive off the Input System actions asset with no on-screen controls.
  `NetDebugOverlay` uses `OnGUI` (IMGUI), which is a poor fit for touch. `EditorBuildSettings.asset`
  lists three scenes and nothing platform-specific. Relay over UnityTransport requires WebSocket
  transport for WebGL, and nothing configures it.
- **What it is**: Not a code defect — a stated-target-vs-repository gap, quantified rather than
  softened per the brief.
- **Why it matters**: If "PC / Mobile / WebGL" appears on a CV or project page next to this repo, the
  claim does not survive a five-minute look. WebGL in particular would need transport reconfiguration
  and a `NetDebugOverlay` rewrite; mobile would need a touch control scheme that does not exist. It is
  also the one place where honest scoping costs nothing: the netcode story is entirely PC-shaped and
  loses nothing by saying so.
- **Recommendation**: Drop Mobile and WebGL from the stated targets, or add them to `docs/03-roadmap.md`
  as an explicit unstarted phase with the two blockers named (WebSocket transport for Relay-over-WebGL;
  a touch scheme + non-IMGUI overlay for mobile). Do not leave them asserted.
- **Effort**: S — to fix the claim. L — to actually support either platform.

### F-A1-11 — The shipped lobby starts a match with one player, which contradicts the documented flow

- **Severity**: Nit
- **Type**: Scope-drift
- **Confidence**: High
- **Evidence**: `Lobby.unity:238` serializes `_minPlayersToStart: 1` on `MainMenuController`, whose
  tooltip reads *"Set to 1 to test the arena alone"* (`MainMenuController.cs:35-37`).
  `docs/00-legacy-analysis.md:18` describes the flow as *"`Lobby` (needs ≥2 players)"*, and
  `RoundReferee.cs:118` requires `_startingPlayers >= 2` before a last-standing verdict is possible.
- **What it is**: A development convenience value committed as the shipped default. Consequence: a
  solo session can start a match that can only ever end by TimeUp, never by LastStanding.
- **Why it matters**: Minor, and the code is explicitly defended against it —
  `MainMenuController.cs:333-337` argues correctly that it gates a button and not the match, and
  `RoundReferee.cs:116-119` handles the solo case deliberately. Worth flagging only because it is the
  committed default rather than an editor-local tweak.
- **Recommendation**: Set it to 2 in `Lobby.unity` before any recorded demo, or leave it and note the
  intent in the roadmap. Not worth a commit on its own.
- **Effort**: S

---

## Quantified Estimates

| Metric | Value | Formula / inputs | Tag |
|---|---:|---|---|
| First-party runtime C# | 6,098 LOC | `wc -l` over all 38 files under `Assets/_Project/Scripts` tracked by git | MEASURED |
| Test C# | 1,195 LOC | 8 files under `Assets/Tests/EditMode` | MEASURED |
| Pillar 1 share of runtime | 38.7 % | 2,358 / 6,098 — `Netcode/` (829) + `Simulation/` (520) + `PredictedPlayer` (691) + `InputReader` (102) + `NetDebugOverlay` (216) | MEASURED |
| Pillar 1 share of tests | 59.2 % | 707 / 1,195 — motor 262, peer-collision 160, buffer 144, interpolator 141 | MEASURED |
| Pillar 1 share of all C# | 42.0 % | 3,065 / 7,293 | MEASURED |
| UI-layer share of runtime | 15.0 % | 913 / 6,098 across 4 files; 216 of the 913 is the netcode overlay | MEASURED |
| Identified scope drift | ~530 LOC = 8.7 % of runtime | arena machinery 85 + spectator pan/clamp/tests 268 + unreachable character-select plumbing ~150 + dead API ~28. Assumption: `ArenaBounds`+`SpectatorInput` counted as drift because Phase 4 owns "spectator polish"; the bare death-camera in `SpectatorCamera` is counted as Pillar 2 | ESTIMATED |
| Test-attribute count | 77 `[Test]`, 0 `[TestCase]` | `grep -c '\[Test\]'` per file: 6+10+10+8+15+10+11+7 | MEASURED |
| Roadmap's "36 tests" | Correct for the 3 named files | 15 + 10 + 11 = 36 | MEASURED |
| Life-timer write rate | 1 write/s baseline, + 1 per fruit pickup, + 1 on death | `MatchConfig.asset LifeReplicationHz: 1`; `PlayerLife.cs:149` interval publish, `:187` pickup publish, `:148` death publish. **No measurement artefact exists in the repo** — the roadmap's word "measured" is unsupported | ESTIMATED |
| Fruit rarity spread | 35.0 % → 1.0 % | weights 35/25/18/12/6/2/1/1, total 100, so `ChanceOf` = the weight | MEASURED |
| README staleness | 39 commits / 19 touching `Assets/` / 2 days | `git rev-list --count 2bcd7b5..dev`; `git log --oneline 2bcd7b5..dev -- Assets \| wc -l` | MEASURED |
| Mobile/WebGL support surface | 0 LOC, 0 platform defines, 0 transport config | grep sweep documented in F-A1-10 | MEASURED |
| Original project vs. rebuild | ~2,000 LOC / 22 scripts → 6,098 LOC / 38 files | `docs/00-legacy-analysis.md:20` vs. measured tree. ~3.0× for the same game plus the netcode layer; `Netcode/`+`Simulation/`+`PredictedPlayer` alone is 2,040 LOC, i.e. the entire growth is accounted for by Pillar 1 | ESTIMATED |

Scenario C figures from `docs/05-validation.md` are **not** cited anywhere above; that section is
self-declared "observed, not measured" (`docs/05-validation.md:103-108`) and recon's contradiction #5
is confirmed as an honest self-disclosure, not a defect.

---

## What is genuinely good here

This section is not padding — the code earns most of it.

- **The pillar the project claims to be about is the one it actually invested in.** 42 % of all C# and
  59 % of test code is Pillar 1. That is unusual; the common failure mode for a portfolio netcode
  project is menu and lobby sprawl swallowing the netcode, and it did not happen here.

- **Complexity that is correctly earned** (mandatory counter-check to F-A1-5, and there is a lot of it):
  - `WorldSnapshotBuffer` + `SimulationContext` (`PredictedPlayer.cs:650-674`) — peer bodies are
    copied as plain boxes per tick rather than read live, so a replay of tick 40 sees tick-40
    positions. That is the difference between predicted peer collision working and quietly lying, and
    the reasoning is written down at `docs/02-netcode.md:88-94`.
  - `InputPacket`'s three-command redundancy window (`PredictedPlayer.cs:324-329`,
    `EnqueueIfNew:400-416`) with the staleness and future-tick guards — redundancy over
    retransmission on an unreliable channel, with the hostile case handled
    (`MaxQueueCapacity`, `MaxInputTickLead`, both with remarks explaining the attack).
  - `TeleportAnnounceCount = 3` (`PredictedPlayer.cs:101-111`) — a teleport flag announced once on an
    unreliable channel can be dropped, turning a deliberate reposition into a counted prediction
    failure. This bug was found *by measurement* and is recorded as a pitfall at
    `docs/05-validation.md:42`. That is the loop a netcode engineer wants to see.
  - Deadline-not-counter for both the start countdown (`MatchDirector.cs:36-50, 221-225`) and the
    round clock (`RoundReferee.cs:30-38`) — publish one `double` and let every peer derive from
    `ServerTime`, instead of replicating a decrementing number. Correct, cheap, and the remarks
    explain why the obvious version was wrong.
  - `LastMeasuredRttMs` derived from `(latestPredictedTick − ackTick)` (`PredictedPlayer.cs:190-203`,
    `:504-505`) after establishing that `UnityTransport.GetCurrentRtt` reads the *reliable* pipeline
    while every packet here is unreliable — and then keeping the transport's number side by side
    purely to show the discrepancy (`docs/05-validation.md:127-131`, 218 ms claimed on idle localhost
    vs. 1219 ms during a 510 ms run). That is a genuinely sharp piece of instrumentation work.
  - `IPredictedPeer` — 40 LOC, six members, one implementation, and it is **not** an over-engineering
    hit: it is the seam that lets `Snackdown.Netcode.asmdef` compile without referencing `Gameplay`,
    which is a compiler-enforced property, not a hypothetical one.
  - `IConnectionProvider` — two real implementations with materially different failure modes
    (`RelayConnectionProvider.PrepareAsync` handles services init, anonymous auth and per-instance
    auth profiles that do not exist on LAN, `:172-222`). Rubric #1 does not fire. The claim at
    `MainMenuController.cs:148-156` — that the entire visible LAN/Relay difference is one label and
    one constructor call — is verifiable in the file and true.
  - `RelayConnectionProvider.LocalProfileName()` (`:203-222`) — deriving an auth profile from
    `Application.dataPath` because two Multiplayer-Play-Mode peers otherwise sign in as the same
    anonymous identity and Sessions rejects the second with *"Unexpected exception processing network
    metadata"*. Nobody writes that comment without having lost an afternoon to it, and writing it
    down is exactly what the project is for.
  - The **reconciler pattern in the UI layer** (`LoadingScreenController.cs:12-24, 123-135`,
    `SpectatorCamera.cs:16-19`, `EndScreenController.cs:12-16`) — every frame ask what the phase is
    rather than react to a change, plus an in-flight-operation guard so a scene load that takes frames
    to land is not started twice. It is the same idea as netcode reconciliation applied to UI, it is
    named as such, and it fixed a real observed bug (two stacked lobby scenes). This is design
    reasoning transferring across layers, which is the single most interview-legible thing in the repo.

- **ADR 0002 is the best artefact in the project.** It records a decision that was *rejected*, with a
  compiled probe against NGO 2.11 and the verbatim `NetworkBehaviourILPP` `NullReferenceException`
  stack trace (`:49-55`), a second probe establishing the exact boundary (`:66-79`), and the closing
  argument that deleting a false sentence beats refactoring measured working code to make it true
  (`:184-199`). It needs the amendment in F-A1-6 and nothing else.

- **Zero dead files, zero orphaned scripts.** Every one of the 38 runtime files is reached from a
  scene, a prefab, a `.asset`, or from a type that is. Every `.asset` config is referenced. Recon's
  observation that there are zero `TODO`/`HACK`/`WIP` markers holds up under a feature-level sweep —
  this is not a repo with abandoned experiments in it.

- **Under-engineering check** (the opposite failure the brief asks about): mostly clean.
  `PredictedPlayer` at 691 LOC with 11 touches is the one god-object risk — it is owner, server and
  remote in one file, and the three roles are held apart by convention and by section-comment banners
  rather than by anything the compiler checks. It is well-organised and heavily documented, so this is
  a watch item rather than a finding, but it is where the next mechanic will hurt. No copy-paste
  gameplay code was found. Authoritative logic is in `MonoBehaviour`s, but they are thin and delegate
  to pure code (`PlayerMotor`, `FruitTable.Roll`, `ArenaBounds.Clamp`) that is unit-tested without a
  scene — which is the seam that matters and it exists.

---

## Open questions for the team

1. **Is a HUD in scope before the project is called portfolio-ready?** `docs/03-roadmap.md:98-100`
   defines portfolio-ready as Phases 0–3, and Phase 3 is marked complete — but a viewer of the demo
   cannot see the life timer that the pitch is built on (F-A1-3). Is that an accepted limitation of
   the demo, or an item that belongs inside Phase 3?
2. **Is the character picker coming, or is the roadmap item overstated?** (F-A1-4.) Both are
   defensible; the current state — checked, plumbed, unreachable — is the only one that is not.
3. **Do Mobile and WebGL stay in the stated target platforms?** (F-A1-10.) They are currently
   asserted in project context with nothing behind them, and dropping them costs the netcode story
   nothing.
4. **Was ADR 0001 planned?** Nothing in git history suggests it existed. A one-line note fixes the
   gap either way (F-A1-6).
5. **Should the metrics CSV path stay under `DefaultCompany`?** `ProjectSettings.asset:15` still has
   `companyName: DefaultCompany`, which `docs/05-validation.md:20` documents as part of the export
   path. Cosmetic, but it is in a doc a reviewer follows.
6. **Is `docs/local/netcode.html` intended to survive?** It exists, it is correctly gitignored per
   `.gitignore:75` and `CLAUDE.md`, and nothing in it was audited. Confirming it holds no content that
   should have been promoted to markdown is a human call.
