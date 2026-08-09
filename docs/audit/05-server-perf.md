# Server Performance & Scalability Audit

**Agent:** A5 · **Commit:** `10a2a13` · **Branch:** `dev` · **Date:** 2026-08-08

> **Reframing (per the brief).** `target_ccu: 0`, `hosting: none — Relay only`. There is no fleet.
> The "server" is one player's PC running a listen-server host. This audit answers two questions:
> **what does hosting cost that player**, and **what is the honest ceiling of this design** — both as
> figures the author can defend in an interview, not as launch blockers.

---

## Verdict

The per-tick simulation cost on the host is **negligible — roughly 0.08 ms against a 33.3 ms budget
(~0.25%)** — because `PlayerMotor.Simulate` performs only **1–2 `Physics2D.BoxCast` calls per player
per tick** and touches nothing else. The reconciliation replay, which the brief expected to be the
cost driver, is **not**: it is bounded at `PredictionBuffer.Capacity` = 1024 ticks
(`PredictionBuffer.cs:21`), it runs on the *owning client* and never on the host, and at realistic
RTTs it costs **0.06–0.35 ms in a single frame**. The design is fast for the right reasons and the
author can say so with a straight face. The two genuine costs found are elsewhere and both are
cheap to fix: **the snapshot RPC fires at 30 Hz regardless of match phase**, so the host serialises
and sends a full world frame to every client while everyone sits in the lobby menu
(`NetworkSimulationLoop.cs:97`); and **`FruitSpawner.ServerDespawnAll()` has zero call sites**
(`FruitSpawner.cs:136`), so the return-to-lobby path never cleans up spawned fruit. There is **no
profiler capture, no benchmark, and no performance test anywhere in the repository** — confirmed by
search — so every number below is `ESTIMATED` and shows its formula.

---

## Scorecard

| Dimension | Score /5 | Note |
|---|---:|---|
| Per-tick host CPU cost | **5** | 1–2 BoxCasts/player/tick, zero LINQ, zero `GetComponent` in the tick path. ~0.25% of budget. |
| Reconciliation replay cost | **5** | Bounded at 1024 ticks by an explicit capacity check (`PredictedPlayer.cs:542-551`). Realistic spike 0.06–0.35 ms. |
| Allocation / GC discipline (simulation) | **4** | Scratch arrays and ring buffers throughout; one recurring boxed-enumerator site contradicts its own comment. |
| Allocation / GC discipline (whole host process) | **2** | `NetDebugOverlay.OnGUI` is IMGUI, ships in the arena scene, is on by default, and dominates host GC by ~50×. |
| Idle cost | **2** | Snapshot broadcast is ungated by phase: 30 Hz of identical frames in Lobby / Loading / Ended. |
| Session lifecycle & teardown | **3** | Shutdown is genuinely careful; the rematch path exists but leaves an orphaned cleanup method unwired. |
| Headless-build readiness | **3** | The role-based branching and the `Simulation` assembly split are real assets; the asmdef graph and a runtime tools reference are not. |
| Measurement evidence | **1** | Zero profiler data, zero benchmarks, zero perf tests in the repo. Netcode *behaviour* is measured (`docs/05`); netcode *cost* is not. |

---

## Findings

### F-A5-1 — The snapshot RPC broadcasts at 30 Hz in every match phase, including the lobby

