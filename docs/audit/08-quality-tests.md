# Code Quality, Testing & Maintainability Audit

**Agent:** A8 · **Target:** `dev` @ `10a2a13` · **Date:** 2026-08-08

---

## Verdict

The test suite is genuinely good at what it covers and covers the wrong third of the project. There
are **77 `[Test]` cases** (not the 36 the roadmap claims) across 8 EditMode files, and they are
well-written: property-based determinism checks, boundary cases, and remarks that state the failure
each test guards. But **every one of them is single-peer, single-process, and synchronous** — there
is no PlayMode test, no `NetcodeIntegrationTest`, no multi-instance harness, and the netcode
package's test utilities are not even wired into `Packages/manifest.json`. For a project whose
stated purpose is "multiplayer programming done right", **no test in the repository can fail because
of a networking bug.** Reconciliation, snapshot ordering, the hostile-input admission filter and
every connection failure path have zero coverage. Compounding that, nothing *runs* the tests: no CI
workflow exists, and neither `CLAUDE.md`'s pre-commit checklist nor `.github/PULL_REQUEST_TEMPLATE.md`
mentions them.

Conventions are otherwise followed with unusual discipline — namespaces match folders 49/49, `var`
usage is compliant 13/13, zero TODO markers, zero commented-out code — with one large, self-declared
exception (the implicit-`private` debt, still 100% present) and a handful of small ones. The real
maintainability risk is `PredictedPlayer.cs`: 691 LOC, seven responsibilities, +104% growth in
14 days with a 3% deletion ratio, and not one line of it under test.

---

## Scorecard

| Dimension | Score /5 | Note |
|---|---:|---|
| Test breadth (share of the system covered) | **2** | 11 of 49 runtime files touched; ~16% of runtime LOC; 0% of the netcode *behaviour* |
| Test depth & craft (where they exist) | **5** | Determinism/replay properties, wrap-point cases, boundary rolls, 136 assertions with reasons |
| Multiplayer / integration testing | **0** | Zero PlayMode, zero `[UnityTest]`, zero integration harness, `testables` not declared |
| Testability of the architecture | **3** | `Simulation` is genuinely pure-testable as claimed; nothing above it is |
| Convention compliance vs `CLAUDE.md` | **3** | Excellent on naming/layout; 4 rules broken, one of them repo-wide and admitted |
| Comments & XML docs | **4** | Best-in-class rationale prose; the required-`<summary>` rule is self-contradictory and unmet |
| Maintainability / hotspot risk | **2** | One 691-LOC accreting god component, untested, on the critical path |
| Process & enforcement | **1** | Exemplary PR/branch hygiene; nothing anywhere executes the tests |
| Onboarding cost for dev #2 | **2** | Docs are strong, but two files are effectively untouchable without a live 2-peer session |

---

## Findings

### F-A8-1 — No test in the repository can catch a bug involving two peers

- **Severity**: Blocker
- **Type**: Correctness / Process
- **Confidence**: High
- **Evidence**: `Assets/Tests/EditMode/` (8 files, only test assembly in the project —
  confirmed against all 8 `.asmdef` files); `Snackdown.Tests.EditMode.asmdef:14-16`
  (`"includePlatforms": ["Editor"]`); zero matches for `[UnityTest]`, `NetcodeIntegrationTest`,
  `MultiprocessTest`, `NetworkManagerHelper` across `Assets/`; `Packages/manifest.json` has no
  `"testables"` entry, so NGO 2.11's `NetcodeIntegrationTest` base class is not even accessible to
  the test assembly.
- **What it is**: All 77 tests construct plain objects or `ScriptableObject`s in the Editor process
  and call synchronous methods. None starts a `NetworkManager`, spawns a second peer, sends an RPC,
  drops a packet, reorders a delivery, or advances a tick. The classes of bug that are structurally
  unreachable by this suite:

  | Bug class | Where it would live | Covered? |
  |---|---|---|
  | Reconciliation converges / diverges | `PredictedPlayer.cs:492-582` | No |
  | Snapshot arrives before spawn message | `NetworkSimulationLoop.cs:129-131` | No |
  | Out-of-order / duplicated unreliable RPC | `NetworkSimulationLoop.cs:119`, `PredictedPlayer.cs:380` | No |
  | Input queue starvation / burst catch-up | `PredictedPlayer.cs:434-449` | No |
  | Hostile input tick lead, queue flooding | `PredictedPlayer.cs:400-416` | No |
  | Owner vs host vs remote role divergence | `PredictedPlayer.cs` (all three paths) | No |
  | Connection approval callback, version mismatch | `ConnectionApproval.cs` (callback half) | No |
  | Relay/Direct failure, timeout, cancellation | `RelayConnectionProvider.cs`, `DirectConnectionProvider.cs` | No |
  | Match phase transition under late join | `MatchDirector.cs`, `RoundReferee.cs` | No |

  `com.unity.multiplayer.playmode` 2.0.2 *is* installed (`Packages/manifest.json:9`), but Multiplayer
  Play Mode is a manual, interactive tool — it produces no pass/fail artifact and cannot run
  unattended.
- **Why it matters**: This is the audit's headline result because of the portfolio framing, not
  despite it. The project's pitch is netcode correctness, and its evidence for that correctness is
  currently (a) prose in `docs/02-netcode.md`, (b) a hand-played session summarised in
  `docs/05-validation.md` — whose Scenario C is explicitly labelled "observed, not measured" — and
  (c) 77 tests that verify a single-player platformer controller. A senior netcode engineer reading
  this repo will ask "what proves the reconciliation converges?" and the honest answer today is
  "someone watched an overlay". One `NetcodeIntegrationTest` that stands up a host and a client,
  injects a 150 ms delay, drives 300 ticks of scripted input and asserts final positions agree within
  tolerance would answer that question permanently — and is exactly the artefact that distinguishes
  this project from every other Unity multiplayer sample.
- **Recommendation**: Add `"testables": ["com.unity.netcode.gameobjects"]` to
  `Packages/manifest.json`, create `Assets/Tests/PlayMode/` with a second `.asmdef`, and write **one**
  `NetcodeIntegrationTest` with 2 clients that asserts (i) owner and server converge on the same
  `PlayerState` after a scripted input sequence, and (ii) a dropped snapshot does not leave the owner
  permanently offset. One test closes the credibility gap; a suite is not required.
- **Effort**: M (1-3d)

---

### F-A8-2 — Reconciliation, snapshot dispatch and the hostile-input filter have zero coverage

