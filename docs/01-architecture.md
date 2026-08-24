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
│   │   │                     ConnectionRequest, ConnectionResult, SessionConnection,
│   │   │                     NetworkConfigReport
│   │   ├── Simulation/       PlayerState, InputCommand, InputPacket, PlayerMotor,
│   │   │                     MovementConfig, SimulationContext — the state, the input,
│   │   │                     the replayable step
│   │   ├── Netcode/          NetworkSimulationLoop, IPredictedPeer, PredictionBuffer,
│   │   │                     SnapshotFrame, SnapshotInterpolator, WorldSnapshotBuffer,
│   │   │                     VisualSmoother, ReconciliationStats, RunRecorder
│   │   ├── Gameplay/
│   │   │   ├── Match/        MatchDirector, MatchPhase, MatchConfig, MatchSettings,
│   │   │   │                 DifficultyCatalog, MatchOutcome,
│   │   │   │                 RoundReferee, ArenaCatalog, ArenaBounds, SpectatorCamera,
│   │   │                 SandboxRunner
│   │   │   ├── Player/       PredictedPlayer, PlayerSession, SessionRoster, PlayerLife,
│   │   │   │                 PlayerSpawnPoints, CharacterAppearance, CharacterCatalog
│   │   │   ├── Fruits/       Fruit, FruitSpawner, FruitTable
│   │   │   └── Combat/       HeadBounce
│   │   ├── UI/               MainMenuController, LoadingScreenController,
│   │   │                     EndScreenController, RoundClockController,
│   │   │                     LifeBarsController, PlayerNameplate, LifeBarStyle,
│   │   │                     LifeText, NetDebugOverlay
│   │   └── Input/            InputReader, SpectatorInput
│   ├── UI/                   MainMenu.uxml, LoadingScreen.uxml, EndScreen.uxml,
│   │                         RoundClock.uxml, LifeBars.uxml, Nameplate.uxml,
│   │                         Snackdown.uss, MenuPanelSettings, WorldSpacePanelSettings
│   ├── Scenes/               Bootstrap, Lobby, Arena01, Sandbox
│   ├── Prefabs/              Player, PlayerSession, Fruit, NetworkSimulation
│   ├── Art/                  placeholder primitives
│   └── Settings/             ScriptableObject configs (movement, match, arenas,
│                             characters, fruit table)
├── Tests/EditMode/           simulation, prediction ring, interpolator, peer collision,
│                             stun, fruit table, arena bounds, nickname sanitation
├── Tests/PlayMode/           the two-peer harness and the handshake it verifies
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
| **Bootstrap** | `NetworkManager` and `SessionConnection`, the `NetworkSimulation` prefab instance — `MatchDirector`, `RoundReferee`, `SessionRoster` and the tick loop on one networked object — plus the loading screen, the round clock, the life bars and the end screen | The whole session |
| **Lobby** | Menu and lobby UI | Between matches |
| **Arena01** | Geometry, spawn points, camera | During a match |
| **Sandbox** | A copy of Bootstrap that hosts and starts a match on Play | Never in a build; opened by hand |

All four are listed in Build Settings, and **Sandbox is listed with its checkbox off** — present so
Unity stops adding it back every time somebody opens it, disabled so it stays out of a player build.
The other three are enabled because Netcode can only load a scene over the network if it is in that
list, which is what an arena is. Removing Sandbox from the list is not a tidy-up: it comes straight
back the next time the scene is opened, and the churn shows up in every diff.

Bootstrap is loaded first and **never unloaded**; the lobby and arenas come and go on top of it
with `LoadSceneMode.Additive`. Loading them as `Single` would unload bootstrap along with
everything else — taking the connection, the roster and the director with it, which are precisely
the things that have to survive a match starting.

Arenas are content, not code: they live in an `ArenaCatalog` asset, so adding one is authoring.
As with the character catalog, the index is what crosses the network, so entries are appended and
never reordered.

**The connection outlives the lobby that opened it.** `SessionConnection` sits in bootstrap and owns
the provider, the approval and the join code; the menu asks it rather than holding them. That is not
tidiness — the lobby scene is unloaded whenever a match runs, so everything the menu held privately
died with it, and *Return to lobby* came back to the host-or-join screen with the session still
running underneath and the join code gone.

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
| Ending a round | **Last one standing**, or the most life left when the clock runs out | Both were already implied by the rules asset; a shared top value at the clock is reported as a draw rather than broken by client id, because `MaxLife` makes an exact tie reachable. The clock is three minutes by default and the host can move it — see below. |
| A player who is out | **Despawned** — and free to pan the camera around the arena | The character was hidden rather than despawned for three phases, because despawning it took the roster entry, the life and the connection's own player object with it. `ps-2` moved the first onto `PlayerSession`, `ps-3` the second, and `ps-4` pointed `NetworkConfig.PlayerPrefab` at the session so the third stopped being true. There is nothing left to preserve, so the body goes. |
| Character selection | 4 skins, mechanically identical | Ships in Phase 2: the chosen character rides in the **same connection-approval payload** as the nickname, so it reinforces that system instead of adding one. |

## Who decides a round is over

`MatchDirector` owns the phase and the arena. `RoundReferee` owns the rules. They are separate
because they change for different reasons: adding a game mode rewrites the referee and leaves scene
loading alone, and adding an arena does the reverse.

The referee replicates a **verdict** — a winner and a reason — not the ingredients it used. A client
holding the raw life values could reach a different conclusion from the same numbers and disagree
about who won; a client holding the verdict cannot.

