# Netcode Correctness & Authority Audit

*Agent A3 · branch `dev` · commit `10a2a13` · audit date 2026-08-08 · read-only*

## Verdict

The prediction/reconciliation/interpolation core is, on the whole, **correctly built and correctly
reasoned** — the state struct is complete, input is quantized at the source, the ack tick is the
right hinge, teleports are disambiguated from mispredictions, and the tick phases are ordered in one
place instead of by spawn order. That part is defensible in an interview and I say so with citations
below. But the **authority boundary has two holes that falsify the project's own stated contract**:
neither of the two hot RPCs validates its sender, and under NGO 2.11 the default
`RpcInvokePermission.Everyone` means a modified client can (a) inject input into *another* player's
server-side queue and (b) push a forged world snapshot to every other client through the server's own
proxy path. `docs/01-architecture.md` states as authority rule 5 that "a `ServerRpc` assumes the
caller is hostile until checked", and `SessionRoster` implements exactly that check — so this is a
gap in application, not in understanding. Third, predicted peer collision does **not** work the way
three documents say it does: on a client the world buffer is filled from remote players' *interpolated
render* positions and labelled with the current tick, so the client resolves contact against peers
roughly half a second stale. Finally, the one property the whole layer rests on — that a rewind and
replay converges on the server's answer — has no test that can fail; the two tests named after it
assert `f(x) == f(x)` on a pure static function in a single process.

## Scorecard

| Dimension | Score /5 | Note |
|---|---|---|
| Authority model (position, life, match outcome) | 4 | Clean and consistent: clients send intent only, every mutator is `if (!IsServer) return`. Loses a point only for what the RPC layer lets through. |
| RPC hygiene / wire safety | 1 | Neither hot RPC checks its sender; `SendTo.NotServer` is client-invokable and proxied; `SnapshotFrame` array length is unbounded on read. |
| Prediction & reconciliation correctness | 4 | Algorithm matches `docs/02` step for step, including the buffer rewrite on replay. Bounded input queue, deduped, teleport-aware. |
| Peer-collision prediction | 2 | Self-consistent between predict and replay, but systematically offset from the server and misdescribed in three docs. |
| Snapshot interpolation | 5 | Holds rather than extrapolates, drops out-of-order and duplicate pushes, 11 focused tests. |
| Spawn / despawn / ownership lifecycle | 3 | Registries maintained on both ends; late joiners mid-match are not placed and get a fresh life bar; one never-called despawn path. |
| Late-join & networked scene sync | 3 | Load gating is genuinely correct (waits for every peer, survives a disconnect mid-load). Mid-match join is not handled. |
| Disconnect / teardown | 2 | Server side recovers cleanly. Client side has no reaction at all to losing the host. |
| Async / concurrency in the connection layer | 3 | No off-main-thread Unity API, no unawaited fire-and-forget in the tick path. Relay ignores its own cancellation token; three `async void` UI handlers. |
| Docs ↔ code fidelity (netcode claims) | 2 | Most claims check out precisely. Two do not, and one of those is an explicitly stated invariant. |
| Test coverage of the core claim | 1 | `Reconcile` has zero coverage; the "replayable" tests are tautologies. |

## Findings

### F-A3-1 — `SubmitInputRpc` accepts input for a character from any client, not just its owner

