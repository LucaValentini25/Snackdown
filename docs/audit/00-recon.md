# 00 — Recon

Read-only reconnaissance of `Snackdown`, produced before any domain agent was launched.
Every downstream agent receives this file verbatim so nobody re-does discovery.

## Audit target

| | |
|---|---|
| Repository | `d:\Unity Projects\Snackdown` |
| Branch audited | `dev` |
| Baseline branch | `main` |
| Audited commit | `10a2a13ede831e308479523acbc3e936f2f741c7` ("Merge pull request #19 from LucaValentini25/feature/win-conditions") |
| Commits `main..dev` | 55 |
| Contributors | 1 — `LucaValentini25 <139935542+LucaValentini25@users.noreply.github.com>` |
| Date range | 2026-07-25 → 2026-08-08 (15 days) |
| Working tree | **Dirty (1 file):** ` M Snackdown.slnx` — IDE solution file, no source impact. Audited as-is. |
| Audit date | 2026-08-08 |

## Project context (confirmed with the owner, not inferred)

```yaml
branch_to_audit: dev
baseline_branch: main
game_pitch: "2D multiplayer last-player-standing platformer. Your life is a countdown timer that
             drains on its own; you collect rarity-weighted fruit to add time and stomp rivals'
             heads to stun them. Last one alive wins; on round timeout, most life left wins.
             It is a portfolio project whose stated purpose is 'multiplayer programming done right'."
core_pillars:
  - "Correct, demonstrable netcode: server authority + client prediction + reconciliation + snapshot interpolation"
  - "Last-player-standing survival gameplay: life timer, fruit, head-bounce stun, win conditions"
  - "One identical join flow over LAN and over the internet (Unity Relay)"
genre: "4-player 2D arena survival platformer"
players_per_session: 4
target_ccu: 0            # PORTFOLIO — no production CCU target. See note below.
netcode_stack: "Netcode for GameObjects 2.11.0"
transport: "Unity Transport + Relay/Lobby via com.unity.services.multiplayer 2.1.3"
topology: "client-host (listen server)"
hosting: "none — Relay only, no dedicated server, no server build target"
target_tick_rate: 30
target_platforms: ["PC (Windows)", "Mobile", "WebGL"]
team_size: 1
project_age: "0.5 months (15 days of commits on dev)"
non_negotiables:
  - "It stays a portfolio project, not a product. Success = legible and defensible in an interview, not scale."
  - "The host/listen-server topology stays. Do not recommend a dedicated server as a fix; it is settled in docs/02."
  - "30 Hz tick and 4 players are fixed. Do not re-litigate."
docs_locations: ["README.md", "docs/00-legacy-analysis.md", "docs/01-architecture.md",
                 "docs/02-netcode.md", "docs/03-roadmap.md", "docs/04-workflow.md",
                 "docs/05-validation.md", "docs/adr/0002-decoupling-the-netcode-layer.md",
                 "CLAUDE.md"]
```

### How `target_ccu: 0` changes the audit

There is no production CCU goal. **Do not** score this project as a failed live service.
Agents A5 (server perf) and A9 (infra) must reframe:

- Report the **CCU ceiling as-built** and the **first bottleneck** as *analytical* figures — evidence
  the author can defend in an interview — not as launch blockers.
- Severity is measured against the portfolio goal. A missing k8s deployment is **not** a Blocker here;
  a netcode claim in the docs that the code does not implement **is**.
- Hosting cost is a per-demo-session figure (Relay egress for a 4-player match), not a monthly bill at scale.

The three non-negotiables act as **severity dampeners**: a finding whose only fix is "use a dedicated
server", "raise the tick rate", or "support more than 4 players" is out of scope — note it as an
observed consequence of a settled decision, do not raise it as a finding.

`target_platforms` includes **Mobile and WebGL**, which the repository shows **no** evidence of
supporting today (no `UnityTransport` WebSocket config found in the manifest scan, no platform gating,
no Addressables, no mobile input path beyond the Input System actions asset). A6 and A9 should treat
this as a genuine gap to quantify, not as an assumption to soften.

## Tech stack (exact versions from `Packages/manifest.json`)