- **Severity**: Major
- **Type**: Correctness
- **Confidence**: High
- **Evidence**: `PredictedPlayer.cs:492-582` (`Reconcile`, 90 lines, 9 exit branches);
  `PredictedPlayer.cs:400-416` (`EnqueueIfNew`); `NetworkSimulationLoop.cs:72-136`;
  `WorldSnapshotBuffer.cs:44-70`. None appears in any test file.
- **What it is**: `Reconcile` has nine distinct outcomes — stale ack (`:499`), RTT derivation
  (`:504`), teleport snap (`:507`), first-sync snap (`:519`), prediction-disabled follow (`:527`),
  duplicate-ack early out (`:537`), ring-capacity overflow snap (`:542`), missing-buffer-entry snap
  (`:553`), within-tolerance no-op (`:562`), and the replay loop (`:565-573`). Each is documented
  with the failure it prevents. None is executed by a test. The same is true of `EnqueueIfNew`,
  whose remarks at `:78-99` state explicitly that it exists because "a ServerRpc assumes the caller
  is hostile" — that hardening landed in `bf71345` (2026-08-01), five days *before* the test assembly
  existed (`e99a6fb`, 2026-08-06), and no test was added for it retroactively.

  `WorldSnapshotBuffer` deserves separate mention: it is `PredictionBuffer`'s structural twin — same
  ring, same `tick % Capacity` index, same per-slot tick validation, same wrap-around failure mode
  (`WorldSnapshotBuffer.cs:16-18` says so in its own remarks). `PredictionBuffer` has 10 tests
  including four specifically about wrapping. `WorldSnapshotBuffer` has none.
- **Why it matters**: These branches are pure logic over plain data. Most of them need no
  `NetworkManager` at all — `EnqueueIfNew` and `WorldSnapshotBuffer` could be tested today with the
  existing EditMode assembly. They are untested not because they are hard, but because they live
  inside a `NetworkBehaviour` (or were written before the suite existed). Any future change to
  reconciliation — and Phase 4's scoreboard will touch this file — has no safety net.
- **Recommendation**: Two steps, in order of value. (1) Test `WorldSnapshotBuffer` immediately —
  it is already a plain class, and 6 tests mirroring `PredictionBufferTests` cost under an hour.
  (2) Extract the input-admission rule from `PredictedPlayer` into a small plain class
  (`InputAdmission` with `TryAdmit(in InputCommand, uint serverTick, Queue<InputCommand>)`) and test
  the staleness, lead-clamp and capacity-drop rules directly.
- **Effort**: S (<1d) for (1), M (1-3d) for (2)

---

### F-A8-3 — Nothing in the project ever runs the tests

- **Severity**: Major
- **Type**: Process
- **Confidence**: High
- **Evidence**: `.github/` contains exactly one file, `PULL_REQUEST_TEMPLATE.md` — no `workflows/`
  directory anywhere in the repo. `.github/PULL_REQUEST_TEMPLATE.md:16-18` lists three checkboxes
  (compiles / console clean / tested with more than one peer) and **no test-run item**.
  `CLAUDE.md:130-141` ("Before every commit or PR") lists five items and likewise never mentions
  running the suite.
- **What it is**: The 77 tests execute only when a human opens the Unity Editor and clicks Run All in
  the Test Runner. No commit, PR or merge is gated on them. Across 19 merged PRs and 55 commits there
  is no evidence any of them was blocked by a failing test.
- **Why it matters**: An unenforced suite decays silently — the first test that breaks stays broken
  until someone happens to look. It also costs the project its main credibility signal: a GitHub
  Actions run with a green badge is the cheapest possible proof to a reviewer who will never open
  the editor. The test suite is currently invisible to exactly the audience it was written for.
- **Recommendation**: Add a `test-run` checkbox to `.github/PULL_REQUEST_TEMPLATE.md` and a sixth
  item to `CLAUDE.md`'s pre-commit list (both are 1-line edits, and cost nothing). Separately,
  consider `game-ci/unity-test-runner` in a GitHub Actions workflow — it needs a Unity Personal
  license secret, which is the only real friction.
- **Effort**: S (<1d) for the checklists; M for CI including licensing

---

### F-A8-4 — `PredictedPlayer.cs` is an accreting, untested hotspot on the critical path