- **Severity**: Major
- **Type**: Performance
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Netcode/NetworkSimulationLoop.cs:84-98` (`BroadcastSnapshot(serverTick)` is called unconditionally after the `IsServer` guard); `NetworkSimulationLoop.cs:100-101` (only early-out is `Players.Count == 0`); contrast `PredictedPlayer.cs:305-307` (`OwnerPredictTick` returns on `!ShouldSimulate` *before* sending input) and `PredictedPlayer.cs:129-147` (`ShouldSimulate` is false in Lobby, Loading and Ended).
- **What it is**: `ShouldSimulate` correctly gates both `OwnerPredictTick` and `ServerSimulateTick` to the Countdown and Playing phases. The tick loop's phase-4 broadcast has no such gate. `PredictedPlayer` registers with the loop in `OnNetworkSpawn` (`PredictedPlayer.cs:270`) unconditionally, and player objects are never despawned between matches (documented as deliberate at `PredictedPlayer.cs:283-288`). So from the moment a client connects, the host builds a `SnapshotFrame` containing every player, serialises it, and sends it to every client, 30 times a second — through the lobby menu, through the arena load, through the countdown, and through the entire end screen. Every one of those frames is byte-identical to the last, because nothing simulated.
- **Why it matters**: The asymmetry is the tell — the *client* correctly sends nothing in the lobby, the *host* sends everything. On the host this is 30 serialisations/s plus 3× that many transport sends, permanently, for zero information. A4 owns the bandwidth figure; the structural point is that it is 100% waste and it is the host player who pays for it. It is also the one thing on this list an interviewer would spot in thirty seconds of reading `OnNetworkTick`, precisely because the function is written to be read as a sequence.
- **Recommendation**: Gate phase 4 the same way phases 1 and 2 are gated — broadcast only when the match phase is Countdown or Playing. **Caveat worth stating in the same commit:** remote peers' `SnapshotInterpolator` goes dry while broadcasting is off, so a client that joins during the lobby has no state for other characters until the countdown begins. If the player character is visible in the lobby, either keep one snapshot per second as a keepalive or send one frame on each phase transition.
- **Effort**: S

### F-A5-2 — `FruitSpawner.ServerDespawnAll()` is never called; the return-to-lobby path does not clean up spawned fruit

- **Severity**: Major
- **Type**: Correctness / Performance
- **Confidence**: Medium *(the method having zero callers is certain; whether the fruit objects survive the arena unload depends on Unity's active-scene behaviour, which I did not run)*
- **Evidence**: `Assets/_Project/Scripts/Gameplay/Fruits/FruitSpawner.cs:135-145` (declaration); repo-wide grep for `ServerDespawnAll` returns **only** that declaration. The teardown path is `EndScreenController.cs:104-108` → `MatchDirector.ServerReturnToLobby` (`MatchDirector.cs:254-268`), which clears `_loaded`, resets `_loadedCount`, sets the phase, calls `roster.ServerClearReady()`, calls `PlayerLife.ServerReset()` on everyone, and calls `UnloadCurrentScene()` — and nothing else. Fruit is created with a bare `Instantiate` at `FruitSpawner.cs:91`, i.e. into the active scene, and the arena is loaded **additively** (`MatchDirector.cs:159, 175`), which does not change the active scene.
- **What it is**: The cleanup method exists, is correct, and is wired to nothing. `FruitSpawner` itself lives in `Arena01.unity` (verified by resolving the scene's script GUIDs) so it dies with the arena, taking its `_active` list and its `_maxActive` = 6 cap with it. The next match starts a fresh spawner that counts from zero.
- **Why it matters**: Three consequences, in order of how bad they are. (1) If the fruit `NetworkObject`s land in `Bootstrap` — which is what an additive load implies — they survive the unload and accumulate across matches: up to 6 per match, so match 3 can be running with 18 spawned fruit. (2) Each surviving fruit runs `Update` on the host with a `Physics2D.OverlapCircle` at render rate (`Fruit.cs:69-73`), so leaked fruit are the only thing in this project whose host cost grows monotonically with session length. (3) `Fruit.Update` checks `IsServer`, `_collected` and `_table` but **not the match phase**, and `PlayerLife.ServerAdd` (`PlayerLife.cs:179-189`) checks `IsServer`/`IsAlive` but not the phase either — so a leaked fruit is collectible while sitting in the lobby.
- **Recommendation**: Call `ServerDespawnAll()` from `ServerReturnToLobby`. If the fruit does turn out to be scene-owned and destroyed with the arena, then per `CLAUDE.md`'s "no leftovers" rule the method is dead code and should be deleted instead — but confirm which, do not guess.
- **Effort**: S

### F-A5-3 — `NetDebugOverlay` is IMGUI, ships in the arena scene, is enabled by default, and dominates host GC

- **Severity**: Major
- **Type**: Performance
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/UI/NetDebugOverlay.cs:123-207` (`OnGUI`); `NetDebugOverlay.cs:25` (`bool _visible = true`); present in `Assets/_Project/Scenes/Arena01.unity` (confirmed by GUID resolution); `NetDebugOverlay.cs:5` imports `Unity.Multiplayer.Tools.NetworkSimulator.Runtime` from a **runtime** assembly, and no asmdef in the project declares `includePlatforms`, `excludePlatforms` or `defineConstraints` (verified across all 8 `.asmdef` files).
- **What it is**: ~25 interpolated-string `AppendLine` calls plus a `StringBuilder.ToString()` of ~700 characters, executed on every IMGUI pass. Unity dispatches `OnGUI` at least twice per frame (Layout and Repaint), and `FrameRatePolicy` pins the frame rate at 60 (`FrameRatePolicy.cs:26, 32`). `NetworkSimulationLoop.ActivePlayers` is iterated here as well (`NetDebugOverlay.cs:146`), boxing an enumerator each pass.
- **Why it matters**: ESTIMATED at **200–700 KB/s of managed allocation** (formula in the table below), against ~12 KB/s from the entire simulation path. That is a factor of roughly **50×**: the debug readout for the netcode costs more in GC pressure than the netcode. `gcIncremental: 1` is set in `ProjectSettings.asset:876`, which spreads the collections rather than eliminating them, so the effect is a small, permanent frame-time tax on the host rather than a visible hitch. The second half of this — a runtime assembly referencing a debug tools package with no build gating — means the overlay and its `NetworkSimulator` dependency go into any player build as-is.
- **Recommendation**: Two independent changes, both small. (a) Rebuild the string only when a value changed, or on a fixed ~4 Hz cadence rather than per IMGUI pass; the numbers are unreadable at 120 updates/second anyway. (b) Wrap the overlay's `NetworkSimulator` usage — and ideally the whole type — in `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, or move `NetDebugOverlay` into its own asmdef with a `defineConstraints` entry. That also removes the one runtime→tools-package edge in the assembly graph.
- **Effort**: S

### F-A5-4 — Fruit pickup runs a physics query per frame per fruit, issuing more queries per second than the entire player simulation