Two things follow from a player being out:

- Their character is despawned, which is what stops them simulating, being solid and being stompable
  — three checks that used to read a flag and are now questions nobody asks, because there is no
  object left to ask them about.
- Their owner gets the camera, which still reads the replicated `IsAlive` flag on their session.

The flag is still replicated rather than derived from the number, and that still matters: life is
*interpolated* on clients — it drains locally between the server's once-a-second updates and can
reach zero a fraction early. Deriving death from that number would hand a player's owner a spectator
camera while the server still considers them alive.

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

**Match rules hang off `MatchDirector`**, not off each thing that reads them. The referee and every
player used to hold their own reference to the same asset, which is three chances for one of them
to point somewhere else and no way to see from the Inspector that it had. Each keeps its own field
as a fallback for a scene with no director. That is also what lets the sandbox run under its own
rules — drain at zero, round at zero, which the referee reads as *no clock* rather than as a round
that ends immediately — without a second player prefab to carry the difference.

**The rules a session is playing by are replicated, and the asset is only where they start.** A
`MatchSettings` struct rides a server-written `NetworkVariable` on the director; the host picks a
preset from `DifficultyCatalog` or edits any number in the lobby, and the server clamps whatever
arrives before publishing it. See ADR D-005.

It has to replicate rather than merely be applied server-side, and the reason is worth stating
because the ADR originally got it wrong. Two of the five numbers are read *by clients*:
`DrainPerSecond` is what a client counts its own life down with between the server's once-a-second
updates, and `MaxLife` is the denominator of every life bar drawn. A host lowering either against
clients still holding the old asset would have every other screen emptying at a different rate,
disagreeing about numbers that decide the match.

Nothing `Simulate()` reads is in there. Movement is executed identically on both sides of the wire,
and a divergence there produces a trembling character whose symptom points at reconciliation, which
is not where the bug would be. Tuning is for the rules, not for the physics.

## Art has a size; it is not scaled to one

**Pixel art is sized through its import settings, never by scaling a transform.** Characters import
at 32 pixels per unit, so a 32×32 sprite is exactly one world unit; fruit at 64, so a pickup is half
of one. Both prefabs sit at scale 1.

This is not tidiness. A scaled parent multiplies everything underneath it, and the life bar over the
character is authored in world units — it silently came out three times its size, and then, once
that was compensated for, a *second* conversion shrank it to a sliver. Numbers that mean what they
say are what makes that class of bug impossible rather than merely fixed.

The arena keeps its scaling and should: it is a white square standing in for geometry, and
stretching it into a platform is the entire point. The rule is about art with pixels in it.

> **World-space UI has its own conversion.** `PanelSettings.pixelsPerUnit` already turns a panel's
> authored pixels into metres, so scaling its transform on top applies a second one. `PlayerNameplate`
> keeps its transform at 1 and drives the document's pixel size from a width in metres instead.

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
`RoundReferee.Current`, `ConnectionApproval.Current`, `SessionRoster.Current`, `ArenaBounds.Current`,
`NetworkSimulationLoop.Instance`, plus the `PlayerLife.All`, `PlayerSession.All` and
`NetworkSimulationLoop.ActivePlayers` registries and two debug toggles.

This is deliberate and it is the pattern the project uses instead of a container. Two reasons, and
both are specific rather than stylistic:

- **Spawn order is not something a scene can promise.** These objects come up as networked spawns, so
  a serialized reference would be null exactly as often as it would be useful.
- **Each peer is its own process, including under Multiplayer Play Mode.** A static is per-process,
  which is the correct scope for "the match director in *this* session" — it is not shared state
  between peers.

That second reason holds for the shipped game and stops holding under the PlayMode harness, which
runs a host and its clients as several `NetworkManager`s inside one editor process. There, every one
of these statics is shared by peers that are supposed to disagree: the last one to spawn wins
`Current`, and `PlayerLife.All` holds the players of both sides at once. It is not a bug in the game
— a player's machine only ever runs one peer — but it is the reason a networked test reads
`NetworkManager.SpawnManager`, which belongs to a peer, instead of a registry that does not.

One of these has since been narrowed rather than accepted. `PlayerSession.Of` takes the peer to look
in, because game code — not just tests — now reaches across objects to find a player: fruit resolves
a character to its owner's session and writes to it. Reading the wrong peer's copy is a wrong answer;
*writing* to it is a permission error NGO logs and nobody reads. `SessionRoster` filters the same way
for the same reason. Where a static is only ever read by the peer that owns it, it stays a static.

The costs are real and worth naming rather than hiding. Every consumer null-checks, so forgetting one
is a `NullReferenceException` during a scene transition; and anything reading one of these cannot be
exercised in an EditMode test — nor, per the paragraph above, told apart between two peers in a
PlayMode one — which is visibly why the test suite covers `Simulation` and `Netcode` and almost
nothing in `Gameplay/Match`. **A DI container would not fix either problem** — it would
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
Snackdown.Tests.PlayMode  →  Gameplay, Connection                          (Play mode)
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

**It caught a second one later, and the same answer worked.** `SessionRoster` shipped in
`Snackdown.Connection` because a lobby list feels like a connection concern. It is not: it is a list
of players, and the moment `PlayerSession` became the thing that holds a player's name, the roster
had to be able to name that type — which `Connection` cannot, since `Gameplay` depends on it and not
the other way round. The roster moved to `Gameplay/Player`, where it was always describing something
that lives. Nothing was abstracted to make it fit; the file was in the wrong folder.
