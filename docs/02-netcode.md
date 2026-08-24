# 02 — Netcode Design

This is the centerpiece. The goal: **movement that feels instant, is impossible to cheat by
lying about position, and stays smooth for everyone watching.** Those three pulls conflict, and the
standard resolution is the trio below — the same approach behind most competitive action games
(Quake → Overwatch → Valorant lineage).

> Netcode for GameObjects does **not** ship client prediction out of the box. This layer is built
> by hand on top of NGO's `NetworkTickSystem` — which is exactly the part worth showing.

## The three pillars

### 1. Server authority
The server runs the **real** simulation. Clients never write their own position to the network.
A hacked client can send bogus input, but the server simulates it under the same rules as everyone
else — so the worst a cheater can do is move *legally*.

That last sentence is only true if two things are enforced, and both are easy to leave out because
neither is code in a method body. **Whose input is this?** `InvokePermission` on the input RPC — NGO
defaults to `Everyone` and enforces the permission on the receiving side, so without the declaration a
client can submit input for someone else's character and the server will faithfully simulate it.
**Is the input in range?** `MoveX` is documented as -1/0/1 and travels as a signed byte; `MoveSpeed`
stops being a ceiling the moment that assumption is not checked, and 127 on the wire is 127× the
intended speed. Both are declared at the boundary in `PredictedPlayer`, and the second is on the struct
itself as `InputCommand.Sanitized`.

### 2. Client-side prediction
Waiting for a round-trip before you move feels awful (100 ms+ of input lag). So the owning client
**simulates its own character immediately** using the same movement code the server runs, without
waiting for confirmation. Locally, the game feels lag-free.

### 3. Server reconciliation
Prediction drifts (the server may resolve a collision differently). When an authoritative state
arrives, the client checks it against what it *predicted* for that tick. If they match — nothing to do.
If they differ — **snap to the server state and replay** every input newer than that tick, so the
correction is applied without throwing away the player's more recent actions.

## The tick

Everything is quantized to a fixed **network tick of 30 Hz** (33.3 ms) from NGO's `NetworkTickSystem`.
Inputs, snapshots, and buffers are all keyed by an integer tick number — never wall-clock time.
A fixed tick is what makes "replay inputs since tick N" well-defined.

30 Hz over 60 Hz on purpose: it halves bandwidth, keeps the prediction buffer small, and — the part
that matters for a project *about* netcode — it makes remote interpolation genuinely necessary.
At 60 Hz raw snapshots already look almost smooth, which hides the very layer we're building.
Rendering still runs at whatever the display allows; it just reads interpolated state.

## Topology

**Host** (listen server): one player's machine is both server and client. This is what Relay assumes,
and it forces an honest treatment of a case a dedicated server would let us ignore — the host's own
player is `IsOwner && IsServer`, has zero latency, and must **not** be predicted or reconciled.
Code branches on *roles*, never on "am I the host", so a headless dedicated build stays possible
(Phase 5) without a rewrite.

## The simulation is kinematic, by necessity

The character is a **kinematic controller written by hand** — `Rigidbody2D` in `Kinematic` mode,
collisions resolved with casts/overlaps — never a dynamic `Rigidbody2D`.

This is not a style preference; **prediction requires it**. Reconciliation means re-simulating one
player across N ticks on demand, and Box2D can't do that: `Physics2D.Simulate` steps the *entire*
world, and its contact ordering and float accumulation don't reproduce identically across machines.
A pure function `Move(state, input, world, dt) → state` re-runs 10 ticks in a loop and yields the
same answer every time, on every machine. That determinism is the whole foundation.

> **We are still using Unity's physics.** Collisions go through `Physics2D.BoxCast` — the same
> engine, layers and colliders as anything else. What is not used is the *dynamic solver*:
> rigidbodies, forces and contact resolution. `NetworkRigidbody` was rejected for the same reason:
> it replicates the result of a simulation instead of predicting it, so an owner sees its own
> movement a round trip late. That is the defect [00 — Legacy analysis](00-legacy-analysis.md)
> records against the original project, which used `NetworkRigidbody2D`.

