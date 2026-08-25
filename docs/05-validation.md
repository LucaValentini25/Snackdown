# 05 — Validating the netcode

Phase 1 built prediction, reconciliation and interpolation. This is how they were shown to work,
and what the numbers were. Everything here is reproducible: the procedure is below, and so is what
went wrong the first four times.

## The procedure

Two peers on one machine, via **Multiplayer Play Mode**. The editor runs the host; a virtual player
runs the client. Only a predicting client reconciles, so **only the client produces measurements** —
pressing `F4` on the host correctly reports that there is nothing to export.

1. **Enable a virtual player** — `Window > Multiplayer > Multiplayer Play Mode`, activate Player 2.
   It clones the project once, which takes a few minutes and several GB.
2. **Editor:** enter Play, start **Host**, then apply a network profile (see below).
3. **Player 2:** enter Play, press **Client**.
4. Play for **60–90 seconds**, moving deliberately: hard stops, direction reversals, jumps into
   walls. Uniform motion barely disagrees with the server and produces almost no data.
5. Press **`F4`** on the client. The run is written to
   `%USERPROFILE%/AppData/LocalLow/DefaultCompany/Snackdown/metrics/run-<timestamp>.csv`
   and a fresh run starts immediately, so scenarios can be chained without reconnecting.

Applying a profile from the editor's console:

```csharp
var sim = FindFirstObjectByType<NetworkSimulator>();
sim.ConnectionPreset = NetworkSimulatorPresets.Mobile2G;          // a built-in profile
// or a custom one:
sim.ConnectionPreset = NetworkSimulatorPreset.Create(
    "B-stress", "150ms / 50ms jitter / 20% loss", 150, 50, 0, 20);
```

### Driving the virtual players from the MCP

The steps above are the manual route. All of them can also be driven from the MCP's `execute_code`
tool, which is what makes a two-peer run testable during QA instead of only by hand: the players can
be listed, activated, tagged and inspected without leaving the editor.

**The API is internal.** In 6000.3.14f1 Multiplayer Play Mode ships *inside* the editor, in
`UnityEditor.MultiplayerModule` — `com.unity.multiplayer.playmode` 2.0.2 is a documentation shell
with no assemblies of its own. Every type below is `internal`; the single public one is
`Unity.Multiplayer.PlayMode.CurrentPlayer`, which a clone uses to read its own tags at runtime.
Two consequences, both of which cost time to rediscover:

- It goes through **reflection**. The `unity_reflect` MCP tool will not find any of it, because that
  tool only scans public types — it reports zero hits for `MultiplayerPlaymode` and looks like the
  API does not exist.
- `execute_code` compiles with CodeDom, which is **C# 6**: no `out var`, no local functions, no
  switch expressions, and `Object` must be qualified as `UnityEngine.Object`.

What is reachable, all under `Unity.Multiplayer.PlayMode.Editor` (verified 2026-08-09):

| Type | What it gives |
|---|---|
| `MultiplayerPlaymode` | `Players`, `PlayerOne`–`PlayerFour`, `PlayerTags` |
| `UnityPlayer` | `Activate`, `Deactivate`, `AddTag`, `RemoveTag`, `ClearTags`, `Tags`, `PlayerState`, `Name`, `Type`, `TypeDependentPlayerInfo` |
| `ScenarioRunner` | `StartScenario`, `StopScenario`, `GetScenarioStatus`, `ActiveScenario`, `IsRunning` |
| `MultiplayerPlaymodeLogUtility` | `PlayerLogs(PlayerIdentifier)` — **counts only** |
| `VirtualProjectsEditor` | `IsClone`, `CloneIdentifier`, `MainEditorProcessId` |

Reading the state of every player:

```csharp
System.Type mpp = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{
    mpp = a.GetType("Unity.Multiplayer.PlayMode.Editor.MultiplayerPlaymode", false);
    if (mpp != null) break;
}
var sflags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
var iflags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
           | System.Reflection.BindingFlags.Instance;

var sb = new System.Text.StringBuilder();
foreach (var p in (System.Collections.IEnumerable)mpp.GetProperty("Players", sflags).GetValue(null, null))
{
    var t = p.GetType();
    sb.AppendLine(t.GetProperty("Name", iflags).GetValue(p, null)
        + " | " + t.GetProperty("Type", iflags).GetValue(p, null)
        + " | " + t.GetProperty("PlayerState", iflags).GetValue(p, null));
}
return sb.ToString();
```

