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

Everything is quantized to a fixed **network tick** (e.g. 30 Hz) from NGO's `NetworkTickSystem`.
Inputs, snapshots, and buffers are all keyed by an integer tick number — never wall-clock time.
A fixed tick is what makes "replay inputs since tick N" well-defined.

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