### The simulation is a sequence of steps

`PlayerMotor.Simulate` is not one method but an ordered list of them:

```
Stun → Horizontal → FeelTimers → Jump → Gravity → MoveAndCollide
```

Adding a mechanic — double jump, wall slide, dash — means writing a step and inserting it at a
defined point, without touching the ones already working. **The order is fixed**, because two
machines running the same steps in a different order stop agreeing, and agreement is what makes
replay possible.

This structure is the answer to a real limit: a single monolithic method stops being safe to extend
at around the third mechanic.

### Characters collide with each other, and it is predicted

Players are solid to one another: they cannot pass through, and they can stand on each other's
heads. This does **not** go through the physics solver either.

Other characters reach the simulation through a `SimulationContext` — plain boxes with a position
and a size, never live object references. The distinction matters: a replay of tick 40 must see
where everyone was *at tick 40*, and a live object would answer with where it is now, producing a
state the server never computed.

That is what `WorldSnapshotBuffer` holds: everyone's position, tick by tick, so reconciliation can
re-run contact against the world as it was. It is the same ring-buffer idea as `PredictionBuffer`,
applied to the rest of the world instead of to oneself.

**What the buffer is filled from, and why it matters.** On the server, the recorded positions are the
authoritative ones for that tick. On a client they are not: a remote character's state is whatever the
interpolator last produced, which is deliberately ~100 ms behind server time — and the client stores
it under its *own* prediction tick, which NGO runs ahead of the server by roughly the round trip. So
the two machines label the same tick number with positions from different moments, by about
`interpolation delay + client lead`.

The consequence is precise and worth stating rather than discovering: **the replay is self-consistent
but the prediction is offset from the authority whenever a peer is moving.** Prediction and replay read
the same buffer, so a correction never comes from a replay disagreeing with itself — nothing looks
broken. What happens instead is that close contact between two moving players reliably produces a
correction, because the client predicted contact against where the rival was rendered and the server
resolved it against where the rival actually was.

This is a design position, not an oversight, and the alternative is not free: filling the buffer from
authoritative snapshots means the newest tick a client can build a world frame for is *behind* its own
prediction tick, so the gap has to be extrapolated or held — trading a known offset for an
extrapolation error. Which trade is better is an open question rather than a settled one; it is
[tracked in the roadmap](03-roadmap.md) for Phase 4, where the game will be playable enough to feel
the difference.

Two rules fall out of this, and they decide where any future mechanic belongs:

| The mechanic depends on… | Where it lives | Examples |
|---|---|---|
| Only your own state and input | A step in `PlayerMotor`, predicted | double jump, wall jump, dash |
| Other players, or the server's judgement | Server-side, arriving as replicated state | head bounce, weapon damage, a trap someone triggered |

The head bounce follows the second row: the server decides it and the result reaches owners as a
`StunTimer` inside the state they already reconcile against. They never predicted it and could not
have — but the replay carries it forward correctly, because it is part of `PlayerState`.

## Owner client — per tick

```
      ┌──────────────────────────── owning client ────────────────────────────┐
      │                                                                        │
 sample input ──▶ InputCommand{ tick, moveX, jump }                            │
      │                 │                                                       │
      │                 ├─▶ send to server (ServerRpc / message)               │
      │                 │                                                       │
      │                 └─▶ apply LOCALLY via shared Move() ──▶ predicted state │
      │                          │                                             │
      │                          └─▶ store in buffer: [tick] = {input, state}  │
      └────────────────────────────────────────────────────────────────────────┘
```

## Server — per tick