The mutating members return `bool` and report the reason through an **out parameter**, so under
reflection they take an `object[]` whose trailing slot comes back filled:

```csharp
object player = mpp.GetProperty("PlayerThree", sflags).GetValue(null, null);
object[] args = new object[] { "predicting-client", null };   // tag, out TagError
bool ok = (bool)player.GetType().GetMethod("AddTag", iflags).Invoke(player, args);
// args[1] holds the TagError when ok is false
```

`Activate` and `Deactivate` follow the same shape (`Activate` also takes a `List<string>` of extra
launch arguments). They start and stop a full editor process, so they are slow and not something to
call speculatively — and the first activation of a player clones the whole project.

**Reading a clone's console needs the log file, not `read_console`.** The MCP bridge lives in the
main editor, so `read_console` only ever returns the main editor's messages, and
`MultiplayerPlaymodeLogUtility.PlayerLogs` returns a `LogCounts` — the number of logs, warnings and
errors, not their text. The messages themselves are on disk, under the clone's own identifier:

```
Library/VP/<VirtualProjectIdentifier>/Logs/Editor.log
```

which is reachable from the player object as
`player.TypeDependentPlayerInfo.VirtualProjectIdentifier`.

> **This is internal API and can break on any editor upgrade**, without a changelog entry, because
> Unity owes no compatibility on it. That is an acceptable trade for a QA aid — it is not acceptable
> for anything the game itself depends on. Code that touches it belongs in the editor-only tooling
> path, and must fail into a readable message rather than a `NullReferenceException` when a member
> moves.

### The sandbox scene

`_Project/Scenes/Sandbox.unity` starts a host and drives the match to *Playing* the moment Play is
pressed — no menu, no lobby, no network service. It is for looking at art and layout on a character
**spawned the way NGO spawns it**: a harness that instantiated the prefab directly would be quicker
and would have hidden every sizing bug this was built to find, since all of them were only visible
on the spawned copy.

It is a copy of Bootstrap rather than a hand-built scene, so it cannot drift from the one that
ships, and it runs under `SandboxMatchConfig` — no life drain, no round clock — so nothing expires
while you are looking at it. It shuts the host down on disable, because leaving one running leaks
the socket and costs an editor restart.

**One peer only.** Anything that crosses the wire still needs two; see the two-peer rule in
[CLAUDE.md](../CLAUDE.md).

### Pitfalls that invalidate a run

Each of these produced numbers that looked real and measured something else entirely.

| Symptom | Cause | Fix |
|---|---|---|
| Stutter that follows whichever window you are *not* using | `runInBackground` off — Unity throttles an unfocused window, so the other peer stops ticking | Now on. See [ProjectSettings]. **Standalone only:** it is a desktop setting, so on Android, iOS or WebGL the underlying problem is unmitigated — a suspended or backgrounded host stops ticking and every client starves. Relevant the day a non-PC build exists; see [03 — Roadmap](03-roadmap.md). |
| Correction rate ~65× too high; host reports starved ticks | Two uncapped renderers saturating one CPU, so the client never gets scheduled to send input | `FrameRatePolicy` caps to 60. |
| `# conditions` says "no impairment" while RTT says 500 ms | The CSV is written by the client and reads its *local* simulator; impairment applied on the host is invisible to it | Read `mean_rtt_measured_ms`, not the label. |
| First correction is several units, ~0.2 s in | The spawn placement teleport, counted as a prediction failure | Was `PlayerSnapshot.IsTeleport`, a flag announcing that the server had moved the character on purpose. **Gone since `ps-4`:** a round now spawns a character *at* its spawn point instead of moving one that already existed, so there is no reposition to mistake for a failure and no flag on the wire. A run recorded before that commit still shows the old symptom. |
| `Failed to bind UDP socket` on the next run | Code recompiled while a session was live; the native socket leaks until the process exits | Never edit scripts with a session running. Restart the editor. |
| `NetworkConfig mismatch`, surfacing through Sessions as an unrelated metadata error | Two peers disagreeing on one of the seven values NGO hashes — in practice a prefab whose `GlobalObjectIdHash` on disk differs from the one the editor computes | Each peer logs those values on connect (`NetworkConfigReport`); diff the two. Re-save the prefab so the file carries the computed hash. |
| The MCP bridge drops every time Play mode is entered from a tool call | The domain reload takes the WebSocket with it | Drive Play mode by hand. Measure through `execute_code` once it is already running. |

**Do not change code while a session is live.** It ends the run, leaks the socket, and costs an
editor restart.

## Network profiles

