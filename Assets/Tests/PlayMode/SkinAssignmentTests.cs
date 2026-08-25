// Editor-only for the same reason as PlayerSessionTests: these load the real prefabs out of the
// asset database, and a player build has none. See the note at the top of that file for why the
// assembly itself stays a Play mode one.
#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Snackdown.Connection;
using Snackdown.Gameplay.Player;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Snackdown.Tests
{
    /// <summary>
    /// Four players in an arena are four different characters, without anybody having chosen.
    /// </summary>
    /// <remarks>
    /// <para>Nothing sets the skin index in the connection payload yet, so every arrival asks for
    /// zero — and every arrival used to be given it. That is not a bug anyone could point at in a
    /// stack trace: the request was honoured exactly as written. It just meant a full session was
    /// four identical characters, which reads as the skin system being broken rather than as it
    /// never having been asked for anything.</para>
    /// <para>Asserted on the session rather than on approval's own table, because the index the
    /// session publishes is what every peer draws from. A skin decided correctly and published
    /// wrongly looks the same from the arena as one never decided at all.</para>
    /// </remarks>
    public class SkinAssignmentTests : NetworkedFixture
    {
        private const string GameVersion = "test";

        /// <summary>What the shipped catalog holds, and what most of these tests assume.</summary>
        private const int DefaultSkinCount = 4;

        private GameObject _avatarPrefab;
        private GameObject _sessionPrefab;
        private GameObject _simulationPrefab;

        private ConnectionApproval _approval;

        /// <summary>
        /// Skins the session admits, and what each joining client asks for. Reset per test.
        /// </summary>
        /// <remarks>
        /// NUnit builds one instance of a fixture and runs every test in it, so a field one test
        /// writes is a field the next one inherits. The nickname fixture learned this the hard way:
        /// the test about the length cap left its name behind and failed three others against
        /// something they never asked for. Reset in SetUp rather than trusted.
        /// </remarks>
        private int _skinCount;

        private int _requestedSkin;

        [SetUp]
        public void LoadPrefabs()
        {
            _skinCount = DefaultSkinCount;
            _requestedSkin = 0;

            _avatarPrefab = LoadPrefab("Assets/_Project/Prefabs/Player.prefab");
            _sessionPrefab = LoadPrefab("Assets/_Project/Prefabs/PlayerSession.prefab");
            _simulationPrefab = LoadPrefab("Assets/_Project/Prefabs/NetworkSimulation.prefab");
        }

        protected override void Configure(NetworkManager peer, bool isHost)
        {
            peer.NetworkConfig.PlayerPrefab = _sessionPrefab;
            peer.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _avatarPrefab });
            peer.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _simulationPrefab });

            if (isHost)
            {
                _approval = new ConnectionApproval(peer, GameVersion, maxPlayers: 4);

                // Zero, like everybody else. The host asking for the same skin as every client is
                // the situation this task exists for, not a special case set up to pass.
                _approval.SetLocalPlayer("Host", characterIndex: 0);

                // In the game this comes from CharacterCatalog.Count, handed over by
                // SessionConnection. The harness has no bootstrap scene, so it says the number
                // itself — which is also what lets one test ask for a catalog bigger than four.
                _approval.CharacterCount = _skinCount;
                _approval.Enable();
                return;
            }

            ConnectionApproval.EnableOnClient(peer);
            peer.NetworkConfig.ConnectionData = new ConnectionPayload
            {
                GameVersion = GameVersion,
                Nickname = $"Guest {Clients.Count + 1}",
                CharacterIndex = _requestedSkin
            }.ToBytes();
        }

        protected override void OnHostStarted()
        {
            Object.Instantiate(_simulationPrefab).GetComponent<NetworkObject>().Spawn();
        }

        [UnityTearDown]
        public IEnumerator DisableApproval()
        {
            _approval?.Disable();
            _approval = null;
            yield break;
        }

        [UnityTest]
        public IEnumerator FourPlayers_AllAskingForTheSameSkin_GetFourDifferentOnes()
        {
            yield return StartSession(clientCount: 3);

            yield return WaitFor(
                () => RosterOn(Host) != null && RosterOn(Host).Count == 4,
                "the server to see all four players");

            var skins = new List<int>();
            for (int i = 0; i < RosterOn(Host).Count; i++) skins.Add(RosterOn(Host)[i].CharacterIndex);

            CollectionAssert.AllItemsAreUnique(skins,
                $"Four players asked for skin 0 and were given {string.Join(", ", skins)}.");

            // Within the catalog, not merely distinct: an index nobody has a sprite for draws
            // nothing, which is worse than a duplicate.
            foreach (int skin in skins)
            {
                Assert.GreaterOrEqual(skin, 0, "a skin index below the catalog");
                Assert.Less(skin, _approval.CharacterCount, "a skin index past the end of the catalog");
            }
        }

        [UnityTest]
        public IEnumerator EveryPeer_SeesTheSameSkinForAPlayer()
        {
            yield return StartSession(clientCount: 2);

            yield return WaitFor(
                () => RosterOn(Host) != null && RosterOn(Host).Count == 3
                      && RosterOn(Clients[0]) != null && RosterOn(Clients[0]).Count == 3,
                "both peers to see all three players");

            ulong other = Clients[1].LocalClientId;

            // The one that matters for the arena: the character a player is drawn as comes from the
            // session, and every peer draws it. Two peers disagreeing means two different characters
            // in the same fight.
            yield return WaitFor(
                () => RosterOn(Host).Of(other) != null
                      && RosterOn(Clients[0]).Of(other) != null
                      && RosterOn(Host).Of(other).CharacterIndex
                         == RosterOn(Clients[0]).Of(other).CharacterIndex,
                "both peers to agree on the third player's skin");
        }

        [UnityTest]
        public IEnumerator ASkinFreedByLeaving_GoesToTheNextArrival()
        {
            yield return StartSession(clientCount: 2);

            yield return WaitFor(
                () => RosterOn(Host) != null && RosterOn(Host).Count == 3,
                "the server to see all three players");

            ulong leaving = Clients[0].LocalClientId;
            int freed = RosterOn(Host).Of(leaving).CharacterIndex;

            Clients[0].Shutdown();

            yield return WaitFor(
                () => RosterOn(Host).Count == 2,
                "the server to drop the player who left");

            yield return JoinAnotherClient();

            ulong arrival = Clients[2].LocalClientId;

            // Approval forgets a client's skin when it disconnects, so the wardrobe is a description
            // of who is here rather than a tally of everyone who ever was. Without that, the fifth
            // player to pass through a four-skin session would find nothing free.
            yield return WaitFor(
                () => RosterOn(Host).Of(arrival) != null
                      && RosterOn(Host).Of(arrival).CharacterIndex == freed,
                $"the arrival to be given skin {freed}, which the player who left had");
        }

        [UnityTest]
        public IEnumerator AClient_AskingForATakenSkin_IsRefused()
        {
            yield return StartSession(clientCount: 1);

            yield return WaitFor(
                () => RosterOn(Host) != null && RosterOn(Host).Count == 2,
                "the server to see both players");

            ulong guest = Clients[0].LocalClientId;

            int hostSkin = RosterOn(Host).Of(NetworkManager.ServerClientId).CharacterIndex;
            int guestSkin = RosterOn(Host).Of(guest).CharacterIndex;

            Assert.AreNotEqual(hostSkin, guestSkin, "the two players started in the same skin");

            // The lobby draws a taken skin disabled, which is a courtesy and not a rule. This is the
            // request a stale screen — or a client ignoring it — would send anyway.
            SessionOn(Clients[0], guest).RequestCharacter(hostSkin);

            for (int frame = 0; frame < 20; frame++) yield return null;

            Assert.AreEqual(guestSkin, RosterOn(Host).Of(guest).CharacterIndex,
                "A client took a skin somebody else was wearing.");

            Assert.AreEqual(hostSkin, RosterOn(Host).Of(NetworkManager.ServerClientId).CharacterIndex,
                "the host's own skin");
        }

        [UnityTest]
        public IEnumerator AClient_CanChangeIntoAFreeSkin()
        {
            yield return StartSession(clientCount: 1);

            yield return WaitFor(
                () => RosterOn(Host) != null && RosterOn(Host).Count == 2,
                "the server to see both players");

            ulong guest = Clients[0].LocalClientId;

            int free = FreeSkinOn(Host);
            Assert.GreaterOrEqual(free, 0, "no free skin to change into");

            SessionOn(Clients[0], guest).RequestCharacter(free);

            yield return WaitFor(
                () => RosterOn(Host).Of(guest).CharacterIndex == free,
                $"the server to accept the change into skin {free}");

            // Everyone draws a character from the session, so a change the rest of the lobby never
            // heard about is somebody standing in a costume only they can see.
            yield return WaitFor(
                () => RosterOn(Clients[0]).Of(guest) != null
                      && RosterOn(Clients[0]).Of(guest).CharacterIndex == free,
                "the client to see its own change");
        }

        [UnityTest]
        public IEnumerator ASkinLetGoOf_CanBeTakenByTheNextArrival()
        {
            yield return StartSession(clientCount: 1);

            yield return WaitFor(
                () => RosterOn(Host) != null && RosterOn(Host).Count == 2,
                "the server to see both players");

            ulong guest = Clients[0].LocalClientId;

            int wasWearing = RosterOn(Host).Of(guest).CharacterIndex;
            int free = FreeSkinOn(Host);

            SessionOn(Clients[0], guest).RequestCharacter(free);

            yield return WaitFor(
                () => RosterOn(Host).Of(guest).CharacterIndex == free,
                "the client to change out of its first skin");

            yield return JoinAnotherClient();

            // The point of telling approval about a change: its table hands skins to arrivals, and
            // left describing what everybody walked in wearing it would keep the abandoned one
            // reserved for nobody.
            yield return WaitFor(
                () => RosterOn(Host).Of(Clients[1].LocalClientId) != null
                      && RosterOn(Host).Of(Clients[1].LocalClientId).CharacterIndex == wasWearing,
                $"the arrival to be given skin {wasWearing}, which was let go of");
        }

        /// <summary>The lowest skin nobody on that peer's roster is wearing, or -1.</summary>
        private static int FreeSkinOn(NetworkManager peer)
        {
            SessionRoster roster = RosterOn(peer);
            if (roster == null) return -1;

            for (int skin = 0; skin < DefaultSkinCount; skin++)
            {
                bool taken = false;
                for (int i = 0; i < roster.Count; i++)
                {
                    if (roster[i].CharacterIndex == skin) { taken = true; break; }
                }

                if (!taken) return skin;
            }

            return -1;
        }

        private static PlayerSession SessionOn(NetworkManager peer, ulong ownerClientId)
        {
            SessionRoster roster = RosterOn(peer);
            return roster != null ? roster.Of(ownerClientId) : null;
        }

        [UnityTest]
        public IEnumerator AFifthSkin_IsReachableOnceTheCatalogSaysThereIsOne()
        {
            // The number used to be a constant 4 in ConnectionApproval, so a fifth authored skin was
            // clamped away with nothing said about it. It comes from CharacterCatalog.Count now, and
            // this is the difference that makes: the same request lands on 4 rather than on 3.
            _skinCount = 5;
            _requestedSkin = 4;

            yield return StartSession(clientCount: 1);

            ulong guest = Clients[0].LocalClientId;

            yield return WaitFor(
                () => RosterOn(Host) != null && RosterOn(Host).Of(guest) != null,
                "the server to see the client");

            Assert.AreEqual(4, RosterOn(Host).Of(guest).CharacterIndex,
                "A skin past the old hardcoded four was clamped away.");
        }

        /// <remarks>
        /// Read off the peer's own <c>SpawnManager</c> rather than <c>SessionRoster.Current</c>:
        /// this process runs a roster per peer and that static answers with whichever spawned last.
        /// </remarks>
        private static SessionRoster RosterOn(NetworkManager peer)
        {
            foreach (NetworkObject spawned in peer.SpawnManager.SpawnedObjectsList)
            {
                var roster = spawned.GetComponent<SessionRoster>();
                if (roster != null) return roster;
            }

            return null;
        }

        private static GameObject LoadPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"{path} is missing. The test cannot check a prefab that is not there.");

            return prefab;
        }
    }
}

#endif
