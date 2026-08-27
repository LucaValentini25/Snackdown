namespace Snackdown.Connection
{
    /// <summary>
    /// One game somebody could join, as the browser needs to show it.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately not the service's own session type. <c>ISessionInfo</c> carries a dozen
    /// fields the menu has no use for and drags <c>Unity.Services.Multiplayer</c> into the UI
    /// assembly, which would put the front screen back in the position the original project's menu
    /// was in — knowing which service it was talking to. This is the four values a row needs.</para>
    /// <para><see cref="Id"/> is opaque on purpose. It is not a join code and cannot be typed; it is
    /// what the browser hands back to the provider that produced it, and no other layer should read
    /// anything into it.</para>
    /// </remarks>
    public readonly struct SessionListing
    {
        /// <summary>What the provider needs to join this game. Not a code, not for showing.</summary>
        public readonly string Id;

        /// <summary>What a player reads in the list — the host's name, in practice.</summary>
        public readonly string Name;

        /// <summary>How many players are in it now.</summary>
        public readonly int Players;

        /// <summary>How many it holds.</summary>
        public readonly int MaxPlayers;

        public SessionListing(string id, string name, int players, int maxPlayers)
        {
            Id = id ?? string.Empty;
            Name = string.IsNullOrWhiteSpace(name) ? "A game" : name;
            Players = players;
            MaxPlayers = maxPlayers;
        }

        /// <summary>Whether there is room. Normally false, and worth checking anyway.</summary>
        /// <remarks>
        /// The query asks the service for games with a slot free, so a full one should not appear at
        /// all. It is checked because a listing is a snapshot: the count on a row is what was true
        /// when the query answered, and the row a player is reading can fill up while they read it.
        /// Showing it full is better than a click that fails for no visible reason.
        /// </remarks>
        public bool IsFull => MaxPlayers > 0 && Players >= MaxPlayers;
    }
}
