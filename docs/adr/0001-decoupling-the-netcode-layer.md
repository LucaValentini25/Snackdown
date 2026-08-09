# ADR 0001 — Decoupling the netcode layer from gameplay

**Status:** **Rejected** — 2026-08-06. The decoupling is not being done; the claim it was meant to
support was removed instead. See [Decision](#decision) at the end.
**Date:** 2026-08-06
**Supersedes:** nothing. **Consequence:** [01 — Architecture](../01-architecture.md) no longer
describes the netcode layer as a reusable core.
**Amended:** 2026-08-09 — one premise in *Context* was overtaken by the code two commits later. See
[Superseded in practice](#superseded-in-practice) at the end. The decision itself still stands.

## Context

[01 — Architecture](../01-architecture.md) states the rule the layering exists to enforce: *lower
layers never know about higher ones.* The netcode layer breaks it, systematically:

| File in `Netcode/` | Depends on |
|---|---|
| `PredictionBuffer.cs` | `PlayerState`, `InputCommand` |
| `SnapshotFrame.cs` | `PlayerState` |
| `SnapshotInterpolator.cs` | `PlayerState` — and interpolates its concrete fields |
| `NetworkSimulationLoop.cs` | `PredictedPlayer` |

`ReconciliationStats`, `RunRecorder` and `VisualSmoother` are already clean.

Until this is fixed, **the "reusable core" description in docs/01 is false**, and the assembly split
that would let the compiler prove otherwise cannot be done — an assembly definition for `Netcode`
would need a reference to `Gameplay`, which is the exact thing it is meant to forbid.

> **Amendment, 2026-08-09.** The second half of that sentence turned out to be wrong: the split *was*
> done, two commits later, by a route this document does not list. See
> [Superseded in practice](#superseded-in-practice). The first half — that the reusable-core claim
> was false — stands, and is what the decision below acts on.

The surface is smaller than it looks. `NetworkSimulationLoop` touches only five members of
`PredictedPlayer`: `IsOwner`, `IsServer`, `OwnerPredictTick`, `ServerSimulateTick`, `BuildSnapshot`
and `ApplySnapshot`.

## The constraint that decides this

Both candidate designs were going to be argued on taste. One of them turns out to be **impossible**,
and it is worth recording how that was established rather than asserted.

A throwaway probe was compiled against NGO 2.11: a generic `NetworkBehaviour` declaring an `[Rpc]`
whose parameter is a struct closed over the class's own generic parameter.

```csharp
public abstract class ProbeLoop<TState> : NetworkBehaviour where TState : struct, INetworkSerializable
{
    [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
    protected void ProbeRpc(ProbeFrame<TState> frame) { }
}
```

This does not fail to work. It fails to **compile**, and not with a diagnostic:

```
Unity.Netcode.Editor.CodeGen.NetworkBehaviourILPP: (0,0): error
  System.NullReferenceException: Object reference not set to an instance of an object.
    at Mono.Cecil.ImportGenericContext.TypeParameter(String type, Int32 position)
    at NetworkBehaviourILPP.GetWriteMethodForParameter(TypeReference paramType, ...)
    at NetworkBehaviourILPP.InjectWriteAndCallBlocks(...)
```

NGO's IL post-processor cannot resolve a serializer for an open generic parameter. The codebase does
contain `GenerateSerializationForGenericParameterAttribute`, but its own documentation scopes it:
*"primarily intended to support subtypes of `NetworkVariableBase`"* — it addresses `NetworkVariable`,
not RPC parameters. `NetworkBehaviourILPP.cs:1628` separately rejects generic RPC **methods** with a
real message: `"RPC method must not be generic!"`.

A second probe established the boundary precisely. Generic base class holding the logic and
declaring **no** RPC, with the RPC on a concrete subclass where every type is closed:

```csharp
public abstract class ProbeLoop<TState> : NetworkBehaviour where TState : struct, INetworkSerializable
{
    protected abstract void Publish(ProbeFrame<TState> frame);   // no Rpc here
}

public class ProbeLoopConcrete : ProbeLoop<ProbeState>
{
    protected override void Publish(ProbeFrame<ProbeState> frame) => ProbeRpc(frame);

    [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
    void ProbeRpc(ProbeFrame<ProbeState> frame) { }              // fully closed
}
```

This compiles, and reflection confirms codegen actually ran — the injected handler
`__rpc_handler_3374773038` is present on the concrete type.

**So: generics are free anywhere that does not cross the wire, and forbidden at the RPC boundary
itself.** That is not a preference. It is the shape the tooling allows.

## Options

### Option 1 — Generics everywhere

`PredictionBuffer<TState, TInput>`, `SnapshotInterpolator<TState>`, `SnapshotFrame<TState>`,
`NetworkSimulationLoop<TState, TInput>` with its `[Rpc]` in place.

**Rejected.** The probe shows it does not compile. Included because it was the obvious first idea
and someone reading this later will have it too.

### Option 2 — Interfaces everywhere

Netcode defines `ISimulationState`, `ISimulationInput`, `ISimulatedPeer`; gameplay implements them.
No generic parameters anywhere.

- **For:** conceptually simple, no interaction with codegen, one mechanism throughout.
- **Against:** `PlayerState` and `InputCommand` are **structs**, and every use through an interface
  boxes them. Reconciliation replay is the hottest path in the project — a single correction replays
  up to 27 ticks, and measurement shows corrections running at 1.14/s under load. Boxing there
  allocates garbage in exactly the loop that must stay predictable. Making them classes instead
  would mean reference semantics in a buffer whose whole job is storing independent snapshots of
  past state, which is a far worse trade.

### Option 3 — Generics for data, an interface at the wire *(recommended)*

Split by whether the type crosses the network, because that is where the tooling draws its line.

**Stays in `Netcode`, generic, no gameplay types:**

```csharp
public class PredictionBuffer<TState, TInput>
    where TState : struct
    where TInput : struct

public class SnapshotInterpolator<TState>
    where TState : struct, IInterpolatable<TState>

public interface IInterpolatable<TState> { TState InterpolateTo(in TState target, float t); }
```

`SnapshotInterpolator` today interpolates `Position`, `Velocity` and `Grounded` by name. Only the
state itself knows which of its fields are continuous and which must not be blended, so that
knowledge moves onto the state — where it belongs — behind `IInterpolatable`.

**Stays in `Netcode`, abstract, declares no RPC:**

```csharp
public abstract class NetworkSimulationLoop<TState, TInput> : NetworkBehaviour
{
    protected abstract void PublishSnapshot(uint tick);   // subclass owns the Rpc
}

public interface IPredictedPeer<TState, TInput>
{
    bool IsOwner { get; }
    bool IsServer { get; }
    void OwnerPredictTick(uint tick);
    void ServerSimulateTick(uint tick);
    // ...
}
```

**Moves to `Gameplay`** — the concrete wire format and the loop that publishes it:

```csharp
public struct PlayerSnapshotFrame : INetworkSerializable { /* PlayerState, closed */ }

public class PlayerSimulationLoop : NetworkSimulationLoop<PlayerState, InputCommand>
{
    protected override void PublishSnapshot(uint tick) => SnapshotRpc(BuildFrame(tick));

    [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
    void SnapshotRpc(PlayerSnapshotFrame frame) { /* ... */ }
}
```

- **For:** no boxing on the hot path; the compiler enforces the layering once assemblies exist;
  matches what the tooling actually supports.
- **Against:** two mechanisms instead of one, and it concedes that the *wire format* is
  game-specific rather than reusable. Which is arguably honest — `PlayerSnapshot` carries
  `IsTeleport` and a player's position; a genuinely reusable core has no opinion about either.

## Consequences if Option 3 is taken

- `SnapshotFrame.cs` leaves `Netcode/`. The layer keeps the *machinery*, gameplay keeps the *format*.
- `PredictedPlayer` keeps its own `SubmitInputRpc` — already concrete, already fine.
- The assembly split becomes possible, which is the point: `Snackdown.Netcode.asmdef` with no
  reference to gameplay, and the compiler refusing anything that reintroduces one.
- `PlayerState` gains `IInterpolatable<PlayerState>`, moving the "how do I blend?" decision next to
  the fields it blends.
- Tests get easier for free: `PredictionBuffer<int, int>` needs no Unity types to exercise.
- **Risk:** this touches every file validated in [05 — Validation](../05-validation.md). It is a
  refactor of measured, working code. The recorded runs are the regression check — re-run scenario B
  afterwards and the correction rate and median error should land in the same range.

## Decision

**None of the options were taken. The requirement was withdrawn instead.**

Presented with option 3 and its consequence — that the wire format leaves the "reusable" layer and
moves into gameplay — the reaction was the right question: if the reusable part keeps shrinking to
preserve the label, what is the label buying?

Nothing, on inspection. The reuse was hypothetical. Nobody lifts a netcode core out of one game into
another without rewriting it around a different state type, a different input and a different wire
format, which is most of what is here. The refactor would have touched every file measured in
[05 — Validation](../05-validation.md) — risking working, verified code to make one sentence of
documentation true, when deleting the sentence costs nothing and is equally honest.

So `Netcode/` keeps importing `PlayerState` and `InputCommand`, as an accepted dependency rather than
as debt. [01 — Architecture](../01-architecture.md) was corrected to describe what the layer is —
this game's netcode, layered so gameplay depends on it and not the reverse — instead of what it was
aspiring to be.

**What survives this rejection:**

- **The layering rule itself.** Gameplay depends on netcode, never the reverse. Still holds, still
  enforced by convention, still worth keeping.
- **Tests.** `PlayerMotor.Simulate`, `PredictionBuffer` and `SnapshotInterpolator` are pure or nearly
  so and testable exactly as they stand today. They never needed this refactor — that was an
  assumption worth discarding early.
- **The NGO finding**, which outlives the decision that prompted it: an `[Rpc]` whose parameter is
  closed over its declaring class's generic parameter crashes the IL post-processor with a
  `NullReferenceException` instead of reporting a diagnostic. Anyone who reaches for a generic
  `NetworkBehaviour` here will hit it, and this is the only place it is written down.

Assembly definitions stay in Phase 5, justified by compile times and test isolation rather than by
proving a decoupling that is no longer a goal.

## Superseded in practice

*Added 2026-08-09, recording what actually happened rather than rewriting what was decided.*

The decision above stands: the netcode layer is not reusable, the claim was deleted instead of
earned, and `Netcode/` still imports `PlayerState` and `InputCommand` as an accepted dependency.

What did not stand is the premise in *Context* that the assembly split "cannot be done". It was done
two commits later, in **`e99a6fb`** — pulled forward from Phase 5 for exactly the justification named
in the last line above — by a **fourth option this document never considered**:

> Extract the shared *data* into its own leaf assembly (`Snackdown.Simulation`: `PlayerState`,
> `InputCommand`, `PlayerMotor`) that both `Netcode` and `Gameplay` reference, and put a narrow
> **non-generic** interface (`IPredictedPeer`, 6 members) at the one seam where the tick loop has to
> call into a character. `Snackdown.Netcode.asmdef` then compiles with no reference to `Gameplay`.

The three options weighed above all tried to make the *wire format* generic, which is what ran into
the IL post-processor. The fourth avoids the problem instead of solving it: nothing generic ever
reaches an `[Rpc]` parameter, so the constraint below never applies. It cost 40 lines and three
`is`-pattern downcasts at the consumers, and it made the `Netcode → Gameplay` edge a compile error
rather than a rule.

Two things worth keeping straight, because they are easy to conflate:

- **The layering rule is now enforced, not promised.** That is a stronger claim than this ADR
  expected to be able to make, and it is the one surviving structural benefit.
- **The layer is still not reusable, and that is still fine.** The split proves direction, not
  portability. Anyone reading `Snackdown.Netcode.asmdef` and inferring "this could be lifted into
  another game" is reading more into it than the compiler is checking.

**The NGO codegen finding recorded above outlives all of it** — the fourth option sidesteps that
constraint rather than disproving it, so the probe and its stack trace stay just as true. That
finding is why this document was marked *Rejected* instead of deleted.