- **Severity**: Minor
- **Type**: Performance
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Gameplay/Fruits/Fruit.cs:69-88` — `Update`, not the network tick; `Fruit.cs:73` `Physics2D.OverlapCircle`; `Fruit.cs:77` `GetComponentInParent<PlayerLife>()` per overlap result; `FruitSpawner.cs:34` `_maxActive = 6`.
- **What it is**: 6 fruit × 60 fps = **360 `OverlapCircle` calls/s on the host**. The whole player simulation issues 4 players × 2 casts × 30 Hz = **240 casts/s**. The query itself is allocation-free (the `ContactFilter2D` + preallocated `Collider2D[8]` overload, `Fruit.cs:29-32` — a good detail). `GetComponentInParent` in a per-frame path is a managed→native hierarchy walk and contradicts `CLAUDE.md`'s "no `GetComponent` in `Update` or in simulation code".
- **Why it matters**: Small in absolute terms (~0.05 ms/frame ESTIMATED) but it makes fruit collection the largest physics consumer on the host, and it runs at a rate the rest of the match rules deliberately avoid. `HeadBounce.cs:52-57` argues at length that match rules belong on the tick and not in `Update`, because a render frame straddling a fast crossing misses the contact. Fruit collection is the same class of rule and takes the opposite decision, without saying why. It also inherits a staleness bug: `ProjectSettings/Physics2DSettings.asset` sets `m_AutoSyncTransforms: 0` and does not set `m_SimulationMode` (so it defaults to `FixedUpdate` at the 0.02 s `Fixed Timestep`), meaning the player collider positions this query reads are synced at 50 Hz and can be up to 20 ms behind the simulated position the same host just wrote.
- **Recommendation**: Move the pickup check onto `NetworkSimulationLoop.AfterServerSimulation`, where `HeadBounce` already runs — that halves the query rate, resolves collection against the same consistent set of positions as everything else, and removes the transform-staleness question. Cache the `PlayerLife` lookup or resolve it from `PlayerLife.All` (`PlayerLife.cs:82`) instead of walking the hierarchy.
- **Effort**: S

### F-A5-5 — Unity's 2D physics world steps 50 times a second on the host for a simulation that does not use the solver

- **Severity**: Minor
- **Type**: Performance
- **Confidence**: High
- **Evidence**: `ProjectSettings/Physics2DSettings.asset` contains no `m_SimulationMode` key (verified by grep across `ProjectSettings/`), so it defaults to `FixedUpdate`; `ProjectSettings/TimeManager.asset` `Fixed Timestep: 0.02` → 50 Hz. `Assets/_Project/Prefabs/Player.prefab` `m_BodyType: 1` (Kinematic), `m_Simulated: 1`. `docs/02-netcode.md:48-64` states explicitly that the dynamic solver is not used and never should be.
- **What it is**: Every host frame budget includes a full Box2D world step at 50 Hz — broadphase, transform sync, sleep management — over 4 kinematic player bodies, up to 6 fruit `CircleCollider2D`s and the arena's static geometry. The project resolves *all* of its collisions with casts and overlaps, which are queries and do not require the world to be stepped.
- **Why it matters**: ESTIMATED 0.05–0.2 ms per step × 50/s = **2.5–10 ms/s of host CPU, ~0.25–1% of one core**, spent producing a result nothing reads. The number is small; the argument is the point. Setting `Physics2D.simulationMode = SimulationMode2D.Script` makes "we do not let the physics engine decide anything that affects the match" (`Fruit.cs:59-62`) a *structural* guarantee rather than a convention, which is a much stronger thing to be able to say about a netcode project.
- **Recommendation**: Set `Physics2D.simulationMode = SimulationMode2D.Script` alongside the existing `FrameRatePolicy` bootstrap. Casts against static geometry are unaffected. **Required together with it:** any query that must see up-to-date *player* collider positions needs an explicit `Physics2D.SyncTransforms()` — which is only F-A5-4's fruit check, and folding that onto the tick makes the sync point obvious.
- **Effort**: S

### F-A5-6 — `foreach` over `IReadOnlyList<IPredictedPeer>` boxes an enumerator on every call, in the path documented as allocation-free

- **Severity**: Minor
- **Type**: Performance / Maintainability
- **Confidence**: High
- **Evidence**: `NetworkSimulationLoop.cs:29` exposes `public static IReadOnlyList<IPredictedPeer> ActivePlayers => Players`. Consumed with `foreach` at `PredictedPlayer.cs:654` (`CaptureWorld`), `HeadBounce.cs:76` (`Collect`), `NetDebugOverlay.cs:60` and `NetDebugOverlay.cs:146`. Compare `PredictedPlayer.cs:60-61`: *"Reused when building a context, so simulating allocates nothing."*
- **What it is**: Because the static type is an interface, `foreach` binds to `IEnumerable<T>.GetEnumerator()`, which boxes `List<T>.Enumerator` (~40 bytes) on the managed heap every call. The loop's own hot path correctly uses indexed `for` (`NetworkSimulationLoop.cs:77, 88, 107`); the consumers do not. `CaptureWorld` runs once per player per tick on the host — 120 calls/s at 4 players — and `Collect` once per tick.
- **Why it matters**: ~6 KB/s on the host. Genuinely trivial next to F-A5-3, and I am reporting it at Minor for exactly that reason. It is here because the comment two lines above the buffer says the path allocates nothing, and it does. In a project whose deliverable is the reasoning in the comments, a comment that is 95% right is worth correcting.
- **Recommendation**: Change the property to `IReadOnlyList<IPredictedPeer>` accessed by index in the three hot consumers (`for (int i = 0; i < ActivePlayers.Count; i++)`), or expose `List<IPredictedPeer>` directly so `foreach` picks up the struct enumerator.
- **Effort**: S

### F-A5-7 — `SnapshotFrame` allocates a fresh `PlayerSnapshot[]` on every deserialise

- **Severity**: Minor
- **Type**: Performance
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Netcode/SnapshotFrame.cs:70` — `if (serializer.IsReader) Players = new PlayerSnapshot[count];`
- **What it is**: Every incoming snapshot allocates an array. The send side is correctly pooled — `NetworkSimulationLoop.cs:104-105` reuses `_snapshotScratch` and only reallocates when the player count changes — so this is a one-sided omission, not an oversight in principle.
- **Why it matters**: 30 Hz × (24 B header + 4 × ~42 B) ≈ **5.8 KB/s on each client**. The host does not pay it (it is the sender). It is worth naming because it is the only allocation in the whole receive path, and closing it would let the author say the snapshot pipeline is allocation-free end to end — which is a better sentence than "almost".
- **Recommendation**: Keep a reusable array on the receiving side and only grow it when `count` exceeds its length, mirroring `_snapshotScratch`. Requires `SnapshotFrame` to hold a length alongside the array, or to be deserialised into a preallocated instance.
- **Effort**: S

### F-A5-8 — Every `PredictedPlayer` allocates all four diagnostic and prediction buffers regardless of role

- **Severity**: Nit
- **Type**: Performance
- **Confidence**: High
- **Evidence**: `PredictedPlayer.cs:55` (`PredictionBuffer`, 1024 entries, `PredictionBuffer.cs:21`), `:58` (`WorldSnapshotBuffer`, 128 × 8 bodies, `WorldSnapshotBuffer.cs:26-29`), `:161` (`ReconciliationStats`, 256 samples), `:162` (`RunRecorder`, `List<Sample>(4096)` at `RunRecorder.cs:35`). All are `readonly ... = new ...` field initialisers, so they are constructed before `OnNetworkSpawn` can tell whether this instance is the owner, the server, or a remote.
- **What it is**: ESTIMATED **~176 KB of managed heap per `PredictedPlayer` instance** (breakdown in the table below), of which ~173 KB is unreachable work on any instance that is not an owning client — the prediction buffer, the world buffer and the run recorder. At 4 players that is **~700 KB total, ~520 KB of it dead**.
- **Why it matters**: Irrelevant on PC and I am scoring it as such. It matters only against the stated `target_platforms` of Mobile and WebGL, where a fixed 700 KB of eagerly-allocated managed heap per session is still fine but is no longer free. Included for completeness of the memory picture, not as something to act on.
- **Recommendation**: Nothing, unless a WebGL build is actually attempted. If it is, construct these lazily in `OnNetworkSpawn` behind the role checks that already exist there.
- **Effort**: S

