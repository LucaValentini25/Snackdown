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
A pure function `Move(state, input, dt) → state` re-runs 10 ticks in a loop and yields the same
answer every time, on every machine. That determinism is the whole foundation.

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
| Input (owner → server) | `[Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]` carrying the **last 3 `InputCommand`s** | Redundancy beats retransmission: a dropped packet is covered by the next one, with no head-of-line blocking. The server ignores ticks it already consumed. |
| Snapshot (server → all) | one **unreliable** `Rpc` per tick with every player's state | One packet per tick, not one per player. Stale snapshots are worthless, so reliability would only add latency. |

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
| Fruit spawn / pickup | server spawn/despawn + RPC | on event |

## How we'll prove it works

- **Artificial latency + packet loss** via NGO's Network Simulator — prediction should hide it, and
  reconciliation corrections should be visible in the debug overlay but not felt.
- A **debug overlay**: predicted vs authoritative position, reconciliation count, RTT, tick.
- **Multiplayer Play Mode** to run host + client in one editor while developing.

## References

- Gabriel Gambetta — *Fast-Paced Multiplayer* (prediction & reconciliation series)
- Glenn Fiedler (*Gaffer On Games*) — snapshot interpolation, networked physics
- Overwatch GDC — *Netcode & Rollback*
