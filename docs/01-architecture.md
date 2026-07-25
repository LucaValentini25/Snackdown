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
│                interpolation  (the reusable core)        │
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
│   │   ├── Core/             Bootstrap, app state machine, scene flow
│   │   ├── Connection/       IConnectionProvider, DirectProvider, RelayProvider, approval
│   │   ├── Netcode/          NetworkTick, prediction buffer, reconciliation, interpolation
│   │   ├── Gameplay/
│   │   │   ├── Player/       Predicted character controller, input command
│   │   │   ├── Fruits/       Spawner, fruit pickup
│   │   │   └── Combat/       Head-bounce, stun
│   │   ├── UI/               Main menu, lobby, HUD, end screen
│   │   └── Input/            Input actions + reader
│   ├── Scenes/
│   ├── Prefabs/
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

## Configuration lives in data, not code

Movement tuning, jump feel, match length, fruit spawn tables, rarity weights → **ScriptableObjects**
under `_Project/Settings/`. Designers (and future-you) tune the game without recompiling, and the
netcode code stays free of magic numbers.

## Assemblies (planned)

Once the code stabilizes, split into assembly definitions so the **Netcode** layer compiles as a
standalone, reusable assembly with no gameplay references — proof that the core is genuinely decoupled.
Deferred to a polish phase to avoid reference churn during rapid iteration.
