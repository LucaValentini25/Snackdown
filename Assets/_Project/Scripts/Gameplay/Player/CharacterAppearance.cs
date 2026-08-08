using Snackdown.Connection;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Gameplay.Player
{
    /// <summary>
    /// Dresses a character in the skin its owner picked, on every peer.
    /// </summary>
    /// <remarks>
    /// <para>Reads the choice from <see cref="SessionRoster"/> rather than replicating it again.
    /// The index already crossed the wire once, inside the connection payload, and was clamped by
    /// approval before it reached the roster — sending it a second time would mean a second copy
    /// that can disagree with the first, and a second place for a client to lie.</para>
    /// <para>Purely visual. Nothing here is read by <see cref="PlayerMotor"/>, which is what makes
    /// the four characters mechanically identical rather than merely intended to be.</para>
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    public class CharacterAppearance : NetworkBehaviour
    {
        [Tooltip("Skins, indexed by the value carried in the connection payload.")]
        [SerializeField] CharacterCatalog _catalog;

        [Tooltip("Renderer to dress. Usually the Visual child that the smoother drives.")]
        [SerializeField] SpriteRenderer _renderer;

        SessionRoster _roster;

        public override void OnNetworkSpawn()
        {
            if (_renderer == null || _catalog == null) return;

            _roster = FindFirstObjectByType<SessionRoster>();

            // The roster is replicated, so a character can spawn before the slot describing it has
            // arrived. Re-applying on every change costs nothing and removes the race.
            if (_roster != null) _roster.Changed += Apply;

            Apply();
        }

        public override void OnNetworkDespawn()
        {
            if (_roster != null) _roster.Changed -= Apply;
            _roster = null;
        }

        void Apply()
        {
            if (_renderer == null || _catalog == null) return;

            CharacterCatalog.Entry entry = _catalog.Get(IndexForOwner());
            if (entry.Portrait != null) _renderer.sprite = entry.Portrait;
        }

        int IndexForOwner()
        {
            if (_roster == null) return 0;

            for (int i = 0; i < _roster.Count; i++)
                if (_roster[i].ClientId == OwnerClientId)
                    return _roster[i].CharacterIndex;

            return 0;
        }
    }
}