- **Severity**: Major
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Gameplay/Player/PredictedPlayer.cs`, 691 LOC (largest file
  in the project, 11.3% of runtime C#); 11 commits `main..dev` (most-churned code file). Measured
  growth, `git show <sha>:<path> | wc -l`:

  | Date | Commit | LOC | Δ |
  |---|---|---:|---:|
  | 2026-07-25 | `965d0f8` | 339 | — |
  | 2026-08-01 | `bf71345` | 435 | +96 |
  | 2026-08-06 | `e99a6fb` | 557 | +122 |
  | 2026-08-08 | `6f4c863` | 585 | +28 |
  | 2026-08-08 | `c2a0df6` | 609 | +24 |
  | 2026-08-08 | `b46adc3` | 650 | +41 |
  | 2026-08-08 | `925f1fb` | 691 | +41 |

  Cumulative across all 11 touches: **713 insertions, 22 deletions** (3.0% deletion ratio).
- **What it is**: The file is **not** thrashing — no commit ever rewrote it, and the low deletion
  ratio proves each change was an insertion into stable structure. That is the good news. The bad
  news is the shape of the growth: it is strictly monotonic, +104% in 14 days, and every Phase 3
  feature (spawn placement, stun, peer collision, win conditions) added ~30-40 lines to the *same*
  class. It now carries seven separable responsibilities: owner prediction (`:305-335`), server
  authority (`:422-455`), input RPC + admission (`:380-416`), reconciliation (`:492-589`), remote
  interpolation (`:591-606`), debug telemetry (`:160-210`), and run recording to disk (`:212-234`).
  It has no direct test coverage of any of them.
- **Why it matters**: Phase 4's three remaining items (live scoreboard, spectator polish, second
  arena) all touch player state. At the observed rate the file reaches ~800 LOC by Phase 4 close.
  The concrete risk is not that the code is wrong — it reads well — but that it is the one file
  nobody can change with confidence, because a mistake in `Reconcile` produces a rubber-banding
  symptom that only appears with two peers and real latency, which nothing automated will reproduce.
  This finding and F-A8-1 are the same problem seen from two ends.
- **Recommendation**: Do not refactor for its own sake — the class is cohesive by role, and splitting
  it into three MonoBehaviours would spread one tick across three files for no isolation gain
  (over-engineering rubric #9). Instead extract the two genuinely stateless pieces that carry the
  most risk and the least Unity coupling: input admission (see F-A8-2) and the reconcile *decision*
  (given `predicted`, `authoritative`, `ackTick`, `latestPredictedTick`, `hasSynced`, return an enum
  of `Ignore | Snap | Replay | Accept`). Both become plain functions the EditMode suite already knows
  how to test, and `PredictedPlayer` keeps the Unity plumbing.
- **Effort**: M (1-3d)

---

### F-A8-5 — Both interfaces are architectural seams; neither is a test seam

- **Severity**: Major
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: `IConnectionProvider.cs:31`; implementations at `DirectConnectionProvider.cs:23` and
  `RelayConnectionProvider.cs:30`; sole consumer `MainMenuController.cs:40,129`. No test file
  references `IConnectionProvider`. `IPredictedPeer.cs:20`; sole implementation
  `PredictedPlayer.cs:31`. No test file references `IPredictedPeer`.
- **What it is**: Judged against the over-engineering rubric, both survive — but for architectural,
  not testing, reasons:
  - **`IConnectionProvider` is earned.** Two live implementations (rubric #1 does not fire), and the
    Task/`CancellationToken`/result-object shape at `:46-55` is driven by a real asymmetry
    (microsecond LAN bind vs a cancellable web round trip), argued at `:17-29`.
  - **`IPredictedPeer` is earned, and provably so.** One implementation, but the justification is
    compile-time and verifiable: it breaks a `Netcode ↔ Gameplay` assembly cycle that the compiler
    now enforces (`IPredictedPeer.cs:10-15`, commit `e99a6fb`). That is a plugin point of a kind —
    the compiler is the consumer.

  What neither does is buy testability, which is what an interface usually pays for. There is no
  `FakeConnectionProvider` proving the menu renders a timeout as a message rather than an exception
  (`IConnectionProvider.cs:23-25` states that is the design intent — untested). There is no
  `FakePredictedPeer` driving `NetworkSimulationLoop`'s four-phase ordering, which
  `NetworkSimulationLoop.cs:11-19` calls the thing that "removes an entire category of 'it desyncs
  sometimes' bugs" — also untested, because `OnNetworkTick` is `private` and reads
  `NetworkManager.LocalTime`/`ServerTime`.

  Secondary observation: `ActivePlayers` is typed `IReadOnlyList<IPredictedPeer>`
  (`NetworkSimulationLoop.cs:29`) but **three of its four consumers immediately downcast** —
  `HeadBounce.cs:77` (`peer is PredictedPlayer player`), `PredictedPlayer.cs:657`
  (`peer is not PredictedPlayer other`), `NetDebugOverlay.cs:146`. Only the tick loop itself
  (`:77-89`) uses the abstraction as an abstraction. That is rubric #10 (a leaking abstraction), and
  it is worth naming honestly even though the interface's core justification stands.
- **Why it matters**: The abstractions that would earn their keep *if* tests used them are exactly
  these two, and adding a fake to each is cheap — the interfaces are already narrow (6 members and
  5 members). Without them, the connection failure matrix and the tick ordering are verified only by
  reading the code.
- **Recommendation**: Add a `FakeConnectionProvider` in the test assembly returning scripted
  `ConnectionResult`s, and test that `MainMenuController` surfaces each `ConnectionFailure` as a
  message. Make `NetworkSimulationLoop`'s phase ordering testable by extracting the tick body into
  an `internal static void RunTick(IReadOnlyList<IPredictedPeer>, uint localTick, uint serverTick,
  bool isServer, Action<float> after)` — the loop keeps the NGO wiring, the ordering becomes a
  function a fake peer can record calls against.
- **Effort**: M (1-3d)

---

### F-A8-6 — `docs/03-roadmap.md` claims 36 unit tests; there are 77

- **Severity**: Minor
- **Type**: Process
- **Confidence**: High
- **Evidence**: `docs/03-roadmap.md:90` — "36 unit tests, they needed no refactor and could have been
  written from day one". Actual count (`grep -ao "\[Test\]"`, binary-safe):

  | File | `[Test]` |
  |---|---:|
  | `PlayerMotorTests.cs` | 15 |
  | `SnapshotInterpolatorTests.cs` | 11 |
  | `PredictionBufferTests.cs` | 10 |
  | `FruitTableTests.cs` | 10 |
  | `ConnectionApprovalTests.cs` | 10 |
  | `PeerCollisionTests.cs` | 8 |
  | `StunTests.cs` | 7 |
  | `ArenaBoundsTests.cs` | 6 |
  | **Total** | **77** |

  Zero `[TestCase]`, zero `[TestCaseSource]`, zero `[UnityTest]` — so 77 attributes = 77 executed
  cases, no parameterised expansion.
- **What it is**: The number was correct when written (`e99a6fb`: 15 + 10 + 11 = 36) and was never
  updated as the suite more than doubled. Notably, the *commit messages* did track it accurately
  (`0607fdc`: "10 more unit tests, 56 total" — also correct at that point). Only the doc drifted.
- **Why it matters**: `CLAUDE.md:90-91` requires docs to be updated in the same commit as the code,
  and this is the one metric a reviewer is most likely to spot-check. It also undersells the work by
  53%.
- **Recommendation**: Change `36` to `77` at `docs/03-roadmap.md:90`, or better, drop the absolute
  number and state what is covered — the count will drift again.
- **Effort**: S (<1d)

---

### F-A8-7 — The "verified over 100k rolls" claim is not reproducible from the repository

- **Severity**: Minor
- **Type**: Process
- **Confidence**: High
- **Evidence**: `docs/03-roadmap.md:65` — "distribution verified over 100k rolls". Commit `0607fdc`'s
  message elaborates: "Verified over 100,000 rolls: every entry landed within 0.08 points of its
  configured chance". `git log -S"100000"` and `-S"100_000"` return no test file in any commit;
  `FruitTableTests.cs` (all 10 tests) contains no loop above 101 iterations (`:81`).
- **What it is**: A real verification was clearly performed — the 0.08-point figure is too specific
  to be invented — but it was run out-of-tree and discarded. The repo cannot reproduce it.
- **Why it matters**: Under the audit's evidence rule this figure is `ESTIMATED`, not `MEASURED`, and
  a reviewer who goes looking for the harness will not find it. The fix is nearly free: `Roll` already
  takes the random value as an argument (`FruitTable.cs:66` region, and `FruitTableTests.cs:11-14`
  explains exactly why), so a seeded 100k-iteration statistical test is ~15 lines and runs in
  milliseconds.
- **Recommendation**: Add `FruitDistribution_MatchesConfiguredWeights` with a fixed-seed
  `System.Random`, 100,000 rolls, and a ±0.1-point assertion per entry. Then the doc line becomes
  true of the repository rather than of a session.
- **Effort**: S (<1d)

---

### F-A8-8 — The terrain-collision half of `PlayerMotor` is untested by construction, and `<summary>` calls it "Pure"

- **Severity**: Minor
- **Type**: Correctness
- **Confidence**: High
- **Evidence**: `PlayerMotor.cs:33` — `/// <summary>Advances one character by exactly one tick.
  Pure: same inputs, same output, always.</summary>`. The method reaches `Physics2D.BoxCast` at
  `PlayerMotor.cs:137`, `:162` and `:188`. All three motor test fixtures neutralise those casts:
  `PlayerMotorTests.cs:44` (`_config.GroundMask = 0; // nothing is solid, so casts never hit`),
  `PeerCollisionTests.cs:36`, `StunTests.cs:37`.