| Area | Package / value |
|---|---|
| Editor | `6000.3.14f1` (`ProjectSettings/ProjectVersion.txt`) |
| Netcode | `com.unity.netcode.gameobjects` **2.11.0** |
| Multiplayer services (Relay/Lobby/Sessions) | `com.unity.services.multiplayer` **2.1.3** |
| Multiplayer tooling | `com.unity.multiplayer.tools` **2.2.8**, `com.unity.multiplayer.playmode` **2.0.2** |
| Render pipeline | `com.unity.render-pipelines.universal` **17.3.0** (URP 2D Renderer) |
| 2D feature set | `com.unity.feature.2d` **2.0.2** |
| Input | `com.unity.inputsystem` **1.19.0** |
| Camera | `com.unity.cinemachine` **3.1.2** |
| Tests | `com.unity.test-framework` **1.6.0** |
| UI | `com.unity.ugui` **2.0.0** + UI Toolkit (`uielements` module) — the shipped menus are UI Toolkit |
| Third-party tooling | `com.coplaydev.unity-mcp` — **git URL pinned to `#main`, not a version tag** |
| Transport | No explicit `com.unity.transport` entry; pulled transitively by NGO 2.11 |

Note for A7/A9: `com.coplaydev.unity-mcp` is a **floating git dependency on a branch**, and it is an
editor-automation package with broad project access. That is a supply-chain observation worth a finding.

## Repository map

```
Snackdown/
├── Assets/
│   ├── _Project/                       ← all first-party content
│   │   ├── Art/                        WhiteSquare.png (1 placeholder)
│   │   ├── Prefabs/                    Fruit.prefab, Player.prefab
│   │   ├── Scenes/                     Bootstrap.unity, Lobby.unity, Arena01.unity
│   │   ├── Scripts/                    8 assemblies, 38 .cs files (see below)
│   │   ├── Settings/                   ArenaCatalog, CharacterCatalog, FruitTable,
│   │   │                               MatchConfig, MovementConfig (.asset)
│   │   └── UI/                         MainMenu.uxml, LoadingScreen.uxml, EndScreen.uxml,
│   │                                   Snackdown.uss, MenuPanelSettings.asset
│   ├── Tests/EditMode/                 8 test files + asmdef
│   ├── Pixel Adventure 1/              third-party art, 3.3 MB
│   ├── DEVNIK 2D/                      third-party UI, 1.5 MB
│   ├── Settings/                       URP pipeline assets
│   ├── TextMesh Pro/, UI Toolkit/
│   ├── DefaultNetworkPrefabs.asset     NGO network prefab list
│   ├── InputSystem_Actions.inputactions
│   └── InputSystem.inputsettings.asset
├── Packages/manifest.json
├── ProjectSettings/
├── docs/                               00–05 + adr/ + local/ (local/ is gitignored)
├── README.md, CLAUDE.md
└── *.csproj, Snackdown.slnx            generated
```

## Assembly graph (from the 8 `.asmdef` files)

```
Snackdown.UI ──────────► Snackdown.Gameplay ──┬──► Snackdown.Netcode ──► Snackdown.Simulation
     │                          │             ├──► Snackdown.Simulation
     ├──► Snackdown.Netcode     │             ├──► Snackdown.Input
     ├──► Snackdown.Connection  │             └──► Snackdown.Connection
     │                          │
Snackdown.Core ────────► Snackdown.Connection

Snackdown.Simulation ──► (Unity.Netcode.Runtime only)
Snackdown.Input ───────► (Unity.InputSystem only)
Snackdown.Tests.EditMode ──► Simulation, Netcode, Connection, Gameplay  [Editor-only]
```

Observations to hand to A2/A8, **not yet findings**:

- No cycles. `Simulation` is the only leaf that both sides of the wire depend on — this matches the
  layering claim in `docs/01-architecture.md`.
- `Snackdown.Simulation` references `Unity.Netcode.Runtime` despite being described as "the pure step".
  Worth checking whether that reference is actually needed or is a serialization convenience.
- `Snackdown.UI` references `Unity.Multiplayer.Tools.NetworkSimulator.Runtime` — a **debug/tools
  package referenced from a runtime assembly**. Check whether it is gated for release builds (A7/A6).
- Every asmdef has `"autoReferenced": true` and none declare `includePlatforms`/`defineConstraints`
  except the test assembly. No server-only or editor-only stripping exists.