### F-A5-9 — `EndScreenController` calls `FindFirstObjectByType` every frame while the end screen is up

- **Severity**: Minor
- **Type**: Performance
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/UI/EndScreenController.cs:52-67` (`Update` runs `TitleFor`/`DetailFor` every frame once `ended`), `:92-102` (`NameOf` performs `FindFirstObjectByType<SessionRoster>()` and `roster[i].Nickname.ToString()`).
- **What it is**: A full scene-graph search plus two or three string allocations per frame, on every peer, for a label whose value cannot change. `MatchDirector.cs:262` performs the same lookup once, correctly, in `ServerReturnToLobby`. `CharacterAppearance.cs:33` and `MainMenuController.cs:282` also cache it once. This is the only per-frame instance.
- **Why it matters**: Small and confined to the post-match screen — but the end screen is exactly the moment a recorded portfolio demo lingers on, and `FindFirstObjectByType` in `Update` is the single most recognisable Unity performance smell there is. `PlayerLife.cs:74-81` contains a well-argued remark about specifically avoiding scene searches at frame rate; this contradicts it.
- **Recommendation**: Cache the roster and the resolved strings when the phase first becomes `Ended`, and keep the per-frame work to the `EnableInClassList` visibility toggle.
- **Effort**: S

---

## Quantified Estimates

**All rows are `ESTIMATED` unless marked otherwise. Confirmed absence of measurement data:** searched
the tree and `git ls-files` for `*.raw`, `*.prof`, `*profil*`, `*benchmark*`, `run-*.csv`,
`ProfilerRecorder`, `Unity.PerformanceTesting` and any performance-testing package in
`Packages/manifest.json` — **zero hits**. `docs/05-validation.md` measures netcode *behaviour*
(correction rate, error magnitude, replay depth) and contains no CPU, frame-time, memory or
allocation figure. The 8 EditMode test files contain no performance assertions.

### Shared assumptions

| Symbol | Value | Basis |
|---|---|---|
| `C_cast` | **5 µs** (range 3–8) | One `Physics2D.BoxCast` against a static arena, including the managed→native transition. **Assumption — unmeasured.** Every CPU figure below scales linearly with it. |
| `C_arith` | **1 µs** | The five pure steps of `PlayerMotor.Simulate` + two `SweepPeers` passes over ≤3 peers: ~80 float ops. |
| `C_xform` | **1 µs** | One `transform.position` write via `ApplyLogicalPosition` (`PredictedPlayer.cs:608-615`). |
| `C_send` | **5 µs** | One NGO unreliable RPC dispatch to one client. |
| tick budget | **33.3 ms** | 30 Hz, `Bootstrap.unity:446` `TickRate: 30`. **MEASURED (config).** |
| n | **4** | `players_per_session`. Settled. |

### Per-tick host CPU cost, 4 players

`NetworkSimulationLoop.OnNetworkTick` (`NetworkSimulationLoop.cs:72-98`), broken out by phase:

| Phase | Work | Formula | Cost |
|---|---|---|---:|
| 1 — owners predict | No-op on the host: every peer is `IsServer` (`NetworkSimulationLoop.cs:77-82`) | 4 interface property reads | ~0 µs |
| 2 — server simulates | `ServerSimulateTick` ×4. Each: `CaptureWorld` (O(n) scan + ≤3 struct copies) + `WorldAt` (≤3 copies) + `PlayerMotor.Simulate` + `ApplyLogicalPosition` | `4 × (2·C_cast + C_arith + 0.5n + C_xform)` = `4 × 14` | **56 µs** |
| 3 — head bounce | `Collect` (O(n)) + C(4,2)=6 pair tests, pure arithmetic (`HeadBounce.cs:58-115`) | `6 × 0.2` | **1.2 µs** |
| 4 — broadcast | 4 × `BuildSnapshot` + serialise ~176 B + send to 3 clients (`NetworkSimulationLoop.cs:100-112`) | `4 × 0.5 + 3 × C_send` | **17 µs** |
| | | **Total** | **≈ 74 µs = 0.074 ms** |
| | | **% of 33.3 ms budget** | **≈ 0.22 %** |

**Physics query count is the honest headline number:** `MoveAndCollideStep` (`PlayerMotor.cs:126-196`)
issues one X-axis `BoxCast` when `dx ≠ 0` and one Y-axis `BoxCast` when `dy ≠ 0`. Because
`GravityStep` sets `Velocity.y = -55/30 = -1.83` every tick before the move
(`PlayerMotor.cs:106-110`, `MovementConfig.asset` `Gravity: 55`), **`dy` is never zero in normal
play** — the `else` branch at `PlayerMotor.cs:184-193` is effectively dead code in a gravity world.
So the real per-player cost is **1 cast when standing still, 2 when moving**, and the total is
**240 casts/s at 4 players**, which is fewer than the fruit pickup check issues (F-A5-4).

**Verified against the doc's claim.** `docs/02-netcode.md:48-64` says the simulation is kinematic and
uses casts, never `Physics2D.Simulate`. Confirmed in code: `PlayerMotor.cs` contains three
`Physics2D.BoxCast` calls and nothing else; `Player.prefab` has `m_BodyType: 1` (Kinematic); a
repo-wide grep for `Physics2D.` returns only the three casts plus `Fruit.cs:73`'s `OverlapCircle`.
**The claim holds.**

### Reconciliation replay — the single-frame spike

Replay runs at `PredictedPlayer.cs:564-573`. Per replayed tick the work is
`TryGetInput` + `PlayerMotor.Simulate` + `OverwriteState`. Critically, **`CaptureWorld` is *not*
called during replay** — peer positions come from `WorldAt(t)`, an O(1) ring lookup
(`WorldSnapshotBuffer.cs:60-70`). So:

> `C_replayTick = 2·C_cast + C_arith + O(1) lookup ≈ 11 µs`
> `spike = pendingTicks × C_replayTick`, where `pendingTicks = _latestPredictedTick − ackTick`

| Scenario | Replay depth | Basis | Spike | vs. 16.7 ms frame @60fps |
|---|---:|---|---:|---:|
| RTT 150 ms | 5 ticks | `⌈150 / 33.3⌉` | **0.055 ms** | 0.3 % |
| RTT 367 ms (`docs/05` measured median) | **11 ticks** | **MEASURED** — `docs/05-validation.md:76` reports median 11 replayed ticks; `367 / 33.3 = 11.0`, so the code and the measurement agree exactly | **0.12 ms** | 0.7 % |
| Worst observed in `docs/05` | **27 ticks** | **MEASURED** — `docs/05-validation.md:77`, scenario B (20 % loss) | **0.30 ms** | 1.8 % |
| Mobile 2G, 520 ms delay treated as RTT | 16 ticks | `⌈520 / 33.3⌉` | **0.18 ms** | 1.1 % |
| Mobile 2G, 520 ms delay per direction | 31 ticks | `⌈1040 / 33.3⌉` | **0.34 ms** | 2.0 % |
| **Hard ceiling enforced by code** | **1024 ticks** | `PredictionBuffer.Capacity` (`PredictionBuffer.cs:21`), enforced at `PredictedPlayer.cs:542-551` — beyond it the client hard-snaps instead of replaying | **11.3 ms** | **68 %** |

**Three things an interviewer would want stated, in order:**

1. **The host never reconciles.** `PredictedPlayer.ApplySnapshot` returns immediately on
   `IsServer` (`PredictedPlayer.cs:477`), and the host takes the server path exclusively
   (`PredictedPlayer.cs:26-28`). In a listen-server topology the replay spike is a **remote client**
   cost, not a host cost. The two questions in this audit's title therefore have different answers
   for the same code.
2. **The spike is bounded by design, not by luck.** The `pendingTicks > Capacity` check at
   `PredictedPlayer.cs:542` is the reason the worst case is 11.3 ms and not unbounded, and its
   comment gives the right reason (replaying past a wrapped ring would land on a state neither side
   computed). 34 seconds of accumulated desync is not a frame-rate problem, it is a disconnection.
3. **At realistic RTTs the spike is under 0.4 ms.** The brief anticipated the replay as the cost
   driver. It is not, and the reason is structural: the per-tick simulation is two box casts.

### Managed allocation per second on the host

| Site | Formula | Rate | Share |
|---|---|---:|---:|
| `NetDebugOverlay.OnGUI` (`NetDebugOverlay.cs:123-207`) | ~25 interpolated strings (~50 B) + `_text.ToString()` (~700 chars ≈ 1.4 KB) + `float.ToString("F3")` per formatted value ≈ **2.7–5 KB/pass** × 2 IMGUI passes × 60 fps | **320–600 KB/s** | **~97 %** |
| `foreach` over `ActivePlayers` — boxed `List<T>.Enumerator`, 40 B (`PredictedPlayer.cs:654`, `HeadBounce.cs:76`, `NetDebugOverlay.cs:60, 146`) | `40 B × (4 CaptureWorld + 1 Collect) × 30 Hz` + `40 B × 2 × 2 × 60 fps` | **~15.6 KB/s** | ~2 % |
| `FruitSpawner.FreeSpawnPoint` — `new List<Transform>` (`FruitSpawner.cs:112`) | one list per spawn attempt, `_interval = 4 s` | **~0.05 KB/s** | <1 % |
| `EndScreenController` — `FindFirstObjectByType` + 3 strings/frame (`EndScreenController.cs:92-102`) | ~150 B × 60 fps, **Ended phase only** | ~9 KB/s (transient) | — |
| `PlayerMotor.Simulate` + `PredictionBuffer` + `WorldSnapshotBuffer` + `SimulationContext` | ring buffers, borrowed arrays, `readonly struct` context (`SimulationContext.cs:28-49`) | **0 B/s** | **0 %** |
| **Host total during a match** | | **≈ 340–620 KB/s** | |

**Read the share column, not the total.** The simulation path allocates **nothing**. The GC pressure
on the host is ~97 % debug overlay. `gcIncremental: 1` (`ProjectSettings.asset:876`) spreads the
collections, so the symptom is a permanent small frame-time tax rather than a hitch.

**`RunRecorder` — checked specifically per the brief.** It does **not** write CSV during a run. It
appends one 24-byte struct to a `List<Sample>` **only inside `Reconcile`, only on an actual
correction** (`PredictedPlayer.cs:578`), and `File.WriteAllText` happens only in `Write`
(`RunRecorder.cs:124`), reachable only via `WriteRunMetrics` ← `NetDebugOverlay.ExportRun` ← the F4
key (`NetDebugOverlay.cs:47, 58`). **It is active by default in every normal client session** —
`_recorder.Begin` is called unconditionally for `IsOwner && !IsServer` at `PredictedPlayer.cs:268` —
but at the measured 1.14 corrections/s (`docs/05-validation.md:71`) that is **27 B/s**, and the
`List(4096)` preallocation covers a 60-minute session before it doubles once. **Not a per-tick cost.**
It never trims, so a pathological multi-hour session grows the list without bound; at realistic rates
this is a non-issue.

### Memory footprint per `PredictedPlayer`

| Buffer | Formula | Size | Used by |
|---|---|---:|---|
| `PredictionBuffer` | 1024 entries × ~48 B (`uint` + `bool` + `InputCommand` 8 B + `PlayerState` 32 B) | **49 KB** | owning client only |
| `WorldSnapshotBuffer` | `Frame[128]` ~12 B + `PeerBody[128×8]` × 24 B | **26 KB** | owner + server |
| `RunRecorder` | `List<Sample>(4096)` × 24 B | **98 KB** | owning **non-host** client only |
| `ReconciliationStats` | 256 × 12 B | **3 KB** | owning client only |
| **Per instance** | | **≈ 176 KB** | |
| **4 players** | | **≈ 700 KB** | of which **~520 KB is unreachable** on remote instances (F-A5-8) |

**Memory is not a bottleneck at any player count this design targets.** A rematch costs **zero
additional memory**: every buffer is a fixed-size ring or a preallocated list, and the player objects
are deliberately never despawned (`PredictedPlayer.cs:283-288`).

### Ceiling analysis — how many players can one host process simulate?

Extrapolating the phase model above. Note the three quadratic terms: `CaptureWorld` is O(n) per
player, `HeadBounce` is C(n,2), and snapshot fanout is O(n) bytes to O(n) clients.

> `T(n) ≈ n·(2·C_cast + C_arith + C_xform + 0.5n) + 0.2·C(n,2) + 0.5n + C_send·(n−1)` µs

| n | Tick cost | % of 33.3 ms | Snapshot bytes/s upstream `(4+42n)(n−1)·30` |
|---:|---:|---:|---:|
| **4** | **0.074 ms** | **0.22 %** | 15.5 KB/s |
| 8 | 0.18 ms | 0.53 % | 71 KB/s |
| 16 | 0.44 ms | 1.3 % | 304 KB/s |
| 32 | 1.2 ms | 3.5 % | 1.25 MB/s |
| 64 | 3.6 ms | 11 % | 5.1 MB/s |
| 128 | 12.1 ms | 36 % | 20.6 MB/s |
| 256 | 43.9 ms | **132 % — over budget** | 82.6 MB/s |

**Bandwidth numbers in the last column are the raw payload arithmetic only, shown to establish
which curve binds first. A4 owns the real bandwidth figure** (headers, NGO framing, Relay
overhead, Relay's own limits).

**The first bottleneck, honestly, in order of when it is hit:**

1. **A hard structural cap at n = 9.** `WorldSnapshotBuffer.MaxBodies = 8`
   (`WorldSnapshotBuffer.cs:29`) and `Store` silently truncates past it (`WorldSnapshotBuffer.cs:47`),
   as does `CaptureWorld` (`PredictedPlayer.cs:656`). At 10 players, peer collision starts dropping
   bodies **without any error** and the client's replay stops matching the server's simulation. This
   is the real ceiling of the design as written, it is documented as matching the 4-player target,
   and it is a **correct, deliberate constant — not a defect**. It is also the answer to "what would
   break first if you doubled the player count", which is a better interview answer than a CPU
   number.
2. **Upstream bandwidth, around n ≈ 20–25.** Payload alone crosses ~5 Mbps — a realistic residential
   upload ceiling — near n = 22 by the formula above. Defer to A4 for the number that counts.
3. **CPU, around n ≈ 200.** Nine times later than bandwidth, and only if rendering and the debug
   overlay are excluded, which on a listen server they cannot be — the host is also drawing the game.
   In practice the host's render loop is a larger share of its frame than the tick is by two orders
   of magnitude.
4. **Memory: never.** ~176 KB per player, linear.

**Conclusion to state in an interview:** *bandwidth binds roughly 9× before CPU, and a hard-coded
`MaxBodies = 8` binds before either.* The 4-player limit is a settled design choice, and this
analysis measures against it rather than arguing with it — at 4 players the host uses **0.22 % of its
tick budget**, i.e. the design has ~450× CPU headroom it will never need and is bounded by the wire
and by a deliberate constant instead.

### Idle cost

| State | Host per-tick work | Snapshot RPC still firing? |
|---|---|---|
| Lobby | `ShouldSimulate` false → `ServerSimulateTick` returns at `PredictedPlayer.cs:424`. Zero casts. | **Yes — 30 Hz, full frame, identical every time.** `NetworkSimulationLoop.cs:97` has no phase gate. |
| Loading | Same | **Yes** |
| Countdown | Full cost (`ShouldSimulate` includes Countdown, `PredictedPlayer.cs:145`) | Yes — correctly |
| Playing, nobody moving | Near-full: `dy ≠ 0` every tick from gravity, so 1 BoxCast/player still runs. ~0.04 ms | Yes — with a byte-identical payload; no dirty check, no delta compression |
| Ended (end screen) | Zero casts | **Yes** — plus `EndScreenController`'s per-frame `FindFirstObjectByType` (F-A5-9) |

`Fruit.Update` and `FruitSpawner.Update` are gated on `MatchDirector.IsPlaying`
(`FruitSpawner.cs:70-71`) — except `Fruit.Update` itself is **not** (`Fruit.cs:71` checks only
`IsServer`, `_collected`, `_table`), which is what makes the F-A5-2 leak collectible in the lobby.

### Session lifecycle

| Aspect | Finding |
|---|---|
| **Cold start** | `Bootstrap.unity` → `AppBootstrap.Start` (`AppBootstrap.cs:24`) → `LoadingScreenController.EnsureMenuLoaded` loads `Lobby` additively (`LoadingScreenController.cs:153-159`). Three scenes in `EditorBuildSettings`, no Addressables, ~5 MB of art. **No measurement exists.** No async warmup, no shader prewarm — `docs/05-validation.md:39` records a cold shader cache costing "seconds" on the first arena load, which is why `MatchDirector` waits for every client's `OnLoadComplete` (`MatchDirector.cs:189-201`) before the countdown. That wait is correct and is a good design point. |
| **Match start** | `ServerStartMatch` → unload current scene (fire-and-forget, `MatchDirector.cs:170-176`) → NGO additive load → wait for all `OnLoadComplete` → 3 s countdown (`_countdownSeconds = 3`). Time-to-playable is bounded by the slowest client's scene load plus 3 s. |
| **Rematch / return to lobby** | **Exists** — `EndScreenController.cs:104-108` → `MatchDirector.ServerReturnToLobby` (`:254-268`), host-only. Cleans up: `_loaded` set, `_loadedCount`, phase, roster ready flags, all `PlayerLife` via `ServerReset` (`PlayerLife.cs:192-201`), arena scene unload. **Does not clean up:** spawned fruit (**F-A5-2**). |
| **Does the process leak across matches?** | **Buffers: no.** All fixed-size rings; `PredictionBuffer`/`WorldSnapshotBuffer`/`ReconciliationStats` reuse their arrays. **Subscriptions: no.** Every `+=` I found has a matching `-=` in `OnNetworkDespawn` — `MatchDirector.cs:112-125`, `PlayerLife.cs:112-118`, `SessionRoster.cs:68-77`, `HeadBounce.cs:50`, `NetworkSimulationLoop.cs:63-70`, `PredictedPlayer.cs:273-278`, and `AppBootstrap.cs:39-45` in `OnDestroy`. **Coroutines: none exist** — repo-wide grep for `StartCoroutine`/`IEnumerator` returns zero hits, so there is nothing to leak there. **Native memory:** `SessionRoster` correctly `override`s `OnDestroy` to `Dispose()` its `NetworkList` and call `base.OnDestroy()`, with a remark explaining why hiding rather than overriding would silently skip NGO's teardown (`SessionRoster.cs:163-173`) — this is exactly right and easy to get wrong. **Spawned `NetworkObject`s: fruit only, see F-A5-2.** |
| **Graceful shutdown** | **Yes, and it is careful.** `DirectConnectionProvider.LeaveAsync` (`:178-195`) calls `NetworkManager.Shutdown()` and then **polls `IsListening` with a 5 s deadline**, because NGO defers teardown to the next update and returning early would leave the next host/join attempt failing. `RelayConnectionProvider.LeaveAsync` (`:141-161`) additionally leaves the Relay session. Reached from the UI at `MainMenuController.cs:392`. |
| **Missing** | No `OnApplicationQuit` handler — quitting mid-session relies on process teardown. `docs/05-validation.md:43` records a real consequence of this class of problem (a leaked UDP socket surviving until process exit after a domain reload), so it is a known area. |

### Headless / dedicated-server viability — **observation, not a demand**

`hosting: none` is settled. This section answers only: *if the author were ever asked "could this run
headless?", what is the honest answer?*

**What already supports it — and it is more than you would expect:**

- **`docs/02-netcode.md:44-46` makes the claim explicitly:** *"Code branches on roles, never on 'am I
  the host', so a headless dedicated build stays possible (Phase 5) without a rewrite."* **I checked
  this and it is substantially true.** Every authority decision branches on `IsServer` / `IsOwner`:
  `PredictedPlayer.cs:428, 477, 479`, `NetworkSimulationLoop.cs:84`, `PlayerLife.cs:131`,
  `MatchDirector.cs:139`, `RoundReferee.cs:88`. There is no `IsHost` check anywhere in the
  simulation path. A server with no owned character would simply take the `else` branch at
  `PredictedPlayer.cs:434` for every player.
- **The `Simulation` assembly split genuinely buys something here — with one qualification.**
  `Snackdown.Simulation` references only `Unity.Netcode.Runtime` (for `INetworkSerializable`), and
  `PlayerMotor` touches `UnityEngine.Mathf`, `Vector2` and `Physics2D` — all present in a headless
  player. It has **no** reference to gameplay, UI, input or connection. So the authoritative step is
  already isolated from everything a headless build would strip. That is a real, non-cosmetic
  benefit and it is the strongest argument for the split. The qualification: it is a *layering*
  benefit, not a *platform* one — the assembly is not gated in any way, and the isolation would be
  just as effective for headless if it were folded back into `Netcode`.
- `stripEngineCode: 1` is already set (`ProjectSettings.asset:185`).

**What would be required:**

| Requirement | Status |
|---|---|
| A server build target | **Absent.** No `UNITY_SERVER`, no `-batchmode`, no `-nographics`, no `DedicatedServer` reference anywhere in `Assets`, `ProjectSettings` or `Packages` (verified by grep). |
| `#if UNITY_SERVER` gating | **Absent.** Zero occurrences. |
| Platform-gated assemblies | **Absent.** All 8 `.asmdef` files lack `includePlatforms`, `excludePlatforms` and `defineConstraints` (verified individually). |
| Rendering strippable | **Partly.** URP 2D + Cinemachine + `SpriteRenderer` on the player and fruit prefabs. `SpectatorCamera` (`LateUpdate`) and `CharacterAppearance` are gameplay-assembly types that would need role gating. |
| UI strippable | **No, as structured.** `Snackdown.Gameplay` → `Snackdown.UI` is not a dependency, but `Snackdown.UI` references `Unity.Multiplayer.Tools.NetworkSimulator.Runtime` from a **runtime** assembly (`NetDebugOverlay.cs:5`) with no gating — a debug tools package pulled into every build (see F-A5-3). |
| Input strippable | **Yes, cleanly.** `Snackdown.Input` is a leaf depending only on `Unity.InputSystem`, and `InputReader` is disabled on non-owners at `PredictedPlayer.cs:259`. A server owning nobody enables none. |
| Audio | Nothing to strip — no audio in the project. |

