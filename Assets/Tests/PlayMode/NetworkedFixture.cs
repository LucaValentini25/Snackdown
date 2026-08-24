using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Snackdown.Tests
{
    /// <summary>
    /// Base class for tests that need real peers: a host and one or more clients, each with its own
    /// <see cref="NetworkManager"/>, handshaking over loopback inside a single Play mode session.
    /// </summary>
    /// <remarks>
    /// <para>This exists because nothing in this repository could fail because of a networking bug.
    /// The Edit mode suite covers the simulation, the prediction buffer and the interpolator, all of
    /// which are single-peer and single-process by construction — so a change that breaks the
    /// handshake, the approval path or a spawn passes every test in the tree. Once a join-breaking
    /// regression survived six merged pull requests; this is the layer that would have caught it on
    /// the first.</para>
    /// <para>Netcode for GameObjects ships its own harness, <c>NetcodeIntegrationTest</c>, and it was
    /// rejected. It lives in <c>Unity.Netcode.Runtime.Tests</c>, which is only compiled by adding the
    /// package to <c>testables</c> in the manifest — and that pulls the package's own 672 tests into
    /// this project's Test Runner alongside ours, so the suite this repository is judged on stops
    /// being visible in its own window. The harness itself is not the expensive part: what it does
    /// for the ordinary case is create a <see cref="NetworkManager"/> per peer, give each one a
    /// <see cref="UnityTransport"/> pointed at loopback, and wait. That is what this class does, in
    /// terms this project can read, with no dependency on an API that has already been renamed once
    /// (<c>Unity.Netcode.TestHelpers.Runtime</c> became <c>Unity.Netcode.Runtime.Tests</c> in 2.0).</para>
    /// <para>Everything here is deliberately explicit rather than inherited from the
    /// <c>Bootstrap</c> scene: a test that reads its wire configuration from a scene asset stops
    /// reporting on the code and starts reporting on whatever was last saved in the Inspector.</para>
    /// </remarks>
    public abstract class NetworkedFixture
    {
        /// <summary>Loopback port the harness binds, deliberately not the game's 7777.</summary>
        /// <remarks>
        /// The editor may be hosting a real session on 7777 while the suite runs — that is the
        /// normal way this project is worked on. Sharing the port would make the tests fail for a
        /// reason that has nothing to do with the code under test, which is the worst kind of
        /// red suite: one that teaches you to ignore it.
        /// </remarks>
        protected const ushort HarnessPort = 7787;

        /// <summary>How long <see cref="WaitFor"/> may wait before failing the test.</summary>
        /// <remarks>
        /// A handshake over loopback completes in a handful of frames. This is not a tuned value —
        /// it is the point past which the answer is "it is never going to happen", and the test
        /// should say so instead of hanging the editor until someone notices.
        /// </remarks>
        protected const float DefaultTimeoutSeconds = 5f;

        private readonly List<NetworkManager> _peers = new List<NetworkManager>();
        private readonly List<NetworkManager> _clients = new List<NetworkManager>();

        /// <summary>Server and client in one process, the way the game actually runs.</summary>
        protected NetworkManager Host { get; private set; }

        /// <summary>The joining clients, in the order <see cref="StartSession"/> started them.</summary>
        protected IReadOnlyList<NetworkManager> Clients => _clients;

        /// <summary>
        /// Stands up a host and <paramref name="clientCount"/> clients and returns once every client
        /// has been approved and synchronized.
        /// </summary>
        /// <remarks>
        /// Returning on "approved and synchronized" rather than on "the socket accepted us" is what
        /// makes the tests that follow readable: by the time this yields, the server has run
        /// approval, both ends agree on a client id, and the peer list each side received has
        /// already been applied. Anything a test asserts after this point is a statement about the
        /// game, not a race against the handshake.
        /// </remarks>
        /// <param name="clientCount">How many clients join the host.</param>
        protected IEnumerator StartSession(int clientCount)
        {
            Host = CreatePeer("Host", isHost: true);
            Assert.IsTrue(Host.StartHost(), $"The host refused to start. Port {HarnessPort} may already be bound.");

            OnHostStarted();

            for (int i = 0; i < clientCount; i++)
            {
                NetworkManager client = CreatePeer($"Client {i + 1}", isHost: false);
                _clients.Add(client);

                Assert.IsTrue(client.StartClient(), $"Client {i + 1} refused to start.");
            }

            yield return WaitFor(
                () => EveryClientIsSynchronized(clientCount),
                $"the host and {clientCount} client(s) to complete the handshake");
        }

        /// <summary>
        /// Runs frames until <paramref name="condition"/> holds, and fails the test if it never does.
        /// </summary>
        /// <remarks>
        /// Networked state does not change when a test asks for it, it changes when a frame runs and
        /// a message is delivered — so every assertion about two peers agreeing has to be preceded
        /// by a wait. Reporting the description on timeout matters more than it looks: without it a
        /// failure reads "expected True, was False" and says nothing about which of the half-dozen
        /// waits in a test gave up.
        /// </remarks>
        /// <param name="condition">Checked once per frame.</param>
        /// <param name="description">What is being waited for, phrased to follow "Timed out waiting for".</param>
        /// <param name="timeoutSeconds">Wall-clock budget, defaulting to <see cref="DefaultTimeoutSeconds"/>.</param>
        protected static IEnumerator WaitFor(Func<bool> condition, string description, float timeoutSeconds = DefaultTimeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;

            while (!condition())
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    Assert.Fail($"Timed out waiting for {description} ({timeoutSeconds:0.#}s).");
                }

                yield return null;
            }
        }

        /// <summary>
        /// Configures one peer before it starts. Override to register prefabs or enable approval.
        /// </summary>
        /// <remarks>
        /// The hook is here rather than in each test because the two halves of a network
        /// configuration have to agree: approval, scene management and the prefab list are part of
        /// what the connection request is deserialized against, and a peer that differs on any of
        /// them is refused with an error that names none of this. Overriding one method that is
        /// called for every peer makes disagreeing take deliberate effort.
        /// </remarks>
        /// <param name="peer">The peer being configured, not yet started.</param>
        /// <param name="isHost">True for the host, false for a joining client.</param>
        protected virtual void Configure(NetworkManager peer, bool isHost)
        {
        }

        /// <summary>
        /// Runs on the server once it is listening and before any client joins. Override to spawn
        /// the objects a session is expected to already contain.
        /// </summary>
        /// <remarks>
        /// In a real session the systems that react to a player arriving are scene objects: they
        /// exist before anyone connects, because the scene was loaded first. A test that spawned
        /// them afterwards would be exercising a join order that never happens, and would quietly
        /// pass over the case where the first client arrives before its handler does.
        /// </remarks>
        protected virtual void OnHostStarted()
        {
        }

        /// <summary>
        /// Shuts every peer down and destroys it, whether the test passed, failed or threw.
        /// </summary>
        /// <remarks>
        /// <para>Peers cannot simply be destroyed. <see cref="NetworkManager.Shutdown"/> only raises
        /// a flag; the socket is closed on the next network update, so a fixture that destroys the
        /// object in the same frame leaves the port bound and the *next* test in the class fails to
        /// start its host — a failure that points at the wrong test and disappears when that test is
        /// run alone.</para>
        /// <para>A <see cref="NetworkManager"/> also puts itself under <c>DontDestroyOnLoad</c>, so
        /// it outlives the Play mode scene the test ran in. Nothing else will collect these.</para>
        /// </remarks>
        [UnityTearDown]
        public IEnumerator ShutDownSession()
        {
            foreach (NetworkManager peer in _peers)
            {
                if (peer != null && peer.IsListening) peer.Shutdown();
            }

            float deadline = Time.realtimeSinceStartup + DefaultTimeoutSeconds;
            while (AnyPeerIsStillListening() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            // Destroyed even if a shutdown hung: leaking a live peer into the next test is worse
            // than the assertion failure that hanging one would have produced.
            foreach (NetworkManager peer in _peers)
            {
                if (peer != null) Object.DestroyImmediate(peer.gameObject);
            }

            _peers.Clear();
            _clients.Clear();
            Host = null;
        }

        private NetworkManager CreatePeer(string name, bool isHost)
        {
            var carrier = new GameObject($"NetworkManager — {name}");
            NetworkManager peer = carrier.AddComponent<NetworkManager>();
            UnityTransport transport = carrier.AddComponent<UnityTransport>();

            // Registered for teardown before it is configured, so that a peer whose configuration
            // throws is still destroyed rather than left running for the rest of the session.
            _peers.Add(peer);

            // Forced, so that an editor launched with -ip or -port cannot retarget the harness.
            // Multiplayer Play Mode starts its virtual players with command line arguments, and
            // UnityTransport reads them by default — which would silently point a test at whatever
            // session that editor was configured for.
            transport.SetConnectionData(true, "127.0.0.1", HarnessPort, "127.0.0.1");

            // Two seconds rather than the default sixty. A minute of retrying is right for a player
            // on a bad connection and wrong for loopback, where a client that has not connected by
            // now is not going to — and where that minute is spent inside a test that already knows
            // the answer.
            transport.MaxConnectAttempts = 4;
            transport.ConnectTimeoutMS = 500;

            // A NetworkManager added at runtime has no config at all — the field is only populated
            // by deserialization, which is why the one in Bootstrap arrives filled in and this one
            // does not.
            peer.NetworkConfig ??= new NetworkConfig();

            peer.NetworkConfig.NetworkTransport = transport;

            // Stated rather than left at the package's default, which is the same 30 today and is
            // not this project's decision to make. A test that measures anything over time would
            // otherwise start running at a rate the game does not use, without saying so.
            peer.NetworkConfig.TickRate = 30;

            // The test runner's scene is not in the build settings and never will be. With scene
            // management on, the client tries to synchronize it on join and the handshake fails for
            // a reason that belongs to the harness rather than to the game.
            peer.NetworkConfig.EnableSceneManagement = false;

            Configure(peer, isHost);

            _peers.Add(peer);
            return peer;
        }

        private bool EveryClientIsSynchronized(int clientCount)
        {
            if (Host.ConnectedClientsIds.Count != clientCount + 1) return false;

            foreach (NetworkManager client in _clients)
            {
                if (!client.IsConnectedClient) return false;
            }

            return true;
        }

        private bool AnyPeerIsStillListening()
        {
            foreach (NetworkManager peer in _peers)
            {
                if (peer != null && peer.IsListening) return true;
            }

            return false;
        }
    }
}
