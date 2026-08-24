using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Gameplay.Player
{
    /// <summary>
    /// Dresses a character in the skin its owner picked, on every peer.
    /// </summary>
    /// <remarks>
    /// <para>Reads the choice off the owner's <see cref="PlayerSession"/> rather than replicating
    /// it again. The index already crossed the wire once, inside the connection payload, and was
    /// clamped by approval before it reached the session — sending it a second time would mean a
    /// second copy that can disagree with the first, and a second place for a client to lie.</para>
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

            // A character can spawn before the session describing it has been synchronized, and
            // the skin index arrives as a delta after that. Re-applying on every roster change costs
            // nothing and removes both races.
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

        int IndexForOwner() => _roster == null ? 0 : _roster.Of(OwnerClientId)?.CharacterIndex ?? 0;
    }
}
