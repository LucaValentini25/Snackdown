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
│                — the state, and the replayable step      │
├─────────────────────────────────────────────────────────┤
│  Connection    IConnectionProvider → LAN | Relay,        │
│                approval, lobby                            │
├─────────────────────────────────────────────────────────┤
│  Core          Bootstrap, scene exclusion, frame policy  │
└─────────────────────────────────────────────────────────┘
        (Netcode for GameObjects + Unity Transport underneath)
```

Two words in that diagram are worth being precise about, because the loose reading of either one is
false.

**"Replayable", not "pure".** `PlayerMotor.Simulate` never reads `Time`, a `Transform` or a
`Rigidbody2D` — everything it needs arrives as an argument, which is what lets a replay re-run tick 40
and get tick 40's answer. But it *does* query the live physics scene for terrain
(`Physics2D.BoxCast` against `GroundMask`). So the property is: **pure with respect to time and object
state; static geometry is read through casts.** That holds today because arenas have no moving
geometry. The first moving platform breaks replay silently, and the extension point for it is
`SimulationContext`, which already exists for exactly this reason.

**`Core` holds bootstrap, not services.** It is two files: `AppBootstrap`, which keeps the menu scene
out of NGO's synchronization, and `FrameRatePolicy`. There is no service locator in it and no app
state machine — see [Ambient lookups](#ambient-lookups) for the pattern the project actually uses and
why. `Core` is expected to grow into the app-level scene flow; the diagram describes what it holds
now.

**Simulation** sits under netcode because both sides of the wire need it and neither owns it. The
server simulates with it, the owning client predicts with it, and the buffer stores what it produced
— so it cannot belong to any one of them. Splitting it out is also what breaks the cycle described
under [Assemblies](#assemblies-one-per-system).

## Folder structure

```
Assets/
├── _Project/                 ← everything we write lives here
│   ├── Scripts/
│   │   ├── Core/             AppBootstrap, FrameRatePolicy
│   │   ├── Connection/       IConnectionProvider, DirectConnectionProvider,
│   │   │                     RelayConnectionProvider, ConnectionApproval, ConnectionPayload,
│   │   │                     ConnectionRequest, ConnectionResult, SessionRoster, PlayerSlot
│   │   ├── Simulation/       PlayerState, InputCommand, InputPacket, PlayerMotor,
│   │   │                     MovementConfig, SimulationContext — the state, the input,
│   │   │                     the replayable step
│   │   ├── Netcode/          NetworkSimulationLoop, IPredictedPeer, PredictionBuffer,
│   │   │                     SnapshotFrame, SnapshotInterpolator, WorldSnapshotBuffer,
│   │   │                     VisualSmoother, ReconciliationStats, RunRecorder
│   │   ├── Gameplay/
│   │   │   ├── Match/        MatchDirector, MatchPhase, MatchConfig, MatchOutcome,
│   │   │   │                 RoundReferee, ArenaCatalog, ArenaBounds, SpectatorCamera
│   │   │   ├── Player/       PredictedPlayer, PlayerLife, PlayerSpawnPoints,
│   │   │   │                 CharacterAppearance, CharacterCatalog
│   │   │   ├── Fruits/       Fruit, FruitSpawner, FruitTable
│   │   │   └── Combat/       HeadBounce
│   │   ├── UI/               MainMenuController, LoadingScreenController,
│   │   │                     EndScreenController, NetDebugOverlay
│   │   └── Input/            InputReader, SpectatorInput
│   ├── UI/                   MainMenu.uxml, LoadingScreen.uxml, EndScreen.uxml,
│   │                         Snackdown.uss, MenuPanelSettings
│   ├── Scenes/               Bootstrap, Lobby, Arena01
│   ├── Prefabs/              Player, Fruit
│   ├── Art/                  placeholder primitives
│   └── Settings/             ScriptableObject configs (movement, match, arenas,
│                             characters, fruit table)
├── Tests/EditMode/           simulation, prediction ring, interpolator, peer collision,
│                             stun, fruit table, arena bounds, nickname sanitation
├── Pixel Adventure 1/        third-party art
├── DEVNIK 2D/                third-party UI
├── Settings/                 URP pipeline assets
└── TextMesh Pro/
```

Third-party art sits at the `Assets/` root (as it shipped); **all authored code is under `_Project/`.**

## Scenes load additively on top of Bootstrap

Three scenes, and the split is what makes several arenas possible:

| Scene | Holds | Lifetime |
|---|---|---|
| **Bootstrap** | `NetworkManager`, `MatchDirector`, `RoundReferee`, `SessionRoster`, the tick loop, the loading screen, the round clock, the life bars and the end screen | The whole session |
| **Lobby** | Menu and lobby UI | Between matches |
| **Arena01** | Geometry, spawn points, camera | During a match |

Bootstrap is loaded first and **never unloaded**; the lobby and arenas come and go on top of it
with `LoadSceneMode.Additive`. Loading them as `Single` would unload bootstrap along with
everything else — taking the connection, the roster and the director with it, which are precisely
the things that have to survive a match starting.

Arenas are content, not code: they live in an `ArenaCatalog` asset, so adding one is authoring.
As with the character catalog, the index is what crosses the network, so entries are appended and
never reordered.

**The lobby scene has exactly one owner**, the reconciler in the UI layer, which brings it up
whenever the phase says it should be there — including before any session exists. When a match ends
the director unloads the arena and stops; it does not load the lobby back. Two owners of an additive
load is what once produced two lobby scenes stacked on top of each other: a load takes frames to
land, so the second owner looks, correctly concludes the scene is not there yet, and starts another.

## Authority rules (the contract)

These are the invariants every system must respect. If a change would break one of these, the design is wrong.

1. **The server is the single source of truth** for anything that affects the match:
   position, life, deaths, fruit ownership, win conditions.
2. **Clients send *intent*, never *result*.** A client sends "I'm holding right + jump on tick N,"
   not "my position is (x, y)."
3. **The owning client may *predict*** its own character to hide latency, but the server's word
   overrides it (reconciliation).
4. **Remote clients only *interpolate*** authoritative snapshots — they never predict other players.
5. **RPCs are validated.** A `ServerRpc` assumes the caller is hostile until checked — and "checked"
   means two separate things, both required. **Who** sent it: declared with `InvokePermission` on the
   attribute, because NGO's default is `Everyone` and it enforces the permission receive-side, so the
   declaration *is* the check. **What** they sent: range-checked where the value enters, not where it
   is used, so the queue and everything built from it inherit the invariant.

## Settled parameters

| Decision | Value | Rationale |
|---|---|---|
| Players per match | **4** | Enough that a client interpolates 3 remotes at once and bandwidth scales for real; matches the 4 Pixel Adventure characters; fits Relay's free tier and Multiplayer Play Mode on one machine. |
| Topology | **Host** (listen server), written so a headless server stays possible | See [02 — Netcode](02-netcode.md#topology). |
| Character controller | **Kinematic, hand-written** — never a dynamic `Rigidbody2D` | Prediction needs a re-runnable pure `Move()`. See [02 — Netcode](02-netcode.md#the-simulation-is-kinematic-by-necessity). |
| Player-vs-player contact | Solid, and **predicted** — resolved in the motor against buffered peer positions, which on a client are the *interpolated* ones | Waiting for the server to decide it would cost a round trip on every bump. The cost of not waiting is that a client predicts contact against peers as they were rendered, so close contact reliably produces a correction. See [02 — Netcode](02-netcode.md#characters-collide-with-each-other-and-it-is-predicted). |
| Ending a round | **Last one standing**, or the most life left when the 3-minute clock runs out | Both were already implied by `MatchConfig`; a shared top value at the clock is reported as a draw rather than broken by client id, because `MaxLife` makes an exact tie reachable. |
| A player who is out | **Hidden, not despawned** — and free to pan the camera around the arena | Despawning would take the roster entry and the life readout with it, and both are still needed by the end screen and the next round. |
| Character selection | 4 skins, mechanically identical | Ships in Phase 2: the chosen character rides in the **same connection-approval payload** as the nickname, so it reinforces that system instead of adding one. |

## Who decides a round is over

`MatchDirector` owns the phase and the arena. `RoundReferee` owns the rules. They are separate
because they change for different reasons: adding a game mode rewrites the referee and leaves scene
loading alone, and adding an arena does the reverse.

The referee replicates a **verdict** — a winner and a reason — not the ingredients it used. A client
holding the raw life values could reach a different conclusion from the same numbers and disagree
about who won; a client holding the verdict cannot.

Two things follow from a player being out, and both are replicated as one flag rather than derived:

- They stop simulating, stop being solid, and stop being stompable.
- Their owner gets the camera.

The flag matters because life is *interpolated* on clients — it drains locally between the
server's once-a-second updates and can reach zero a fraction early. Deriving death from that number
would let a client bury a player the server still considers alive, and hand their owner a spectator
camera mid-match.

### The camera is never replicated

Where a spectator is looking changes no outcome, so it stays local. `ArenaBounds` — a rectangle
authored per arena — clamps the pan, and when a map is smaller than the camera view the clamp
collapses to its centre and the camera simply holds still. One component covers both kinds of map
without asking the level designer which kind they built. **Arena01 is the small kind**: it is 26×9
against a 24.9×14 view, so a spectator there gets about half a unit of horizontal slack and nothing
vertical. Panning becomes visible on the first arena that is bigger than the screen.

## Configuration lives in data, not code

Movement tuning, jump feel, match length, fruit spawn tables, rarity weights → **ScriptableObjects**
under `_Project/Settings/`. Designers (and future-you) tune the game without recompiling, and the
netcode code stays free of magic numbers.

## The netcode layer is built for this game, not for reuse

An earlier version of this document called the netcode layer "the reusable core". That claim is
**dropped**, deliberately — lifting this layer into another game would mean a different state type, a
different input type and a different wire format, which is most of what is in it. The cost was real
and the payoff hypothetical. [ADR 0001](adr/0001-decoupling-the-netcode-layer.md) records the full
analysis, including the NGO constraint that an `[Rpc]` cannot take a parameter closed over its
class's generic parameter — the code generator crashes rather than reporting an error.

What survives, and is enforced rather than promised, is the **layering**: gameplay depends on
netcode, never the reverse. Enforced means the compiler rejects the other direction, not that the
layer is portable — the ADR is explicit that it is not.

## Ambient lookups

Several systems are reached through a static rather than an injected reference: `MatchDirector.Current`,
`RoundReferee.Current`, `ConnectionApproval.Current`, `ArenaBounds.Current`,
`NetworkSimulationLoop.Instance`, plus the `PlayerLife.All` and `NetworkSimulationLoop.ActivePlayers`
registries and two debug toggles.

This is deliberate and it is the pattern the project uses instead of a container. Two reasons, and
both are specific rather than stylistic:

- **Spawn order is not something a scene can promise.** These objects come up as networked spawns, so
  a serialized reference would be null exactly as often as it would be useful.
- **Each peer is its own process, including under Multiplayer Play Mode.** A static is per-process,
  which is the correct scope for "the match director in *this* session" — it is not shared state
  between peers.

The costs are real and worth naming rather than hiding. Every consumer null-checks, so forgetting one
is a `NullReferenceException` during a scene transition; and anything reading one of these cannot be
exercised in an EditMode test, which is visibly why the test suite covers `Simulation` and `Netcode`
and almost nothing in `Gameplay/Match`. **A DI container would not fix either problem** — it would
move the first and keep the second. What fixes the second is extracting the logic worth testing out
of the `MonoBehaviour` that owns the static, which is a per-case decision.

## Assemblies — one per system

Every system is its own assembly definition, referencing only what it uses. Not deferred to a polish
phase: an assembly is created when a system is, because retrofitting them is a migration while
starting with them is free.

```
Snackdown.Simulation   →  (nothing of ours)
Snackdown.Netcode      →  Simulation
Snackdown.Input        →  (nothing of ours)
Snackdown.Connection   →  (nothing of ours)
Snackdown.Gameplay     →  Simulation, Netcode, Input, Connection
Snackdown.Core         →  Connection
Snackdown.UI           →  Netcode, Gameplay, Connection
Snackdown.Tests.EditMode  →  Simulation, Netcode, Gameplay, Connection   (Editor only)
```

**This is what caught the cycle.** `Netcode/` imported `PlayerState` and `InputCommand` while
`Gameplay/` imported the buffer and interpolator — each layer depending on the other, in flat
contradiction of the rule stated at the top of this document. Inside a single `Assembly-CSharp` that
is legal and invisible. Across two assemblies it does not compile.

Breaking it needed no abstraction, only honesty about where the types belonged: `PlayerState`,
`InputCommand`, `InputPacket`, `PlayerMotor`, `MovementConfig` and `SimulationContext` were never
gameplay rules, they are the simulation, and they now live in their own layer that both sides depend
on. The one remaining
edge — the tick loop iterating the concrete character — is met by `IPredictedPeer`, an interface of
the six members the loop actually calls.

The payoff beyond the compiler check: `Snackdown.Simulation` is pure enough to unit test without a
scene, a `NetworkManager` or Play mode, which is exactly what `Snackdown.Tests.EditMode` does.