```
      ┌──────────────────────────────── server ───────────────────────────────┐
      │  for each player:                                                      │
      │    read latest InputCommand ──▶ shared Move() ──▶ authoritative state  │
      │                                        │                               │
      │  broadcast StateSnapshot{ tick, pos, vel, lastProcessedInputTick }     │
      └────────────────────────────────────────────────────────────────────────┘
```

The `Move()` function is **shared** between client and server (one method, one file). Prediction only
works if both sides simulate identically.

## Reconciliation — when a snapshot arrives (owner)

```
 snapshot for tick N arrives
        │
        ├─ compare snapshot.state  vs  buffer[N].predictedState
        │
        ├─ within tolerance?  ──▶ yes ──▶ done (prediction was correct)
        │
        └─ no ──▶  1. set current state = snapshot.state          (rewind)
                   2. for t = N+1 .. currentTick:                 (replay)
                        state = Move(state, buffer[t].input)
                   3. buffer[t].predictedState = state  (updated)
```

### What the player sees during a correction

The **simulated state snaps** — the logic never runs on a position neither side believes in.
The **visual** doesn't: the sprite lives on a child transform that absorbs the positional error and
decays it to zero over ~100 ms. So corrections are logically instant and visually invisible, and the
debug overlay can draw both the corrected and the smoothed position at once.

Interpolating the *simulated* state instead would be the tempting shortcut and the wrong one: errors
would compound tick over tick instead of closing.

## Wire format

| Direction | Mechanism | Why |
|---|---|---|
| Input (owner → server) | `[Rpc(SendTo.Server, Unreliable, InvokePermission = Owner)]` carrying the **last 3 `InputCommand`s** | Redundancy beats retransmission: a dropped packet is covered by the next one, with no head-of-line blocking. The server ignores ticks it already consumed. `Owner` is what stops a client submitting input for someone else's character. |
| Snapshot (server → all) | one **unreliable** `Rpc` per tick with every player's state, `InvokePermission = Server` | One packet per tick, not one per player. Stale snapshots are worthless, so reliability would only add latency. `SendTo.NotServer` says where a message goes, not who may send it — without `Server`, NGO proxies a client's forged frame to everyone. |

`NetworkVariable` is deliberately **not** used for input: it replicates *latest value*, collapsing
intermediate ones — and a gap in the input sequence makes replay impossible. It stays where it fits:
slow, state-like data (life timer, match state).

## Remote players — interpolation

Non-owned players are **not** predicted. Their snapshots are buffered and rendered ~100 ms *in the past*,
interpolating between the two snapshots that straddle "render time." Trades a hair of latency for
motion that's smooth regardless of packet jitter or tick rate.

```
 snapshots:   [tick 10]──[tick 11]──[tick 12]──[tick 13] (newest)
 render here:              ▲ interpolate between 11 and 12
                           (buffer keeps us ~100ms behind newest)
```

## What replicates how

| Data | Mechanism | Rate |
|------|-----------|------|
| Own input | client → server command | every tick |
| Own transform | predicted locally, reconciled | every tick (server → owner) |
| Remote transforms | snapshot + interpolation | every tick (server → clients) |
| Life timer | `NetworkVariable` | ~1 Hz (only when it changes meaningfully) |
| Match / round state | `NetworkVariable` + events | on change |
| Fruit spawn / pickup | server spawn/despawn + `NetworkVariable<int>` for the kind, published in the server's `OnNetworkSpawn` so it rides the spawn message | on event |

## How we'll prove it works

- **Artificial latency + packet loss** via NGO's Network Simulator — prediction should hide it, and
  reconciliation corrections should be visible in the debug overlay but not felt.
- A **debug overlay**: predicted vs authoritative position, reconciliation count, RTT, tick.
- **Multiplayer Play Mode** to run host + client in one editor while developing.

## References

- Gabriel Gambetta — *Fast-Paced Multiplayer* (prediction & reconciliation series)
- Glenn Fiedler (*Gaffer On Games*) — snapshot interpolation, networked physics
- Overwatch GDC — *Netcode & Rollback*
