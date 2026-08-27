namespace Snackdown.Connection
{
    /// <summary>Whether a join target is something a player typed or something they clicked.</summary>
    /// <remarks>
    /// <para>The two are different strings pointing at the same session, and the service has a
    /// separate call for each. A code is short, shareable and typed by a human; a listing id is
    /// opaque and comes back from a query. Passing one where the other is expected fails as "no
    /// such session", which reads to the player as the game having ended.</para>
    /// <para>Named rather than inferred from the shape of the string. Guessing would work until the
    /// day a code happened to look like an id, and the failure would be a join that quietly stopped
    /// working for one player in a hundred.</para>
    /// <para><see cref="Typed"/> is the default so the older two-argument
    /// <see cref="ConnectionRequest.Join"/> keeps meaning exactly what it meant.</para>
    /// </remarks>
    public enum JoinTargetKind
    {
        /// <summary>An address or a join code, as the player entered it.</summary>
        Typed = 0,

        /// <summary>An opaque id from <see cref="SessionListing.Id"/>.</summary>
        Listing = 1
    }
}
