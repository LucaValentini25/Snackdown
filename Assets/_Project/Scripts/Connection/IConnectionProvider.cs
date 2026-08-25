using System.Threading;
using System.Threading.Tasks;

namespace Snackdown.Connection
{
    /// <summary>
    /// One way of getting two peers talking. The rest of the game asks to host or to join and never
    /// learns whether that happened over a LAN socket or through a relay on the other side of the
    /// world.
    /// </summary>
    /// <remarks>
    /// <para>The abstraction exists because the join flow is the part most likely to be rewritten,
    /// and the part a menu is most likely to grow warts around. In the original project the UI
    /// talked to the transport directly, so a server-discovery feature that was never finished left
    /// the client with no way to join at all — the menu had assumptions about connecting baked into
    /// it that nobody could see from the outside.</para>
    /// <para><b>Asynchronous by necessity, not by fashion.</b> A direct connection resolves in
    /// microseconds, but allocating a relay allocation means a round trip to a web service that can
    /// fail, time out, or be cancelled by a player who changed their mind. Modelling the fast case
    /// as synchronous and the slow one as something else would push that difference into every
    /// caller. So both are <see cref="Task"/>, and every attempt takes a
    /// <see cref="CancellationToken"/>.</para>
    /// <para>Failure is a <b>return value</b>, not an exception. "The code was wrong" and "the host
    /// hung up" are both ordinary outcomes here, and a menu has to render them as a message rather
    /// than catch them.</para>
    /// <para><b>Never block on these tasks.</b> Calling <c>.Wait()</c> or <c>.Result</c> from the
    /// main thread deadlocks the editor: continuations resume on Unity's synchronization context,
    /// which only runs on the thread being blocked. <c>await</c> them, or start them and read the
    /// result later — the callers here are event handlers, which can do exactly that.</para>
    /// </remarks>
    public interface IConnectionProvider
    {
        /// <summary>Shown in the UI when offering this way of connecting.</summary>
        string DisplayName { get; }

        /// <summary>
        /// True when joining needs a share code rather than an address — which is what decides
        /// whether the menu shows an address field or a code field.
        /// </summary>
        bool JoinsByCode { get; }

        /// <summary>
        /// Starts a session others can join. On success the result carries whatever a peer needs to
        /// reach it: an address for a direct connection, a code for a relayed one.
        /// </summary>
        Task<ConnectionResult> HostAsync(ConnectionRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// True when this way of connecting can list what there is to join, rather than only
        /// accepting something the player already knows.
        /// </summary>
        /// <remarks>
        /// A relay has a directory behind it; a LAN socket does not, and finding games on one means
        /// broadcasting and listening, which is a different feature with a different failure mode.
        /// So the menu asks rather than assuming, and shows the browser only where there is one.
        /// </remarks>
        bool CanBrowse { get; }

        /// <summary>
        /// Lists the games that are open to anybody. Meaningless unless <see cref="CanBrowse"/>.
        /// </summary>
        /// <remarks>
        /// Returns a snapshot, not a subscription. The list is out of date the moment it arrives —
        /// a game can fill up or end between the query and the click — so the browser is a
        /// convenience for finding a session, and joining it is still the thing that can fail.
        /// </remarks>
        Task<BrowseResult> BrowseAsync(CancellationToken cancellationToken = default);

        /// <summary>Joins an existing session identified by <see cref="ConnectionRequest.Target"/>.</summary>
        Task<ConnectionResult> JoinAsync(ConnectionRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Leaves whatever is running and returns to a state where hosting or joining is possible
        /// again. Safe to call when nothing is running.
        /// </summary>
        Task LeaveAsync();
    }
}