**Honest summary:** the *architecture* is headless-ready and the doc's claim survives inspection; the
*build configuration* is not started. Getting there is an afternoon of asmdef `defineConstraints` and
a Dedicated Server module install, not a rewrite. That is a good answer to give, and it costs nothing
to leave as-is given `hosting: none`.

---

## What is genuinely good here

Specific, cited, and load-bearing — this is not a courtesy section.

1. **`PlayerMotor` is a genuinely pure function and it pays off exactly where it was supposed to.**
   `PlayerMotor.cs:31-47` is a `static class` taking `(state, input, cfg, in world, dt)` and reading
   no `Time`, no `Transform`, no `Rigidbody2D`. That is why a 27-tick replay costs 0.30 ms instead of
   being impossible: `WorldAt(t)` is an O(1) ring lookup and `CaptureWorld` is correctly **excluded**
   from the replay loop (`PredictedPlayer.cs:564-573`). Most projects that claim "our simulation is
   pure" have a `Time.deltaTime` three frames deep. This one does not.

2. **The replay depth is explicitly bounded, with the right reasoning written down.**
   `PredictedPlayer.cs:542-551` catches `pendingTicks > PredictionBuffer.Capacity` and hard-snaps
   rather than replaying through overwritten ring entries. This is the difference between a worst
   case of 11.3 ms and an unbounded one, and the comment gives the correct reason (the replay would
   land on a state neither side ever computed). Very few hobby netcode implementations have this
   check at all.

