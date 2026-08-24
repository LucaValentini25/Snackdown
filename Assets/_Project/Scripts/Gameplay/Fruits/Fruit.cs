using Snackdown.Gameplay.Player;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Gameplay.Fruits
{
    /// <summary>
    /// One piece of fruit sitting in the arena, waiting to be collected.
    /// </summary>
    /// <remarks>
    /// <para>Collection is decided by the server and nowhere else. A client detecting its own
    /// pickups would be choosing when it gains life, which is the same thing as choosing not to
    /// lose — so the trigger runs server-side and clients find out because the object despawns.</para>
    /// <para>The kind of fruit is replicated rather than baked into the prefab: one prefab covers
    /// every fruit, and the index tells each peer which sprite to draw. Spawning eight different
    /// prefabs would mean eight entries in the network prefab list for objects that differ only in
    /// a sprite and a number.</para>
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    public class Fruit : NetworkBehaviour
    {
        [SerializeField] FruitTable _table;
        [SerializeField] SpriteRenderer _renderer;

        [Tooltip("How close a player must be to collect this. Generous on purpose — missing a fruit you walked through feels broken.")]
        [SerializeField] float _pickupRadius = 0.5f;

        /// <summary>Reused so the per-frame overlap query allocates nothing.</summary>
        static readonly Collider2D[] _overlaps = new Collider2D[8];

        /// <summary>Everything, including triggers — a player's body may well be one.</summary>
        static readonly ContactFilter2D _filter = new ContactFilter2D { useTriggers = true };

        /// <summary>Index into <see cref="FruitTable"/>, published by the server on spawn.</summary>
        readonly NetworkVariable<int> _kind = new NetworkVariable<int>(0);

        /// <summary>The kind the spawner chose, held until this object is spawned.</summary>
        /// <remarks>
        /// A plain field and not the <see cref="NetworkVariable{T}"/> itself, because the spawner
        /// decides what a fruit is before there is a fruit on the network to tell. Writing the
        /// networked value that early logs <i>"NetworkVariable is written to, but doesn't know its
        /// NetworkBehaviour yet"</i> on every spawn — NGO does not attach a variable to its
        /// behaviour until the object spawns, and the flag that silences the warning is internal to
        /// the package. Nothing is lost by waiting: the server runs its own
        /// <see cref="OnNetworkSpawn"/> before the spawn message is serialized, so a value written
        /// there is still part of the initial state every client receives rather than a second
        /// message after it.
        /// </remarks>
        int _chosenKind;

        /// <summary>Guards against two collisions in one frame both banking the same fruit.</summary>
        bool _collected;

        public override void OnNetworkSpawn()
        {
            _kind.OnValueChanged += (_, __) => Dress();

            if (IsServer) _kind.Value = _chosenKind;

            Dress();
        }

        /// <summary>Chooses what this fruit is. Called on the instance before it is spawned.</summary>
        /// <remarks>
        /// Takes effect on spawn rather than immediately — see <see cref="_chosenKind"/>. There is
        /// no <c>IsServer</c> guard because there is no server yet to check against: the object is
        /// not spawned, so the answer would come from whichever <c>NetworkManager</c> happens to be
        /// the singleton. Only the spawner calls this, and only the server runs the spawner.
        /// </remarks>
        public void ServerSetKind(int kindIndex) => _chosenKind = kindIndex;

        void Dress()
        {
            if (_table == null || _renderer == null || _table.Count == 0) return;
            _renderer.sprite = _table.Get(_kind.Value).Sprite;
        }

        /// <remarks>
        /// <para>Collection is an explicit overlap query rather than <c>OnTriggerEnter2D</c>, for
        /// the same reason <c>PlayerMotor</c> uses casts instead of stepping the physics world: this
        /// project does not let the physics engine decide anything that affects the match.</para>
        /// <para>There is also a concrete reason it could not work. The player's body is a
        /// <b>kinematic</b> <c>Rigidbody2D</c>, and a kinematic body raises no trigger events
        /// against colliders that have no rigidbody of their own unless
        /// <c>useFullKinematicContacts</c> is switched on. Relying on that would make fruit
        /// collection depend on a checkbox in a component nobody would think to look at.</para>
        /// </remarks>
        void Update()
        {
            if (!IsServer || _collected || _table == null) return;

            int count = Physics2D.OverlapCircle(transform.position, _pickupRadius, _filter, _overlaps);

            for (int i = 0; i < count; i++)
            {
                PlayerSession player = PlayerBehind(_overlaps[i]);
                if (player == null) continue;

                if (!player.ServerCollectFruit(_table.Get(_kind.Value).LifeSeconds)) continue;

                _collected = true;

                // Despawned rather than disabled: a collected fruit has no further use, and leaving
                // it around means every peer keeps an object nobody can interact with.
                NetworkObject.Despawn();
                return;
            }
        }

        /// <summary>
        /// The session of whoever owns the character this collider belongs to, or null.
        /// </summary>
        /// <remarks>
        /// <para>Matched on <see cref="PredictedPlayer"/> rather than on anything more general,
        /// because the overlap is not selective: the filter includes triggers, and this fruit's own
        /// <c>CircleCollider2D</c> is in range of itself. Asking for a <c>NetworkObject</c> in the
        /// parents would find that one, read its owner — the server — and hand every fruit in the
        /// arena to the host the instant it spawned.</para>
        /// <para>Then the owner id, not the object. Life and the fruit counter left the avatar in
        /// this task and live on the session, so the character is only ever the thing that says
        /// <i>which player</i> walked into this.</para>
        /// </remarks>
        PlayerSession PlayerBehind(Collider2D collider)
        {
            PredictedPlayer character = collider.GetComponentInParent<PredictedPlayer>();
            if (character == null) return null;

            return PlayerSession.Of(NetworkManager, character.OwnerClientId);
        }
    }
}