- **What it is**: `Simulate` is a pure function of `(state, input, config, world, dt)` **plus the
  Physics2D scene**. The class-level `<remarks>` at `:10-22` is scrupulously honest about this —
  it explains casts-vs-`Physics2D.Simulate` and why a query is repeatable. Only the one-line
  `<summary>` overstates it. The consequence is a real coverage hole: the skin-width arithmetic at
  `:143` and `:168` (`sign * Mathf.Max(0f, hit.distance - skin)`), the grounded-on-landing rule at
  `:169`, and the standing-still downward probe at `:188-192` — the highest-arithmetic-density,
  most-off-by-one-prone code in the simulation — are executed by **zero** of the 77 tests, because
  all three fixtures switch the mask off. `PlayerMotorTests.cs:15-19` states this openly and gives
  the reason, which is to its credit; but the gap is still the gap.
- **Why it matters**: A skin-width regression would desync client and server identically (both run
  the same wrong code), so it would surface as a *gameplay* bug — walking into walls, sticking, a
  free jump — not a netcode one, and would be caught only by playing. The claim "Pure: same inputs,
  same output, always" is also the kind of sentence an interviewer will test by grepping for
  `Physics2D`.
- **Recommendation**: Two independent fixes. (1) Soften `:33` to "Deterministic: same inputs and
  same scene, same output" — one word, and it matches the honest `<remarks>` directly below it.
  (2) Add a small EditMode fixture that builds a `GameObject` with a `BoxCollider2D` on a real layer
  and asserts the character stops flush against it — `ArenaBoundsTests.cs:21-30` already establishes
  the pattern of building scene objects in EditMode, so this is not new machinery.
- **Effort**: S (<1d)

---

### F-A8-9 — `GetComponent` in `Update` and in simulation code, against the repo's own rule

- **Severity**: Minor
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: `CLAUDE.md:61` — "Cache component lookups in `Awake`; no `GetComponent` in `Update`
  or in simulation code." Violated at:
  - `Fruit.cs:77` — `_overlaps[i].GetComponentInParent<PlayerLife>()`, inside `Update()` (`:69`),
    once per overlap hit, every frame, on the server, for every live fruit.
  - `HeadBounce.cs:95` — `lower.GetComponent<PlayerLife>()`, inside `Resolve`, called from
    `ResolveAll` (`:58`) which is subscribed to `NetworkSimulationLoop.AfterServerSimulation`
    (`:47`) — i.e. simulation code, 30 Hz, over every candidate pair (O(n²), 6 pairs at 4 players).
- **What it is**: Two literal violations of a rule the repository states as binding. Both are
  avoidable: `HeadBounce` already holds a `List<PredictedPlayer>` (`:43`) and `PredictedPlayer`
  already caches `_life` in `OnNetworkSpawn` (`PredictedPlayer.cs:251`) and exposes `IsSolid`
  (`:150`) derived from it — the lookup at `HeadBounce.cs:95` is re-deriving something the object
  next to it already knows.
- **Why it matters**: The performance cost at 4 players is negligible and should not be the argument.
  The cost that matters is consistency: `CLAUDE.md` is part of the portfolio deliverable, and a rule
  the code breaks twice is a rule a reviewer will read as aspirational.
- **Recommendation**: In `HeadBounce.Resolve`, replace the lookup with `lower.IsSolid` (already
  false for a dead player — `PredictedPlayer.cs:150`), which makes the check free and removes the
  duplicate liveness rule. In `Fruit.Update`, cache `PlayerLife` per `Collider2D` in a small
  dictionary, or have `PlayerLife` register its collider on spawn.
- **Effort**: S (<1d)

---

### F-A8-10 — Seven dead public members

- **Severity**: Minor
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: Full-solution symbol scan (declaration is the only occurrence, tests included):

  | Member | Location |
  |---|---|
  | `DirectConnectionProvider.Approval` | `Connection/DirectConnectionProvider.cs:44` |
  | `RelayConnectionProvider.JoinCode` | `Connection/RelayConnectionProvider.cs:43` |
  | `FruitSpawner.ServerDespawnAll()` | `Gameplay/Fruits/FruitSpawner.cs:136` |
  | `ArenaBounds.Center` | `Gameplay/Match/ArenaBounds.cs:24` |
  | `MatchDirector.ArenaIndex` | `Gameplay/Match/MatchDirector.cs:59` |
  | `PredictedPlayer.LastReplayedTicks` | `Gameplay/Player/PredictedPlayer.cs:166` (written at `:576`, never read) |
  | `WorldSnapshotBuffer.Clear()` | `Netcode/WorldSnapshotBuffer.cs:72` |

- **What it is**: `CLAUDE.md:137` forbids "dead code, unused fields" at every commit. These are the
  exceptions. `WorldSnapshotBuffer.Clear()` is the interesting one: its sibling `PredictionBuffer.Clear()`
  *is* called (`PredictedPlayer.cs:351`, `:515`), so the world buffer is deliberately or accidentally
  never cleared on a prediction toggle or a teleport. That is harmless today — `ContextFor` validates
  the slot's tick (`WorldSnapshotBuffer.cs:63`), so a stale frame can never be read as a fresh one —
  but the asymmetry looks like an oversight and reads like one.
- **Why it matters**: Small, but this repo's whole thesis is that the code is the deliverable, and
  `RelayConnectionProvider.JoinCode` in particular is the join code — a reader will assume the menu
  displays it and go looking for where.
- **Recommendation**: Delete the six unused accessors, or wire `JoinCode`/`ArenaIndex` into the UI if
  that was the intent. Either call `_world.Clear()` alongside `_buffer.Clear()` in `OnPredictionToggled`
  and the teleport branch, or delete `WorldSnapshotBuffer.Clear()` and note in its remarks that
  tick-keying makes clearing unnecessary.
