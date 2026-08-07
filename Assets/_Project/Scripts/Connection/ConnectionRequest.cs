namespace Snackdown.Connection
{
    /// <summary>
    /// Everything a provider needs to host or join: who is asking, and what they are joining.
    /// </summary>
    /// <remarks>
    /// The player-facing details travel here rather than being set on a component after the fact.
    /// The original project pushed nicknames through a retry loop and a <c>Task.Delay(100)</c>
    /// because the name arrived after the character already existed; carrying it with the request
    /// means it is available at the moment the connection is approved, which is the only moment it
    /// can be validated.
    /// </remarks>
    public struct ConnectionRequest
    {
        /// <summary>Address, join code, or empty when hosting a provider that generates its own.</summary>
        public string Target;

        /// <summary>The player's chosen name. Validated on arrival — never trusted as sent.</summary>
        public string Nickname;

        /// <summary>Index of the chosen character skin. Purely cosmetic.</summary>
        public int CharacterIndex;

        public static ConnectionRequest Host(string nickname, int characterIndex = 0) => new ConnectionRequest
        {
            Target = string.Empty,
            Nickname = nickname,
            CharacterIndex = characterIndex
        };

        public static ConnectionRequest Join(string target, string nickname, int characterIndex = 0) => new ConnectionRequest
        {
            Target = target,
            Nickname = nickname,
            CharacterIndex = characterIndex
        };
    }
}