- `docs/01-architecture.md` claims `Core` holds "Bootstrap, scene flow, **service locators**"; `Core`
  contains only `AppBootstrap.cs` (68 LOC) and `FrameRatePolicy.cs`. A1/A2 should check that claim.

## Module inventory — 38 first-party runtime files, ~6.2k LOC

| Assembly | Files | Key types |
|---|---|---|
| `Snackdown.Core` | 2 | `AppBootstrap`, `FrameRatePolicy` |
| `Snackdown.Connection` | 9 | `IConnectionProvider`, `DirectConnectionProvider`, `RelayConnectionProvider`, `ConnectionApproval`, `ConnectionPayload`, `ConnectionRequest`, `ConnectionResult`, `PlayerSlot`, `SessionRoster` |
| `Snackdown.Simulation` | 6 | `PlayerState`, `PlayerMotor`, `InputCommand`, `InputPacket`, `MovementConfig`, `SimulationContext` |
| `Snackdown.Netcode` | 9 | `NetworkSimulationLoop`, `IPredictedPeer`, `PredictionBuffer`, `SnapshotFrame`, `SnapshotInterpolator`, `WorldSnapshotBuffer`, `VisualSmoother`, `ReconciliationStats`, `RunRecorder` |
| `Snackdown.Gameplay` | 14 | **Player:** `PredictedPlayer`, `PlayerLife`, `PlayerSpawnPoints`, `CharacterAppearance`, `CharacterCatalog` · **Match:** `MatchDirector`, `RoundReferee`, `MatchPhase`, `MatchOutcome`, `MatchConfig`, `ArenaBounds`, `ArenaCatalog`, `SpectatorCamera` · **Fruits:** `Fruit`, `FruitSpawner`, `FruitTable` · **Combat:** `HeadBounce` |
| `Snackdown.Input` | 2 | `InputReader`, `SpectatorInput` |
| `Snackdown.UI` | 4 | `MainMenuController`, `LoadingScreenController`, `EndScreenController`, `NetDebugOverlay` |
| `Snackdown.Tests.EditMode` | 8 | `PlayerMotorTests`, `PredictionBufferTests`, `SnapshotInterpolatorTests`, `PeerCollisionTests`, `StunTests`, `FruitTableTests`, `ArenaBoundsTests`, `ConnectionApprovalTests` |

### Networked surface (grep inventory — agents must verify semantics)

**10 `NetworkBehaviour` types:** `SessionRoster`, `HeadBounce`, `Fruit`, `FruitSpawner`,
`MatchDirector`, `RoundReferee`, `CharacterAppearance`, `PlayerLife`, `PredictedPlayer`,
`NetworkSimulationLoop`.

**12 `NetworkVariable` declarations:**

| File:line | Variable |
|---|---|
| `Gameplay/Fruits/Fruit.cs:35` | `NetworkVariable<int> _kind` |
| `Gameplay/Match/MatchDirector.cs:33` | `NetworkVariable<MatchPhase> _phase` |
| `Gameplay/Match/MatchDirector.cs:34` | `NetworkVariable<int> _arenaIndex` |
| `Gameplay/Match/MatchDirector.cs:50` | `NetworkVariable<double> _playStartsAtServerTime` |
| `Gameplay/Match/MatchDirector.cs:82` | `NetworkVariable<int> _loadedCount` |
| `Gameplay/Match/MatchDirector.cs:83` | `NetworkVariable<int> _expectedCount` |
| `Gameplay/Match/RoundReferee.cs:27` | `NetworkVariable<ulong> _winner` |
| `Gameplay/Match/RoundReferee.cs:28` | `NetworkVariable<MatchOutcome> _outcome` |
| `Gameplay/Match/RoundReferee.cs:38` | `NetworkVariable<double> _roundEndsAtServerTime` |
| `Gameplay/Player/PlayerLife.cs:29` | `NetworkVariable<float> _life` |
| `Gameplay/Player/PlayerLife.cs:47` | `NetworkVariable<bool> _alive` |
| (`SessionRoster` uses a `NetworkList`-style roster — verify the exact container) | |

**Only 3 RPC declarations in the whole project:**