3. **Allocation discipline in the simulation path is essentially perfect.** `_worldScratch` reused
   across `CaptureWorld`/`WorldAt` (`PredictedPlayer.cs:61`); `_snapshotScratch` reused and regrown
   only on player-count change (`NetworkSimulationLoop.cs:104-105`); `SimulationContext` a `readonly
   struct` over a borrowed array (`SimulationContext.cs:28-49`); `PredictionBuffer` and
   `WorldSnapshotBuffer` ring-indexed with per-slot tick stamps so a wrapped entry cannot masquerade
   as fresh (`PredictionBuffer.cs:33, 50`); `Fruit`'s overlap query using the non-allocating
   `ContactFilter2D` + preallocated array overload (`Fruit.cs:29-32`). Measured result: **0 bytes/s
   from the simulation**. My allocation findings are all *outside* this path, which is the correct
   place for them to be.

4. **Two O(n)-avoidance decisions made for stated reasons rather than by reflex.**
   `PlayerLife.All` is a spawn/despawn-maintained registry, with a remark
   (`PlayerLife.cs:74-81`) explaining that `FindObjectsByType` at frame rate "is the kind of cost
   that does not show up until a profiler is opened" — and `RoundReferee` then queries it every frame
   (`RoundReferee.cs:143-151`) with an indexed `for`. And `PlayerLife` replicates on an interval with
   client-side interpolation instead of per-frame `NetworkVariable` writes (`PlayerLife.cs:135-161,
   163-172`), cutting a documented 60 writes/s/player to 1. `MatchDirector` and `RoundReferee` both
   replicate a **deadline** instead of a counter (`MatchDirector.cs:36-50`, `RoundReferee.cs:30-38`).
   All three are the right call, and all three name the alternative they rejected.