- **Effort**: S (<1d)

---

### F-A8-11 — The XML-doc rule contradicts itself, and the netcode layer meets neither half

- **Severity**: Minor
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: `CLAUDE.md:72-78` requires "an XML `<summary>` on **every** public type and public
  member of the netcode layer" and, four lines later, forbids "filler `<summary>` on self-evident
  members". Measured coverage of public declarations carrying a `///` block:

  | Assembly | Documented / total | Excluding overrides & interface impls |
  |---|---|---|
  | `Snackdown.Netcode` | 33 / 64 (52%) | 33 / 60 (55%) — **27 undocumented** |
  | `Snackdown.Simulation` | 21 / 54 (39%) | 21 / 47 (45%) |
  | `Snackdown.Connection` | 45 / 74 (61%) | 45 / 59 (76%) |

  Concrete cases: `PredictionBuffer.Store` (`:35`), `TryGetState` (`:54`), `TryGetInput` (`:67`),
  `Clear` (`:80`) — four undocumented public members on a class whose own type-level `<remarks>`
  (`:9-17`) is one of the best pieces of prose in the repository. `MovementConfig.cs:18-54` has 12
  undocumented public fields (each does carry a `[Tooltip]`, which serves the same purpose in the
  Inspector).
- **What it is**: Not sloppiness — a judgment call. Faced with two rules that collide on
  `void Clear()`, the author consistently chose "no filler" over "document everything". That is the
  right call. But the rule as written is unmeetable, and a reviewer measuring the repo against its
  own `CLAUDE.md` will score it as 52% compliant.
- **Why it matters**: `CLAUDE.md` is a public artefact of this portfolio. A rule that the codebase
  intentionally and correctly violates should be rewritten rather than quietly broken.
- **Recommendation**: Change `CLAUDE.md:72-73` to require `<summary>` on every public **type** in the
  netcode layer and on every public member **whose contract is not evident from its signature** —
  which is what the code already does — keeping the `<remarks>`-for-*why* requirement untouched.
- **Effort**: S (<1d)

---

### F-A8-12 — The admitted implicit-`private` debt is total, not partial

- **Severity**: Minor
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: `CLAUDE.md:54` requires access modifiers "**Always explicit**"; `CLAUDE.md:65-66`
  admits "the existing scripts omit the implicit `private` modifier". Measured on `dev @ 10a2a13`:
  - **42 of 42** `[SerializeField]` declarations omit `private` — zero compliant instances anywhere
    in the project. `CLAUDE.md:60` names this pattern specifically (`[SerializeField] private`).
  - **~290 implicit-private declarations** across **34 of 49** runtime files (excluding interface
    members, which correctly carry no modifier). Worst: `MainMenuController.cs` (42),
    `PredictedPlayer.cs` (35), `LoadingScreenController.cs` (18), `NetDebugOverlay.cs` (16).
- **What it is**: The debt is not partial or legacy — it is the codebase's *actual* consistent style,
  applied to every file including the ones written last week (`ArenaBounds.cs`, `RoundReferee.cs`,
  `SpectatorCamera.cs`, all 2026-08-08). The declared convention and the practised convention are
  simply different.
- **Why it matters**: Low functional risk (C# defaults to `private`), and internally consistent, which
  is what actually matters for readability. The risk is presentational: a reviewer who reads
  `CLAUDE.md` first and the code second finds the very first stated rule contradicted on line 34 of
  the largest file. The remediation is mechanical and total — 290 sites, one regex, one commit —
  which is why it is worth either doing or formally retracting rather than leaving as standing debt.
- **Recommendation**: Pick one and close it. Either run the normalisation as the isolated commit
  `CLAUDE.md:65-66` already anticipates, or amend the convention table to say Unity-style implicit
  private on fields and explicit modifiers on types and public members — the pattern the code
  actually follows.
- **Effort**: S (<1d)

---

### F-A8-13 — Tests reach into private state by reflection, and duplicate their fixtures

- **Severity**: Minor
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**:
  - `FruitTableTests.cs:28-31` — `typeof(FruitTable).GetField("_entries", NonPublic | Instance).SetValue(table, entries)`.
  - `ArenaBoundsTests.cs:26-29` — `new UnityEditor.SerializedObject(_bounds)` +
    `FindProperty("_center")` / `FindProperty("_size")`.
  - Identical 13-line `MovementConfig` setup copy-pasted in `PlayerMotorTests.cs:32-44`,
    `PeerCollisionTests.cs:24-37` and `StunTests.cs:25-37`.
- **What it is**: Two of the eight fixtures cannot construct their subject through any public API, so
  they poke private fields by name. Renaming `_entries` or `_center` compiles cleanly and fails the
  tests at runtime with a `NullReferenceException` pointing at the wrong place. The duplicated
  `MovementConfig` block means a new field in `MovementConfig` needs three identical edits, and a
  fixture that drifts from its siblings will not be noticed.
- **Why it matters**: These are the two places where the "make it testable by passing arguments"
  discipline that `PlayerMotor` and `FruitTable.Roll` exemplify was not applied to the *construction*
  side. It is the seam that is missing, not the willingness to test.
- **Recommendation**: Give `FruitTable` an internal/test-visible `SetEntries` (or a static factory)
  and `ArenaBounds` a `Configure(Vector2 center, Vector2 size)`; both are two-line additions that
  remove all reflection. Extract the shipped-defaults `MovementConfig` into a shared
  `TestConfigs.Movement()` helper in the test assembly.
- **Effort**: S (<1d)

---

### F-A8-14 — Raw control bytes embedded in a source file make it opaque to tooling

- **Severity**: Nit
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: `ConnectionApprovalTests.cs` contains a literal `0x07` (BEL) at byte offset 2160
  (line 52, inside `SanitizeNickname("Lu\nca\t␇", 1)`) and a literal `0x00` (NUL) at offset 2400
  (line 60, inside `SanitizeNickname("\n\t␀", 3)`). `file` reports the file as `data`, not text;
  `grep -o` reports "Binary file matches" and refuses to enumerate hits — it is the only one of the
  49 runtime + 8 test files with this property.
- **What it is**: The test data is correct and the intent is right (verifying control characters are
  stripped), but the characters were pasted as raw bytes rather than written as C# escapes.
- **Why it matters**: The bytes are invisible in the editor, in `git diff` and in a GitHub PR view, so
  the test reads as if it passes `"Lu\nca\t"` twice. Any editor that normalises on save, or any tool
  that treats the file as binary, silently changes what the test asserts. A NUL byte in a C# source
  file is also outside what most C# tooling promises to handle.
- **Recommendation**: Replace with escapes — `"Lu\nca\t\a"` and `"\n\t\0"` — which are exactly
  equivalent, visible in review, and make the file plain text again.
- **Effort**: S (<1d)

---

### F-A8-15 — References to the deleted `NetTest` scene survive in tracked docs

- **Severity**: Nit
- **Type**: Process
- **Confidence**: High
- **Evidence**: `NetTest.unity` and `Core/NetTestBootstrap.cs` were deleted (absent from the tree;
  4 history touches each). Surviving references in **tracked** files: `README.md:48` ("Open
  `Assets/_Project/Scenes/NetTest.unity` and press Play") and `docs/03-roadmap.md:35` ("Host session
  in `NetTest.unity`"). Untracked/ignored occurrences (`Assembly-CSharp.csproj:64` — `*.csproj` is
  gitignored at `.gitignore:37`; `docs/local/netcode.html:380,396` — `/docs/local/` gitignored at
  `.gitignore:75`) are correctly out of scope.
- **What it is**: Two orphaned references in the repository's two most-read documents. Beyond those
  two, the sweep for leftovers came back clean: **zero** `TODO`/`HACK`/`WIP`/`FIXME`/`XXX` markers,
  **zero** blocks of commented-out code across all 57 C# files (the only `//`-then-code matches are
  two comments that legitimately begin with the word "return"), and exactly one `Debug.Log`
  (`NetDebugOverlay.cs:69`) which reports a written file path to the user rather than being a leftover.
