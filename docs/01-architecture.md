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
│  Simulation    PlayerState, InputCommand, PlayerMotor    │
│                — the state, and the pure step over it    │
├─────────────────────────────────────────────────────────┤
│  Connection    IConnectionProvider → LAN | Relay,        │
│                approval, lobby                            │
├─────────────────────────────────────────────────────────┤
│  Core          Bootstrap, scene flow, service locators   │
└─────────────────────────────────────────────────────────┘
        (Netcode for GameObjects + Unity Transport underneath)
```

**Simulation** sits under netcode because both sides of the wire need it and neither owns it. The
server simulates with it, the owning client predicts with it, and the buffer stores what it produced
— so it cannot belong to any one of them. Splitting it out is also what breaks the cycle described
under [Assemblies](#assemblies-one-per-system).

## Folder structure

```
Assets/
├── _Project/                 ← everything we write lives here
│   ├── Scripts/
│   │   ├── Core/             FrameRatePolicy (Phase 2: app state machine, scene flow)
│   │   ├── Connection/       IConnectionProvider, DirectProvider, RelayProvider, approval
│   │   ├── Simulation/       PlayerState, InputCommand, InputPacket, PlayerMotor,
│   │   │                     MovementConfig — the state, the input, the pure step
│   │   ├── Netcode/          NetworkSimulationLoop, IPredictedPeer, PredictionBuffer,
│   │   │                     SnapshotFrame, SnapshotInterpolator, VisualSmoother,
│   │   │                     ReconciliationStats, RunRecorder
│   │   ├── Gameplay/
│   │   │   ├── Player/       PredictedPlayer, PlayerSpawnPoints
│   │   │   ├── Fruits/       Spawner, fruit pickup
│   │   │   └── Combat/       Head-bounce, stun
│   │   ├── UI/               NetDebugOverlay, MainMenuController
│   │   └── Input/            InputReader
│   ├── UI/                   MainMenu.uxml, Snackdown.uss, MenuPanelSettings
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

An earlier version of this document called the netcode layer "the reusable core". That claim is
**dropped**, deliberately — lifting this layer into another game would mean a different state type, a
different input type and a different wire format, which is most of what is in it. The cost was real
and the payoff hypothetical. [ADR 0002](adr/0002-decoupling-the-netcode-layer.md) records the full
analysis, including the NGO constraint that an `[Rpc]` cannot take a parameter closed over its
class's generic parameter — the code generator crashes rather than reporting an error.

What survives, and is enforced rather than promised, is the **layering**: gameplay depends on
netcode, never the reverse.

## Assemblies — one per system

Every system is its own assembly definition, referencing only what it uses. Not deferred to a polish
phase: an assembly is created when a system is, because retrofitting them is a migration while
starting with them is free.

```
Snackdown.Simulation   →  (nothing)
Snackdown.Netcode      →  Simulation
Snackdown.Input        →  (nothing)
Snackdown.Gameplay     →  Simulation, Netcode, Input
Snackdown.Core         →  (nothing of ours)
Snackdown.UI           →  Netcode, Gameplay
Snackdown.Tests.EditMode  →  Simulation, Netcode          (Editor only)
```

`Connection/` has no assembly yet because it has no code yet. It gets one in Phase 2, when there is
something to put in it.

**This is what caught the cycle.** `Netcode/` imported `PlayerState` and `InputCommand` while
`Gameplay/` imported the buffer and interpolator — each layer depending on the other, in flat
contradiction of the rule stated at the top of this document. Inside a single `Assembly-CSharp` that
is legal and invisible. Across two assemblies it does not compile.

Breaking it needed no abstraction, only honesty about where the types belonged: `PlayerState`,
`InputCommand`, `InputPacket`, `PlayerMotor` and `MovementConfig` were never gameplay rules, they are
the simulation, and they now live in their own layer that both sides depend on. The one remaining
edge — the tick loop iterating the concrete character — is met by `IPredictedPeer`, an interface of
the six members the loop actually calls.

The payoff beyond the compiler check: `Snackdown.Simulation` is pure enough to unit test without a
scene, a `NetworkManager` or Play mode, which is exactly what `Snackdown.Tests.EditMode` does.
