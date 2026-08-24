// Editor-only for the same reason as PlayerSessionTests: these load the real prefabs out of the
// asset database, and a player build has none. See the note at the top of that file for why the
// assembly itself stays a Play mode one.
#if UNITY_EDITOR

using System.Collections;
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
    /// A player outlives the character they are wearing: the body comes and goes with the round,
    /// the player does not.
    /// </summary>
    /// <remarks>
    /// <para>This is what the whole epic was for. Before <c>ps-4</c> the character <i>was</i> the
    /// player object, so a player who ran out had to be hidden rather than despawned — despawning
    /// would have taken their name, their life and their place in the round with it. Now
    /// <c>NetworkConfig.PlayerPrefab</c> is the session and the character is an ordinary prefab it
    /// spawns per round, so the body can genuinely go away.</para>
    /// <para>The assertions are made on both sides of the wire on purpose. A despawn the server
    /// performed and a client never applied leaves a character standing in the arena that nobody can
    /// hit and nothing controls, which is indistinguishable from the old hiding bug except that it
    /// is worse.</para>
    /// </remarks>
    public class AvatarLifecycleTests : NetworkedFixture
    {
        private const string GameVersion = "test";
        private const string HostNickname = "Luca";
        private const string ClientNickname = "Guest";

        private static readonly Vector2 FirstSpot = new Vector2(4f, 0f);
        private static readonly Vector2 SecondSpot = new Vector2(-7f, 2f);

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
            // The player object is the session, not the character — see ADR D-004. The character is
            // an ordinary prefab the session spawns per round, so it has to be registered like one.
            peer.NetworkConfig.PlayerPrefab = _sessionPrefab;
            peer.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _avatarPrefab });
            peer.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _simulationPrefab });

            if (isHost)
            {
                _approval = new ConnectionApproval(peer, GameVersion, maxPlayers: 4);
                _approval.SetLocalPlayer(HostNickname, characterIndex: 0);
                _approval.Enable();
                return;
            }

            ConnectionApproval.EnableOnClient(peer);
            peer.NetworkConfig.ConnectionData = new ConnectionPayload
            {
                GameVersion = GameVersion,
                Nickname = ClientNickname,
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
        public IEnumerator ThePlayerObject_IsTheSessionAndNotTheCharacter()
        {
            yield return StartSession(clientCount: 1);

            ulong clientId = Clients[0].LocalClientId;

            // NGO's own clientId-to-object lookup is what the arena uses to find who to give a body
            // to, and what the rest of the package uses for ownership. Repointing PlayerPrefab is
            // what makes it answer with the object worth reaching rather than with a corpse.
            yield return WaitFor(
                () => PlayerObjectOn(Host, clientId) != null,
                "the server to have a player object for the client");

            Assert.IsNotNull(PlayerObjectOn(Host, clientId).GetComponent<PlayerSession>(),
                "GetPlayerNetworkObject did not return the session.");

            // Connecting is not a round. Nobody gets a character until one starts.
            Assert.IsNull(AvatarOn(Host, clientId), "The client was given a character just for connecting.");
            Assert.IsFalse(SessionOn(Host, clientId).HasAvatar, "the session thinks it has a body");
        }

        [UnityTest]
        public IEnumerator RunningOut_DespawnsTheCharacterAndLeavesThePlayer()
        {
            yield return StartSession(clientCount: 1);

            ulong clientId = Clients[0].LocalClientId;

            yield return BeginRoundFor(clientId, FirstSpot);

            yield return WaitFor(
                () => AvatarOn(Clients[0], clientId) != null,
                "the client to see its own character");

            // Banked first, so the life the session is left holding is a number this test chose
            // rather than whatever the drain happened to leave. If a despawn took the session with
            // it, this is what would come back wrong.
            LifeOn(Host, clientId).ServerAdd(7f);
            yield return WaitFor(
                () => Mathf.Approximately(LifeOn(Clients[0], clientId).Remaining, LifeOn(Host, clientId).Remaining),
                "both peers to agree on the life before the round ends");

            float lifeAtTheEnd = LifeOn(Host, clientId).Remaining;

            LifeOn(Host, clientId).ServerEndRound();

            yield return WaitFor(
                () => AvatarOn(Host, clientId) == null && AvatarOn(Clients[0], clientId) == null,
                "the character to be despawned on both peers");

            // The point of the epic, asserted on the side that would have lost it.
            Assert.IsNotNull(SessionOn(Clients[0], clientId), "The player went away with their body.");
            Assert.AreEqual(ClientNickname, SessionOn(Clients[0], clientId).Nickname, "the name after death");
            Assert.AreEqual(lifeAtTheEnd, LifeOn(Clients[0], clientId).Remaining, 0.01f,
                "The session did not keep the life it ended the round with.");
            Assert.IsFalse(LifeOn(Host, clientId).IsAlive, "the server still counts this player as alive");
        }

        [UnityTest]
        public IEnumerator TheNextRound_GivesTheSamePlayerANewCharacter()
        {
            yield return StartSession(clientCount: 1);

            ulong clientId = Clients[0].LocalClientId;

            yield return BeginRoundFor(clientId, FirstSpot);

            ulong firstBody = AvatarOn(Host, clientId).NetworkObjectId;

            LifeOn(Host, clientId).ServerEndRound();

            yield return WaitFor(
                () => AvatarOn(Host, clientId) == null,
                "the first character to go");

            yield return BeginRoundFor(clientId, SecondSpot);

            // A different object, not the old one switched back on. Same id would mean the body had
            // been reused, which is the arrangement this task removed.
            Assert.AreNotEqual(firstBody, AvatarOn(Host, clientId).NetworkObjectId,
                "The next round handed back the same character object.");

            Assert.IsTrue(LifeOn(Host, clientId).IsAlive, "the player is still out after a new round began");

            // Placed rather than moved: the character is created at the spawn point, which is what
            // let the teleport flag leave PlayerSnapshot in this task.
            Assert.AreEqual(SecondSpot.x, AvatarOn(Host, clientId).transform.position.x, 0.01f, "spawn x");
            Assert.AreEqual(SecondSpot.y, AvatarOn(Host, clientId).transform.position.y, 0.01f, "spawn y");

            yield return WaitFor(
                () => AvatarOn(Clients[0], clientId) != null,
                "the client to receive the new character");
        }

        [UnityTest]
        public IEnumerator BeginningARound_RefillsTheLifeTheLastOneDrained()
        {
            yield return StartSession(clientCount: 1);

            ulong clientId = Clients[0].LocalClientId;

            yield return BeginRoundFor(clientId, FirstSpot);

            float full = LifeOn(Host, clientId).Remaining;

            LifeOn(Host, clientId).ServerEndRound();
            yield return WaitFor(() => !LifeOn(Host, clientId).IsAlive, "the player to leave the round");

            // The reset used to live on the way back to the lobby, which a rematch started from the
            // end screen never travels. Handing it out with the body is what closes that.
            yield return BeginRoundFor(clientId, SecondSpot);

            Assert.AreEqual(full, LifeOn(Host, clientId).Remaining, 0.01f,
                "The new round did not start this player back at a full life.");
        }

        /// <summary>Starts a round for one player the way the arena does, and waits for the body.</summary>
        private IEnumerator BeginRoundFor(ulong clientId, Vector2 at)
        {
            yield return WaitFor(
                () => SessionOn(Host, clientId) != null,
                "the server to hold the client's session");

            SessionOn(Host, clientId).ServerBeginRound(at);

            yield return WaitFor(
                () => AvatarOn(Host, clientId) != null,
                "the server to spawn the character for the round");
        }

        private static NetworkObject PlayerObjectOn(NetworkManager peer, ulong clientId)
            => peer.SpawnManager.GetPlayerNetworkObject(clientId);

        /// <remarks>
        /// Read off the peer's own <c>SpawnManager</c> rather than a static registry, which in this
        /// one process holds the objects of every peer at once. Both sides of a despawn have to be
        /// checked separately or the test cannot tell "gone" from "gone here".
        /// </remarks>
        private static PlayerSession SessionOn(NetworkManager peer, ulong ownerClientId)
            => ComponentOn<PlayerSession>(peer, ownerClientId);

        private static PredictedPlayer AvatarOn(NetworkManager peer, ulong ownerClientId)
            => ComponentOn<PredictedPlayer>(peer, ownerClientId);

        private static PlayerLife LifeOn(NetworkManager peer, ulong ownerClientId)
            => SessionOn(peer, ownerClientId)?.Life;

        private static T ComponentOn<T>(NetworkManager peer, ulong ownerClientId) where T : Component
        {
            foreach (NetworkObject spawned in peer.SpawnManager.SpawnedObjectsList)
            {
                if (spawned.OwnerClientId != ownerClientId) continue;

                var found = spawned.GetComponent<T>();
                if (found != null) return found;
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
