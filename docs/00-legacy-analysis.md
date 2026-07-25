# 00 — Legacy Analysis

This project began as a university assignment (*"Final-Redes"*) — a working multiplayer 2D
platformer built with Unity 6 and Netcode for GameObjects. Snackdown is a from-scratch rebuild.
This document analyzes the original honestly: what it demonstrated, and why it was worth rebuilding.

> Analyzing your own past code is part of the portfolio. It shows you can read a codebase,
> judge it, and know *why* a cleaner design is cleaner.

## What the original was

A last-player-standing survival platformer:

- Each player has a **life timer** (max 60s) that continuously drains.
- **Fruits** spawn around the level with rarity-weighted odds (common 35% → legendary 1%) and heal 3–20s.
- Players can **jump on each other's heads** to stun them for 2s (and bounce off).
- Death → spectator. Last alive wins, or on timeout the player with most life wins.
- Scene flow: `MainMenu` → `Lobby` (needs ≥2 players) → `Level-1` (countdown → play) → `GameEnd` → back to lobby.

~2,000 lines of hand-written C# across 22 scripts, using a **host-client** topology.

## What it got right ✅

The original covered most of the Netcode for GameObjects surface area correctly:

- `NetworkBehaviour` lifecycle (`OnNetworkSpawn` / `OnNetworkDespawn`)
- `NetworkVariable` with explicit read/write permissions (Server-write and Owner-write)
- `ServerRpc` / `ClientRpc`, including **targeted** RPCs via `ClientRpcParams`
- Dynamic **spawn / despawn** of networked objects (fruits)
- **Server-authoritative game state** — life countdown, fruit collection, and win conditions all resolve on the server
- Networked **scene management** via `NetworkManager.SceneManager`
- **Late-join handling** (players who connect mid-match become spectators)

As a first networked game, this is a solid, complete vertical slice.

## What this rebuild fixes 🔧

Ordered by impact on "does this person understand netcode."

### 1. Authority model — movement was client-authoritative
The owner wrote velocity **directly to its `Rigidbody2D`** every frame, and `NetworkRigidbody2D`
replicated the result. That means a client is trusted with its own position — trivially cheatable,
and it mixes physics stepping between owner and server.
→ **Rebuild:** server-authoritative movement with client-side prediction & reconciliation.

### 2. Bandwidth — RPCs and NetworkVariables fired every frame
`SyncMovementServerRpc` was sent **once per frame per player**, and `LifeTime` changed every frame
(so it replicated every frame). No tick rate, no batching.
→ **Rebuild:** fixed network tick, input batching, and rate-limited state (timers tick ~once/sec).

### 3. Cross-authority in the jump
`GroundChecker` ran **only on the server** (`if (!IsServer) return;`), but the *owner* read
`IsGrounded` to decide whether it could jump — a value that arrives with round-trip latency.
Classic authority mismatch.
→ **Rebuild:** ground checks are part of the predicted simulation, computed where the input is consumed.

### 4. The join flow didn't actually work
Server discovery was a `// TODO`; the server list in the client UI was never populated, so
**a client had no way to join** from the menu (only host / localhost worked). The Relay/Lobby/Sessions
packages were installed but the custom UI bypassed them.
→ **Rebuild:** a real connection layer — LAN direct **and** Relay/Lobby behind one interface.

### 5. No connection approval
Nicknames were pushed through several racy paths (`Task.Delay(100)`, retries in a `while` loop).
→ **Rebuild:** **connection approval** carries the nickname in the connect payload, once, deterministically.

### 6. Teleport hacks were a symptom, not a bug
`ForceTeleportRoutine` looped 5 physics frames forcing position; spawn used `await Task.Delay(1000)`.
These are workarounds for a muddy authority model, not isolated issues — a clean model removes the need.

### 7. Smaller items
- `async void` in networked code (swallows exceptions, can run after despawn → null refs)
- `NetworkBehaviour` singletons with `DontDestroyOnLoad` — fragile across networked scene loads
- No tests (the Test Framework package was present but unused)
- Inconsistent namespaces; `Enviorment` typo

## Takeaway

The original proves the concepts *work*. The rebuild proves they're *understood* —
by committing to one authority model and building the prediction/interpolation layer that a
production netcode stack actually needs.