Unity's built-in presets are calibrated against real connections, which makes them worth more than
invented numbers. The worst real profile Unity models — `Mobile 2G` — has **7 % packet loss**.

| Profile | Delay | Jitter | Loss |
|---|---|---|---|
| Home Fiber | 10 ms | 1 ms | 0 % |
| Home Broadband (WiFi / cable) | 32 ms | 12 ms | 2 % |
| Home Broadband, congested | 50 ms | 50 ms | 1 % |
| Mobile 4G (LTE) | 100 ms | 20 ms | 4 % |
| Mobile 3G | 360 ms | 30 ms | 7 % |
| Mobile 2G | 520 ms | 50 ms | 7 % |

## Results

Two sets, and both are kept. The 2026-08-06 runs are what Phase 1 was signed off on. The 2026-08-25
runs were taken after `Reconcile` was extracted into `Reconciler` (`vf-2`), because a refactor of the
headline mechanism is exactly the change a unit test can miss.

### 2026-08-25 — after the reconciler extraction

Measured 30 Hz tick, `MoveSpeed` 7 u/s, two peers via Multiplayer Play Mode.

| | **A — home network** | **B — stress test** |
|---|---|---|
| Profile | 150 ms / 20 ms / **2 %** | 150 ms / 50 ms / **20 %** |
| Duration | 91.8 s | 122.1 s |
| Corrections | 9 | 140 |
| **Corrections / s** | 0.098 | **1.146** |
| **Median error** | 0.472 u *(n=9)* | **0.407 u** |
| Mean error | 1.332 u | 0.670 u |
| p95 error | — *(n=9)* | 2.624 u |
| Max error | 3.233 u | 3.733 u |
| Median replayed ticks | — | 13 |
| Max replayed ticks | 23 | 39 |
| Median measured RTT | 411 ms | 433 ms |
| Corrections of exactly one tick | — | **29 %** |
| Corrections of one tick or less | — | 36 % |

**Did the extraction change behaviour?** The honest answer is *not detectably, and the comparison is
weaker than it looks*. Scenario B is the only one with enough corrections to compare: its median
error went from **0.302 u to 0.407 u** — but the median measured RTT went from **367 ms to 433 ms**
in the same runs, and median replayed ticks from 11 to 13. A client that has predicted further ahead
disagrees by more. The two runs were not taken under matched round trips, and round trip is not
something the profile sets directly.

What did *not* move is the shape, and that is the part that tests the claim. The share of corrections
worth exactly one tick of movement is **27 % before and 29 % after**; one tick or less, 39 % and
36 %. The typical disagreement is still one missing input rather than a diverging simulation, which
is what [02 — Netcode](02-netcode.md) argues and what a broken replay would have destroyed.

> **Still open.** A run at matched RTT would settle it. Until one exists, this says the mechanism
> behaves the same in shape and that the level moved with conditions — not that the level is
> unchanged.

One thing got worse and is worth naming: corrections that replayed **zero** ticks went from 10 of 98
to 28 of 140 — from 10 % to 20 %. That is the open item below, still undiagnosed, and it is now twice
as common.

### 2026-08-06 — as Phase 1 shipped

| | **A — home network** | **B — stress test** | **C — worst real case** |
|---|---|---|---|
| Profile | 150 ms / 20 ms / **2 %** | 150 ms / 50 ms / **20 %** | Mobile 2G — 520 ms / 50 ms / 7 % |
| Duration | 78.9 s | 85.6 s | not recorded |
| Corrections | 2 | 98 | — |
| **Corrections / s** | **0.025** | **1.144** | — |
| Median error | — *(n=2)* | **0.302 u** | — |
| Mean error | — *(n=2)* | 0.525 u | — |
| p95 error | — *(n=2)* | 2.100 u | — |
| Max error | 1.87 u | 3.883 u | — |
| Median replayed ticks | 11 | 11 | — |
| Max replayed ticks | 12 | 27 | — |
| Median measured RTT | 367 ms | 367 ms | — |

**Scenario B carries the argument.** 20 % packet loss is nearly three times worse than the worst
real profile Unity models, and under it the typical disagreement between client and server was
**0.3 units — about a third of the character's width.** The 2026-08-25 repeat put it at 0.407 u at a
higher round trip; either way it is a fraction of a character.

In scenario A the two corrections happened at 1.7 s and 9.3 s. The remaining **69 seconds ran
without a single correction** at 367 ms round trip.

> Report the **median**, not the mean. The distribution has a long tail — 10 % of corrections exceed
> 1 unit and drag the average up. With n=2, scenario A has no meaningful average at all.