- **Severity**: Critical
- **Type**: Security
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Gameplay/Player/PredictedPlayer.cs:380-389` and `:400-416`;
  NGO 2.11 `Library/PackageCache/com.unity.netcode.gameobjects@60c1d83693e8/Runtime/Messaging/RpcAttributes.cs:24-38, 88-93`;
  enforcement in `.../Runtime/Messaging/Messages/RpcMessages.cs:66-77`;
  contrast with `Assets/_Project/Scripts/Connection/SessionRoster.cs:128-146`;
  invariant stated in `docs/01-architecture.md` §"Authority rules (the contract)" rule 5.
- **What it is**: The input channel is declared

  ```csharp
  [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
  void SubmitInputRpc(InputPacket packet)
  ```

  with no `InvokePermission` and no `RpcParams`. In NGO 2.11 `RpcInvokePermission` defaults to
  `Everyone` (`RpcAttributes.cs:24-28`, and the migration note at `:144-146` says so explicitly), and
  the server-side handler only rejects senders for the `Server` and `Owner` permissions
  (`RpcMessages.cs:69-71`). The handler body never reads `rpcParams.Receive.SenderClientId`. Any
  connected client can therefore address this RPC at any spawned `PredictedPlayer` NetworkObject and
  have the server enqueue those commands into that player's `_incomingInputs`.
- **Why it matters**: The server will then simulate the victim's character from the attacker's
  buttons and broadcast the result as authoritative; the victim's own client reconciles *toward* it.
  There is a second, cheaper exploit on the same call: `EnqueueIfNew` advances
  `_highestReceivedInputTick` before any other check (`:404-409`), and accepts ticks up to
  `serverTick + MaxInputTickLead` (64 ≈ 2.1 s at 30 Hz). One forged packet stamped `serverTick + 64`
  makes every honest input from the real owner fail the `input.Tick <= _highestReceivedInputTick`
  test for the next ~2 seconds; repeated once per second it freezes the target permanently while the
  server logs nothing. This directly falsifies `docs/01-architecture.md` rule 5 and materially
  falsifies `README.md:35` ("No client is trusted with its own position" is literally true, but a
  client is trusted with *another client's input*, which is strictly worse).
- **Recommendation**: `[Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]`.
  That is a one-line change and NGO then rejects the message before it reaches the handler. Belt and
  braces: add `RpcParams rpcParams = default` and `if (rpcParams.Receive.SenderClientId != OwnerClientId) return;`,
  matching the pattern already written in `SessionRoster.cs:133-138`.
- **Effort**: S

### F-A3-2 — `SnapshotRpc` can be invoked by a client and is proxied by the server to every other client

- **Severity**: Critical
- **Type**: Security
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Netcode/NetworkSimulationLoop.cs:119-136`;
  NGO `Runtime/Messaging/RpcTargets/NotServerRpcTarget.cs:15-27, 41-65` (a non-server caller is routed
  through `ProxyRpcTargetGroup`); `Runtime/Messaging/Messages/ProxyMessage.cs:47-70`
  (`RpcInvokePermission.Everyone => true`, i.e. the server relays it);
  consequences at `Assets/_Project/Scripts/Gameplay/Player/PredictedPlayer.cs:499`, `:507-517` and
  `Assets/_Project/Scripts/Netcode/SnapshotInterpolator.cs:43`; unbounded read at
  `Assets/_Project/Scripts/Netcode/SnapshotFrame.cs:67-72`.
- **What it is**: `[Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]` with the default
  `Everyone` permission. `SendTo.NotServer` invoked from a client is not an error in NGO 2.11 — the
  target implementation switches to a proxy group and asks the server to forward it, and the server's
  proxy validator waves `Everyone` through. The receiving handler takes the frame at face value: it
  never checks `rpcParams.Receive.SenderClientId`, never checks `frame.Tick` against the local server
  clock, and never bounds `frame.Players.Length`.
- **Why it matters**: Three distinct effects from a single forged datagram.
  1. `Reconcile` gates staleness on `snapshot.LastProcessedInputTick` (`PredictedPlayer.cs:499`). A
     forged frame with `LastProcessedInputTick = uint.MaxValue` sets `_lastAckedTick` to the ceiling,
     and every subsequent legitimate snapshot then fails `ackTick < _lastAckedTick` and returns
     immediately. **The victim's client never reconciles again for the rest of the session** and
     free-runs its own simulation.
  2. Set `IsTeleport` and the victim is hard-snapped to any position the attacker chooses
     (`:507-517`) — client-side only, so it does not move them on the server, but it does
     desynchronise their view and clear their prediction buffer.
  3. `SnapshotInterpolator.Push` rejects any sample not newer than the newest held
     (`SnapshotInterpolator.cs:43`), and `snapshotTime` is derived straight from `frame.Tick`
     (`NetworkSimulationLoop.cs:122`). A frame with a huge tick **permanently freezes every remote
     character on every client that receives it.**

  Separately, `SnapshotFrame.NetworkSerialize` reads `count` off the wire and allocates
  `new PlayerSnapshot[count]` (`SnapshotFrame.cs:67-70`) before validating it — a memory-amplification
  primitive reachable over the same path.
- **Recommendation**: `InvokePermission = RpcInvokePermission.Server` on `SnapshotRpc` (NGO then
  refuses it at the client's `__beginSendRpc` *and* at the server's proxy validator). Independently,
  clamp `count` in `SnapshotFrame.NetworkSerialize` to `WorldSnapshotBuffer.MaxBodies` and drop frames
  whose `Tick` is more than a second away from `NetworkManager.ServerTime.Tick` — defence in depth is
  cheap here and is exactly the sort of thing the reviewer this project is aimed at will look for.
- **Effort**: S

### F-A3-3 — Predicted peer collision runs against interpolated peer positions, not tick-aligned ones

- **Severity**: Major
- **Type**: Correctness
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Gameplay/Player/PredictedPlayer.cs:650-672` (`CaptureWorld`
  reads `other.State.Position`), `:591-606` (`Update` overwrites `_state` with the interpolator's
  output for every non-owned, non-server character), `:47` (`_interpolationDelay = 0.1f`);
  `Assets/_Project/Scripts/Netcode/WorldSnapshotBuffer.cs:9-15`; claims in
  `docs/02-netcode.md` §"Characters collide with each other, and it is predicted" (lines 88-94),
  `docs/01-architecture.md` §"Settled parameters" row *Player-vs-player contact*,
  `docs/03-roadmap.md:68`.
- **What it is**: On a client, a remote `PredictedPlayer`'s `_state` is whatever
  `SnapshotInterpolator.TryEvaluate(ServerTime.Time - 0.1)` last produced — a render-time position
  deliberately held ~100 ms in the past. `CaptureWorld(tick)` reads exactly that field and stores it
  in the ring under the *owner's current local tick*, which NGO deliberately runs ahead of server
  time by roughly the round trip. The server, meanwhile, captures peers at their true position at
  `serverTick`. So the label on the client's world frame and the positions inside it describe two
  different moments, and the server's frame for the same tick number contains different numbers.
- **Why it matters**: Replay is still self-consistent (prediction and replay read the same buffer, so
  the replay reproduces the prediction), which is why nothing looks obviously broken. But the
  prediction itself is offset from the authority whenever a peer is moving, so every close-quarters
  interaction — the head-stomp mechanic that is pillar 2 of the game — mispredicts and is corrected.
  Using the project's own measured numbers (`docs/05-validation.md`: median measured RTT 367 ms,
  median 11 replayed ticks), the peer position fed into the client's `SweepPeers` is roughly
  `0.1 s + 0.367 s ≈ 0.47 s` stale, i.e. up to **3.3 units** off at `MoveSpeed` 7 against a
  0.7-unit-wide character. More importantly for a portfolio project, three documents assert the
  opposite: `WorldSnapshotBuffer`'s own `<remarks>` says reading positions live "would produce a
  state the server never computed" — which is precisely what this does, one level up.
- **Recommendation**: Two honest options, and the choice is a design call, not a bug fix.
  (a) Key the world buffer off the *authoritative* peer state rather than the rendered one: remote
  peers already receive `PlayerSnapshot` with the frame's tick, so store `snapshot.State.Position`
  under `frame.Tick` in a per-peer buffer and have `CaptureWorld` read that, accepting that the
  newest tick a client can build a world frame for is behind its own prediction tick (and extrapolate
  or hold for the gap). (b) Keep the behaviour and correct the three documents to say that peer
  contact is predicted against *render-delayed* peer positions and is therefore expected to generate
  corrections during contact. Option (b) is a legitimate answer and costs an afternoon; option (a) is
  the one that makes the claim true.
- **Effort**: M (option a) / S (option b)

### F-A3-4 — A client joining mid-match is never placed and receives a full life bar

- **Severity**: Major
- **Type**: Correctness
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Gameplay/Player/PlayerSpawnPoints.cs:31-52` (placement fires
  once, only on the `Lobby → Countdown` edge, latched by `_placedForThisMatch`);
  `Assets/_Project/Scripts/Gameplay/Player/PlayerLife.cs:98-103` (`_exactLife = _config.StartingLife`
  and `_alive = true` on every spawn, unconditionally);
  `Assets/_Project/Scripts/Connection/ConnectionApproval.cs:145-184` (admits on version + player cap
  only; no phase gate); `Assets/_Project/Scripts/Gameplay/Match/RoundReferee.cs:122-130`
  (`_startingPlayers` is sampled once at `BeginRound`).
- **What it is**: Nothing refuses or specially handles a connection during `Countdown`, `Playing` or
  `Ended`. NGO spawns the player prefab at its prefab transform (origin), `ServerTeleport` is never
  called for it, and `PlayerLife` hands it 60 s of life into a round where everyone else has been
  draining for two minutes.
- **Why it matters**: The late joiner falls out of the arena from the origin — the exact failure the
  `ShouldSimulate` remarks at `PredictedPlayer.cs:119-128` were written to prevent for the lobby case
  — and, because `BestSurvivor()` picks the most life left at `TimeUp`, someone who joined ten
  seconds before the clock expired wins the round. This is the "late-joiner state sync" question in
  its most concrete form, and it is currently unanswered rather than answered wrongly.
- **Recommendation**: Cheapest correct answer for a 4-player portfolio game: refuse the connection in
  `ConnectionApproval.Approve` when `MatchDirector.Current.Phase` is not `Lobby` or `Loading`, with
  the reason text "A match is already in progress." (the refusal path already exists and already
  reaches the player, `ConnectionApproval.cs:195-203`). If joining mid-match is wanted later, the
  work is: place through `PredictedPlayer.ServerTeleport` on spawn when the phase is past
  `Countdown`, and seed life from the round's remaining time rather than from `StartingLife`.
- **Effort**: S

### F-A3-5 — Clients have no reaction to the host disappearing mid-match

- **Severity**: Major
- **Type**: Correctness
- **Confidence**: Medium
- **Evidence**: Repo-wide grep over `Assets/_Project` finds `OnClientDisconnectCallback` subscribed in
  exactly four places — `ConnectionApproval.cs:120` (server), `SessionRoster.cs:58` (server),
  `MatchDirector.cs:106` (server) and `DirectConnectionProvider.cs:138`, which unsubscribes in its
  own `finally` at `:167` — and **no** `OnClientStopped`, `OnServerStopped` or `OnTransportFailure`
  subscription anywhere. Teardown paths: `MatchDirector.cs:178-187` (`UnloadCurrentScene`, server-only,
  and the director is despawned by then), `LoadingScreenController.cs:56-61` (on
  `MatchDirector.Current == null` it loads the Lobby scene back, additively).
- **What it is**: There is no host migration and none is required — that is settled. What is missing
  is the *client-side consequence*. When the host drops, NGO shuts the client's `NetworkManager` down
  and despawns the networked scene objects, so `MatchDirector.Current` becomes null. The only code
  that observes that is `LoadingScreenController`, which additively loads the Lobby back — **on top of
  the still-loaded arena scene**, which nothing unloads because the only unload path is server-side.
  The player gets a menu drawn over a dead arena, with no message explaining what happened.
- **Why it matters**: This is the failure mode a reviewer will produce in thirty seconds by
  alt-F4'ing the host, and it is one of the two states this project can end up in that it cannot
  explain to the player. The server side, by contrast, is genuinely well handled: a client timing out
  mid-match is removed from the roster (`SessionRoster.cs:99-105`), removed from the load gate
  (`MatchDirector.cs:207-218`), removed from `PlayerLife.All` (`PlayerLife.cs:112-118`) and
  unregistered from the tick loop (`PredictedPlayer.cs:273-278`), so `RoundReferee` recovers and can
  still declare `LastStanding` or `NoWinner`. The asymmetry is what makes this worth fixing.
- **Confidence note**: Medium rather than High because I have not run it — the scene-stacking outcome
  is inferred from the ownership of the unload path plus `LoadingScreenController`'s reconciler, not
  observed. The *absence* of any disconnect handling is High confidence; the exact visual result is
  not.
- **Recommendation**: Subscribe `NetworkManager.OnClientStopped` (and `OnTransportFailure`) in one
  place that lives in Bootstrap — the same component that owns `LoadingScreenController` is the
  natural home — and on fire: unload any loaded arena scene locally, return to the menu, and surface
  `NetworkManager.DisconnectReason` (or "The host left the game" when it is empty) through the
  existing `SetStatus` path in `MainMenuController`.
- **Effort**: S

### F-A3-6 — The rewind-and-replay claim has no test that can fail

- **Severity**: Major
- **Type**: Process
- **Confidence**: High
- **Evidence**: `Assets/Tests/EditMode/PeerCollisionTests.cs:140-156` —
  `Assert.AreEqual(Replay(start, world).Position, Replay(start, world).Position)`;
  `Assets/Tests/EditMode/StunTests.cs:118-135` — the same shape. `Replay` is a loop over the pure
  static `PlayerMotor.Simulate`, called twice with identical arguments in one process. The method
  under test at `Assets/_Project/Scripts/Gameplay/Player/PredictedPlayer.cs:492-582` (`Reconcile`) is
  referenced by no test. There are 8 EditMode files and no PlayMode / `NetcodeIntegrationTest`
  assembly in the tree. `docs/03-roadmap.md:37` calls this "the property everything else depends on".
- **What it is**: Both "replayable" tests assert that a deterministic function of its arguments
  returns the same value when called twice with the same arguments. That can only fail if
  `PlayerMotor` reads mutable global state, which it very nearly doesn't (see F-A3-9 for the one
  exception). It does not test the property that matters: that starting from an authoritative state at
  tick N and replaying the buffered inputs N+1..M lands on the state the server would compute.