| File:line | Signature |
|---|---|
| `Connection/SessionRoster.cs:133` | `[Rpc(SendTo.Server)]` |
| `Gameplay/Player/PredictedPlayer.cs:380` | `[Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]` — the input channel |
| `Netcode/NetworkSimulationLoop.cs:119` | `[Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]` — the snapshot channel |

This is the entire wire surface. A3 and A4 should start here: the traffic model is essentially
**one unreliable input RPC up + one unreliable snapshot RPC down per tick**, plus low-rate
`NetworkVariable` writes. There is no `NetworkTransform` in the grep results — verify against the
prefabs (`Player.prefab`, `Fruit.prefab`) before concluding, since components live in YAML.

**5 `ScriptableObject` configs:** `FruitTable`, `ArenaCatalog`, `MatchConfig`, `CharacterCatalog`,
`MovementConfig` — each with exactly one `.asset` instance in `Assets/_Project/Settings/`.
A2 must judge these against over-engineering rubric #4 (one live value) **and** against the
legitimate case (designer-tunable data, and `MovementConfig` must be shared by client and server).

**Zero `TODO`/`HACK`/`WIP`/`FIXME`/`XXX` markers** across `Assets/_Project/Scripts` and `Assets/Tests`.
Treat this as a real signal, not a search failure: A1 cannot use TODO clusters as a drift proxy and
must build the feature inventory from types instead.

## Ten most-churned files, `main..dev`

Command: `git log --format=format: --name-only main..dev | sort | uniq -c | sort -rn`

| Touches | Path | Note |
|---:|---|---|
| 13 | `docs/03-roadmap.md` | The single most-edited file in the repo |
| 11 | `Assets/_Project/Scripts/Gameplay/Player/PredictedPlayer.cs` | Also the largest file, 691 LOC — **hotspot: high churn × high size × single author** |
| 7 | `docs/01-architecture.md` | |
| 6 | `Assets/_Project/Scripts/UI/NetDebugOverlay.cs` | |
| 4 | `Assets/_Project/Scripts/UI/MainMenuController.cs` | 413 LOC, 2nd largest |
| 4 | `Assets/_Project/Scripts/UI/LoadingScreenController.cs` | |
| 4 | `Assets/_Project/Scripts/Gameplay/Match/MatchDirector.cs` | |
| 4 | `Assets/_Project/Scripts/Core/NetTestBootstrap.cs` | **Deleted — no longer in the tree** |
| 4 | `Assets/_Project/Scripts/Connection/DirectConnectionProvider.cs` | 293 LOC |
| 4 | `Assets/_Project/Scripts/Connection/ConnectionApproval.cs` | 234 LOC |
| 4 | `Assets/_Project/Scenes/NetTest.unity` | **Deleted — no longer in the tree** |
| 4 | `Assets/_Project/Scenes/Bootstrap.unity` | |
| 4 | `Assets/_Project/Scenes/Arena01.unity` | |

## Largest first-party files

| LOC | Path |
|---:|---|
| 691 | `Assets/_Project/Scripts/Gameplay/Player/PredictedPlayer.cs` |
| 413 | `Assets/_Project/Scripts/UI/MainMenuController.cs` |
| 293 | `Assets/_Project/Scripts/Connection/DirectConnectionProvider.cs` |
| 273 | `Assets/_Project/Scripts/Simulation/PlayerMotor.cs` |
| 270 | `Assets/_Project/Scripts/Gameplay/Match/MatchDirector.cs` |
| 262 | `Assets/Tests/EditMode/PlayerMotorTests.cs` |
| 252 | `Assets/_Project/Scripts/Connection/RelayConnectionProvider.cs` |
| 234 | `Assets/_Project/Scripts/Connection/ConnectionApproval.cs` |
| 216 | `Assets/_Project/Scripts/UI/NetDebugOverlay.cs` |
| 203 | `Assets/_Project/Scripts/Gameplay/Player/PlayerLife.cs` |
| 196 | `Assets/_Project/Scripts/Gameplay/Match/RoundReferee.cs` |
| 175 | `Assets/_Project/Scripts/Connection/SessionRoster.cs` |
| 174 | `Assets/_Project/Scripts/UI/LoadingScreenController.cs` |