5. **`FrameRatePolicy` is a performance fix derived from an actual observed failure.**
   `FrameRatePolicy.cs:8-22` documents that two uncapped renderers on one dev machine starved the
   network tick, producing 122 starved ticks and 1.66 corrections/s under 2 % loss — *"None of those
   corrections were the network's doing."* It caps at 60 (2× the tick) and clears vSync first because
   vSync silently overrides `targetFrameRate`. This is the only place in the repo where a performance
   change is tied to a measurement, and it is a textbook example of the diagnosis being worth more
   than the fix.

6. **Teardown is more careful than the codebase's age suggests.** Zero coroutines. Every event
   subscription has a matching unsubscribe. `SessionRoster.OnDestroy` correctly `override`s rather
   than hides, with the reason written down (`SessionRoster.cs:163-173`). `LeaveAsync` polls for
   `IsListening` to clear with a 5 s deadline rather than trusting `Shutdown()` to be synchronous
   (`DirectConnectionProvider.cs:171-195`). F-A5-2 is the only leak I found, and it is a missing call
   to a method that already exists and is already correct.

7. **Zero LINQ, zero `GetComponent` in the tick path.** Repo-wide grep: no `using System.Linq`
   anywhere; `GetComponent` appears 8 times, of which 6 are in `Awake`/`OnNetworkSpawn` and one is at
   instantiation. The two exceptions (`HeadBounce.cs:95`, `Fruit.cs:77`) are both behind early-outs.
   For a 15-day-old Unity project this is unusual.