- **Why it matters**: `Reconcile` is where every subtle bug in this layer lives — the ordering guards
  at `:499` and `:537`, the capacity guard at `:542`, the buffer-hole `continue` at `:569`, the
  buffer rewrite at `:572`. None of it is exercised. For a project whose stated deliverable is
  demonstrable netcode, "the core algorithm has 0 % coverage while the ring buffer around it has 10
  tests" is the single most awkward line in the test report.
- **Recommendation**: Extract the rewind-and-replay body of `Reconcile` into a static, engine-free
  function in `Snackdown.Netcode` — `(PredictionBuffer, WorldSnapshotBuffer, PlayerState authoritative,
  uint ackTick, uint latestTick, MovementConfig, float dt) -> PlayerState` — and test it in EditMode:
  seed a buffer by simulating forward, corrupt the state at tick N, replay, and assert the result
  equals a straight simulation from the authoritative state over the same input sequence. That is one
  new test file and it makes the headline claim falsifiable. The extraction also relieves F-A3-10.
- **Effort**: M

### F-A3-7 — `PredictedPlayer` is a 691-line god object, and that is why `Reconcile` is untestable

- **Severity**: Major
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Gameplay/Player/PredictedPlayer.cs` — 691 LOC, the largest
  first-party file and the most-churned source file on `main..dev` (11 touches, per `audit/00-recon.md`).
  It holds: owner prediction (`:305-335`), the prediction toggle (`:348-358`), input sampling
  (`:360-370`), the server input queue and its hostile-caller bounds (`:380-416`), server simulation
  (`:422-455`), snapshot building (`:457-469`), reconciliation (`:492-582`), remote interpolation
  (`:591-606`), transform/smoother application (`:608-615`), stun and bounce mutators (`:626-639`),
  world capture (`:650-674`), teleport (`:682-689`), plus RTT derivation and eight telemetry
  properties (`:160-234`).
- **What it is**: This is the under-engineering counterpart to the over-engineering rubric —
  authoritative simulation logic living in one `MonoBehaviour` alongside presentation, telemetry and
  file export. The `Snackdown.Simulation` split (ADR 0002) was made precisely so the pure step could
  be tested without a scene; the *netcode* step was not given the same treatment and consequently
  cannot be reached from an EditMode test at all.
- **Why it matters**: It is the direct cause of F-A3-6, and the churn number says it is still moving.
  Three roles behind one `IsOwner`/`IsServer` branch is also the file where the F-A3-1 omission was
  easy to make: the sender check belongs next to `EnqueueIfNew`, which is buried at line 400 of a file
  about six other things.
- **Recommendation**: Do not restructure the component. Lift two engine-free pieces out into
  `Snackdown.Netcode`: the reconciliation algorithm (see F-A3-6) and the server-side input queue
  (`_incomingInputs`, `EnqueueIfNew`, the drain rule and both caps) as a small `ServerInputQueue`
  class. Both are pure, both are the parts a reviewer will ask about, and both become testable
  without touching how the component is wired.
- **Effort**: M

### F-A3-8 — The replay guard checks the wrong buffer's capacity

- **Severity**: Minor
- **Type**: Correctness
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Gameplay/Player/PredictedPlayer.cs:542-551` guards on
  `pendingTicks > PredictionBuffer.Capacity` (1024, `PredictionBuffer.cs:21`), and the comment at
  `:566-568` states "Overflow can no longer reach here; the capacity check above caught it". But the
  replay loop also reads `WorldAt(t)`, backed by `WorldSnapshotBuffer.Capacity` = **128**
  (`WorldSnapshotBuffer.cs:26`).
