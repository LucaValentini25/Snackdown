using Unity.Netcode;

namespace Snackdown.Netcode
{
    /// <summary>
    /// What <see cref="NetworkSimulationLoop"/> needs a character to be able to do, so the loop can
    /// order the tick without knowing what kind of character it is ordering.
    /// </summary>
    /// <remarks>
    /// <para>This interface exists to break a dependency cycle, and the cycle was invisible until
    /// assembly definitions made the compiler check it. The loop iterated the concrete gameplay
    /// character while that same character used the netcode layer's buffer and interpolator — so
    /// each layer imported the other. That is legal inside one assembly and impossible across two,
    /// which is the entire argument for defining assemblies early: the rule in
    /// <c>docs/01</c> was stated from the start and quietly broken anyway.</para>
    /// <para>Kept deliberately narrow. It carries the six members the tick loop calls and nothing
    /// else — not the debug telemetry, which belongs to whoever is being debugged rather than to the
    /// contract for being simulated.</para>
    /// </remarks>
    public interface IPredictedPeer
    {
        /// <summary>True on the peer whose player controls this character.</summary>
        bool IsOwner { get; }

        /// <summary>True on the authority. A host is both this and <see cref="IsOwner"/>.</summary>
        bool IsServer { get; }

        /// <summary>Owning clients only: sample input, simulate immediately, remember both.</summary>
        void OwnerPredictTick(uint tick);

        /// <summary>The authoritative step. Whatever this produces is true by definition.</summary>
        void ServerSimulateTick(uint serverTick);

        /// <summary>This character's authoritative state, for the frame going out this tick.</summary>
        PlayerSnapshot BuildSnapshot();

        /// <summary>Reconcile against it if we own it; interpolate toward it if we do not.</summary>
        /// <param name="serverTick">
        /// The tick the frame describes. Carried as well as <paramref name="snapshotTime"/> because
        /// the two answer different questions: the time lines remote characters up against the
        /// render clock, and the tick is what says how far a state has to be carried forward to
        /// reach the one being predicted. Deriving one from the other at each call would be the same
        /// division written in two places, waiting to disagree about rounding.
        /// </param>
        void ApplySnapshot(in PlayerSnapshot snapshot, double snapshotTime, uint serverTick);
    }
}