---

## Open questions for the team

1. **Do dynamically-spawned fruit `NetworkObject`s survive the additive arena unload?** This decides
   whether F-A5-2 is a real cross-match leak (wire the call) or dead code (delete the method). One
   two-match Play Mode session with the hierarchy open answers it. I could not answer it by reading.

2. **Was the snapshot broadcast left ungated by phase deliberately** — e.g. to keep late joiners'
   interpolators warm, or to hold remote characters visible in the lobby — **or is it an oversight?**
   The fix for F-A5-1 differs depending on the answer, and if it was deliberate the reasoning belongs
   in `docs/02-netcode.md` next to the other tick-flow decisions.

3. **Is `NetDebugOverlay` intended to ship in a player build?** It is the project's whole thesis on
   screen at once (`NetDebugOverlay.cs:15-19`), which is a real argument for keeping it in a
   *portfolio* build. If so, F-A5-3(a) — throttling the rebuild — matters and F-A5-3(b) — build
   gating — does not. If not, both do.

4. **Is `PlayerMotor.cs:184-193` (the `dy == 0` grounded probe) reachable in practice?** With
   `Gravity: 55` at 30 Hz, `Velocity.y` is set to `-1.83` every tick before the move, so `dy` is never
   `Mathf.Approximately(0f)`. Either there is a path I did not trace, or the branch — and its
   `StandingOnPeer` helper (`PlayerMotor.cs:254-271`) — is dead. Worth confirming before someone
   relies on it.

5. **Should the 4-player physics-query rate ever be measured?** `C_cast = 5 µs` is the assumption
   every CPU number here rests on and it is unverified. A single Unity Profiler capture of one
   `OnNetworkTick` on the host would convert this entire report's core figures from `ESTIMATED` to
   `MEASURED`, and `docs/05-validation.md` already has the procedure and the discipline for recording
   conditions alongside results. That is a ~30-minute task with an outsized payoff for an interview.