- **What it is**: For any replay longer than 128 ticks (4.3 s) but shorter than 1024 (34 s), the guard
  passes, the input buffer answers correctly, and the world buffer silently returns
  `SimulationContext.Empty` for the older ticks (`WorldSnapshotBuffer.cs:60-63`) — replaying peer
  contact against an empty world.
- **Why it matters**: Low, because a 4-second replay means the connection has already collapsed and
  the next snapshot will correct it. Reported because the comment asserts a safety property the code
  does not have, and in this repo the comments are part of the deliverable.
- **Recommendation**: Guard on `Mathf.Min(PredictionBuffer.Capacity, WorldSnapshotBuffer.Capacity)`,
  or state in the comment that peer context degrades to empty past `WorldSnapshotBuffer.Capacity`.
  While there: `PredictionBuffer.Capacity = 1024` (34 s of history for a replay that empirically peaks
  at 27 ticks, `docs/05-validation.md`) is ~8× more than any recoverable case, which is what makes the
  guard unreachable in practice.
- **Effort**: S

### F-A3-9 — `PlayerMotor` is not a pure function of its arguments; it queries the live physics scene

- **Severity**: Minor
- **Type**: Correctness
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Simulation/PlayerMotor.cs:137-139`, `:162-164`, `:188-189`
  (`Physics2D.BoxCast` against `cfg.GroundMask`), versus the contract in its own `<remarks>` at
  `:12-14`: "It never reads `Time`, a `Transform`, or a `Rigidbody2D`. Everything it needs arrives as
  an argument."
- **What it is**: Terrain is read implicitly from the scene, not passed in. Today this is *safe*: I
  verified `MovementConfig.asset` has `GroundMask.m_Bits: 256` (layer 8) and `Player.prefab:21` has
  `m_Layer: 0`, so players are never hit by these casts, and the arena geometry in `Arena01.unity` is
  static — a replay of tick 40 therefore sees the same terrain it saw live.
- **Why it matters**: The determinism argument in `docs/02-netcode.md:48-57` is the load-bearing
  justification for the whole hand-written kinematic controller, and it currently holds *by
  circumstance* rather than by construction. The first moving platform, destructible tile or
  toggling hazard breaks replay silently and in a way that will read as random desync.
- **Recommendation**: No code change now. Amend the `PlayerMotor` remarks and
  `docs/02-netcode.md` to say the pure-function property holds *for static geometry*, and that any
  moving collider must enter through `SimulationContext` (which `SimulationContext.cs:21-25` already
  anticipates as the extension point). One paragraph, and it converts a latent trap into a stated
  design boundary — which is more valuable in an interview than the omission.
- **Effort**: S

### F-A3-10 — The owner stops reconciling entirely while the server's ack tick stalls

- **Severity**: Minor
- **Type**: Correctness
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Gameplay/Player/PredictedPlayer.cs:537`
  (`if (ackTick == _lastAckedTick) return;`) against `:443-449` (on starvation the server re-simulates
  with `_lastConsumedInput` and does **not** advance `_lastProcessedInputTick`, only `StarvedTicks`).
