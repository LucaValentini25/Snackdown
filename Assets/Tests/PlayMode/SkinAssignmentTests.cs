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

        private GameObject _avatarPrefab;
        private GameObject _sessionPrefab;
        private GameObject _simulationPrefab;

        private ConnectionApproval _approval;

        [SetUp]
        public void LoadPrefabs()
        {
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
                _approval.Enable();
                return;
            }

            ConnectionApproval.EnableOnClient(peer);
            peer.NetworkConfig.ConnectionData = new ConnectionPayload
            {
                GameVersion = GameVersion,
                Nickname = $"Guest {Clients.Count + 1}",
                CharacterIndex = 0
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