### The shape of the errors

The most common correction is not noise. At `MoveSpeed` 7 over a 30 Hz tick, one tick of movement is
**7 ÷ 30 = 0.2333 units**, and in scenario B:

- **27 %** of corrections were *exactly* 0.2333 u (within 0.002)
- **39 %** were one tick of movement or less

So the typical disagreement is **one missing input**, not a diverging simulation. When both sides
process the same commands they reach the same state; corrections come from commands the server never
received, and it repeated the previous one instead. That is the determinism claim in
[02 — Netcode](02-netcode.md) holding up under measurement.

### Scenario C — observed, not measured

The `F4` export was missed, so this is a play-tester's report rather than data:

> *"Había tirones y elasticidad para compensar, pero era manejable."*

Which matches what the design predicts, and is worth stating because the two halves degrade
differently:

- **Prediction degrades gracefully.** It never waits for the network, so the local character stays
  responsive at 520 ms. The "elasticity" *is* reconciliation working — corrections landing and being
  absorbed.
- **Interpolation does not.** It buffers 100 ms of snapshots, so at 520 ms of delay with 50 ms of
  jitter it runs dry and remote characters visibly stutter.

That asymmetry is the honest limit of the current design: the player you control survives a terrible
connection; the players you watch do not.

## Bandwidth

Measured 2026-08-25 from the host, with the Unity Profiler's network counters, over the frames of a
running match. **Two players — the host and one client.**

Every byte figure in this project used to be derived by reading the serializers. These are counted.

| Counter | A — 2 % loss | B — 20 % loss |
|---|---|---|
| **Total bytes sent** | 3 657 B/s | 3 669 B/s |
| Total bytes received | 1 393 B/s | 1 026 B/s |
| of which RPC sent — the snapshots | 3 001 B/s | 3 002 B/s |
| of which `NetworkVariable` sent | 21 B/s | 21 B/s |

**Outgoing is identical under both profiles, and that is the expected answer.** Snapshots go out once
per tick whether or not the last one arrived; packet loss is something that happens to them
afterwards. Incoming falls from 1 393 to 1 026 B/s — a factor of 0.74 against the 0.8 that 20 % loss
on the client's input would predict. The two measurements confirm each other, which is the main
reason to trust either.

**The derived figure was close, and short by the framing.** [`SnapshotFrame`](../Assets/_Project/Scripts/Netcode/SnapshotFrame.cs)
computes `8 + 41N` bytes of payload; at two players that is 90 bytes, and at 30 Hz, 2 700 B/s. The
measured RPC traffic is 3 001 B/s — about **11 bytes per snapshot more**, which is NGO's own RPC
metadata. Reading the serializers was honest arithmetic about the payload and silent about the
envelope.

`NetworkVariable` traffic is **21 B/s**: the life timer at roughly 1 Hz per player, plus the phase,
the roster and the match settings, which move only when somebody changes them. The design decision
in [02 — Netcode](02-netcode.md) to publish life on an interval rather than every frame is what keeps
that number two orders of magnitude below the snapshots.

> **Two players, not four.** Extrapolating the payload to four gives 172 bytes plus framing, sent to
> each of three clients — roughly 16 KB/s out of the host. That is arithmetic, not a measurement, and
> is exactly the kind of claim this section exists to stop making.

## Open items

- **Scenario C has no recorded run.** Repeat it and export.
- **No run at four players.** Everything measured so far is two peers on one machine. The bandwidth
  figures above extrapolate to four by arithmetic, which is what the rest of this section exists to
  replace.
- **The two scenario B runs were not taken at matched RTT**, so whether the reconciler extraction
  moved the error level is still open. See the note under Results.
- **Corrections that replay zero ticks**, meaning the server was level with or ahead of the client's
  prediction. NGO deliberately runs the client clock ahead so this should not happen; these are
  moments where the client fell behind. Ten of 98 in the 2026-08-06 run, **28 of 140** in the
  2026-08-25 repeat — twice as common, and still not diagnosed.
- **`UnityTransport.GetCurrentRtt` is not usable here** and is recorded only for contrast. It reads
  `RttInfo.LastRtt` off the *reliable sequenced* pipeline, while every packet this project sends is
  deliberately unreliable. It reported 218 ms on an idle localhost connection and 1219 ms during a
  run whose real round trip was 510 ms. `LastMeasuredRttMs` — derived from how far prediction has
  run past the server's acknowledged tick — is the figure to trust.