- **What it is**: Freshness is keyed solely on `LastProcessedInputTick`. During an input-starvation
  window the server keeps advancing the authoritative state — with a repeated input — while the owner
  predicted its *new* inputs, and the owner discards every snapshot describing that divergence.
- **Why it matters**: Bounded and self-healing in normal operation (the 3-command redundancy window
  means starvation needs three consecutive losses), so on its own this is Minor. It becomes unbounded
  when combined with F-A3-1's tick-lead poisoning, which is precisely a way to make the ack tick stall
  forever. `SnapshotFrame.Tick` is already on the wire and already carried into the handler
  (`NetworkSimulationLoop.cs:122`) but is used only as an interpolation timestamp.
- **Recommendation**: Order snapshots by `frame.Tick` and reconcile against the newest buffered tick
  `<= ackTick` even when `ackTick` repeats, rather than returning early.
- **Effort**: S

### F-A3-11 — Spawned fruit outlives the arena: `ServerDespawnAll` is never called

- **Severity**: Minor
- **Type**: Correctness
- **Confidence**: Medium
- **Evidence**: `Assets/_Project/Scripts/Gameplay/Fruits/FruitSpawner.cs:136-145` — repo-wide grep for
  `ServerDespawnAll` across `Assets/_Project` and `Assets/Tests` returns the declaration and nothing
  else. `FruitSpawner.cs:91` `Instantiate(...)` with no target scene, so instances land in the active
  scene (Bootstrap — `Arena01` is loaded additively at `MatchDirector.cs:175` and nothing calls
  `SetActiveScene`). `MatchDirector.ServerReturnToLobby` (`:254-268`) unloads the arena and resets
  life but never touches fruit. `Fruit.Update` (`Fruit.cs:69-88`) has no match-phase gate.
- **What it is**: Fruit spawned during a round is not parented to the arena and is not despawned when
  the round ends, so it survives the arena unload as an orphaned `NetworkObject` in Bootstrap, keeps
  running its server-side overlap query, and can be collected in the lobby — `PlayerLife.ServerAdd`
  (`PlayerLife.cs:179-189`) checks `IsServer` and `IsAlive` but not the phase.
- **Why it matters**: A slow leak across rounds (up to `_maxActive` = 6 per round) plus free life
  banked between matches. Confidence is Medium only on the active-scene inference; the dead
  `ServerDespawnAll` and the missing phase gate are High.
- **Recommendation**: Call `ServerDespawnAll` from `MatchDirector.ServerReturnToLobby` alongside
  `life.ServerReset()`. The method already exists and already does the right thing.
- **Effort**: S

### F-A3-12 — Relay join/host ignore their own cancellation token

