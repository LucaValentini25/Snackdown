namespace Snackdown.Gameplay.Match
{
    /// <summary>Where a session is in the arc from gathering players to showing a winner.</summary>
    /// <remarks>
    /// <para>One value, owned by the server, replicated to everyone. Every system that behaves
    /// differently before and during a match reads this instead of keeping its own flag — the life
    /// timer, the fruit spawner and the input path all need to know, and three private booleans
    /// answering the same question is how they end up disagreeing.</para>
    /// <para><see cref="Loading"/> exists because scene loading is not instantaneous across a
    /// network. Players finish loading at different times, and starting the countdown when the
    /// <i>host</i> is ready would give it a head start measured in whole seconds.</para>
    /// </remarks>
    public enum MatchPhase : byte
    {
        /// <summary>Gathering players. The arena is not loaded yet.</summary>
        Lobby = 0,

        /// <summary>Everyone is loading the arena. Nobody moves until all of them arrive.</summary>
        Loading = 1,

        /// <summary>Arena loaded, players placed, counting down. Input is ignored.</summary>
        Countdown = 2,

        /// <summary>The match is running.</summary>
        Playing = 3,

        /// <summary>Someone won. Showing the result before returning to the lobby.</summary>
        Ended = 4
    }
}
