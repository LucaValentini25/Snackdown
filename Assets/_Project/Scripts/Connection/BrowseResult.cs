using System.Collections.Generic;

namespace Snackdown.Connection
{
    /// <summary>The outcome of asking a provider what there is to join.</summary>
    /// <remarks>
    /// <para>Shaped like <see cref="ConnectionResult"/>, and for the same reason: failure is a
    /// return value rather than an exception, because "the service is down" and "there are no games"
    /// are both ordinary things for a browser to show. Reusing
    /// <see cref="ConnectionFailure"/> keeps one vocabulary for everything that can go wrong
    /// between this game and another one.</para>
    /// <para>An empty list on success is not a failure and must not be rendered as one. Nobody
    /// hosting is the normal state of a game nobody is playing yet, and telling the player something
    /// went wrong would send them looking for a problem that is not there.</para>
    /// </remarks>
    public readonly struct BrowseResult
    {
        public readonly bool Success;
        public readonly ConnectionFailure Failure;

        /// <summary>What was found. Empty on failure, and possibly empty on success.</summary>
        public readonly IReadOnlyList<SessionListing> Sessions;

        /// <summary>Detail for the log. Never rendered to a player as-is.</summary>
        public readonly string Diagnostic;

        BrowseResult(bool success, ConnectionFailure failure, IReadOnlyList<SessionListing> sessions, string diagnostic)
        {
            Success = success;
            Failure = failure;
            Sessions = sessions ?? System.Array.Empty<SessionListing>();
            Diagnostic = diagnostic ?? string.Empty;
        }

        public static BrowseResult Ok(IReadOnlyList<SessionListing> sessions)
            => new BrowseResult(true, ConnectionFailure.None, sessions, string.Empty);

        public static BrowseResult Failed(ConnectionFailure failure, string diagnostic = "")
            => new BrowseResult(false, failure, null, diagnostic);

        /// <summary>A short, player-facing explanation. Deliberately free of technical detail.</summary>
        public string PlayerFacingMessage => Failure switch
        {
            ConnectionFailure.None => string.Empty,
            ConnectionFailure.Cancelled => string.Empty,
            ConnectionFailure.TimedOut => "Took too long. Try again?",
            _ => "Couldn't reach the game list."
        };
    }
}