- **Severity**: Minor
- **Type**: Correctness
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Connection/RelayConnectionProvider.cs:79`
  (`CreateSessionAsync(options)`) and `:124` (`JoinSessionByCodeAsync(code.ToUpperInvariant())`) — the
  `cancellationToken` parameter is checked only inside `PrepareAsync` (`:179`, `:187`) and is never
  passed to the Sessions calls. Cancel path: `MainMenuController.cs:202` (`OnCancelClicked` only
  cancels the CTS), `:121-122` (`OnDisable` cancels **and disposes** it while an attempt may be live).
- **What it is**: Once `PrepareAsync` returns, pressing Cancel has no effect on the operation that is
  actually slow. If the join then succeeds, `_session` is assigned and the peer is genuinely in a
  Relay session with a live `NetworkManager` while the UI has already returned to the menu — and
  nothing schedules a `LeaveAsync`. `DirectConnectionProvider` does *not* have this problem: it races
  the outcome against `Task.Delay(..., cancellationToken)` and calls `LeaveAsync()` on both the
  timeout and the cancel path (`DirectConnectionProvider.cs:145-163`), which is the right shape.
- **Why it matters**: A cancelled Relay join leaves a half-live session; the next Host or Join attempt
  then meets a `NetworkManager` that is already listening. `Contributing`: the three UI handlers are
  `async void` (`MainMenuController.cs:180`, `:191`, `:388`) and touch UI state after the await, so a
  throw from any of them surfaces as an unhandled exception rather than a status line.
- **Recommendation**: Pass the token to both Sessions calls if the SDK overload accepts one;
  otherwise, after the await, check `cancellationToken.IsCancellationRequested` and call
  `await LeaveAsync()` before returning `Cancelled` — mirroring what `DirectConnectionProvider`
  already does. Do not dispose the CTS in `OnDisable` while an attempt is in flight; cancel only.
- **Effort**: S

### F-A3-13 — Snapshot receive allocates on every tick

- **Severity**: Minor
- **Type**: Performance
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Netcode/SnapshotFrame.cs:70` — `if (serializer.IsReader)
  Players = new PlayerSnapshot[count];`, executed once per received snapshot, i.e. 30×/s on every
  client. The send path is allocation-free by contrast (`NetworkSimulationLoop.cs:104-108` reuses
  `_snapshotScratch`), and `SimulationContext` is explicitly designed to avoid allocation
  (`SimulationContext.cs:25-27`) — so this is the one place the discipline slips.
- **Why it matters**: ~5.8 KB/s of garbage per client (see Quantified Estimates). Negligible on PC;
  it is worth a line because `target_platforms` includes Mobile and WebGL, where a steady 30 Hz
  allocation on the receive path is the kind of thing that shows up as periodic GC hitches.
- **Recommendation**: Keep a per-`NetworkSimulationLoop` receive scratch array sized to
  `WorldSnapshotBuffer.MaxBodies` and reuse it when `count` fits — which also gives you the natural
  place to enforce the bound demanded by F-A3-2.
- **Effort**: S

### F-A3-14 — `docs/02` lists an RPC for fruit that does not exist

- **Severity**: Nit
- **Type**: Correctness
- **Confidence**: High
- **Evidence**: `docs/02-netcode.md:193` — "Fruit spawn / pickup | server spawn/despawn + **RPC** | on
  event". The implementation uses `NetworkVariable<int> _kind` set before `Spawn()`
  (`FruitSpawner.cs:96-99`, `Fruit.cs:35, 47-51`) and a plain `NetworkObject.Despawn()`
  (`Fruit.cs:85`). There are exactly three RPCs in the project and none is fruit-related.
- **Recommendation**: Change the cell to "server spawn/despawn + `NetworkVariable`". The chosen design
  is better than the documented one — spawn-time `NetworkVariable` avoids the extra message the
  `Fruit` remarks at `:14-17` correctly argue against — so the doc is undercutting the code.
- **Effort**: S

## Quantified Estimates

All rows **ESTIMATED** — derived by reading serialization code and config assets. No profiler capture,
bandwidth capture or network-metrics export exists anywhere in the repository (searched
`Assets/`, repo root and `docs/`); `docs/05-validation.md` records reconciliation error and RTT, not
bandwidth or allocation.

### Wire size per tick

Inputs: `BufferSerializer.SerializeValue` writes fixed-width — `ulong` 8 B, `Vector2` 8 B, `bool` 1 B,
`float` 4 B, `uint` 4 B, `sbyte`/`byte` 1 B (`PlayerState.cs:40-48`, `PlayerSnapshot.NetworkSerialize`
`SnapshotFrame.cs:41-47`, `InputCommand.cs:41-46`). NGO RPC framing ≈ 22 B (message header +
`NetworkObjectId` 8 + `NetworkBehaviourId` 2 + `NetworkRpcMethodId` 4).

| Item | Formula | Bytes |
|---|---|---:|
| `PlayerState` | 8 + 8 + 1 + 4 + 4 + 4 | 29 |
| `PlayerSnapshot` | 8 + 29 + 4 + 1 | 42 |
| `SnapshotFrame` (4 players) | 4 + 4 + 4×42 | 176 |
| Snapshot datagram | 176 + ~22 framing | ~198 |
| `InputCommand` | 4 + 1 + 1 | 6 |
| `InputPacket` datagram | 3×6 + ~22 framing | ~40 |

| Flow | Formula | Rate |
|---|---|---:|
| Host → each client (snapshots) | 198 B × 30 Hz | **5.9 KB/s** |
| Host uplink total, 4-player match | 5.9 KB/s × 3 clients | **17.8 KB/s** |
| Client → host (input) | 40 B × 30 Hz | **1.2 KB/s** |
| Host downlink total | 1.2 KB/s × 3 | **3.6 KB/s** |

Well inside a single MTU per tick, which is what the one-frame-for-everyone design at
`SnapshotFrame.cs:50-57` was aiming for. It stays under the non-fragmented unreliable ceiling
(`NetworkBehaviour.__endSendRpc` throws above it) until roughly 30 players.

### Peer-position staleness in client-side collision prediction (F-A3-3)

| Input | Value | Source |
|---|---|---|
| Interpolation delay | 0.100 s | `PredictedPlayer.cs:47` |
| Client lead over server (median measured) | 0.367 s | `docs/05-validation.md` "Median measured RTT", scenario A/B |
| `MoveSpeed` | 7 u/s | `Settings/MovementConfig.asset` |
| Character width | 0.7 u | `Settings/MovementConfig.asset` `ColliderSize` |