- **Why it matters**: `README.md:48` is the first instruction a reviewer follows, and it names a file
  that does not exist. (A1 owns the doc-drift domain; recorded here because it is the leftover this
  audit's sweep was asked to look for.)
- **Recommendation**: Point both at `Bootstrap.unity`.
- **Effort**: S (<1d)

---

### F-A8-16 — Four files carry two top-level types

- **Severity**: Nit
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: `CLAUDE.md:56` — "One file / One top-level type, file named after it". Broken at:
  `Connection/ConnectionResult.cs` (`ConnectionFailure` + `ConnectionResult`),
  `Netcode/ReconciliationStats.cs` (`ReconciliationStats` + `ReconciliationWindow`),
  `Netcode/SnapshotFrame.cs` (`PlayerSnapshot` + `SnapshotFrame`),
  `Simulation/SimulationContext.cs` (`PeerBody` + `SimulationContext`).
- **What it is**: In each case the second type is a small companion (a failure enum, a result struct,
  an element struct) that has no meaning apart from its host. This is the defensible version of the
  violation — but it is still four instances of a rule stated without exceptions.
- **Why it matters**: Cosmetic. Listed for completeness because the surrounding conventions are
  followed so precisely that these stand out: namespaces match folders **49/49**, `var` is used only
  where the RHS names the type **13/13**, Allman braces and 4-space indentation are universal, and
  the `_camelCase` private-field convention holds throughout.
- **Recommendation**: Either split them, or add "unless the second type is a companion with no
  independent meaning" to `CLAUDE.md:56`. The latter is probably the honest edit.
- **Effort**: S (<1d)

---

### F-A8-17 — The README never mentions the test suite

- **Severity**: Minor
- **Type**: Process
- **Confidence**: High
- **Evidence**: `README.md` sections are Gameplay, Tech stack, Netcode highlights, Running it,
  Documentation, Status (`grep -n "^#"`). No occurrence of "test", "EditMode", "NUnit" or "coverage"
  anywhere in the file. Across all docs, tests are mentioned exactly twice:
  `docs/01-architecture.md:191-192` and `docs/03-roadmap.md:90` — the latter with the stale count
  (F-A8-6), and the former as an aside inside the assembly-graph section.
- **What it is**: 77 tests, 1,195 LOC, 136 assertions and 16% of the project's C# are invisible to
  anyone who reads only the README — which, for a portfolio repository, is most readers.
- **Why it matters**: This is the cheapest available fix in the entire audit. The suite is the
  strongest quality signal the project currently has, and it is not being shown. Two lines under
  "Tech stack" or "Status" would surface it.
- **Recommendation**: Add a short line to `README.md` stating the count, what it covers (the pure
  simulation, the prediction ring, the interpolator, the fruit table, nickname sanitation) and —
  honestly — what it does not yet cover (see F-A8-1). Stating the gap explicitly reads as
  engineering maturity; omitting the suite entirely reads as not having one.
- **Effort**: S (<1d)

---

## Quantified Estimates

### Test inventory — `MEASURED` (counted from source at `10a2a13`)

| Metric | Value | Method |
|---|---:|---|
| `[Test]` cases | **77** | `grep -ao "\[Test\]"` per file, summed (binary-safe; see F-A8-14) |
| `[TestCase]` / `[TestCaseSource]` | 0 | grep |
| `[UnityTest]` (PlayMode/coroutine) | 0 | grep across all of `Assets/` |
| Test files | 8 | `Assets/Tests/EditMode/*.cs` |
| Test LOC | 1,195 | `wc -l` |
| Runtime LOC | 6,098 | `wc -l` over 49 first-party `.cs` |
| Test share of C# | **16.4%** | 1195 / (1195 + 6098) |
| Test : runtime LOC ratio | **1 : 5.1** | — |
| `Assert.*` calls | 136 | grep |
| Assertions per test | 1.77 | 136 / 77 |
| Test assemblies | 1 (Editor-only) | 8 `.asmdef` files inspected |

> Roadmap claim of "36 unit tests" (`docs/03-roadmap.md:90`) was accurate at commit `e99a6fb`
> (15+10+11) and is now understated by 41 (see F-A8-6).

### Coverage map — `MEASURED` (per-file, by whether any test references the type)

| Assembly | Files | With coverage | Zero coverage |
|---|---:|---:|---:|
| `Snackdown.Simulation` | 6 | 5 | 1 (`InputPacket`) |
| `Snackdown.Netcode` | 9 | 2 | 7 |
| `Snackdown.Connection` | 9 | 2 (1 partial) | 7 |
| `Snackdown.Gameplay` | 17 | 2 | 15 |
| `Snackdown.Core` | 2 | 0 | 2 |
| `Snackdown.Input` | 2 | 0 | 2 |
| `Snackdown.UI` | 4 | 0 | 4 |
| **Total** | **49** | **11 (22%)** | **38 (78%)** |

> Recon's "38 first-party runtime files" undercounts; `find … -name "*.cs"` returns **49** for
> 6,098 LOC (its LOC figure of ~6.2k is right). The `Gameplay` row is 17, not 14.

Line coverage is not instrumentable without a coverage package, so this is `ESTIMATED`: covered
files total 1,140 LOC (18.7% of runtime), but `ConnectionApproval.cs` (234 LOC) is covered only for
its static `SanitizeNickname` (~30 LOC — the approval callback needs a live `NetworkManager`, stated
at `ConnectionApprovalTests.cs:10-12`), and `PlayerMotor.cs`'s three `Physics2D.BoxCast` branches are
switched off by every fixture (F-A8-8). Effective covered runtime LOC ≈ **900-960, or 15-16%**.
Formula: `1140 − 204 (ConnectionApproval callback) − ~35 (terrain-cast branches) ≈ 900`.

### Risky untested surfaces, ranked — `MEASURED` (locations) / `ESTIMATED` (risk)

| Rank | Surface | Location | LOC | Branches | Why risky |
|---:|---|---|---:|---:|---|
| 1 | `Reconcile` | `PredictedPlayer.cs:492-582` | 90 | 9 | The project's headline mechanism |
| 2 | Tick phase ordering | `NetworkSimulationLoop.cs:72-98` | 27 | 4 phases | Documented as preventing "it desyncs sometimes" |
| 3 | Input admission | `PredictedPlayer.cs:400-416` | 17 | 4 | Written explicitly against hostile clients |
| 4 | Snapshot dispatch | `NetworkSimulationLoop.cs:119-136` | 18 | 3 | Unreliable, out-of-order, pre-spawn |
| 5 | `WorldSnapshotBuffer` | `WorldSnapshotBuffer.cs:44-70` | 27 | 3 | Structural twin of a class with 10 tests |
| 6 | Server input drain / starvation | `PredictedPlayer.cs:422-455` | 34 | 3 | Host vs client vs starved paths |
| 7 | Win conditions | `RoundReferee.cs:86-194` | 109 | 6 | Tie-break rule at `:187` is pure and untestable as written |
| 8 | Match phase machine | `MatchDirector.cs` | 270 | — | 4 `NetworkVariable`s, scene load, late join |
| 9 | Connection failure paths | `RelayConnectionProvider.cs`, `DirectConnectionProvider.cs` | 545 | — | Timeout / cancel / bad code / version mismatch |
| 10 | Terrain collision | `PlayerMotor.cs:126-196` | 71 | 6 | Disabled in all 3 fixtures |

### Hotspot: `PredictedPlayer.cs` — `MEASURED` (`git log --numstat`)

| Metric | Value |
|---|---|
| Current LOC | 691 (11.3% of runtime C#) |
| LOC at creation (`965d0f8`, 2026-07-25) | 339 |
| Growth over 14 days | **+104%**, strictly monotonic — no commit reduced it |
| Commits touching it (`main..dev`) | 11 (most-churned code file) |
| Insertions / deletions, cumulative | 713 / 22 (**3.0%** deletion ratio) |
| Growth in Phase 3 alone (2026-08-08, 4 commits) | +134 LOC |
| Mean LOC added per feature commit | ~35 |
| Direct test coverage | 0 |
| Projected LOC at Phase 4 close (3 items × ~35) | ~800 `ESTIMATED` |

Interpretation: **accreting, not thrashing.** A 3% deletion ratio means every change was an
insertion into structure that held — the design is stable. What is not stable is the size, and the
absence of tests means the stability is a property of the author's memory rather than of the repo.

### Bus factor — `MEASURED`

| Metric | Value |
|---|---|
| Contributors | 1 (`LucaValentini25`) |
| Commits `main..dev` | 55, over 15 days |
| Merged PRs | 19, all `feature/*`, `bugfix/*` or `docs/*` → `dev` |
| Bus factor | **1** |
| Files where the author is the only reader-with-context | 49 / 49 |
| Test files modified after creation | **0 of 8** — every fixture was written once with its feature and never revisited |

That last row is the quiet signal: no test has ever been added in response to a bug. All three
`bugfix/*` PRs (#2, #3, #4) predate the test assembly (`e99a6fb`, 2026-08-06), including
`bugfix/phase1-hardening` (`bf71345`) which added the entire hostile-input admission filter that
still has zero coverage.

### Onboarding cost for a second developer — `ESTIMATED`

Assumes a competent Unity dev with NGO familiarity, given the repo and nothing else. Derived from
file sizes, coupling and doc coverage — no onboarding was observed.

| Milestone | Estimate | What blocks them |
|---|---|---|
| Build, run, host a local session | **0.5 day** | `README.md:48` sends them to a deleted scene (F-A8-15); recovery is guessing `Bootstrap.unity` |
| Understand the netcode model | **0.5 day** | Genuinely fast — `docs/02-netcode.md` plus the inline `<remarks>` are unusually good |
| Ship a **pure-simulation** feature (a dash, a wall slide) | **1 day** | Low risk. `PlayerMotor` is ordered steps by design (`:23-26`), and 30 tests catch a mistake |
| Ship a **UI or config** feature | **1-2 days** | `MainMenuController.cs` is 413 LOC with 42 implicit-private members and no tests |
| Ship a feature that touches **player state on the wire** | **4-6 days** | `PredictedPlayer.cs` — see below |
| Confidently change **reconciliation** | **not safely achievable today** | No test can tell them they broke it; the only feedback loop is two editors, hand-driven, under a simulated-latency profile |

**The file they would fear touching is `PredictedPlayer.cs`** — and correctly so. It is the largest
file, the most-churned, holds seven roles that share `_state` (`:50`), and has zero tests. Its
saving grace is that it is *well-commented*: a newcomer can read `:15-29` and know which of the three
roles they are in. That is why the estimate is 4-6 days rather than open-ended. The gap between
"can read it" and "can change it safely" is precisely the missing test layer.

---

## What is genuinely good here

This section is not a courtesy. Several things in this domain are better than most professional
codebases, and they should be defended, not traded away for coverage numbers.

1. **The tests that exist are the *right kind* of tests.** They assert properties, not
   implementations. `PlayerMotorTests.cs:71-94` replays a 40-tick pseudo-random input sequence twice
   and asserts identical end states — that is the exact invariant reconciliation depends on, tested
   as an invariant rather than as a golden value. `PeerCollisionTests.cs:142-150` and
   `StunTests.cs:120-129` do the same for their features. `PlayerMotorTests.cs:96-108` verifies
   `Simulate` does not mutate its argument, with a comment (`:99-101`) explaining that a replay reads
   past states repeatedly. This is how netcode is supposed to be tested.

2. **Every test states the failure it prevents.** `PredictionBufferTests.cs:9-16` — "a ring indexed
   by `tick % Capacity` will happily return a stale entry for a tick it never stored… and a
   reconciliation that trusts a wrong entry corrects toward a state that never existed."
   `SnapshotInterpolatorTests.cs:97-104` — "a bool must resolve to one side, never to an average",
   with the consequence named (a half-airborne remote character). `ArenaBoundsTests.cs:10-14` even
   explains why a test exists for a case the *shipped arena cannot exhibit*. A reader who has never
   seen this codebase learns the design from the test names alone.

3. **The tests correctly document their own limits.** `PlayerMotorTests.cs:15-19` states plainly that
   collision cases are absent and why. `ConnectionApprovalTests.cs:9-13` states that the approval
   callback needs a live `NetworkManager` and that only the pure half is covered. That is intellectual
   honesty in the artefact itself, and it is rarer than good coverage.

4. **`docs/01-architecture.md:191-192`'s testability claim is true.** Verified: no test file
   references `NetworkManager`, none enters Play mode, none loads a scene. `Snackdown.Simulation`
   is genuinely pure enough to test from the Editor, and the design decisions that made it so are
   deliberate and visible — `FruitTable.Roll` takes the random number as a parameter
   (`FruitTableTests.cs:11-14` explains why), `PlayerMotor.Simulate` takes `dt`, config and world as
   arguments (`PlayerMotor.cs:12-14`), and `PlayerState` carries `StunTimer` specifically so a replay
   reproduces through a stun (`StunTests.cs:122-124`). The claim in the doc is the outcome of a
   discipline, not a description bolted on afterwards.

5. **The assembly split earns its keep, verifiably.** Commit `e99a6fb`'s message documents that
   creating the `.asmdef`s made the compiler reject a `Netcode ↔ Gameplay` cycle that had existed
   invisibly inside `Assembly-CSharp` while `docs/01` claimed it did not. That is an architectural
   decision with a falsifiable justification and a recorded outcome — far stronger evidence than
   the usual "we split it for cleanliness". `IPredictedPeer` (`:20`, six members) and the
   `Simulation` assembly both exist because of it. The one wart is that three of four consumers
   downcast back to `PredictedPlayer` (F-A8-5), which is worth knowing but does not undo the argument.

6. **The rationale comments are the deliverable, and they hold up.** This repo invests unusually
   heavily in inline `<remarks>`, and judged honestly in both directions the investment is sound.
   Best-in-class examples: `PredictedPlayer.cs:193-202` explains that `UnityTransport.GetCurrentRtt`
   reads the *reliable* pipeline while all of this project's traffic is unreliable, and derives the
   honest measurement from `(latestPredictedTick − ackTick)` instead — that is a non-obvious
   engineering insight a reader could not recover from the code. `PredictedPlayer.cs:101-109`
   (why a teleport flag is announced three times over an unreliable channel),
   `PredictedPlayer.cs:340-347` (why toggling prediction must clear the stats window, or the demo
   lies), `Fruit.cs:59-67` (kinematic `Rigidbody2D` raises no trigger events without
   `useFullKinematicContacts` — a genuine Unity trap), and `NetworkSimulationLoop.cs:16-19` (the two
   clocks) are all reasoning that would otherwise be lost.

   The counter-check, since this must be judged in both directions: I looked for restating-the-code
   and over-narration and found **very little**. `PlayerMotor.cs:98-99` and `PredictedPlayer.cs:385`
   ("Oldest first, so the queue stays ordered") are the closest to restatement, and both still add
   the *reason*. The zero TODO/HACK/WIP count and zero commented-out blocks across 57 files are real,
   not a search artefact. The one place the prose overreaches is `PlayerMotor.cs:33`'s "Pure"
   (F-A8-8) — and the `<remarks>` immediately below it already contains the accurate version.

7. **Process hygiene is excellent where it exists.** 19 PRs, disciplined branch naming
   (`feature/*`, `bugfix/*`, `docs/*`), no direct commits to `dev`, and a PR template
   (`.github/PULL_REQUEST_TEMPLATE.md:10-14`) that demands "be specific and honest… List what you
   did NOT verify too". Commit messages carry multi-paragraph rationale including rejected
   alternatives (`e99a6fb`, `0607fdc`) and — notably — kept the test count accurate as it grew, even
   though the roadmap doc did not. That is a strong portfolio artefact independent of the code.

---

## Open questions for the team

1. **Is a headless CI run acceptable, or is the Unity Personal licence the blocker?** A GitHub
   Actions badge is the single highest-value addition available (F-A8-3), but `game-ci` needs a
   licence secret. If that is off the table, the fallback is the two checklist lines — which is
   worth knowing before recommending the larger option.

2. **Was the 100k-roll verification run in a scratch script, in the Editor console, or elsewhere?**
   (F-A8-7.) If the harness still exists locally, promoting it into `FruitTableTests` is a 15-minute
   job rather than a rewrite.

3. **Should the implicit-`private` debt be normalised or the convention amended?** (F-A8-12.) The
   codebase is 100% consistent with itself and 0% consistent with `CLAUDE.md:54`. Both resolutions
   are defensible; only a human can decide which reads better to the intended audience. Note that
   `CLAUDE.md:65-66` requires asking before doing it, which is why this is a question rather than a
   recommendation.

4. **Is `PredictedPlayer` intended to keep absorbing Phase 4 features, or is a split already planned?**
   The extraction proposed in F-A8-4 is deliberately minimal (two pure functions, no MonoBehaviour
   surgery) precisely because a larger split may already be intended — or deliberately rejected as
   fragmentation.

5. **Was `WorldSnapshotBuffer.Clear()` meant to be called alongside `PredictionBuffer.Clear()`?**
   (F-A8-10.) Today it is harmless because slots are tick-keyed, but the answer determines whether
   the fix is a call site or a deletion.

6. **Are `JoinCode` and `ArenaIndex` staged for a UI that has not landed yet?** If so they are not
   dead code but pre-wiring, and should be either used in Phase 4 or removed until then.

---

*A8 scope: test inventory and coverage, testability of the architecture, code conventions vs
`CLAUDE.md`, XML doc and comment quality, dead code and leftovers, churn/hotspot and bus-factor
risk, onboarding cost. Netcode semantics (A3/A4), performance (A5), security (A7) and doc-drift
(A1) are covered by other agents; overlaps are noted where they were unavoidable and attributed.*
