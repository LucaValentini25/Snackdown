using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Snackdown.UI
{
    /// <summary>Where a player's remaining life is drawn.</summary>
    public enum LifeBarPlacement
    {
        /// <summary>A bar floating above each character, in the world.</summary>
        OverTheCharacter = 0,

        /// <summary>A strip along the bottom of the screen, one entry per player.</summary>
        AlongTheBottom = 1
    }

    /// <summary>
    /// Which of the two life-bar presentations is on screen: the room's default, unless this machine
    /// has said otherwise.
    /// </summary>
    /// <remarks>
    /// <para>Both presentations are built and both are shipped, because which one reads better is a
    /// judgement that cannot be made from a description — it has to be seen with four characters
    /// moving. The toggle exists so the comparison happens in one session instead of across two
    /// builds.</para>
    /// <para><b>The default is replicated and the choice is not.</b> The host decides what a session
    /// opens with, so a demo looks the way whoever is running it wants; anybody watching can
    /// disagree, and their disagreement stays on their machine. It changes nothing about the match,
    /// which is the test of whether something belongs on the wire — see ADR D-006. This is the only
    /// replicated value in the project that no rule reads.</para>
    /// <para>Networked, and therefore on the session's own object rather than loose in a scene: a
    /// room default is something a room has. Where there is no session there is no room, and the
    /// static below keeps answering on its own.</para>
    /// </remarks>
    public class LifeBarStyle : NetworkBehaviour
    {
        [Tooltip("Presentation a session opens with, before the host changes it.")]
        [SerializeField] LifeBarPlacement _roomDefault = LifeBarPlacement.AlongTheBottom;

        [Tooltip("Key that swaps between the two, for comparing them live. Local to this machine.")]
        [SerializeField] Key _toggleKey = Key.F3;

        /// <remarks>
        /// An <c>int</c> on the wire rather than the enum. NGO can serialize an enum, but the value
        /// crossing is then tied to the declaration order of something that lives in the UI layer,
        /// and a reordered enum is a silent change of meaning on every peer that was not rebuilt.
        /// </remarks>
        readonly NetworkVariable<int> _replicatedDefault = new NetworkVariable<int>((int)LifeBarPlacement.AlongTheBottom);

        /// <summary>The presentation every life-bar view should currently be drawing.</summary>
        /// <remarks>
        /// A static because both views read it every frame and neither has a reference to reach it
        /// by — the strip is in the bootstrap scene and the nameplate is on a character that spawns
        /// per round. It defaults to the bottom strip so a scene with none of this in it still shows
        /// life somewhere, rather than showing nothing and looking broken.
        /// </remarks>
        public static LifeBarPlacement Placement { get; private set; } = LifeBarPlacement.AlongTheBottom;

        /// <summary>The room's default, for a screen that offers to change it.</summary>
        public LifeBarPlacement RoomDefault => (LifeBarPlacement)_replicatedDefault.Value;

        /// <summary>The style in force for this session, if there is one.</summary>
        public static LifeBarStyle Current { get; private set; }

        public override void OnNetworkSpawn()
        {
            Current = this;
            _replicatedDefault.OnValueChanged += OnRoomDefaultChanged;

            // Written in the server's own spawn, so it is inside the spawn message every client
            // receives rather than a delta chasing it.
            if (IsServer) _replicatedDefault.Value = (int)_roomDefault;

            Apply();
        }

        public override void OnNetworkDespawn()
        {
            _replicatedDefault.OnValueChanged -= OnRoomDefaultChanged;

            if (ReferenceEquals(Current, this)) Current = null;
        }

        void OnRoomDefaultChanged(int previous, int current) => Apply();

        /// <summary>Sets what this session opens with. Server-only.</summary>
        public void ServerSetRoomDefault(LifeBarPlacement placement)
        {
            if (!IsServer) return;

            _replicatedDefault.Value = (int)placement;
        }

        /// <remarks>
        /// A machine that has chosen keeps its choice, including when the host moves the room
        /// underneath it. One that has not follows along — which is what makes the host's setting
        /// worth replicating rather than being a fourth way to say "the default".
        /// </remarks>
        void Apply()
        {
            Placement = HudLayoutPreference.HasChoice
                ? HudLayoutPreference.Choice
                : (LifeBarPlacement)_replicatedDefault.Value;
        }

        void Update()
        {
            if (Keyboard.current == null || !Keyboard.current[_toggleKey].wasPressedThisFrame) return;

            // Pressing the key is choosing. From here on this machine stops following the room,
            // which is the point — the alternative is a toggle the host can undo from another
            // computer while you are looking at it.
            LifeBarPlacement swapped = Placement == LifeBarPlacement.OverTheCharacter
                ? LifeBarPlacement.AlongTheBottom
                : LifeBarPlacement.OverTheCharacter;

            HudLayoutPreference.Choose(swapped);
            Apply();
        }
    }
}
