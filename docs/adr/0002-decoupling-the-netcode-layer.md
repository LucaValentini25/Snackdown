# ADR 0002 — Decoupling the netcode layer from gameplay

**Status:** Proposed — awaiting a decision
**Date:** 2026-08-06
**Supersedes:** nothing. **Blocks:** the assembly split, and the "reusable netcode core" claim in the README.

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

Until this is fixed, **"a reusable netcode core" in the README is false**, and the assembly split
that would let the compiler prove otherwise cannot be done — an assembly definition for `Netcode`
would need a reference to `Gameplay`, which is the exact thing it is meant to forbid.

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

## Open question for the decision

Option 3 is recommended. The remaining judgement call is **how far to take the abstraction**: whether
`IPredictedPeer` is worth defining at all, or whether the loop should simply hold an abstract
`protected abstract void TickPeers(uint localTick, uint serverTick)` and let the concrete subclass
iterate its own concrete list. The second is less code and less ceremony; the first keeps the
per-tick phase ordering — the thing `NetworkSimulationLoop` exists to guarantee — inside the reusable
layer instead of duplicating it per game.
