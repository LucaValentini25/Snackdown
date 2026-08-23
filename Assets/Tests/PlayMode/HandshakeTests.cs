using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine.TestTools;

namespace Snackdown.Tests
{
    /// <summary>
    /// The first tests in this repository that can fail because of a networking bug: two real peers,
    /// a real transport, and the handshake that every other networked feature is built on top of.
    /// </summary>
    /// <remarks>
    /// These assert the floor rather than any game rule. Nothing here knows about life, fruit or
    /// prediction — the point is that a host accepts a client, both ends agree on who is in the
    /// session, and the harness that arranges all of it survives being run several times in a row.
    /// The features that need more than a handshake extend <see cref="NetworkedFixture"/> from here.
    /// </remarks>
    public class HandshakeTests : NetworkedFixture
    {
        [UnityTest]
        public IEnumerator AClientJoiningAHost_IsSynchronized()
        {
            yield return StartSession(clientCount: 1);

            Assert.IsTrue(Clients[0].IsConnectedClient, "The client started but was never approved.");
        }

        [UnityTest]
        public IEnumerator TheHost_SeesTheClient()
        {
            yield return StartSession(clientCount: 1);

            Assert.That(Host.ConnectedClientsIds, Does.Contain(Clients[0].LocalClientId));
            Assert.AreEqual(2, Host.ConnectedClientsIds.Count, "The host counts someone who is not in this session.");
        }

        [UnityTest]
        public IEnumerator TheClient_SeesTheHost()
        {
            yield return StartSession(clientCount: 1);

            // Worth asserting separately from the host's view: the two lists are built by different
            // code on different sides of the wire, and a client that connects while believing it is
            // alone in the session is the shape of bug this suite exists to catch.
            Assert.That(Clients[0].ConnectedClientsIds, Does.Contain(NetworkManager.ServerClientId));
            Assert.That(Clients[0].ConnectedClientsIds, Does.Contain(Clients[0].LocalClientId));
        }

        [UnityTest]
        public IEnumerator TwoClients_SeeEachOther()
        {
            yield return StartSession(clientCount: 2);

            // A client learns about a peer that joined after it did from a message of its own, not
            // from the list it was handed on approval — so this is a second mechanism, and it is
            // the one the lobby roster depends on.
            yield return WaitFor(
                () => Clients[0].ConnectedClientsIds.Count == 3 && Clients[1].ConnectedClientsIds.Count == 3,
                "both clients to learn about each other");

            Assert.That(Clients[0].ConnectedClientsIds, Does.Contain(Clients[1].LocalClientId));
            Assert.That(Clients[1].ConnectedClientsIds, Does.Contain(Clients[0].LocalClientId));
        }
    }
}