`staleness = 0.100 + 0.367 = 0.467 s` → worst-case peer displacement `0.467 × 7 = 3.27 u`
≈ **4.7 character widths**. At the 32 ms "Home Broadband" profile the same formula gives
`0.132 × 7 = 0.92 u` ≈ 1.3 widths — still larger than the 0.03 u reconciliation tolerance
(`PredictedPlayer.cs:37`), so contact against a moving peer mispredicts at every latency this project
targets.

### Receive-path allocation (F-A3-13)

`(24 B array header + 4 × 42 B) × 30 Hz ≈ 5.8 KB/s` of Gen-0 garbage per client, per session, from
`SnapshotFrame.cs:70` alone.

## What is genuinely good here

This section is not padding. Several of these are things I regularly do not find in hand-rolled
prediction layers, including commercial ones.

1. **`PlayerState` is genuinely complete, and the completeness is on the wire.** `CoyoteTimer`,
   `JumpBufferTimer` and `StunTimer` are serialized alongside position and velocity
   (`Simulation/PlayerState.cs:40-48`). This is the single most common source of "random" desync in
   hand-written reconciliation — replicating pos/vel and leaving the feel timers local — and it is
   correctly avoided, with the reasoning written down at `PlayerState.cs:9-15`. The stun genuinely is
   replayed: `StunStep` runs first and clears the input rather than skipping the remaining steps
   (`PlayerMotor.cs:58-63`), so a stunned character keeps falling and colliding, which is both the
   right game feel and the right thing for replay convergence.

2. **`LastProcessedInputTick` is the right hinge and is used correctly.** `SnapshotFrame.cs:14-23`
   states why, and `Reconcile` restarts the replay at exactly `ackTick + 1` and rewrites the buffer's
   predicted state as it goes (`PredictedPlayer.cs:564-573`) — the step at `docs/02-netcode.md:148`
   that implementations most often skip, leaving stale predictions for the *next* correction to
   compare against.

3. **Teleport is distinguished from misprediction, redundantly.** `PlayerSnapshot.IsTeleport`
   (`SnapshotFrame.cs:25-39`) with a 3-snapshot announce window (`PredictedPlayer.cs:101-111`,
   `:464-467`) because the channel is unreliable. Without it, a spawn placement is indistinguishable
   on the wire from a catastrophic prediction failure — and `docs/05-validation.md:42` shows this was
   found the honest way, by a first correction of 3.8 units against a real one of 0.29.

4. **Input is quantized at the source, not at the boundary.** `InputReader.MoveX` returns
   `sbyte` −1/0/1 with a deadzone (`Input/InputReader.cs:78-87`) and `InputCommand.MoveX` is `sbyte`
   (`InputCommand.cs:22-23`). A float axis rounding differently on two machines is a desync that is
   miserable to find; this closes it structurally rather than by convention.

5. **Redundant input over unreliable delivery, with a correct server-side consumer.** Three commands
   per packet (`InputPacket.cs`, `PredictedPlayer.cs:324-329`), deduped by a monotonic
   `_highestReceivedInputTick`, a hard queue ceiling separate from the latency-bounding depth, an
   extra drain when the queue runs long so a burst does not become permanent lag, and repetition of
   the last command on starvation (`PredictedPlayer.cs:400-416`, `:434-449`). That is the textbook
   design and it is implemented correctly, bound checks and all — which is what makes the missing
   *sender* check in F-A3-1 stand out as an oversight rather than a misunderstanding.

6. **The tick has one owner and an explicit phase order.** `NetworkSimulationLoop.OnNetworkTick`
   (`:72-98`): everyone predicts → the server simulates everyone → interactions resolve → one
   snapshot goes out. The `<remarks>` at `:11-19` names the bug class this removes (ordering by spawn
   order) and correctly distinguishes `LocalTime` from `ServerTime` at `:76` and `:87`. Running the
   head bounce between simulation and publish (`:91-94`) so its result ships in the same snapshot as
   the movement that caused it is a detail worth pointing at in an interview.

7. **The host is handled honestly, and the code branches on roles rather than on "am I the host".**
   `PredictedPlayer.cs:26-28` and the `IsOwner && !IsServer` / `IsOwner` split at
   `NetworkSimulationLoop.cs:80` and `PredictedPlayer.cs:428-432` — the host takes the server path
   exclusively and is never predicted or reconciled. `docs/02-netcode.md:40-46` claims this and the
   code delivers it.

8. **Correction is logically instant and visually absorbed, in the right place.** The smoother lives
   on a child transform and carries a decaying offset (`Netcode/VisualSmoother.cs`), the logical
   transform always holds the corrected truth (`PredictedPlayer.cs:608-615`), and errors above
   `_maxSmoothedError` are snapped rather than slid. Exponential decay makes it frame-rate
   independent. `VisualError` (`:177-182`) exposes the one number that proves the correction was
   absorbed rather than hidden.

9. **The RTT derivation is a genuinely sharp piece of work.** `PredictedPlayer.cs:190-205` and
   `:504-505` — the observation that `UnityTransport.GetCurrentRtt` reports `RttInfo.LastRtt` off the
   *reliable sequenced* pipeline while every packet this layer sends is unreliable, and that
   `(latestPredictedTick − ackTick) × tickDelta` is the honest measurement, is correct and is backed
   by contrasting numbers in `docs/05-validation.md:127-131` (218 ms on idle localhost, 1219 ms
   against a real 510 ms). Recording both side by side in the CSV rather than discarding the bad one
   is the right call.