Totals: **7,293 LOC** of C# across first-party runtime + tests; ~**6,200** runtime, ~**1,100** tests
(≈18% of the C# in the project is test code).

## Documentation inventory (all read during recon)

| Doc | Contents |
|---|---|
| `README.md` | Pitch, tech-stack table, netcode highlights, run instructions, doc index, status |
| `docs/00-legacy-analysis.md` | Analysis of the original university project this rebuilds |
| `docs/01-architecture.md` | 6-layer diagram (UI → Gameplay → Netcode → Simulation → Connection → Core), folder structure, authority rules, assemblies |
| `docs/02-netcode.md` | Three pillars, the tick, topology, kinematic simulation rationale, ordered simulation steps, predicted peer collision, per-tick owner/server flow, reconciliation, wire format, interpolation, "what replicates how" |
| `docs/03-roadmap.md` | Phases 0–5 with checkboxes; 0–3 marked done, 4–5 partially |
| `docs/04-workflow.md` | Branch model, PR flow, releases, Unity YAML merge driver setup |
| `docs/05-validation.md` | Netcode measurement procedure, network profiles, results, "Scenario C — observed, not measured", open items |
| `docs/adr/0002-decoupling-the-netcode-layer.md` | Options analysis for decoupling netcode from gameplay; **ADR 0001 does not exist** |
| `CLAUDE.md` | Working rules: language, reporting, conventions, comment policy, git permissions, pre-commit checklist |

### Documentation contradictions already visible in recon

These are **handed to A1 as leads, with evidence**, so agents converge instead of rediscovering:

1. **`README.md` "Running it"** instructs opening `Assets/_Project/Scenes/NetTest.unity`. That scene
   was deleted (4 touches in history, absent from the tree). The real entry scene is `Bootstrap.unity`.
2. **`README.md` "Status"** says *"Phase 1 (netcode core) is in … pending validation against a live
   remote peer"*, while `docs/03-roadmap.md` marks Phases 1, 2 **and** 3 complete and validation
   written up. `CLAUDE.md` requires README and docs to be updated in the same commit as the code.
3. **`docs/01-architecture.md`** lists `Core/` as holding "Bootstrap, scene flow, **service locators**"
   and `Core/` as holding "`FrameRatePolicy` (Phase 2: app state machine, scene flow)". Neither a
   service locator nor an app state machine exists in `Core/` (2 files, 68 + ~40 LOC).
4. **`docs/01-architecture.md`** folder listing omits `Match/ArenaBounds`, `Match/RoundReferee`,
   `Match/MatchConfig`, `Match/SpectatorCamera`, `Player/PlayerLife`, `Player/CharacterCatalog`,
   `Input/SpectatorInput`, `UI/LoadingScreenController`, `UI/EndScreenController` — the doc predates Phase 3.
5. **`docs/05-validation.md`** contains an explicit "Scenario C — observed, not measured" section and
   an "Open items" list. Agents must **not** cite Scenario C figures as MEASURED.
6. **ADR numbering starts at 0002.** Either 0001 was never written or it was removed.

## Testing and CI baseline

- **8 EditMode test files, ~1,100 LOC.** No PlayMode tests, no multi-instance / `NetcodeIntegrationTest`
  runs, no headless test job found in recon.
- No `.github/workflows/` directory was found at the repo root during the tree scan — **A9 must confirm
  and report the absence explicitly** rather than assume.
- The roadmap claims "36 unit tests"; A8 should verify the count against the actual `[Test]`/`[TestCase]`
  attributes rather than trusting the doc.

## What recon deliberately did NOT determine

Left for the domain agents, so nothing here is mistaken for a verified finding:

- Component composition of `Player.prefab` / `Fruit.prefab` (Unity YAML — is there a `NetworkTransform`?).
- Actual serialized field values in the 5 `.asset` configs (tick-relevant numbers: life drain rate,
  round length, fruit weights, movement speeds).
- Whether the snapshot RPC broadcasts all players to all clients (the O(n²) question) — grep found the
  RPC, not its payload shape.
- Whether `NetDebugOverlay` and the `NetworkSimulator` reference are stripped from release builds.
- Any profiler capture or bandwidth measurement in the repo — none was seen, but absence must be
  confirmed per-agent before being reported as a finding.
