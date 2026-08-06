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

### Pitfalls that invalidate a run

Each of these produced numbers that looked real and measured something else entirely.

| Symptom | Cause | Fix |
|---|---|---|
| Stutter that follows whichever window you are *not* using | `runInBackground` off — Unity throttles an unfocused window, so the other peer stops ticking | Now on. See [ProjectSettings]. |
| Correction rate ~65× too high; host reports starved ticks | Two uncapped renderers saturating one CPU, so the client never gets scheduled to send input | `FrameRatePolicy` caps to 60. |
| `# conditions` says "no impairment" while RTT says 500 ms | The CSV is written by the client and reads its *local* simulator; impairment applied on the host is invisible to it | Read `mean_rtt_measured_ms`, not the label. |
| First correction is several units, ~0.2 s in | The spawn placement teleport, counted as a prediction failure | `PlayerSnapshot.IsTeleport`. |
| `Failed to bind UDP socket` on the next run | Code recompiled while a session was live; the native socket leaks until the process exits | Never edit scripts with a session running. Restart the editor. |

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

Measured 2026-08-06, 30 Hz tick, `MoveSpeed` 7 u/s.

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
**0.3 units — about a third of the character's width.**

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

## Open items

- **Scenario C has no recorded run.** Repeat it and export.
- **Ten corrections in B replayed zero ticks**, meaning the server was level with or ahead of the
  client's prediction. NGO deliberately runs the client clock ahead so this should not happen; these
  are moments where the client fell behind. Not diagnosed.
- **`UnityTransport.GetCurrentRtt` is not usable here** and is recorded only for contrast. It reads
  `RttInfo.LastRtt` off the *reliable sequenced* pipeline, while every packet this project sends is
  deliberately unreliable. It reported 218 ms on an idle localhost connection and 1219 ms during a
  run whose real round trip was 510 ms. `LastMeasuredRttMs` — derived from how far prediction has
  run past the server's acknowledged tick — is the figure to trust.