10. **`SessionRoster.SetReadyRpc` does exactly the right thing** — takes the client id from
    `rpcParams.Receive.SenderClientId` and never from the body, with the reasoning written out at
    `SessionRoster.cs:128-132`. And `NetworkList` rather than a broadcast RPC, precisely so a late
    joiner gets the current roster instead of the change it missed (`:14-18`).

11. **Deadline replication instead of counted-down timers**, in two places
    (`MatchDirector.cs:36-50, 220-236`; `RoundReferee.cs:30-38, 122-130`). Every peer derives the
    number from `NetworkManager.ServerTime`, so they agree because they read the same clock rather
    than because someone keeps telling them. The load gate that waits for *every* client's
    `OnLoadComplete` before starting the countdown, with a disconnect-during-load escape hatch
    (`MatchDirector.cs:189-218`), is also correct — I verified against NGO 2.11 that a client joining
    during `Loading` does emit `LoadComplete` back to the server through the synchronization path
    (`NetworkSceneManager.cs:2198-2214`, `:2447-2457`), so that gate does not deadlock.

12. **`_alive` is a replicated flag rather than derived from the interpolated life value**
    (`PlayerLife.cs:37-49`). Deriving it would let a client bury a player the server still considers
    alive, over a rounding difference, and take their collision and camera with it. That is a subtle
    call and the reasoning is recorded.

13. **The buffers carry their own tick in every slot**, so a wrapped entry cannot masquerade as a
    fresh one (`PredictionBuffer.cs:50, 57, 70`; `WorldSnapshotBuffer.cs:63`), and both are tested for
    exactly that (`PredictionBufferTests.cs:56, 96`). `SnapshotInterpolator` holds rather than
    extrapolates and drops out-of-order and duplicate pushes (`SnapshotInterpolator.cs:43, 73-77`) —
    correct for an unreliable channel, with 11 tests covering the boundaries.

### Over-engineering check (my domain)

I looked for the ten patterns and found **one soft hit**, plus one genuine under-engineering finding
(F-A3-7). Counter-check per the brief: where the complexity is earned, it is earned.

- **`IPredictedPeer` — one implementation, no test double.** Rubric #1 on its face. **Earned**: the
  asmdef graph makes the cycle it breaks a compile error, not a style preference
  (`IPredictedPeer.cs:9-19`, `docs/01-architecture.md` §Assemblies), and the interface is six members
  wide with the telemetry deliberately left out.
- **`IConnectionProvider` — two live implementations** (Direct + Relay), both reachable from one
  serialized toggle. Not speculative.
- **`ReconciliationStats` + `RunRecorder` — two measurement types.** Brushes rubric #9. **Earned**:
  one answers "how is it going right now" for the overlay, the other "how did that run go" for the
  CSV that `docs/05-validation.md` is built on, and a rolling window structurally cannot answer the
  second. Both have live consumers.
- **`NetworkSimulationLoop.AfterServerSimulation` — a static event with one publisher and one
  subscriber** (`HeadBounce`). Rubric #3, **soft hit**. The ordering guarantee it buys is real and
  stated (`NetworkSimulationLoop.cs:35-41`), and a direct call from the loop into `Gameplay` would
  invert the layer dependency the whole assembly split exists to enforce. I would leave it, and I
  would expect the author to be able to say exactly that.
- **`PredictionBuffer.Capacity = 1024`** — 34 s of history against a measured peak of 27 replayed
  ticks. Over-provisioned by ~8×, ~40 KB per player, and it is what makes the guard in F-A3-8
  unreachable. Nit-level, folded into F-A3-8.

## Open questions for the team

1. **`docs/05-validation.md` open item — "Ten corrections in B replayed zero ticks."** I can offer a
   mechanism but not a confirmation. `pendingTicks == 0` requires `ackTick >= _latestPredictedTick`
   *and* `_buffer.TryGetState(ackTick)` to succeed (`PredictedPlayer.cs:540, 553`), since the
   buffer-miss path hard-snaps and never records a sample. Since `_latestPredictedTick` only ever
   grows within a session, the only way the server can acknowledge a tick the client has not yet
   predicted is if **`NetworkManager.LocalTime` was reset backwards** — which NGO's time system does
   when it detects the client running too far ahead. Note `Bootstrap.unity` has
   `EnableTimeResync: 0`. Confirming is one temporary log line recording
   `_latestPredictedTick`, `ackTick` and `LocalTime.Tick` whenever `pendingTicks == 0`. Worth doing:
   "we diagnosed it" is a better interview answer than the current "not diagnosed".
2. **Is mid-match joining a supported scenario at all?** F-A3-4's cheap fix (refuse outside
   Lobby/Loading) is correct only if the answer is no. If it is yes, the work is larger and touches
   life seeding, which is a game-design decision, not a netcode one.
3. **Should `MovementConfig` be replicated?** Today client and server each read their own local copy
   (`Settings/MovementConfig.asset`). A tampered client mispredicts and is corrected, so there is no
   exploit — but a *version-skewed* client (same `Application.version`, different asset) desyncs
   continuously with no diagnostic. Is the version check in `ConnectionApproval` considered sufficient
   cover, or is a config hash in the connection payload worth the six lines?
4. **`FrameRatePolicy` caps to 60 fps** (per `docs/05-validation.md:40`) after two uncapped renderers
   starved the client's input. Was that measured as a *test-harness* artefact of two peers on one
   machine, or does it also apply to a shipped build? It is currently a global cap solving a
   development-time problem.
5. **Consequence of the settled host topology, noted not raised:** with no host migration, the host
   leaving ends the match for everyone by construction. F-A3-5 is only about the *client's reaction*
   to that, not about the topology.
