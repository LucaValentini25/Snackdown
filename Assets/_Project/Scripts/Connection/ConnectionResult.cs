namespace Snackdown.Connection
{
    /// <summary>Why a connection attempt ended the way it did.</summary>
    /// <remarks>
    /// Distinct cases rather than a message string, because the menu reacts differently to each:
    /// a bad code is worth re-prompting for, a full session is not worth retrying, and a cancelled
    /// attempt should say nothing at all.
    /// </remarks>
    public enum ConnectionFailure
    {
        None = 0,

        /// <summary>
        /// What was typed is not an address at all — a typo, not a missing game.
        /// </summary>
        /// <remarks>
        /// Worth separating from <see cref="NotFound"/> because the player's next action differs:
        /// one means re-read what you typed, the other means the game is gone. It is also easy to
        /// hit by accident — <c>10.0.01</c> looks close enough to correct that .NET's own
        /// <c>IPAddress.TryParse</c> accepts it, while the transport does not.
        /// </remarks>
        InvalidAddress,

        /// <summary>The address or join code did not resolve to anything.</summary>
        NotFound,

        /// <summary>Reached, but it refused us — wrong version, session full, or rejected by approval.</summary>
        Rejected,

        /// <summary>No answer in time. Distinct from <see cref="NotFound"/>: this one is worth retrying.</summary>
        TimedOut,

        /// <summary>The player backed out while the attempt was in flight.</summary>
        Cancelled,

        /// <summary>The transport or the service failed in a way this layer cannot interpret.</summary>
        Error
    }

    /// <summary>The outcome of a hosting or joining attempt.</summary>
    public readonly struct ConnectionResult
    {
        public readonly bool Success;
        public readonly ConnectionFailure Failure;

        /// <summary>
        /// What other players need in order to reach this session — an address for a direct
        /// connection, a share code for a relayed one. Empty when joining rather than hosting.
        /// </summary>
        public readonly string JoinTarget;

        /// <summary>Detail for the log. Never rendered to a player as-is.</summary>
        public readonly string Diagnostic;

        ConnectionResult(bool success, ConnectionFailure failure, string joinTarget, string diagnostic)
        {
            Success = success;
            Failure = failure;
            JoinTarget = joinTarget ?? string.Empty;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public static ConnectionResult Ok(string joinTarget = "")
            => new ConnectionResult(true, ConnectionFailure.None, joinTarget, string.Empty);

        public static ConnectionResult Failed(ConnectionFailure failure, string diagnostic = "")
            => new ConnectionResult(false, failure, string.Empty, diagnostic);

        /// <summary>A short, player-facing explanation. Deliberately free of technical detail.</summary>
        public string PlayerFacingMessage => Failure switch
        {
            ConnectionFailure.None => "Connected.",
            ConnectionFailure.InvalidAddress => "That doesn't look like an address.",
            ConnectionFailure.NotFound => "Couldn't find that game.",
            ConnectionFailure.Rejected => "That game turned us away.",
            ConnectionFailure.TimedOut => "No answer. Try again?",
            ConnectionFailure.Cancelled => "",
            _ => "Something went wrong connecting."
        };
    }
}
