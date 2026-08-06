# 01 — Architecture

The project is organized in **layers**, from transport up to gameplay. The rule that keeps it clean:
**lower layers never know about higher ones.** Netcode doesn't know what a "fruit" is; the connection
layer doesn't know there's a game at all.

```
┌─────────────────────────────────────────────────────────┐
│  UI            Menus, HUD, lobby, end screen             │
├─────────────────────────────────────────────────────────┤
│  Gameplay      Player, fruits, combat, match rules       │
├─────────────────────────────────────────────────────────┤
│  Netcode       Tick, prediction, reconciliation,         │
│                interpolation                             │
├─────────────────────────────────────────────────────────┤
│  Connection    IConnectionProvider → LAN | Relay,        │
│                approval, lobby                            │
├─────────────────────────────────────────────────────────┤
│  Core          Bootstrap, scene flow, service locators   │
└─────────────────────────────────────────────────────────┘
        (Netcode for GameObjects + Unity Transport underneath)
```

## Folder structure

```
Assets/
├── _Project/                 ← everything we write lives here
│   ├── Scripts/
│   │   ├── Core/             NetTestBootstrap (Phase 2: app state machine, scene flow)
│   │   ├── Connection/       IConnectionProvider, DirectProvider, RelayProvider, approval
│   │   ├── Netcode/          NetworkSimulationLoop, PredictionBuffer, SnapshotFrame,
│   │   │                     SnapshotInterpolator, VisualSmoother
│   │   ├── Gameplay/
│   │   │   ├── Player/       PlayerState, InputCommand, PlayerMotor, PredictedPlayer,
│   │   │   │                 MovementConfig, PlayerSpawnPoints
│   │   │   ├── Fruits/       Spawner, fruit pickup
│   │   │   └── Combat/       Head-bounce, stun
│   │   ├── UI/               NetDebugOverlay (Phase 2: menu, lobby, HUD, end screen)
│   │   └── Input/            InputReader
│   ├── Scenes/               NetTest
│   ├── Prefabs/              Player
│   ├── Art/                  placeholder primitives for the test arena
│   └── Settings/             ScriptableObject configs (movement, match, spawn tables)
├── Pixel Adventure 1/        third-party art
├── DEVNIK 2D/                third-party UI
├── Settings/                 URP pipeline assets
└── TextMesh Pro/
```

Third-party art sits at the `Assets/` root (as it shipped); **all authored code is under `_Project/`.**

## Authority rules (the contract)

These are the invariants every system must respect. If a change would break one of these, the design is wrong.

1. **The server is the single source of truth** for anything that affects the match:
   position, life, deaths, fruit ownership, win conditions.
2. **Clients send *intent*, never *result*.** A client sends "I'm holding right + jump on tick N,"
   not "my position is (x, y)."
3. **The owning client may *predict*** its own character to hide latency, but the server's word
   overrides it (reconciliation).
4. **Remote clients only *interpolate*** authoritative snapshots — they never predict other players.
5. **RPCs are validated.** A `ServerRpc` assumes the caller is hostile until checked.

## Settled parameters

| Decision | Value | Rationale |
|---|---|---|
| Players per match | **4** | Enough that a client interpolates 3 remotes at once and bandwidth scales for real; matches the 4 Pixel Adventure characters; fits Relay's free tier and Multiplayer Play Mode on one machine. |
| Topology | **Host** (listen server), written so a headless server stays possible | See [02 — Netcode](02-netcode.md#topology). |
| Character controller | **Kinematic, hand-written** — never a dynamic `Rigidbody2D` | Prediction needs a re-runnable pure `Move()`. See [02 — Netcode](02-netcode.md#the-simulation-is-kinematic-by-necessity). |
| Character selection | 4 skins, mechanically identical | Ships in Phase 2: the chosen character rides in the **same connection-approval payload** as the nickname, so it reinforces that system instead of adding one. |

## Configuration lives in data, not code

Movement tuning, jump feel, match length, fruit spawn tables, rarity weights → **ScriptableObjects**
under `_Project/Settings/`. Designers (and future-you) tune the game without recompiling, and the
netcode code stays free of magic numbers.

## The netcode layer is built for this game, not for reuse

An earlier version of this document called the netcode layer "the reusable core" and planned an
assembly split to prove it. That goal is **dropped**, deliberately.

The layering rule still holds and still matters: gameplay depends on netcode, never the reverse, and
that keeps the tick, prediction and reconciliation reasonable about in isolation. What is not being
pursued is the stronger claim — that the layer could be lifted out and dropped into another project.
Achieving it would mean generic buffers, an interpolation contract on the state, and moving the wire
format out of the layer that defines it, and the result would still need rewriting for any game whose
state is not a 2D position and a velocity. The cost is real and the payoff was hypothetical.

See [ADR 0002](adr/0002-decoupling-the-netcode-layer.md) for the full analysis, including the NGO
constraint that shaped it: an `[Rpc]` cannot take a parameter closed over its class's generic
parameter — the code generator crashes rather than reporting an error.

Today `Netcode/` imports `PlayerState` and `InputCommand` from `Gameplay/Player/`. That is a known,
accepted dependency rather than an oversight.

Assembly definitions stay in Phase 5, where they were, and on their own merits — compile times and
letting tests reference a narrow slice of the project — not as proof of a decoupling nobody needs.
