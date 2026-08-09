namespace Snackdown.Gameplay.Match
{
    /// <summary>How a match came to an end.</summary>
    /// <remarks>
    /// Replicated alongside the winner because "you won" and "you outlasted everyone" are different
    /// sentences, and the end screen should be able to say which one happened without inferring it
    /// from the clock.
    /// </remarks>
    public enum MatchOutcome : byte
    {
        /// <summary>The match has not ended.</summary>
        Undecided = 0,

        /// <summary>Everyone else ran out of life.</summary>
        LastStanding = 1,

        /// <summary>The round clock ran out; the most life left wins.</summary>
        TimeUp = 2,

        /// <summary>Nobody was left to win — the last players went out together, or all left.</summary>
        NoWinner = 3
    }
}
