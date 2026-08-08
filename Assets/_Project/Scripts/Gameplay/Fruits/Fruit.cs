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

        /// <summary>Index into <see cref="FruitTable"/>. Set by the server before spawning.</summary>
        readonly NetworkVariable<int> _kind = new NetworkVariable<int>(0);

        /// <summary>Guards against two collisions in one frame both banking the same fruit.</summary>
        bool _collected;

        public override void OnNetworkSpawn()
        {
            _kind.OnValueChanged += (_, __) => Dress();
            Dress();
        }

        /// <summary>Chooses what this fruit is. Server-only, called before the object is spawned.</summary>
        public void ServerSetKind(int kindIndex)
        {
            if (!IsServer) return;
            _kind.Value = kindIndex;
        }

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
                PlayerLife life = _overlaps[i].GetComponentInParent<PlayerLife>();
                if (life == null || !life.IsAlive) continue;

                _collected = true;
                life.ServerAdd(_table.Get(_kind.Value).LifeSeconds);

                // Despawned rather than disabled: a collected fruit has no further use, and leaving
                // it around means every peer keeps an object nobody can interact with.
                NetworkObject.Despawn();
                return;
            }
        }
    }
}
